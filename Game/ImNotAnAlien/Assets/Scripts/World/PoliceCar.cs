using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Viatura com dois policiais dentro (Missão 19). Criada pelo TrafficSystem.
// - Patrulha como um carro do trânsito (quem dirige é o TrafficCar).
// - Chamada a um crime (OnDispatched): liga o giroflex (pisca azul e vermelho) e corre mais.
// - Ao chegar (TrafficCar.Arrived): as duas portas da frente abrem e os policiais descem,
//   virando policiais de verdade (PopulationSpawner) que vão atrás do jogador.
// - Levantada pela telecinese / atingida: os dois policiais são ejetados.
[DisallowMultipleComponent]
public sealed class PoliceCar : MonoBehaviour
{
    private const float DoorOpenAngle = 70f;
    private const float DoorOpenTime = 0.6f;

    private GameObject driver;
    private GameObject passenger;
    private TrafficCar trafficCar;
    private readonly List<(Transform door, Quaternion closed, float sign)> frontDoors = new List<(Transform, Quaternion, float)>();
    private readonly List<Renderer> sirenBlue = new List<Renderer>();
    private readonly List<Renderer> sirenRed = new List<Renderer>();
    private bool sirenOn;
    private bool officersOut;

    public bool OfficersOut => officersOut;

    public void Setup(GameObject driverModel, GameObject passengerModel)
    {
        driver = driverModel;
        passenger = passengerModel;
    }

    private void Start()
    {
        trafficCar = GetComponent<TrafficCar>();
        if (trafficCar != null)
        {
            trafficCar.Arrived += OnArrived;
        }

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            // Portas da frente (do Blender: Porta_EF = esquerda, Porta_DF = direita). A origem é a dobradiça.
            if (t.name.StartsWith("Porta_") && t.name.EndsWith("F"))
            {
                float side = transform.InverseTransformPoint(t.position).x < 0f ? 1f : -1f;
                frontDoors.Add((t, Quaternion.Inverse(transform.rotation) * t.rotation, side));
            }
            else if (t.name == "Giroflex_0" && t.TryGetComponent(out Renderer blue))
            {
                sirenBlue.Add(blue);
            }
            else if (t.name == "Giroflex_2" && t.TryGetComponent(out Renderer red))
            {
                sirenRed.Add(red);
            }
        }
    }

    private void OnDestroy()
    {
        if (trafficCar != null)
        {
            trafficCar.Arrived -= OnArrived;
        }
    }

    private void Update()
    {
        if (!sirenOn)
        {
            return;
        }

        // Pisca: azul e vermelho alternando.
        bool phase = Mathf.Repeat(Time.time * 3f, 1f) < 0.5f;
        foreach (Renderer r in sirenBlue) if (r != null) r.enabled = phase;
        foreach (Renderer r in sirenRed) if (r != null) r.enabled = !phase;
    }

    // ---------- Chamado ----------

    public void OnDispatched()
    {
        sirenOn = true;
    }

    private void OnArrived()
    {
        if (!officersOut)
        {
            StartCoroutine(GetOut());
        }
    }

    private IEnumerator GetOut()
    {
        officersOut = true;
        yield return new WaitForSeconds(0.3f);

        // Abre as portas da frente.
        for (float t = 0f; t < DoorOpenTime; t += Time.deltaTime)
        {
            SetDoors(Mathf.SmoothStep(0f, 1f, t / DoorOpenTime));
            yield return null;
        }

        SetDoors(1f);

        // Os policiais descem, um de cada lado.
        SpawnOfficer(driver, -1f, Vector3.zero);
        yield return new WaitForSeconds(0.25f);
        SpawnOfficer(passenger, 1f, Vector3.zero);
    }

    private void SetDoors(float open)
    {
        foreach ((Transform door, Quaternion closed, float sign) in frontDoors)
        {
            if (door != null)
            {
                door.rotation = Quaternion.AngleAxis(sign * DoorOpenAngle * open, transform.up) * (transform.rotation * closed);
            }
        }
    }

    // side: -1 = lado do motorista (esquerda), +1 = carona (direita).
    private void SpawnOfficer(GameObject seated, float side, Vector3 throwVelocity)
    {
        if (seated == null || !seated.activeSelf)
        {
            return;
        }

        seated.SetActive(false);
        if (PopulationSpawner.Instance == null)
        {
            return;
        }

        bool thrown = throwVelocity != Vector3.zero;
        Vector3 spot = thrown
            ? seated.transform.position + Vector3.up * 1.6f
            : transform.TransformPoint(new Vector3(side * 1.8f, 0.1f, 0.2f));
        if (!thrown && NavMesh.SamplePosition(spot, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            spot = hit.position;
        }

        Quaternion facing = Quaternion.LookRotation(transform.right * side, Vector3.up);
        PopulationSpawner.Instance.SpawnPoliceOfficer(spot, facing, thrown ? throwVelocity : (Vector3?)null);
    }

    // ---------- Telecinese ----------

    public void OnAbandoned(Vector3 velocity)
    {
        sirenOn = false;
        StopAllCoroutines();
        if (officersOut)
        {
            return; // já desceram; ninguém dentro
        }

        officersOut = true;
        SpawnOfficer(driver, -1f, velocity * 0.6f + Vector3.up * 6f + Random.insideUnitSphere * 2f);
        SpawnOfficer(passenger, 1f, velocity * 0.6f + Vector3.up * 6f + Random.insideUnitSphere * 2f);
    }
}
