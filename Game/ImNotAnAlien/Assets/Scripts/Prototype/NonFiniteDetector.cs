using System.Text;
using UnityEngine;

// Caça-NaN (17/11): quando o Unity reclama "Assertion failed on expression: 'IsFinite(...)'" ou
// "Invalid AABB", algum objeto da tela ficou com posição inválida (NaN, infinito ou longe demais),
// mas o aviso não diz qual. Este script percebe o aviso, procura na cena quem está com problema
// e escreve no Console uma mensagem amarela começando com "[Caça-NaN]", com o caminho do objeto.
// Só funciona no Editor e em build de desenvolvimento. Criado sozinho: não precisa pôr na cena.
// Pode apagar depois que o problema for achado e consertado.
public sealed class NonFiniteDetector : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private const float TooFar = 1e6f;      // 1000 km: nada do jogo deveria estar tão longe
    private const float ScanCooldown = 3f;  // segundos entre uma busca e outra
    private const int MaxReports = 5;       // depois disso para de procurar (não lota o Console)
    private const int MaxLines = 12;

    private static volatile bool triggered;

    private float nextScan;
    private int reports;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        triggered = false;
        GameObject go = new GameObject("Caca_NaN");
        DontDestroyOnLoad(go);
        go.AddComponent<NonFiniteDetector>();
    }

    private void OnEnable()
    {
        Application.logMessageReceivedThreaded += OnLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceivedThreaded -= OnLog;
    }

    // Pode vir de outra thread (renderização): só marca, a busca é feita no Update.
    // Atenção: as mensagens deste script não podem conter as palavras procuradas aqui.
    private static void OnLog(string message, string stack, LogType type)
    {
        if (triggered || message == null)
        {
            return;
        }

        if (message.Contains("IsFinite") || message.Contains("Invalid AABB") || message.Contains("aabb.Is"))
        {
            triggered = true;
        }
    }

    private void LateUpdate()
    {
        if (!triggered || reports >= MaxReports || Time.unscaledTime < nextScan)
        {
            return;
        }

        nextScan = Time.unscaledTime + ScanCooldown;
        reports++;
        Scan();
        triggered = false;
    }

    private void Scan()
    {
        StringBuilder found = new StringBuilder();
        int lines = 0;

        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (lines >= MaxLines) break;
            string problem = null;

            if (!Ok(r.transform.position)) problem = $"posição {r.transform.position}";
            else if (!Ok(r.transform.lossyScale)) problem = $"escala {r.transform.lossyScale}";
            else
            {
                Bounds b = r.bounds;
                if (!Ok(b.center) || !Ok(b.extents)) problem = $"caixa de desenho centro {b.center} tamanho {b.extents}";
            }

            if (problem == null && r is TrailRenderer trail) problem = CheckTrail(trail);
            if (problem == null && r is LineRenderer line) problem = CheckLine(line);

            if (problem != null)
            {
                found.AppendLine($"  • {Path(r.transform)} ({r.GetType().Name}): {problem}");
                lines++;
            }
        }

        foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (lines >= MaxLines) break;
            if (!Ok(light.transform.position) || !float.IsFinite(light.range))
            {
                found.AppendLine($"  • {Path(light.transform)} (Light): posição {light.transform.position}, alcance {light.range}");
                lines++;
            }
        }

        foreach (Camera cam in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (lines >= MaxLines) break;
            Quaternion q = cam.transform.rotation;
            if (!Ok(cam.transform.position) || !float.IsFinite(q.x + q.y + q.z + q.w))
            {
                found.AppendLine($"  • {Path(cam.transform)} (Camera): posição {cam.transform.position}, rotação {q}");
                lines++;
            }
        }

        foreach (Rigidbody body in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (lines >= MaxLines) break;
            if (!Ok(body.position) || !Ok(body.linearVelocity))
            {
                found.AppendLine($"  • {Path(body.transform)} (Rigidbody): posição {body.position}, velocidade {body.linearVelocity}");
                lines++;
            }
        }

        string context = $"(tempo de jogo {Time.time:0.0}s, timeScale {Time.timeScale:0.00}, quadro {Time.frameCount})";
        if (found.Length > 0)
        {
            Debug.LogWarning($"[Caça-NaN] Achei objeto(s) com posição inválida {context}:\n{found}");
        }
        else
        {
            Debug.LogWarning($"[Caça-NaN] O Unity reclamou de posição inválida, mas na busca não achei mais nada " +
                             $"{context}. O problema durou só um quadro (objeto já apagado ou corrigido).");
        }

        if (reports >= MaxReports)
        {
            Debug.LogWarning("[Caça-NaN] Já avisei 5 vezes: parei de procurar até o próximo Play.");
        }
    }

    private string CheckTrail(TrailRenderer trail)
    {
        int count = trail.positionCount;
        if (count <= 0) return null;
        Vector3[] buffer = new Vector3[count];
        trail.GetPositions(buffer);
        for (int i = 0; i < count; i++)
        {
            if (!Ok(buffer[i])) return $"ponto {i} do rastro {buffer[i]}";
        }

        return null;
    }

    private string CheckLine(LineRenderer line)
    {
        int count = line.positionCount;
        if (count <= 0) return null;
        Vector3[] buffer = new Vector3[count];
        line.GetPositions(buffer);
        for (int i = 0; i < count; i++)
        {
            if (!Ok(buffer[i])) return $"ponto {i} da linha {buffer[i]}";
        }

        return null;
    }

    private static bool Ok(Vector3 v) =>
        float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z) &&
        Mathf.Abs(v.x) < TooFar && Mathf.Abs(v.y) < TooFar && Mathf.Abs(v.z) < TooFar;

    private static string Path(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent)
        {
            path = p.name + "/" + path;
        }

        return path;
    }
#endif
}
