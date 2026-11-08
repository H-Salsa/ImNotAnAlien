using System;
using UnityEngine;

// DNA acumulado pelo jogador. É o RECURSO do jogo (vai pagar as evoluções).
// Não confundir com o número de alienígenas consumidos, que é o OBJETIVO do setor
// (veja SectorTracker).
[DisallowMultipleComponent]
public sealed class DnaInventory : MonoBehaviour
{
    public static DnaInventory Instance { get; private set; }

    public int Total { get; private set; }

    // (total novo, quanto entrou agora)
    public event Action<int, int> Changed;

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

    public void Add(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Total += amount;
        Changed?.Invoke(Total, amount);
    }

    // Jogo salvo (Missão 17): define o total de uma vez.
    public void SetTotal(int total)
    {
        Total = Mathf.Max(0, total);
        Changed?.Invoke(Total, 0);
    }

    // Para as evoluções futuras: retorna false se não houver DNA suficiente.
    public bool TrySpend(int amount)
    {
        if (amount < 0 || amount > Total)
        {
            return false;
        }

        Total -= amount;
        Changed?.Invoke(Total, -amount);
        return true;
    }
}
