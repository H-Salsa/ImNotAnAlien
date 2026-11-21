using UnityEngine;

// Sons do menu (20/11): "plim" ao passar o mouse (ou mudar a seleção pelo teclado/controle) e "blup" ao clicar.
// Arquivos em Assets/Resources/Menu (Menu_Passar.wav, Menu_Clicar.wav): para trocar o som, troque o
// arquivo mantendo o nome. O volume segue o Volume das Configurações (AudioListener).
// O tocador fica vivo entre cenas, para o clique de "Novo jogo" não ser cortado ao carregar o jogo.
public static class MenuSounds
{
    private static AudioSource source;
    private static AudioClip hover;
    private static AudioClip click;
    private static float lastHover;
    private static float quietUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        source = null;
        hover = null;
        click = null;
        lastHover = 0f;
        quietUntil = 0f;
    }

    // Passou por cima de um botão. Não repete em menos de 50 ms (mouse + seleção no mesmo quadro).
    public static void Hover()
    {
        float now = Time.unscaledTime;
        if (now < quietUntil || now - lastHover < 0.05f)
        {
            return;
        }

        lastHover = now;
        Play(ref hover, "Menu/Menu_Passar", 0.55f, Random.Range(0.97f, 1.03f));
    }

    public static void Click()
    {
        lastHover = Time.unscaledTime; // o clique "engole" o plim da seleção que vem junto
        Play(ref click, "Menu/Menu_Clicar", 0.8f, 1f);
    }

    // Seleção feita pelo próprio menu (abrir uma tela, entrar o primeiro botão): sem plim.
    public static void Quiet(float seconds = 0.1f)
    {
        quietUntil = Time.unscaledTime + seconds;
    }

    private static void Play(ref AudioClip clip, string path, float volume, float pitch)
    {
        if (clip == null)
        {
            clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                return;
            }
        }

        if (source == null)
        {
            GameObject go = new GameObject("Sons_Menu");
            Object.DontDestroyOnLoad(go);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
        }

        source.pitch = pitch;
        source.PlayOneShot(clip, volume);
    }
}
