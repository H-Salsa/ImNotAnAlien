using UnityEngine;

// Pausa do jogo (usada pelo menu de evoluções).
// Enquanto pausado: o tempo para, o cursor fica livre e o controle do personagem ignora o mouse.
public static class GamePause
{
    public static bool IsPaused { get; private set; }

    public static void Pause()
    {
        if (IsPaused)
        {
            return;
        }

        IsPaused = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public static void Resume()
    {
        if (!IsPaused)
        {
            return;
        }

        IsPaused = false;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Garante que um Play novo não comece "pausado" (quando o Domain Reload está desligado).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        IsPaused = false;
    }
}
