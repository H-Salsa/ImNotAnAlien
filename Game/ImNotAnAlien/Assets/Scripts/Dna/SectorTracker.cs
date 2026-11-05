using System;
using System.Collections.Generic;
using UnityEngine;

// Contagem do OBJETIVO do setor: quantos alienígenas existem e quantos já foram consumidos.
// Regra (decisão D10): um alienígena conta como consumido quando o ÚLTIMO orbe de DNA dele
// é absorvido. Cada alienígena conta 1, não importa quanto DNA rendeu.
// Quando todos são consumidos, o setor fica limpo e o jogador ganha um bônus de DNA.
[DisallowMultipleComponent]
public sealed class SectorTracker : MonoBehaviour
{
    public static SectorTracker Instance { get; private set; }

    [SerializeField] private string sectorName = "Esquina";
    [Tooltip("DNA de bônus ao limpar o setor.")]
    [SerializeField, Min(0)] private int clearBonusDna = 25;

    // Orbes que ainda faltam absorver, por alienígena (chave = ID do alienígena).
    private readonly Dictionary<int, int> pendingOrbs = new Dictionary<int, int>();

    public string SectorName => sectorName;
    public int ClearBonusDna => clearBonusDna;
    public int TotalAliens { get; private set; }
    public int ConsumedAliens { get; private set; }
    public bool IsCleared { get; private set; }

    public event Action Changed;
    public event Action<SectorTracker> Cleared;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        TotalAliens = FindObjectsByType<AlienHealth>(FindObjectsSortMode.None).Length;
        Changed?.Invoke();
    }

    // Para alienígenas que surgirem depois do início (reforços, por exemplo).
    public void RegisterAlien()
    {
        TotalAliens++;
        Changed?.Invoke();
    }

    public void OnAlienDied(int alienId, int orbCount)
    {
        if (orbCount <= 0)
        {
            MarkConsumed();
            return;
        }

        pendingOrbs[alienId] = orbCount;
    }

    public void OnOrbAbsorbed(int alienId)
    {
        if (!pendingOrbs.TryGetValue(alienId, out int remaining))
        {
            return;
        }

        remaining--;
        if (remaining > 0)
        {
            pendingOrbs[alienId] = remaining;
            return;
        }

        pendingOrbs.Remove(alienId);
        MarkConsumed();
    }

    // Para o futuro "Devorar": consome na hora, sem orbes.
    public void ConsumeDirectly()
    {
        MarkConsumed();
    }

    private void MarkConsumed()
    {
        ConsumedAliens++;
        Changed?.Invoke();

        if (!IsCleared && TotalAliens > 0 && ConsumedAliens >= TotalAliens)
        {
            IsCleared = true;
            if (DnaInventory.Instance != null)
            {
                DnaInventory.Instance.Add(clearBonusDna);
            }

            GameFeel.SectorCleared();
            Cleared?.Invoke(this);
            Debug.Log($"Setor '{sectorName}' limpo! +{clearBonusDna} DNA de bônus.");
        }
    }
}
