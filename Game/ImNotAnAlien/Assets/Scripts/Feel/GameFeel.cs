using System.Collections;
using UnityEngine;

// "Game feel" da Versão 0.1: estilhaços, sons provisórios, tremida de câmera e pausa de impacto.
// Fica num objeto "GameFeel" na cena (criado por ImNotAnAlien > Preparar Efeitos).
// Os outros scripts chamam os métodos estáticos (GameFeel.Impact, GameFeel.Lift, ...).
// Se não houver GameFeel na cena, as chamadas simplesmente não fazem nada.
//
// Os sons são gerados por código (sintetizados) só para o protótipo. Mais tarde,
// troque pelos sons de verdade preenchendo os campos "Sons (opcional)".
[DisallowMultipleComponent]
public sealed class GameFeel : MonoBehaviour
{
    public static GameFeel Instance { get; private set; }

    [Header("Estilhaços")]
    [Tooltip("Material de partícula (URP Particles/Unlit).")]
    [SerializeField] private Material particleMaterial;

    [Header("Impacto")]
    [Tooltip("Batidas mais lentas que isso (m/s) não geram efeito.")]
    [SerializeField, Min(0f)] private float minImpactSpeed = 5f;
    [Tooltip("Velocidade (m/s) considerada impacto máximo.")]
    [SerializeField, Min(1f)] private float maxImpactSpeed = 30f;
    [Tooltip("Impactos acima disso (m/s) fazem a pausa de impacto.")]
    [SerializeField, Min(0f)] private float hitStopSpeed = 18f;
    [SerializeField, Min(0f)] private float hitStopDuration = 0.06f;
    [Tooltip("Impactos mais longe que isso (m) da câmera não fazem a tela tremer.")]
    [SerializeField, Min(1f)] private float shakeRange = 30f;

    [Header("Volume")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 0.8f;

    [Header("Sons (opcional, substituem os sintetizados)")]
    [SerializeField] private AudioClip impactClip;
    [SerializeField] private AudioClip liftClip;
    [SerializeField] private AudioClip throwClip;
    [SerializeField] private AudioClip alienScreamClip;
    [SerializeField] private AudioClip dnaPickupClip;
    [SerializeField] private AudioClip sectorClearedClip;
    [SerializeField] private AudioClip policeChargeClip;
    [SerializeField] private AudioClip policeShotClip;
    [SerializeField] private AudioClip playerHitClip;
    [SerializeField] private AudioClip devourClip;
    [SerializeField] private AudioClip shockwaveClip;
    [SerializeField] private AudioClip evolutionClip;
    [SerializeField] private AudioClip collapseClip;
    [SerializeField] private AudioClip splashClip;
    [SerializeField] private AudioClip sirenClip;
    [SerializeField] private AudioClip hornClip;
    [SerializeField] private AudioClip explosionClip;

    private const int SampleRate = 44100;
    private const int VoiceCount = 10;

    private ParticleSystem debris;
    private AudioSource[] voices;
    private int nextVoice;
    private float hitStopCooldownUntil;
    private bool inHitStop;
    private int pickupCombo;
    private float lastPickupTime = -10f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Há mais de um GameFeel na cena; este será ignorado.", this);
            enabled = false;
            return;
        }

        Instance = this;
        CreateDebrisSystem();
        CreateVoices();

        // (Comparação com == null de propósito: campos vazios da Unity não funcionam com ??=.)
        if (impactClip == null) impactClip = SynthImpact();
        if (liftClip == null) liftClip = SynthLift();
        if (throwClip == null) throwClip = SynthThrow();
        if (alienScreamClip == null) alienScreamClip = SynthScream();
        if (dnaPickupClip == null) dnaPickupClip = SynthPickup();
        if (sectorClearedClip == null) sectorClearedClip = SynthFanfare();
        if (policeChargeClip == null) policeChargeClip = SynthCharge();
        if (policeShotClip == null) policeShotClip = SynthShot();
        if (playerHitClip == null) playerHitClip = SynthPlayerHit();
        if (devourClip == null) devourClip = SynthDevour();
        if (shockwaveClip == null) shockwaveClip = SynthShockwave();
        if (evolutionClip == null) evolutionClip = SynthEvolution();
        if (collapseClip == null) collapseClip = SynthCollapse();
        if (splashClip == null) splashClip = SynthSplash();
        if (sirenClip == null) sirenClip = SynthSiren();
        if (hornClip == null) hornClip = SynthHorn();
        if (explosionClip == null) explosionClip = SynthExplosion();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            Time.timeScale = 1f;
        }
    }

    // ---------- API usada pelos outros scripts ----------

    // Batida de um objeto/alienígena. "speed" = velocidade relativa da colisão.
    public static void Impact(Vector3 position, float speed, Color color)
    {
        if (Instance == null || speed < Instance.minImpactSpeed)
        {
            return;
        }

        Instance.DoImpact(position, speed, color);
    }

    public static void Lift(Vector3 position)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.EmitDebris(position, 14, new Color(0.25f, 0.95f, 1f), 0.6f);
        Instance.Play(Instance.liftClip, position, 0.5f, 0.95f, 1.1f);
    }

    public static void Throw(Vector3 position)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.Play(Instance.throwClip, position, 0.6f, 0.9f, 1.15f);
        CameraShake.AddTrauma(0.12f);
    }

    public static void AlienScream(Vector3 position)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.Play(Instance.alienScreamClip, position, 0.45f, 0.85f, 1.3f);
    }

    public static void AlienDefeated(Vector3 position, Color color)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.EmitDebris(position + Vector3.up, 40, color, 1.6f);
        Instance.EmitDebris(position + Vector3.up, 15, Color.white, 1.2f);
        Instance.Play(Instance.alienScreamClip, position, 0.7f, 0.55f, 0.65f); // grito mais grave
        Instance.ShakeFrom(position, 0.5f);
        Instance.HitStop(Instance.hitStopDuration * 2f);
    }

    // Orbe de DNA absorvido. Coletas em sequência sobem o tom (fica viciante).
    public static void DnaAbsorbed(Vector3 position, Color color)
    {
        if (Instance == null)
        {
            return;
        }

        GameFeel feel = Instance;
        feel.pickupCombo = Time.unscaledTime - feel.lastPickupTime < 0.4f ? feel.pickupCombo + 1 : 0;
        feel.lastPickupTime = Time.unscaledTime;

        float pitch = Mathf.Min(1f + feel.pickupCombo * 0.06f, 2f);
        feel.Play(feel.dnaPickupClip, position, 0.5f, pitch, pitch);
        feel.EmitDebris(position, 5, color, 0.4f);
    }

    public static void SectorCleared()
    {
        if (Instance == null)
        {
            return;
        }

        Camera cam = Camera.main;
        Vector3 position = cam != null ? cam.transform.position : Vector3.zero;
        Instance.Play(Instance.sectorClearedClip, position, 0.9f, 1f, 1f);
    }

    public static void PoliceCharge(Vector3 position)
    {
        if (Instance != null)
        {
            Instance.Play(Instance.policeChargeClip, position, 0.35f, 0.95f, 1.05f);
        }
    }

    public static void PoliceShot(Vector3 position)
    {
        if (Instance != null)
        {
            Instance.Play(Instance.policeShotClip, position, 0.8f, 0.92f, 1.08f);
        }
    }

    public static void PlayerHit(Vector3 position)
    {
        if (Instance != null)
        {
            Instance.Play(Instance.playerHitClip, position, 0.7f, 0.9f, 1.05f);
        }
    }

    // "NHAC!": devorar um alienígena.
    public static void Devour(Vector3 position, Color color)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.Play(Instance.devourClip, position, 1f, 0.9f, 1.05f);
        Instance.EmitDebris(position, 35, color, 1.3f);
        Instance.EmitDebris(position, 20, new Color(0.3f, 1f, 0.6f), 1f); // "respingos" de DNA
        CameraShake.AddTrauma(0.4f);
        Instance.HitStop(Instance.hitStopDuration * 1.5f);
    }

    // Onda de Impacto: anel de estilhaços no chão, "BUM" grave e tela tremendo.
    public static void Shockwave(Vector3 center, float radius, Color color)
    {
        if (Instance == null)
        {
            return;
        }

        GameFeel feel = Instance;
        if (feel.debris == null)
        {
            return;
        }

        int count = Mathf.RoundToInt(radius * 6f);
        for (int i = 0; i < count; i++)
        {
            float angle = i / (float)count * Mathf.PI * 2f;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                position = center + direction * 0.8f + Vector3.up * 0.3f,
                applyShapeToPosition = false,
                startColor = Color.Lerp(color, Color.white, Random.Range(0f, 0.4f)),
                velocity = direction * radius * Random.Range(1.6f, 2.2f) + Vector3.up * Random.Range(1f, 3f),
                startLifetime = 0.55f,
            };
            feel.debris.Emit(emit, 1);
        }

        feel.Play(feel.shockwaveClip, center, 1f, 0.95f, 1.05f);
        CameraShake.AddTrauma(0.55f);
    }

    // Estrutura atingida (sem cair): poeira e lascas.
    public static void StructureHit(Vector3 point, Color color)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.EmitDebris(point, 14, color, 1f);
        Instance.EmitDebris(point, 10, new Color(0.75f, 0.72f, 0.68f), 0.7f); // poeira
        Instance.Play(Instance.impactClip, point, 0.9f, 0.6f, 0.7f);          // baque mais grave
    }

    // Estrutura desmoronando: estrondo, nuvem de poeira e tela tremendo.
    public static void StructureCollapse(Vector3 center, Color color, float size)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.EmitDebris(center, 60, color, 1.8f);
        Instance.EmitDebris(center + Vector3.up, 50, new Color(0.75f, 0.72f, 0.68f), 1.3f);
        Instance.Play(Instance.collapseClip, center, 1f, 0.9f, 1.05f);
        Instance.ShakeFrom(center, Mathf.Clamp(size / 15f, 0.4f, 0.9f));
        Instance.HitStop(Instance.hitStopDuration);
    }

    // Algo caiu na água: respingos azuis e "tchibum".
    public static void Splash(Vector3 position, float strength)
    {
        if (Instance == null)
        {
            return;
        }

        float amount = Mathf.Clamp01(strength);
        Instance.EmitDebris(position, Mathf.RoundToInt(Mathf.Lerp(8f, 30f, amount)), new Color(0.55f, 0.85f, 1f),
            Mathf.Lerp(0.6f, 1.4f, amount));
        Instance.Play(Instance.splashClip, position, Mathf.Lerp(0.35f, 0.9f, amount), 0.85f, 1.15f);
    }

    // Subiu uma estrela de procurado: sirene curta.
    public static void WantedUp()
    {
        if (Instance == null)
        {
            return;
        }

        Camera cam = Camera.main;
        Instance.Play(Instance.sirenClip, cam != null ? cam.transform.position : Vector3.zero, 0.55f, 1f, 1f);
    }

    // Buzina de carro do trânsito.
    public static void Horn(Vector3 position)
    {
        if (Instance != null)
        {
            Instance.Play(Instance.hornClip, position, 0.6f, 0.9f, 1.15f);
        }
    }

    // ---------- Fogo, fumaça e explosão (carros) ----------

    // Explosão: bola de fogo em cubinhos, fumaça escura, clarão de luz, estrondo e tela tremendo.
    public static void Explosion(Vector3 position, float radius)
    {
        if (Instance == null)
        {
            return;
        }

        GameFeel feel = Instance;
        feel.EmitDebris(position, 45, new Color(1f, 0.45f, 0.1f), 2.2f);   // fogo laranja
        feel.EmitDebris(position, 30, new Color(1f, 0.85f, 0.3f), 1.6f);   // miolo amarelo
        feel.EmitDebris(position + Vector3.up, 45, new Color(0.18f, 0.17f, 0.17f), 1.1f); // fumaça
        feel.Play(feel.explosionClip, position, 1f, 0.9f, 1.05f);
        feel.ShakeFrom(position, Mathf.Clamp(radius / 9f, 0.5f, 1f));
        feel.HitStop(feel.hitStopDuration * 1.6f);
        feel.StartCoroutine(feel.FlashRoutine(position, radius));
    }

    private System.Collections.IEnumerator FlashRoutine(Vector3 position, float radius)
    {
        GameObject flashObject = new GameObject("Clarao_Explosao");
        flashObject.transform.position = position + Vector3.up;
        Light flash = flashObject.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.6f, 0.25f);
        flash.range = radius * 2.5f;
        flash.shadows = LightShadows.None;

        const float duration = 0.45f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            flash.intensity = Mathf.Lerp(12f, 0f, t / duration);
            yield return null;
        }

        Destroy(flashObject);
    }

    // Fogo contínuo (cubinhos laranja subindo). Preso ao objeto: acompanha o carro.
    public static ParticleSystem CreateFire(Transform parent, Vector3 localOffset)
    {
        return Instance != null
            ? Instance.CreateLoop("Fogo", parent, localOffset, new Color(1f, 0.4f, 0.08f), new Color(1f, 0.85f, 0.3f),
                30f, new Vector2(1.5f, 3f), new Vector2(0.15f, 0.45f), 0.7f)
            : null;
    }

    // Fumaça contínua (cubinhos cinza escuro subindo devagar).
    public static ParticleSystem CreateSmoke(Transform parent, Vector3 localOffset)
    {
        return Instance != null
            ? Instance.CreateLoop("Fumaca", parent, localOffset, new Color(0.15f, 0.15f, 0.15f), new Color(0.4f, 0.4f, 0.42f),
                10f, new Vector2(1f, 2f), new Vector2(0.3f, 0.7f), 2f)
            : null;
    }

    private ParticleSystem CreateLoop(string name, Transform parent, Vector3 localOffset, Color colorA, Color colorB, float rate,
        Vector2 speed, Vector2 size, float lifetime)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localOffset;
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.15f; // sobe
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = rate;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.5f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // para cima

        ParticleSystem.SizeOverLifetimeModule shrink = system.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        ParticleSystemRenderer particleRenderer = go.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        particleRenderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        if (particleMaterial != null)
        {
            particleRenderer.sharedMaterial = particleMaterial;
        }

        system.Play();
        return system;
    }

    public static void EvolutionBought()
    {
        if (Instance == null)
        {
            return;
        }

        Camera cam = Camera.main;
        Instance.Play(Instance.evolutionClip, cam != null ? cam.transform.position : Vector3.zero, 0.8f, 1f, 1f);
    }

    // ---------- Implementação ----------

    private void DoImpact(Vector3 position, float speed, Color color)
    {
        float strength = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, speed); // 0 a 1

        EmitDebris(position, Mathf.RoundToInt(Mathf.Lerp(6f, 30f, strength)), color, Mathf.Lerp(0.6f, 1.6f, strength));
        Play(impactClip, position, Mathf.Lerp(0.25f, 1f, strength), 0.85f, 1.15f);
        ShakeFrom(position, Mathf.Lerp(0.08f, 0.45f, strength));

        if (speed >= hitStopSpeed)
        {
            HitStop(hitStopDuration);
        }
    }

    private void ShakeFrom(Vector3 position, float trauma)
    {
        Camera cam = Camera.main;
        float distance = cam != null ? Vector3.Distance(cam.transform.position, position) : 0f;
        float falloff = Mathf.Clamp01(1f - distance / shakeRange);
        CameraShake.AddTrauma(trauma * falloff);
    }

    private void HitStop(float duration)
    {
        if (inHitStop || GamePause.IsPaused || Time.unscaledTime < hitStopCooldownUntil)
        {
            return;
        }

        StartCoroutine(HitStopRoutine(duration));
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        inHitStop = true;
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = GamePause.IsPaused ? 0f : 1f; // se o menu abriu no meio, continua pausado
        inHitStop = false;
        hitStopCooldownUntil = Time.unscaledTime + 0.25f; // evita várias pausas seguidas
    }

    // ---------- Estilhaços ----------

    private void CreateDebrisSystem()
    {
        GameObject go = new GameObject("Estilhacos");
        go.transform.SetParent(transform, false);
        debris = go.AddComponent<ParticleSystem>();
        debris.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = debris.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.25f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 1.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 600;

        ParticleSystem.EmissionModule emission = debris.emission;
        emission.enabled = false; // emitimos manualmente

        ParticleSystem.ShapeModule shape = debris.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.25f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // "meia esfera" para cima

        ParticleSystem.RotationOverLifetimeModule spin = debris.rotationOverLifetime;
        spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
        spin.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
        spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

        ParticleSystem.SizeOverLifetimeModule shrink = debris.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        // Pequenos cubos em vez de quadradinhos: combina com o visual de blocos.
        ParticleSystemRenderer particleRenderer = go.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        particleRenderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        if (particleMaterial != null)
        {
            particleRenderer.sharedMaterial = particleMaterial;
        }
        else
        {
            Debug.LogWarning("GameFeel sem material de partícula: os estilhaços podem aparecer cor-de-rosa. Rode ImNotAnAlien > Preparar Efeitos.", this);
        }
    }

    private void EmitDebris(Vector3 position, int count, Color color, float speedScale)
    {
        if (debris == null || count <= 0)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            // Variação leve de tom para não ficar tudo igual.
            Color tint = Color.Lerp(color, Color.white, Random.Range(0f, 0.35f));
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                position = position,
                applyShapeToPosition = true,
                startColor = tint,
                velocity = (Random.onUnitSphere + Vector3.up * 1.2f).normalized * Random.Range(2f, 7f) * speedScale,
            };
            debris.Emit(emit, 1);
        }
    }

    // ---------- Áudio ----------

    private void CreateVoices()
    {
        voices = new AudioSource[VoiceCount];
        for (int i = 0; i < VoiceCount; i++)
        {
            GameObject go = new GameObject("Som_" + i);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0.7f; // meio 3D: dá direção sem sumir de longe
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 5f;
            source.maxDistance = 60f;
            voices[i] = source;
        }
    }

    private void Play(AudioClip clip, Vector3 position, float volume, float minPitch, float maxPitch)
    {
        if (clip == null || voices == null)
        {
            return;
        }

        AudioSource source = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        source.transform.position = position;
        source.pitch = Random.Range(minPitch, maxPitch);
        source.volume = volume * masterVolume;
        source.clip = clip;
        source.Play();
    }

    // ---------- Sons sintetizados (provisórios) ----------

    // Baque grave: seno caindo de 140 Hz para 45 Hz + um "estalo" de ruído.
    private static AudioClip SynthImpact()
    {
        return Synth("Impacto", 0.3f, (t, n) =>
        {
            float freq = Mathf.Lerp(140f, 45f, t / 0.3f);
            float body = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 16f);
            float crack = n * Mathf.Exp(-t * 45f) * 0.5f;
            return (body + crack) * 0.9f;
        });
    }

    // Zumbido subindo, com um leve vibrato: "energia" da telecinese.
    private static AudioClip SynthLift()
    {
        return Synth("Levantar", 0.4f, (t, n) =>
        {
            float freq = Mathf.Lerp(180f, 520f, t / 0.4f) + Mathf.Sin(t * 2f * Mathf.PI * 18f) * 12f;
            float envelope = Mathf.Min(t / 0.05f, 1f) * Mathf.Exp(-t * 5f);
            return (Mathf.Sin(2f * Mathf.PI * freq * t) * 0.6f + n * 0.08f) * envelope;
        });
    }

    // "Fuuu": ruído filtrado que sobe e desce.
    private static AudioClip SynthThrow()
    {
        float filtered = 0f;
        return Synth("Arremesso", 0.35f, (t, n) =>
        {
            float sweep = Mathf.Sin(Mathf.PI * t / 0.35f); // 0 -> 1 -> 0
            float cutoff = Mathf.Lerp(0.02f, 0.35f, sweep);
            filtered += (n - filtered) * cutoff; // filtro passa-baixa simples
            return filtered * sweep * 1.6f;
        });
    }

    // Gritinho de desenho animado: tom agudo subindo com vibrato rápido.
    private static AudioClip SynthScream()
    {
        return Synth("Grito_Alien", 0.3f, (t, n) =>
        {
            float freq = Mathf.Lerp(650f, 1350f, t / 0.3f) + Mathf.Sin(t * 2f * Mathf.PI * 32f) * 90f;
            float envelope = Mathf.Min(t / 0.02f, 1f) * Mathf.Clamp01((0.3f - t) / 0.08f);
            float wave = Mathf.Sin(2f * Mathf.PI * freq * t);
            wave = Mathf.Sign(wave) * Mathf.Pow(Mathf.Abs(wave), 0.6f); // um pouco "rasgado"
            return wave * envelope * 0.45f;
        });
    }

    // "Plim": duas notas rápidas subindo.
    private static AudioClip SynthPickup()
    {
        return Synth("DNA", 0.16f, (t, n) =>
        {
            float freq = t < 0.06f ? 988f : 1319f;
            float envelope = Mathf.Exp(-(t < 0.06f ? t : t - 0.06f) * 30f);
            return Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.5f;
        });
    }

    // Fanfarra curtinha de "setor limpo": arpejo Dó-Mi-Sol-Dó.
    private static AudioClip SynthFanfare()
    {
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        const float noteLength = 0.13f;
        return Synth("Setor_Limpo", noteLength * 3f + 0.6f, (t, n) =>
        {
            int index = Mathf.Min((int)(t / noteLength), notes.Length - 1);
            float local = t - index * noteLength;
            float decay = index == notes.Length - 1 ? 3f : 18f; // a última nota sustenta
            float wave = Mathf.Sin(2f * Mathf.PI * notes[index] * t);
            wave += 0.3f * Mathf.Sin(2f * Mathf.PI * notes[index] * 2f * t); // um pouco de brilho
            return wave * Mathf.Exp(-local * decay) * 0.4f;
        });
    }

    // Aviso do policial: zumbido subindo (dá tempo de reagir).
    private static AudioClip SynthCharge()
    {
        return Synth("Policial_Carga", 0.45f, (t, n) =>
        {
            float freq = Mathf.Lerp(300f, 950f, t / 0.45f);
            float envelope = Mathf.Min(t / 0.05f, 1f) * 0.7f;
            return Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.3f;
        });
    }

    // "PAH!": estampido de tiro. Estalo seco de ruído + "soco" grave + cauda curta abafada.
    private static AudioClip SynthShot()
    {
        float muffled = 0f;
        return Synth("Policial_Tiro", 0.4f, (t, n) =>
        {
            float crack = n * Mathf.Exp(-t * 60f);                                  // estalo inicial
            float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(150f, 55f, Mathf.Clamp01(t / 0.08f)) * t)
                          * Mathf.Exp(-t * 28f);                                    // impacto grave
            muffled += (n - muffled) * 0.08f;                                       // ruído abafado (eco)
            float tail = muffled * Mathf.Exp(-t * 9f) * 2.2f;
            float mix = crack * 0.9f + thump * 0.8f + tail * 0.5f;
            return Mathf.Clamp(mix * 1.4f, -1f, 1f);                                // satura um pouco: mais "seco"
        });
    }

    // Jogador atingido: zumbido grave e áspero.
    private static AudioClip SynthPlayerHit()
    {
        return Synth("Jogador_Dano", 0.28f, (t, n) =>
        {
            float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 95f * t)) * 0.5f;
            return (buzz + n * 0.6f) * Mathf.Exp(-t * 11f) * 0.6f;
        });
    }

    // "NHAC... glup": mordida (ruído curto) + engolida (tom grave descendo com tremido).
    private static AudioClip SynthDevour()
    {
        return Synth("Devorar", 0.6f, (t, n) =>
        {
            float bite = n * Mathf.Exp(-t * 30f) * 0.9f;
            float gulpT = Mathf.Max(0f, t - 0.12f);
            float freq = Mathf.Lerp(320f, 70f, gulpT / 0.48f) + Mathf.Sin(gulpT * 2f * Mathf.PI * 14f) * 25f;
            float gulp = Mathf.Sin(2f * Mathf.PI * freq * gulpT) * Mathf.Min(gulpT / 0.03f, 1f) * Mathf.Exp(-gulpT * 4f);
            return (bite + gulp * 0.8f) * 0.8f;
        });
    }

    // "BUM": grave profundo descendo + rajada de ar.
    private static AudioClip SynthShockwave()
    {
        float air = 0f;
        return Synth("Onda_Impacto", 0.9f, (t, n) =>
        {
            float freq = Mathf.Lerp(110f, 32f, Mathf.Clamp01(t / 0.5f));
            float boom = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 4.5f);
            air += (n - air) * 0.12f;
            float whoosh = air * Mathf.Exp(-t * 6f) * 2.2f;
            return Mathf.Clamp((boom * 1.1f + whoosh * 0.6f) * 1.2f, -1f, 1f);
        });
    }

    // Evolução comprada: arpejo rápido e brilhante subindo.
    private static AudioClip SynthEvolution()
    {
        float[] notes = { 659.25f, 830.61f, 987.77f, 1318.5f, 1661.2f };
        const float noteLength = 0.07f;
        return Synth("Evolucao", noteLength * notes.Length + 0.45f, (t, n) =>
        {
            int index = Mathf.Min((int)(t / noteLength), notes.Length - 1);
            float local = t - index * noteLength;
            float decay = index == notes.Length - 1 ? 4f : 20f;
            float wave = Mathf.Sin(2f * Mathf.PI * notes[index] * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * notes[index] * 1.5f * t);
            return wave * Mathf.Exp(-local * decay) * 0.35f;
        });
    }

    // Desmoronamento: ronco grave de ruído + estalos de coisas quebrando.
    private static AudioClip SynthCollapse()
    {
        float rumble = 0f;
        int crackSamples = 0;
        System.Random crackRandom = new System.Random(7);
        return Synth("Desmoronamento", 1.8f, (t, n) =>
        {
            rumble += (n - rumble) * 0.025f;                                   // ruído bem abafado = ronco
            float body = rumble * 6f * Mathf.Exp(-t * 1.6f);
            float boom = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(70f, 30f, Mathf.Clamp01(t)) * t) * Mathf.Exp(-t * 3f);

            // Estalos: rajadas curtas de ruído (~10 ms), cada vez mais raras.
            if (crackSamples <= 0 && crackRandom.NextDouble() < 0.0006 * Mathf.Exp(-t * 1.5f))
            {
                crackSamples = 450;
            }

            float crack = 0f;
            if (crackSamples > 0)
            {
                crack = n * 0.9f * (crackSamples / 450f);
                crackSamples--;
            }

            return Mathf.Clamp(body * 0.8f + boom * 0.9f + crack, -1f, 1f);
        });
    }

    // "Tchibum": rajada de ruído brilhante que vai abafando, com um "glub" grave.
    private static AudioClip SynthSplash()
    {
        float filtered = 0f;
        return Synth("Splash", 0.7f, (t, n) =>
        {
            float cutoff = Mathf.Lerp(0.6f, 0.05f, Mathf.Clamp01(t / 0.5f));
            filtered += (n - filtered) * cutoff;
            float spray = filtered * Mathf.Exp(-t * 6f) * 1.4f;
            float glub = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(180f, 90f, Mathf.Clamp01(t / 0.2f)) * t) * Mathf.Exp(-t * 14f) * 0.5f;
            return Mathf.Clamp(spray + glub, -1f, 1f);
        });
    }

    // Sirene "uí-ó uí-ó": dois tons alternando, com subida suave entre eles.
    private static AudioClip SynthSiren()
    {
        return Synth("Sirene", 0.9f, (t, n) =>
        {
            float phase = Mathf.Repeat(t / 0.45f, 1f);
            float freq = phase < 0.5f ? Mathf.Lerp(720f, 960f, phase * 2f) : Mathf.Lerp(960f, 720f, (phase - 0.5f) * 2f);
            float wave = Mathf.Sin(2f * Mathf.PI * freq * t);
            wave = Mathf.Sign(wave) * Mathf.Pow(Mathf.Abs(wave), 0.5f); // um pouco "metálico"
            float envelope = Mathf.Min(t / 0.03f, 1f) * Mathf.Clamp01((0.9f - t) / 0.15f);
            return wave * envelope * 0.3f;
        });
    }

    // "Biii-biiip": buzina com dois toques, timbre de corneta (quadrada suavizada, dois tons juntos).
    private static AudioClip SynthHorn()
    {
        return Synth("Buzina", 0.55f, (t, n) =>
        {
            bool on = t < 0.16f || t > 0.24f && t < 0.52f;
            if (!on)
            {
                return 0f;
            }

            float a = Mathf.Sin(2f * Mathf.PI * 415f * t);
            float b = Mathf.Sin(2f * Mathf.PI * 523f * t);
            float wave = Mathf.Clamp((a + b) * 1.6f, -1f, 1f);
            return wave * 0.28f;
        });
    }

    // "KABUM": estalo forte + ronco grave longo + chiado abafado da bola de fogo.
    private static AudioClip SynthExplosion()
    {
        float rumble = 0f;
        float roar = 0f;
        return Synth("Explosao", 1.6f, (t, n) =>
        {
            float crack = n * Mathf.Exp(-t * 35f);
            rumble += (n - rumble) * 0.02f;
            roar += (n - roar) * 0.15f;
            float boom = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(65f, 28f, Mathf.Clamp01(t / 0.8f)) * t) * Mathf.Exp(-t * 2.6f);
            float mix = crack * 0.9f + boom * 1.1f + rumble * 7f * Mathf.Exp(-t * 1.8f) + roar * 1.5f * Mathf.Exp(-t * 4f);
            return Mathf.Clamp(mix * 1.1f, -1f, 1f);
        });
    }

    private static AudioClip Synth(string name, float duration, System.Func<float, float, float> sample)
    {
        int length = Mathf.CeilToInt(duration * SampleRate);
        float[] data = new float[length];
        System.Random random = new System.Random(name.GetHashCode());
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            data[i] = Mathf.Clamp(sample(t, noise), -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(name, length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
