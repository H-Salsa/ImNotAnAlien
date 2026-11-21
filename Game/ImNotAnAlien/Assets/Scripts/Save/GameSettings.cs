using UnityEngine;

// Configurações do jogador (20/11), guardadas no PlayerPrefs e aplicadas ao abrir o jogo.
// Telas: menu inicial > Configurações. Quem precisa lê daqui (ex.: sensibilidade do mouse no controle).
public static class GameSettings
{
    private const string Prefix = "ImNotAnAlien.Config.";

    public static float Volume { get; private set; } = 1f;
    public static float LookSensitivity { get; private set; } = 1f;
    public static bool Fullscreen { get; private set; } = true;
    public static bool VSync { get; private set; } = true;
    public static int Quality { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        Volume = PlayerPrefs.GetFloat(Prefix + "Volume", 1f);
        LookSensitivity = PlayerPrefs.GetFloat(Prefix + "Sensibilidade", 1f);
        Fullscreen = PlayerPrefs.GetInt(Prefix + "TelaCheia", 1) == 1;
        VSync = PlayerPrefs.GetInt(Prefix + "VSync", 1) == 1;
        Quality = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Qualidade", QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
        Apply();
    }

    public static void SetVolume(float value) { Volume = Mathf.Clamp01(value); Save(); }
    public static void SetLookSensitivity(float value) { LookSensitivity = Mathf.Clamp(value, 0.25f, 3f); Save(); }
    public static void SetFullscreen(bool value) { Fullscreen = value; Save(); }
    public static void SetVSync(bool value) { VSync = value; Save(); }
    public static void SetQuality(int value) { Quality = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1); Save(); }

    public static string QualityName => QualitySettings.names.Length > 0 ? QualitySettings.names[Quality] : "Padrão";

    private static void Save()
    {
        PlayerPrefs.SetFloat(Prefix + "Volume", Volume);
        PlayerPrefs.SetFloat(Prefix + "Sensibilidade", LookSensitivity);
        PlayerPrefs.SetInt(Prefix + "TelaCheia", Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "VSync", VSync ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "Qualidade", Quality);
        PlayerPrefs.Save();
        Apply();
    }

    private static void Apply()
    {
        AudioListener.volume = Volume;
        if (QualitySettings.GetQualityLevel() != Quality)
        {
            QualitySettings.SetQualityLevel(Quality, true);
        }

        QualitySettings.vSyncCount = VSync ? 1 : 0; // depois da qualidade (cada nível tem o seu vSync)
#if !UNITY_EDITOR
        Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
#endif
    }
}
