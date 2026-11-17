using System.Collections.Generic;
using UnityEngine;

// Ajudante dos efeitos elementais (Missão 21): cuida do que precisa de tempo — clarões de luz que
// apagam — e dos SONS dos poderes (sintetizados por código, como os do GameFeel; nenhum arquivo de áudio).
// Criado sozinho na primeira vez que um poder é usado.
[DisallowMultipleComponent]
public sealed class ElementalFxRunner : MonoBehaviour
{
    public enum Sound
    {
        FireShot, FireBoom, IceShot, IceHit, Freeze, Shatter, BoltZap, BoltThunder, Switch, Deny, Sizzle,
    }

    private const int SampleRate = 22050;
    private const int VoiceCount = 8;
    private const int MaxFlashes = 6;

    private struct FlashLight
    {
        public Light light;
        public float start;
        public float duration;
        public float intensity;
    }

    private static ElementalFxRunner instance;

    private readonly List<FlashLight> flashes = new List<FlashLight>();
    private readonly Dictionary<Sound, AudioClip> clips = new Dictionary<Sound, AudioClip>();
    private AudioSource[] voices;
    private int nextVoice;
    private AudioSource hum;
    private AudioClip humClip;

    [Tooltip("Volume geral dos sons dos poderes.")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

    public static ElementalFxRunner Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("Efeitos_Elementais");
                instance = go.AddComponent<ElementalFxRunner>();
            }

            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    private void Awake()
    {
        voices = new AudioSource[VoiceCount];
        for (int i = 0; i < VoiceCount; i++)
        {
            GameObject go = new GameObject("Som_" + i);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0.6f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 6f;
            source.maxDistance = 70f;
            voices[i] = source;
        }

        GameObject humObject = new GameObject("Som_Carga");
        humObject.transform.SetParent(transform, false);
        hum = humObject.AddComponent<AudioSource>();
        hum.playOnAwake = false;
        hum.loop = true;
        hum.spatialBlend = 0f;
        hum.volume = 0f;
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
        for (int i = flashes.Count - 1; i >= 0; i--)
        {
            FlashLight flash = flashes[i];
            float t = (Time.time - flash.start) / flash.duration;
            if (t >= 1f || flash.light == null)
            {
                if (flash.light != null)
                {
                    Destroy(flash.light.gameObject);
                }

                flashes.RemoveAt(i);
                continue;
            }

            flash.light.intensity = flash.intensity * (1f - t) * (1f - t);
        }
    }

    // ---------- Clarões ----------

    public void AddFlash(Vector3 position, Color color, float intensity, float range, float duration)
    {
        // Poucas luzes ao mesmo tempo: a mais antiga dá lugar.
        if (flashes.Count >= MaxFlashes)
        {
            if (flashes[0].light != null)
            {
                Destroy(flashes[0].light.gameObject);
            }

            flashes.RemoveAt(0);
        }

        GameObject go = new GameObject("Clarao_Elemental");
        go.transform.position = position;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        flashes.Add(new FlashLight { light = light, start = Time.time, duration = Mathf.Max(0.05f, duration), intensity = intensity });
    }

    // ---------- Sons ----------

    public static void Play(Sound sound, Vector3 position, float loudness = 1f, float pitch = 1f)
    {
        Instance.PlayClip(sound, position, loudness, pitch);
    }

    private void PlayClip(Sound sound, Vector3 position, float loudness, float pitch)
    {
        if (!clips.TryGetValue(sound, out AudioClip clip) || clip == null)
        {
            clip = Build(sound);
            clips[sound] = clip;
        }

        AudioSource source = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        source.transform.position = position;
        source.pitch = pitch * Random.Range(0.94f, 1.06f);
        source.volume = loudness * volume;
        source.clip = clip;
        source.Play();
    }

    // Desliga o zumbido de carga (sem criar o ajudante se ele não existir, ex.: ao fechar a cena).
    public static void StopHum()
    {
        if (instance != null && instance.hum != null && instance.hum.isPlaying)
        {
            instance.hum.Stop();
        }
    }

    // Zumbido enquanto carrega o disparo: mais agudo e mais forte conforme a carga (0 = desliga).
    public void SetHum(ElementKind kind, float charge)
    {
        if (charge <= 0.001f)
        {
            if (hum.isPlaying)
            {
                hum.Stop();
            }

            return;
        }

        if (humClip == null)
        {
            humClip = BuildHum();
            hum.clip = humClip;
        }

        float basePitch = kind == ElementKind.Fire ? 0.8f : kind == ElementKind.Ice ? 1.5f : 1.15f;
        hum.pitch = basePitch * Mathf.Lerp(0.9f, 1.5f, charge);
        hum.volume = Mathf.Lerp(0.12f, 0.4f, charge) * volume;
        if (!hum.isPlaying)
        {
            hum.Play();
        }
    }

    // ---------- Síntese ----------

    private static AudioClip Build(Sound sound)
    {
        switch (sound)
        {
            case Sound.FireShot:
                return Make("El_Fogo_Disparo", 0.45f, 11, (t, n, s) =>
                {
                    s.low += (n - s.low) * 0.12f;
                    float env = Mathf.Min(1f, t / 0.02f) * Mathf.Exp(-t * 6f);
                    return (s.low * 2.2f + Mathf.Sin(2f * Mathf.PI * (95f - 50f * t) * t) * 0.5f) * env;
                });
            case Sound.FireBoom:
                return Make("El_Fogo_Explosao", 0.95f, 12, (t, n, s) =>
                {
                    float cut = Mathf.Lerp(0.35f, 0.03f, Mathf.Clamp01(t / 0.6f));
                    s.low += (n - s.low) * cut;
                    float thump = Mathf.Sin(2f * Mathf.PI * (75f - 40f * t) * t) * Mathf.Exp(-t * 5f);
                    return (s.low * 2.4f * Mathf.Exp(-t * 3.5f) + thump * 0.9f) * Mathf.Min(1f, t / 0.005f);
                });
            case Sound.IceShot:
                return Make("El_Gelo_Disparo", 0.35f, 13, (t, n, s) =>
                {
                    s.low += (n - s.low) * 0.25f;
                    float hiss = (n - s.low) * 0.6f * Mathf.Exp(-t * 9f);
                    float glass = Mathf.Sin(2f * Mathf.PI * (1900f + 900f * t) * t) * 0.25f * Mathf.Exp(-t * 12f);
                    return (hiss + glass) * Mathf.Min(1f, t / 0.01f);
                });
            case Sound.IceHit:
                return Make("El_Gelo_Impacto", 0.5f, 14, (t, n, s) =>
                {
                    float click = Random01(s, 0.012f) * Mathf.Exp(-t * 7f);
                    float chime = (Mathf.Sin(2f * Mathf.PI * 2200f * t) + Mathf.Sin(2f * Mathf.PI * 3150f * t) * 0.6f +
                                   Mathf.Sin(2f * Mathf.PI * 4100f * t) * 0.4f) * 0.16f * Mathf.Exp(-t * 9f);
                    return click * n * 0.9f + chime;
                });
            case Sound.Freeze:
                return Make("El_Congelou", 0.7f, 15, (t, n, s) =>
                {
                    float grow = Mathf.Clamp01(t / 0.5f);
                    float crackle = Random01(s, 0.004f + grow * 0.03f) * n * 0.8f * (1f - Mathf.Clamp01((t - 0.5f) / 0.2f));
                    float chime = Mathf.Sin(2f * Mathf.PI * (1400f + 1700f * grow) * t) * 0.14f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.7f));
                    return crackle + chime;
                });
            case Sound.Shatter:
                return Make("El_Gelo_Quebrou", 0.7f, 16, (t, n, s) =>
                {
                    s.low += (n - s.low) * 0.2f;
                    float burst = (n - s.low) * 0.9f * Mathf.Exp(-t * 14f);
                    float tinkle = Random01(s, 0.03f * Mathf.Exp(-t * 3f)) *
                                   Mathf.Sin(2f * Mathf.PI * (2400f + 2600f * s.hold) * t) * 0.5f * Mathf.Exp(-t * 4f);
                    return burst + tinkle;
                });
            case Sound.BoltZap:
                return Make("El_Raio_Disparo", 0.32f, 17, (t, n, s) =>
                {
                    float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * (760f - 900f * t) * t)) * 0.35f;
                    float gate = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 62f * t)) * 0.5f + 0.5f;
                    return (buzz * (0.5f + 0.5f * gate) + n * 0.45f) * Mathf.Exp(-t * 10f);
                });
            case Sound.BoltThunder:
                return Make("El_Raio_Trovao", 1.1f, 18, (t, n, s) =>
                {
                    s.low += (n - s.low) * 0.06f;
                    float crack = n * Mathf.Exp(-t * 28f) * 1.1f;
                    float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * (520f - 380f * t) * t)) * 0.3f * Mathf.Exp(-t * 7f);
                    float rumble = s.low * 3.2f * Mathf.Exp(-t * 3f);
                    return crack + buzz + rumble;
                });
            case Sound.Switch:
                return Make("El_Troca", 0.14f, 19, (t, n, s) =>
                    Mathf.Sin(2f * Mathf.PI * (620f + 2600f * t) * t) * 0.4f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.14f)));
            case Sound.Deny:
                return Make("El_Sem_Energia", 0.2f, 20, (t, n, s) =>
                    Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 130f * t)) * 0.25f * Mathf.Exp(-t * 12f));
            default: // Sizzle
                return Make("El_Chiado", 0.6f, 21, (t, n, s) =>
                {
                    s.low += (n - s.low) * 0.3f;
                    return (n - s.low) * 0.5f * Mathf.Min(1f, t / 0.03f) * Mathf.Exp(-t * 5f);
                });
        }
    }

    // Zumbido em loop (1 s, frequências inteiras: emenda sem estalo).
    private static AudioClip BuildHum()
    {
        return Make("El_Carga", 1f, 22, (t, n, s) =>
        {
            float tremolo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 9f * t);
            return (Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.3f +
                    Mathf.Sin(2f * Mathf.PI * 331f * t) * 0.15f) * tremolo;
        });
    }

    // Memória entre uma amostra e outra (filtro simples e sorteios que "seguram" um valor).
    private sealed class State
    {
        public float low;
        public float hold;
        public System.Random random;
    }

    // De vez em quando devolve 1 (um estalo) e sorteia um novo valor em "hold"; senão 0.
    private static float Random01(State state, float chance)
    {
        if (state.random.NextDouble() < chance)
        {
            state.hold = (float)state.random.NextDouble();
            return 1f;
        }

        return 0f;
    }

    private delegate float Sample(float time, float noise, State state);

    private static AudioClip Make(string name, float duration, int seed, Sample sample)
    {
        int length = Mathf.CeilToInt(duration * SampleRate);
        float[] data = new float[length];
        State state = new State { random = new System.Random(seed) };
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)(state.random.NextDouble() * 2.0 - 1.0);
            data[i] = Mathf.Clamp(sample(t, noise, state), -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(name, length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
