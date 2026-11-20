using System.Collections.Generic;
using UnityEngine;

// Caixa dos pedaços de destruição (etapa 3 do reaproveitamento, 18/11). Criada sozinha; fica no objeto
// "Destrocos_Caixa" da cena. Faz três coisas, sempre aos poucos para não travar:
// 1. Guarda os pedaços que afundaram e sumiram, para o próximo prédio que quebrar usar de novo.
// 2. Enche a caixa devagar no começo (alguns pedaços por quadro), para o primeiro desabamento não
//    precisar criar centenas de objetos de uma vez.
// 3. Deixa prontos, antes do golpe, os cacos das casas perto do jogador (RetroHouse.PreparePiecesStep).
[DisallowMultipleComponent]
public sealed class DebrisPool : MonoBehaviour
{
    private const int Cap = 450;              // mais que isso guardado: o excedente é destruído
    private const int WarmTarget = 220;       // quantos pedaços deixar prontos de começo
    private const int WarmPerFrame = 4;
    private const double PrepareBudgetMs = 1.2; // tempo por quadro para montar cacos de casas
    private const float NearHouseRange = 45f;   // casas mais perto que isso ficam com os cacos prontos
    // 20/11: dentro de uma cidade há prédios demais para 45 m / 1,2 ms (um prédio levou 40 ms para virar
    // cacos na hora do golpe). Na cidade: mais longe, um pouco mais de tempo por quadro e o mais perto primeiro.
    private const double CityPrepareBudgetMs = 2.5;
    private const float CityNearHouseRange = 100f;
    private bool inCity;
    private readonly List<(float, HouseBreakable)> nearby = new List<(float, HouseBreakable)>();
    private const float ScanInterval = 0.5f;

    private static DebrisPool instance;

    private readonly Stack<PooledDebris> free = new Stack<PooledDebris>();
    private int warmed;
    private float nextScan;
    private Transform player;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    // Liga a caixa no carregamento (chamado quando a primeira casa quebrável nasce).
    public static void Ensure()
    {
        if (instance == null)
        {
            instance = new GameObject("Destrocos_Caixa").AddComponent<DebrisPool>();
        }
    }

    // Um pedaço desligado, pronto para configurar (ResetForUse) e ligar.
    public static PooledDebris Take()
    {
        Ensure();
        while (instance.free.Count > 0)
        {
            PooledDebris debris = instance.free.Pop();
            if (debris != null)
            {
                debris.InBox = false;
                return debris;
            }
        }

        PooledDebris created = PooledDebris.Create(instance.transform);
        created.InBox = false;
        return created;
    }

    // Devolve um pedaço que terminou (afundou e sumiu).
    public static void Return(PooledDebris debris)
    {
        if (debris == null || debris.InBox)
        {
            return;
        }

        if (instance == null || instance.free.Count >= Cap)
        {
            Destroy(debris.gameObject);
            return;
        }

        debris.InBox = true;
        debris.Fragment.enabled = false;
        debris.Part.Bind(null);
        Rigidbody body = debris.Body;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        body.isKinematic = true;
        debris.gameObject.SetActive(false);
        debris.transform.SetParent(instance.transform, false);
        instance.free.Push(debris);
    }

    private void Update()
    {
        // 1. Enche a caixa aos poucos.
        for (int i = 0; i < WarmPerFrame && warmed < WarmTarget; i++)
        {
            warmed++;
            PooledDebris debris = PooledDebris.Create(transform);
            debris.InBox = true;
            free.Push(debris);
        }

        // 2. Casas perto do jogador: cacos na fila de preparo.
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + ScanInterval;
            QueueNearbyHouses();
        }

        // 3. Monta um pouco dos cacos (com limite de tempo).
        RetroHouse.PreparePiecesStep(inCity ? CityPrepareBudgetMs : PrepareBudgetMs);
    }

    private void QueueNearbyHouses()
    {
        if (player == null)
        {
            PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
            if (controller == null)
            {
                return;
            }

            player = controller.transform;
        }

        Vector3 here = player.position;
        inCity = Planet.Active && Cities.At(Planet.PlanetBlockAt(WorldOrigin.ToLogical(here))) != null;
        float reach = inCity ? CityNearHouseRange : NearHouseRange;
        float range = reach * reach;
        List<HouseBreakable> houses = HouseBreakable.All;
        nearby.Clear();
        for (int i = 0; i < houses.Count; i++)
        {
            HouseBreakable house = houses[i];
            if (house == null || house.Fractured)
            {
                continue;
            }

            Vector3 d = house.transform.position - here;
            d.y = 0f;
            if (d.sqrMagnitude <= range)
            {
                nearby.Add((d.sqrMagnitude, house));
            }
        }

        // O mais perto primeiro (é o que o jogador vai atacar antes).
        nearby.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        foreach (var (_, house) in nearby)
        {
            RetroHouse.QueuePieces(house);
        }
    }
}
