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

    private const int SampleRate = 44100;
    private const int VoiceCount = 10;

    private ParticleSystem debris;
    private AudioSource[] voices;
    private int nextVoice;
    private float hitStopCooldownUntil;
    private bool inHitStop;

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
        if (inHitStop || Time.unscaledTime < hitStopCooldownUntil)
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
        Time.timeScale = 1f;
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
