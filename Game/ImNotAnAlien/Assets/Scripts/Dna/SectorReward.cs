using System.Collections;
using UnityEngine;

// Comemoração de SETOR LIMPO (Missão 22) — a recompensa por consumir todos os alienígenas do setor.
// O bônus de DNA não entra mais calado na conta: ele cai do céu como uma CHUVA de orbes que voam
// até você, então o número do DNA vai subindo na tela enquanto a chuva chega.
// A sequência: clarão dourado + tela de comemoração (GameHud), câmera lenta por um instante,
// tremida de tela e a chuva em ondas. A fanfarra é a do GameFeel, tocada pelo SectorTracker.
// Criado sozinho na primeira vez que um setor é limpo: não precisa colocar nada na cena.
[DisallowMultipleComponent]
public sealed class SectorReward : MonoBehaviour
{
    private const float SlowScale = 0.32f;      // o quanto o tempo desacelera
    private const float SlowHold = 0.55f;       // segundos (de verdade) em câmera lenta
    private const float SlowRecover = 0.8f;     // segundos para o tempo voltar ao normal
    private const float WaveInterval = 0.16f;   // intervalo entre as ondas da chuva
    private const float RainRadius = 5f;        // raio da chuva em volta de você
    private const float RainHeight = 9f;        // de que altura os orbes caem
    private const int DnaPerOrb = 6;            // mais DNA = mais orbes (até o limite abaixo)
    private const int MaxOrbs = 40;

    private static SectorReward instance;

    private Coroutine routine;
    private bool slowing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    private static SectorReward Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new GameObject("Recompensa_Setor").AddComponent<SectorReward>();
            }

            return instance;
        }
    }

    // Comemora um setor (region = false) ou uma região inteira (region = true) e ENTREGA o bônus
    // em forma de chuva. Devolve false se não deu para fazer a chuva: aí quem chamou entrega o
    // DNA direto, como antes (cena sem sistema de orbes, jogador ausente ou bônus zero).
    public static bool Celebrate(string title, int dna, bool region)
    {
        DnaOrbSystem orbs = DnaOrbSystem.Instance;
        if (dna <= 0 || orbs == null || orbs.Player == null)
        {
            return false;
        }

        Instance.Run(dna, region);
        return true;
    }

    private void Run(int dna, bool region)
    {
        // Dois setores limpos quase juntos: a comemoração nova substitui a anterior.
        if (routine != null)
        {
            StopAllCoroutines();
            EndSlowMotion();
        }

        routine = StartCoroutine(Celebration(dna, region));
    }

    private IEnumerator Celebration(int dna, bool region)
    {
        CameraShake.AddTrauma(region ? 0.7f : 0.45f);
        if (GameHud.Instance != null)
        {
            GameHud.Instance.FlashCelebration(region ? 1f : 0.75f);
        }

        // A câmera lenta corre por fora: assim as primeiras gotas de DNA caem devagar,
        // e o tempo volta ao normal enquanto elas ainda estão vindo para você.
        StartCoroutine(SlowMotion(region));

        // Chuva de DNA em ondas.
        Transform player = DnaOrbSystem.Instance != null ? DnaOrbSystem.Instance.Player : null;
        int orbCount = Mathf.Clamp(dna / DnaPerOrb, 8, MaxOrbs);
        orbCount = Mathf.Min(orbCount, dna);
        int waves = Mathf.Clamp(orbCount / 8, 2, 5);
        int dnaLeft = dna;
        int orbsLeft = orbCount;

        for (int wave = 0; wave < waves; wave++)
        {
            if (DnaOrbSystem.Instance == null || player == null)
            {
                // Sistema sumiu no meio (troca de cena): entrega o que falta direto.
                if (DnaInventory.Instance != null && dnaLeft > 0)
                {
                    DnaInventory.Instance.Add(dnaLeft);
                }

                break;
            }

            int left = waves - wave;
            int count = Mathf.Max(1, orbsLeft / left);
            int value = dnaLeft / left;
            if (wave == waves - 1)
            {
                count = Mathf.Max(1, orbsLeft);
                value = dnaLeft; // a última onda leva o resto (nada de DNA sumir)
            }

            DnaOrbSystem.Instance.SpawnRain(player.position, value, count, RainRadius, RainHeight);
            dnaLeft -= value;
            orbsLeft -= count;
            yield return Wait(WaveInterval);
        }

        routine = null;
    }

    private IEnumerator SlowMotion(bool region)
    {
        // Com o jogo pausado (menu, mapa), nem começa: o tempo é do menu.
        if (GamePause.IsPaused)
        {
            yield break;
        }

        slowing = true;
        Time.timeScale = SlowScale;
        yield return Wait(region ? SlowHold * 1.6f : SlowHold);

        for (float t = 0f; t < SlowRecover && slowing; t += Time.unscaledDeltaTime)
        {
            if (GamePause.IsPaused)
            {
                slowing = false; // o menu abriu no meio
                yield break;
            }

            Time.timeScale = Mathf.Lerp(SlowScale, 1f, t / SlowRecover);
            yield return null;
        }

        EndSlowMotion();
    }

    private static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            yield return null;
        }
    }

    private void EndSlowMotion()
    {
        if (!slowing)
        {
            return;
        }

        slowing = false;
        if (!GamePause.IsPaused)
        {
            Time.timeScale = 1f;
        }
    }

    // Se a cena for trocada no meio da comemoração, o tempo não pode ficar lento para sempre.
    private void OnDisable()
    {
        EndSlowMotion();
    }

    private void OnDestroy()
    {
        EndSlowMotion();
        if (instance == this)
        {
            instance = null;
        }
    }
}
