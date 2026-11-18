using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

// Medidor de desempenho (17/11). Criado sozinho no Play: não precisa pôr na cena.
//   F3 — mostra/esconde o painel (FPS ao vivo, quem está segurando o jogo, quanta coisa tem na tela).
//        O jogo lembra se o painel estava aberto.
//   F4 — grava uma medição de 30 s (F4 de novo termina antes). Durante a medição o VSync é
//        desligado, para ver até onde o jogo vai. No fim salva dois arquivos:
//          Docs\Medicoes\fps_<data>.txt  — resumo em português (é este que o Claude lê)
//          Docs\Medicoes\fps_<data>.csv  — uma linha por segundo, para gráfico
//        (Numa build, que não tem a pasta Docs, salva na pasta de dados do jogo.)
// Com o menu aberto (jogo pausado) os quadros não entram na conta.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public sealed class PerformanceMonitor : MonoBehaviour
{
    private const float MeasureSeconds = 30f;
    private const int HistorySize = 600;           // ~10 s a 60 FPS para o "1% baixo" ao vivo
    private const float PanelRefresh = 0.5f;       // atualiza o texto do painel
    private const float CountInterval = 1f;        // contagem de objetos (custa um pouco)
    private const string PrefKey = "ImNotAnAlien.PerfPanel";

    private static PerformanceMonitor instance;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private bool visible;

    // Ao vivo
    private readonly float[] history = new float[HistorySize];
    private readonly float[] sortBuffer = new float[HistorySize];
    private int historyCount;
    private int historyIndex;
    private float panelTimer;
    private int panelFrames;
    private float panelTime;
    private string panelText = "";
    private Color fpsColor = Color.white;

    // Tempos de CPU/GPU (FrameTimingManager)
    private readonly FrameTiming[] timings = new FrameTiming[1];
    private float mainMs, renderMs, gpuMs;
    private bool hasCpu, hasGpu;

    // Contadores do Unity (desenho e memória)
    private ProfilerRecorder batchesRec, setPassRec, trisRec, gcRec;

    // Contagem de objetos na cena
    private float nextCount;
    private float pendingCountCost;   // custo da contagem: tirado do quadro seguinte para não sujar a medição
    private float lastCountCost;
    private int particles, particleSystems, lights, shadowLights, visibleRenderers, totalRenderers;
    private int aliens, orbs, awakeBodies, blocks;

    // Gravação
    private bool recording;
    private float recElapsed;
    private int savedVSync;
    private int savedTargetFps;
    private readonly List<float> recMs = new List<float>(4096);
    private readonly List<float> recAt = new List<float>(4096);
    private StringBuilder csv;
    private double sumMain, sumRender, sumGpu;
    private int nMain, nGpu;
    private int secIndex;
    private int secFrames;
    private float secTime, secWorst;
    private double secMain, secRender, secGpu, secBatches, secSetPass, secTris, secGc;
    private int secMainN, secGpuN, secRecN;
    private readonly List<float[]> secRows = new List<float[]>(64);
    private string lastResult;
    private float lastResultUntil;

    private GUIStyle textStyle, bigStyle;
    private Transform player;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null)
        {
            return;
        }

        GameObject go = new GameObject("Medidor_FPS");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PerformanceMonitor>();
    }

    private void Awake()
    {
        visible = PlayerPrefs.GetInt(PrefKey, 0) == 1;
    }

    private void OnEnable()
    {
        batchesRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        setPassRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        trisRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
    }

    private void OnDisable()
    {
        if (recording)
        {
            StopRecording(save: false);
        }

        batchesRec.Dispose();
        setPassRec.Dispose();
        trisRec.Dispose();
        gcRec.Dispose();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
        {
            visible = !visible;
            PlayerPrefs.SetInt(PrefKey, visible ? 1 : 0);
        }

        if (keyboard != null && keyboard.f4Key.wasPressedThisFrame)
        {
            if (recording) StopRecording(save: true);
            else StartRecording();
        }

        // Tempo do quadro (sem câmera lenta: unscaled), sem o custo da nossa própria contagem.
        float ms = Mathf.Max(0.01f, Time.unscaledDeltaTime * 1000f - pendingCountCost);
        pendingCountCost = 0f;
        bool paused = GamePause.IsPaused;

        if (!paused)
        {
            history[historyIndex] = ms;
            historyIndex = (historyIndex + 1) % HistorySize;
            historyCount = Mathf.Min(historyCount + 1, HistorySize);
        }

        bool active = visible || recording;
        if (active)
        {
            ReadFrameTimings();
        }

        if (recording && !paused)
        {
            RecordFrame(ms);
        }

        if (visible)
        {
            panelFrames++;
            panelTime += Time.unscaledDeltaTime;
            panelTimer -= Time.unscaledDeltaTime;
            if (panelTimer <= 0f)
            {
                panelTimer = PanelRefresh;
                BuildPanelText();
                panelFrames = 0;
                panelTime = 0f;
            }
        }
    }

    private void LateUpdate()
    {
        // Conta os objetos de vez em quando (só com o painel aberto ou gravando).
        if ((visible || recording) && Time.unscaledTime >= nextCount)
        {
            nextCount = Time.unscaledTime + CountInterval;
            Stopwatch watch = Stopwatch.StartNew();
            CountScene();
            lastCountCost = (float)watch.Elapsed.TotalMilliseconds;
            pendingCountCost = lastCountCost;
        }
    }

    // ---------- Medidas ----------

    private void ReadFrameTimings()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timings) < 1)
        {
            hasCpu = hasGpu = false;
            return;
        }

        FrameTiming t = timings[0];
        hasCpu = t.cpuMainThreadFrameTime > 0.0;
        hasGpu = t.gpuFrameTime > 0.0;
        // Suaviza um pouco para dar para ler.
        float k = 0.15f;
        if (hasCpu)
        {
            mainMs = Mathf.Lerp(mainMs, (float)t.cpuMainThreadFrameTime, k);
            renderMs = Mathf.Lerp(renderMs, (float)t.cpuRenderThreadFrameTime, k);
        }

        if (hasGpu)
        {
            gpuMs = Mathf.Lerp(gpuMs, (float)t.gpuFrameTime, k);
        }

        if (recording && !GamePause.IsPaused)
        {
            if (hasCpu)
            {
                sumMain += t.cpuMainThreadFrameTime;
                sumRender += t.cpuRenderThreadFrameTime;
                nMain++;
                secMain += t.cpuMainThreadFrameTime;
                secRender += t.cpuRenderThreadFrameTime;
                secMainN++;
            }

            if (hasGpu)
            {
                sumGpu += t.gpuFrameTime;
                nGpu++;
                secGpu += t.gpuFrameTime;
                secGpuN++;
            }
        }
    }

    private void CountScene()
    {
        particles = particleSystems = 0;
        foreach (ParticleSystem ps in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            int n = ps.particleCount;
            if (n > 0)
            {
                particles += n;
                particleSystems++;
            }
        }

        lights = shadowLights = 0;
        foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!light.enabled || light.intensity <= 0f) continue;
            lights++;
            if (light.shadows != LightShadows.None) shadowLights++;
        }

        visibleRenderers = totalRenderers = 0;
        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.enabled) continue;
            totalRenderers++;
            if (r.isVisible) visibleRenderers++;
        }

        aliens = FindObjectsByType<AlienHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        orbs = FindObjectsByType<DnaOrb>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;

        awakeBodies = 0;
        foreach (Rigidbody body in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!body.isKinematic && !body.IsSleeping()) awakeBodies++;
        }

        blocks = WorldStreamer.Instance != null ? WorldStreamer.Instance.LoadedCount : 0;
    }

    private static long Last(ProfilerRecorder rec) => rec.Valid ? rec.LastValue : -1;

    private string Limiter()
    {
        if (!hasCpu && !hasGpu) return "?";
        float cpu = Mathf.Max(mainMs, renderMs);
        if (hasGpu && gpuMs > cpu * 1.1f) return "placa de vídeo (GPU)";
        if (mainMs >= renderMs) return "processador: scripts e física";
        return "processador: preparar o desenho";
    }

    // ---------- Painel ----------

    private void BuildPanelText()
    {
        float fps = panelTime > 0f ? panelFrames / panelTime : 0f;
        float avgMs = fps > 0f ? 1000f / fps : 0f;

        // Pior quadro e "1% baixo" dos últimos ~10 s.
        float worst = 0f, low1 = 0f;
        if (historyCount > 0)
        {
            System.Array.Copy(history, sortBuffer, historyCount);
            System.Array.Sort(sortBuffer, 0, historyCount);
            worst = sortBuffer[historyCount - 1];
            int n = Mathf.Max(1, historyCount / 100);
            double sum = 0;
            for (int i = historyCount - n; i < historyCount; i++) sum += sortBuffer[i];
            low1 = (float)(1000.0 / (sum / n));
        }

        fpsColor = fps >= 55f ? new Color(0.45f, 1f, 0.45f) : fps >= 30f ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.4f, 0.35f);

        long batches = Last(batchesRec), setPass = Last(setPassRec), tris = Last(trisRec), gc = Last(gcRec);
        string vsync = QualitySettings.vSyncCount > 0 ? "ligado" : "desligado";

        StringBuilder sb = new StringBuilder(512);
        sb.Append("FPS ").Append(fps.ToString("0", Inv)).Append("   (").Append(avgMs.ToString("0.0", Inv)).Append(" ms)\n");
        sb.Append("1% baixo ").Append(low1.ToString("0", Inv)).Append(" FPS · pior quadro ").Append(worst.ToString("0", Inv)).Append(" ms\n");
        sb.Append("CPU ").Append(hasCpu ? mainMs.ToString("0.0", Inv) : "—")
          .Append(" · desenho ").Append(hasCpu ? renderMs.ToString("0.0", Inv) : "—")
          .Append(" · GPU ").Append(hasGpu ? gpuMs.ToString("0.0", Inv) : "—").Append(" ms\n");
        sb.Append("Segurando o jogo: ").Append(Limiter()).Append('\n');
        sb.Append("batches ").Append(Num(batches)).Append(" · setpass ").Append(Num(setPass))
          .Append(" · triângulos ").Append(tris >= 0 ? (tris / 1000000f).ToString("0.00", Inv) + " mi" : "—").Append('\n');
        sb.Append("partículas ").Append(particles).Append(" (").Append(particleSystems).Append(" efeitos) · luzes ")
          .Append(lights).Append(" (").Append(shadowLights).Append(" c/ sombra)\n");
        sb.Append("na tela ").Append(visibleRenderers).Append(" de ").Append(totalRenderers).Append(" objetos · blocos ").Append(blocks).Append('\n');
        sb.Append("aliens ").Append(aliens).Append(" · orbes ").Append(orbs).Append(" · física acordada ").Append(awakeBodies).Append('\n');
        sb.Append("lixo (GC) ").Append(gc >= 0 ? (gc / 1024f).ToString("0.0", Inv) + " KB/quadro" : "—")
          .Append(" · VSync ").Append(vsync).Append('\n');
        sb.Append("F4: medir 30 s   ·   F3: esconder");
        panelText = sb.ToString();
    }

    private static string Num(long v) => v >= 0 ? v.ToString(Inv) : "—";

    private void OnGUI()
    {
        float scale = Screen.height / 1080f;
        if (textStyle == null)
        {
            textStyle = new GUIStyle(GUI.skin.label) { richText = false, wordWrap = false };
            bigStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
        }

        textStyle.fontSize = Mathf.RoundToInt(17f * scale);
        bigStyle.fontSize = Mathf.RoundToInt(26f * scale);

        if (visible)
        {
            Rect box = new Rect(24f * scale, 70f * scale, 520f * scale, 262f * scale);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;

            Rect inner = new Rect(box.x + 12f * scale, box.y + 8f * scale, box.width - 24f * scale, box.height - 16f * scale);
            GUI.Label(inner, panelText, textStyle);

            // Pinta a primeira linha (o FPS) com a cor da nota.
            int cut = panelText.IndexOf('\n');
            if (cut > 0)
            {
                GUI.color = fpsColor;
                GUI.Label(inner, panelText.Substring(0, cut), textStyle);
                GUI.color = Color.white;
            }
        }

        if (recording)
        {
            float left = Mathf.Max(0f, MeasureSeconds - recElapsed);
            string text = $"● MEDINDO DESEMPENHO… {Mathf.CeilToInt(left)} s   (F4 termina antes)";
            Shadowed(new Rect(0f, 18f * scale, Screen.width, 40f * scale), text, new Color(1f, 0.45f, 0.4f));
        }
        else if (lastResult != null && Time.unscaledTime < lastResultUntil)
        {
            Shadowed(new Rect(0f, 18f * scale, Screen.width, 80f * scale), lastResult, new Color(0.6f, 1f, 0.75f));
        }
    }

    private void Shadowed(Rect rect, string text, Color color)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, bigStyle);
        GUI.color = color;
        GUI.Label(rect, text, bigStyle);
        GUI.color = Color.white;
    }

    // ---------- Gravação (F4) ----------

    private void StartRecording()
    {
        recording = true;
        recElapsed = 0f;
        recMs.Clear();
        recAt.Clear();
        secRows.Clear();
        sumMain = sumRender = sumGpu = 0;
        nMain = nGpu = 0;
        secIndex = 0;
        ResetSecond();

        savedVSync = QualitySettings.vSyncCount;
        savedTargetFps = Application.targetFrameRate;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        csv = new StringBuilder(8192);
        csv.AppendLine("segundo,fps,pior_ms,cpu_ms,desenho_ms,gpu_ms,batches,setpass,triangulos_mil,particulas,efeitos,luzes,luzes_sombra,na_tela,aliens,orbes,fisica_acordada,blocos,gc_kb_quadro,x,y,z");
        nextCount = 0f; // conta já no primeiro segundo
        Debug.Log("[Medidor] Medição de desempenho começou (30 s). VSync desligado durante a medição.");
    }

    private void ResetSecond()
    {
        secFrames = 0;
        secTime = 0f;
        secWorst = 0f;
        secMain = secRender = secGpu = secBatches = secSetPass = secTris = secGc = 0;
        secMainN = secGpuN = secRecN = 0;
    }

    private void RecordFrame(float ms)
    {
        recElapsed += ms / 1000f;
        recMs.Add(ms);
        recAt.Add(recElapsed);

        secFrames++;
        secTime += ms / 1000f;
        secWorst = Mathf.Max(secWorst, ms);
        long b = Last(batchesRec), s = Last(setPassRec), t = Last(trisRec), g = Last(gcRec);
        if (b >= 0)
        {
            secBatches += b;
            secSetPass += System.Math.Max(0, s);
            secTris += System.Math.Max(0, t);
            secGc += System.Math.Max(0, g);
            secRecN++;
        }

        if (recElapsed >= secIndex + 1)
        {
            WriteSecond();
            secIndex++;
            ResetSecond();
        }

        if (recElapsed >= MeasureSeconds)
        {
            StopRecording(save: true);
        }
    }

    private void WriteSecond()
    {
        float fps = secTime > 0f ? secFrames / secTime : 0f;
        float cpu = secMainN > 0 ? (float)(secMain / secMainN) : -1f;
        float draw = secMainN > 0 ? (float)(secRender / secMainN) : -1f;
        float gpu = secGpuN > 0 ? (float)(secGpu / secGpuN) : -1f;
        float batches = secRecN > 0 ? (float)(secBatches / secRecN) : -1f;
        float setPass = secRecN > 0 ? (float)(secSetPass / secRecN) : -1f;
        float tris = secRecN > 0 ? (float)(secTris / secRecN / 1000.0) : -1f;
        float gc = secRecN > 0 ? (float)(secGc / secRecN / 1024.0) : -1f;
        Vector3 p = PlayerPosition();

        float[] row =
        {
            secIndex + 1, fps, secWorst, cpu, draw, gpu, batches, setPass, tris,
            particles, particleSystems, lights, shadowLights, visibleRenderers, aliens, orbs, awakeBodies, blocks, gc,
        };
        secRows.Add(row);

        StringBuilder line = new StringBuilder(256);
        for (int i = 0; i < row.Length; i++)
        {
            line.Append(row[i].ToString("0.##", Inv)).Append(',');
        }

        line.Append(p.x.ToString("0", Inv)).Append(',').Append(p.y.ToString("0", Inv)).Append(',').Append(p.z.ToString("0", Inv));
        csv.AppendLine(line.ToString());
    }

    // Posição "lógica" (a mesma do jogo salvo), que não muda quando o mundo recentra.
    private Vector3 PlayerPosition()
    {
        if (player == null)
        {
            PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
            player = controller != null ? controller.transform : null;
        }

        return player != null ? WorldOrigin.ToLogical(player.position) : Vector3.zero;
    }

    private void StopRecording(bool save)
    {
        recording = false;
        QualitySettings.vSyncCount = savedVSync;
        Application.targetFrameRate = savedTargetFps;

        if (!save)
        {
            return;
        }

        if (recMs.Count < 30 || recElapsed < 3f)
        {
            lastResult = "Medição curta demais (menos de 3 s): nada foi salvo.";
            lastResultUntil = Time.unscaledTime + 5f;
            return;
        }

        if (secFrames > 0)
        {
            WriteSecond(); // o pedaço final
        }

        string report = BuildReport(out string headline);
        string folder = OutputFolder();
        string stamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", Inv);
        string txtPath = Path.Combine(folder, $"fps_{stamp}.txt");
        string csvPath = Path.Combine(folder, $"fps_{stamp}.csv");

        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(txtPath, report, new UTF8Encoding(true));
            File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(true));
            Debug.Log($"[Medidor] {headline}\nSalvo em: {txtPath}\n\n{report}");
            lastResult = headline + "\nsalvo em " + txtPath;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Medidor] Não consegui salvar a medição: {e.Message}\n\n{report}");
            lastResult = headline + "\n(não salvou o arquivo; o resumo está no Console)";
        }

        lastResultUntil = Time.unscaledTime + 10f;
    }

    private static string OutputFolder()
    {
        // No Editor: C:\WorkSpace\ImNotAnAlien\Docs\Medicoes (Assets -> ImNotAnAlien -> Game -> raiz).
        string docs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Docs"));
        if (Application.isEditor && Directory.Exists(docs))
        {
            return Path.Combine(docs, "Medicoes");
        }

        return Path.Combine(Application.persistentDataPath, "Medicoes");
    }

    private string BuildReport(out string headline)
    {
        int n = recMs.Count;
        float[] sorted = recMs.ToArray();
        System.Array.Sort(sorted);
        double total = 0;
        for (int i = 0; i < n; i++) total += sorted[i];

        float avgFps = (float)(n / (total / 1000.0));
        float median = sorted[n / 2];
        float low1 = LowFps(sorted, 0.01f);
        float low01 = LowFps(sorted, 0.001f);
        float worst = sorted[n - 1];
        int over16 = 0, over33 = 0, over50 = 0;
        for (int i = 0; i < n; i++)
        {
            if (sorted[i] > 16.7f) over16++;
            if (sorted[i] > 33.3f) over33++;
            if (sorted[i] > 50f) over50++;
        }

        float cpu = nMain > 0 ? (float)(sumMain / nMain) : -1f;
        float draw = nMain > 0 ? (float)(sumRender / nMain) : -1f;
        float gpu = nGpu > 0 ? (float)(sumGpu / nGpu) : -1f;
        string limiter;
        if (cpu < 0f && gpu < 0f) limiter = "não deu para saber (o Unity não informou os tempos)";
        else if (gpu > Mathf.Max(cpu, draw) * 1.1f) limiter = "PLACA DE VÍDEO (GPU): o que pesa é desenhar (sombras, partículas, luzes, grama, água, resolução)";
        else if (cpu >= draw) limiter = "PROCESSADOR — scripts e física (aliens, trânsito, destruição, mundo carregando)";
        else limiter = "PROCESSADOR — preparar o desenho (muitos objetos separados na tela, muitas luzes)";

        string grade = avgFps >= 60f && low1 >= 45f ? "ÓTIMO"
            : avgFps >= 50f && low1 >= 30f ? "BOM"
            : avgFps >= 30f ? "ACEITÁVEL (dá para melhorar)"
            : "RUIM (precisa otimizar)";

        headline = $"Medição: FPS médio {avgFps:0} · 1% baixo {low1:0} · pior quadro {worst:0} ms · {grade}";

        StringBuilder r = new StringBuilder(4096);
        r.AppendLine("MEDIÇÃO DE DESEMPENHO — I'm Not an Alien");
        r.AppendLine($"Data: {System.DateTime.Now:dd/MM/yyyy HH:mm:ss}   ·   {(Application.isEditor ? "dentro do Editor (fica mais lento que a build)" : "build")}");
        r.AppendLine($"Duração: {recElapsed:0.0} s · {n} quadros (pausa do menu não conta)");
        r.AppendLine();
        r.AppendLine($"NOTA: {grade}");
        r.AppendLine($"FPS médio ............ {avgFps:0.0}");
        r.AppendLine($"FPS mediano .......... {1000f / median:0.0}");
        r.AppendLine($"1% baixo ............. {low1:0.0} FPS   (média dos 1% piores quadros: é o que se sente como 'travadinha')");
        r.AppendLine($"0,1% baixo ........... {low01:0.0} FPS");
        r.AppendLine($"Pior quadro .......... {worst:0.0} ms");
        r.AppendLine($"Quadros acima de 16,7 ms (abaixo de 60 FPS): {Pct(over16, n)}");
        r.AppendLine($"Quadros acima de 33,3 ms (abaixo de 30 FPS): {Pct(over33, n)}");
        r.AppendLine($"Engasgos (acima de 50 ms): {over50}");
        r.AppendLine();
        r.AppendLine($"Tempo médio — CPU scripts/física: {Ms(cpu)} · CPU preparando desenho: {Ms(draw)} · GPU: {Ms(gpu)}");
        r.AppendLine($"Quem segura o jogo: {limiter}");
        r.AppendLine();
        r.AppendLine("Média / máximo por segundo:");
        AppendStat(r, "batches", 6);
        AppendStat(r, "setpass", 7);
        AppendStat(r, "triângulos (mil)", 8);
        AppendStat(r, "partículas", 9);
        AppendStat(r, "efeitos de partícula ativos", 10);
        AppendStat(r, "luzes", 11);
        AppendStat(r, "luzes com sombra", 12);
        AppendStat(r, "objetos na tela", 13);
        AppendStat(r, "aliens", 14);
        AppendStat(r, "orbes de DNA", 15);
        AppendStat(r, "física acordada", 16);
        AppendStat(r, "blocos do mundo", 17);
        AppendStat(r, "lixo GC (KB/quadro)", 18);
        r.AppendLine();

        // Os 10 piores quadros e quando aconteceram.
        List<int> idx = new List<int>(n);
        for (int i = 0; i < n; i++) idx.Add(i);
        idx.Sort((a, b) => recMs[b].CompareTo(recMs[a]));
        r.AppendLine("10 piores quadros (segundo da medição → tempo):");
        for (int i = 0; i < Mathf.Min(10, n); i++)
        {
            r.AppendLine($"  {recAt[idx[i]]:0.0} s → {recMs[idx[i]]:0.0} ms");
        }

        r.AppendLine();
        r.AppendLine("Segundo a segundo (FPS · pior ms · partículas · luzes · aliens):");
        foreach (float[] row in secRows)
        {
            r.AppendLine($"  {row[0],3:0}s  {row[1],5:0} FPS  {row[2],6:0.0} ms  {row[9],5:0} part.  {row[11],3:0} luzes  {row[14],3:0} aliens");
        }

        r.AppendLine();
        r.AppendLine("Máquina e configuração:");
        r.AppendLine($"  Placa de vídeo: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB, {SystemInfo.graphicsDeviceType})");
        r.AppendLine($"  Processador: {SystemInfo.processorType} ({SystemInfo.processorCount} núcleos) · RAM {SystemInfo.systemMemorySize} MB");
        r.AppendLine($"  Resolução do jogo: {Screen.width}x{Screen.height} · tela {Screen.currentResolution.refreshRateRatio.value:0} Hz");
        r.AppendLine($"  Qualidade: {QualitySettings.names[QualitySettings.GetQualityLevel()]} · VSync normal: {(savedVSync > 0 ? "ligado" : "desligado")} (desligado durante a medição)");
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
        {
            r.AppendLine($"  URP: {urp.name} · escala de render {urp.renderScale:0.00} · distância da sombra {urp.shadowDistance:0} m · MSAA {urp.msaaSampleCount}x · HDR {(urp.supportsHDR ? "sim" : "não")}");
        }

        Vector3 p = PlayerPosition();
        r.AppendLine($"  Posição do jogador no fim: {p.x:0}, {p.y:0}, {p.z:0}");
        r.AppendLine($"  Custo da contagem de objetos do medidor: {lastCountCost:0.0} ms (tirado do quadro seguinte)");
        r.AppendLine();
        r.AppendLine("O que você estava fazendo durante a medição? (anote aqui embaixo antes de mandar)");
        r.AppendLine("> ");
        return r.ToString();
    }

    private void AppendStat(StringBuilder r, string label, int column)
    {
        double sum = 0;
        float max = float.MinValue;
        int count = 0;
        foreach (float[] row in secRows)
        {
            if (row[column] < 0f) continue;
            sum += row[column];
            max = Mathf.Max(max, row[column]);
            count++;
        }

        r.AppendLine(count == 0
            ? $"  {label}: —"
            : $"  {label}: {sum / count:0.#} / {max:0.#}");
    }

    private static float LowFps(float[] sortedMs, float fraction)
    {
        int n = sortedMs.Length;
        int k = Mathf.Max(1, Mathf.RoundToInt(n * fraction));
        double sum = 0;
        for (int i = n - k; i < n; i++) sum += sortedMs[i];
        return (float)(1000.0 / (sum / k));
    }

    private static string Pct(int part, int total) => $"{part} ({100f * part / Mathf.Max(1, total):0.0}%)";

    private static string Ms(float v) => v < 0f ? "—" : v.ToString("0.0", Inv) + " ms";
}
