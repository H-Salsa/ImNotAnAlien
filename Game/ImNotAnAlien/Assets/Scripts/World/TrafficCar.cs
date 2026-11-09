using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

// Um carro do trânsito (criado pelo TrafficSystem).
// - Anda na faixa da direita de um nó da rede até o próximo; no cruzamento escolhe um caminho
//   ao acaso (sem voltar); no fim da rua faz o retorno.
// - Freia diante de obstáculos que se movem (jogador, alienígenas, carros, caixas) e buzina
//   se o jogador ficar na frente.
// - Se a telecinese levantar o carro (ou a Onda de Impacto acertar), ele vira um carro comum
//   com física e o motorista é ejetado (vira um alienígena civil de verdade).
// - Missão 19: pode receber uma MISSÃO (viatura chamada a um crime): segue o caminho mais curto
//   pela rede de ruas até um trecho e estaciona num ponto dele (Arrived). A viatura (PoliceCar)
//   cuida do resto.
[RequireComponent(typeof(Rigidbody))]
public sealed class TrafficCar : MonoBehaviour
{
    private const float Acceleration = 4f;
    private const float Braking = 14f;
    private const float LookAhead = 6.5f;

    private readonly List<Vector3> path = new List<Vector3>();
    private readonly List<float> pathSpeeds = new List<float>();
    private readonly RaycastHit[] hits = new RaycastHit[12];

    private TrafficSystem system;
    private Rigidbody body;
    private GameObject driver;
    private int fromNode;
    private int toNode;
    private int pathIndex;
    private float speed;
    private float blockedTime;
    private float ignoreObstaclesUntil;
    private float nextHonk;
    private bool abandoned;

    // Missão (viatura chamada a um crime).
    private bool hasMission;
    private int goalFrom;
    private int goalTo;
    private Vector3 goalStop;
    private bool finalLeg;
    private int legFrom;
    private int legTo;

    public bool IsAbandoned => abandoned;
    public bool IsParked { get; private set; }
    public bool OnMission => hasMission;
    public float SpeedFactor { get; set; } = 1f;

    // Chegou ao ponto da missão e parou.
    public event Action Arrived;

    private void OnEnable()
    {
        WorldOrigin.Shifted += OnOriginShifted;
    }

    private void OnDisable()
    {
        WorldOrigin.Shifted -= OnOriginShifted;
    }

    // O mundo recentralizou (Missão 16): a rota já montada anda junto.
    private void OnOriginShifted(Vector3 delta)
    {
        for (int i = 0; i < path.Count; i++)
        {
            path[i] -= delta;
        }

        goalStop -= delta;
    }

    // Vai até o trecho de rua goalA → goalB e para em "stop" (posição de MUNDO, na faixa da direita).
    public void SetMission(int goalA, int goalB, Vector3 stop)
    {
        hasMission = true;
        goalFrom = goalA;
        goalTo = goalB;
        goalStop = stop;
        SpeedFactor = Mathf.Max(SpeedFactor, 1.5f);

        // Já está nesse trecho, antes do ponto: para nele sem dar a volta.
        if (pathIndex == 0 && legFrom == goalFrom && legTo == goalTo && body != null)
        {
            Vector3 along = system.Node(goalTo) - system.Node(goalFrom);
            if (Vector3.Dot(goalStop - body.position, along) > 3f)
            {
                StartFinalLeg();
            }
        }
    }

    // Para já, no meio-fio, alguns metros à frente (viatura que viu o procurado passando).
    public void StopNow()
    {
        if (abandoned || IsParked || body == null || system == null)
        {
            return;
        }

        Vector3 forward = body.rotation * Vector3.forward;
        forward.y = 0f;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float distance = Mathf.Max(5f, speed * speed / 12f + 2f);

        hasMission = true;
        goalFrom = goalTo = -1;
        goalStop = body.position + forward * distance + right * 1.2f;
        StartFinalLeg();
    }

    public void SetDriver(GameObject driverVisual)
    {
        driver = driverVisual;
    }

    public void Init(TrafficSystem owner, int from, int to)
    {
        system = owner;
        body = GetComponent<Rigidbody>();
        fromNode = from;
        toNode = to;
        speed = owner.CruiseSpeed * 0.6f;
        PlanLeg(transform.position);
    }

    private void Update()
    {
        // Levantado pela telecinese (deixou de ser cinemático): vira carro comum.
        if (!abandoned && body != null && !body.isKinematic)
        {
            Abandon();
        }
    }

    private void FixedUpdate()
    {
        if (abandoned || IsParked || system == null || pathIndex >= path.Count)
        {
            return;
        }

        float dt = Time.fixedDeltaTime;
        Vector3 position = body.position;
        Vector3 target = path[pathIndex];
        target.y = position.y;

        // Velocidade desejada: a do trecho, ou parar se houver obstáculo.
        float wanted = pathSpeeds[pathIndex] * SpeedFactor;
        if (finalLeg)
        {
            // Freia para parar certinho no ponto.
            wanted = Mathf.Min(wanted, Mathf.Sqrt(2f * 6f * Vector3.Distance(position, target)) + 0.5f);
        }

        if (CheckObstacle(out bool isPlayer, out bool isCar))
        {
            wanted = 0f;
            blockedTime += dt;
            // Viatura parando por causa do procurado: o jogador na frente = chegou, desce todo mundo.
            if (finalLeg && isPlayer && hasMission && blockedTime > 0.6f && speed < 0.5f)
            {
                Park();
                return;
            }

            if (isPlayer && blockedTime > 1.2f && Time.time > nextHonk)
            {
                nextHonk = Time.time + 2.5f;
                GameFeel.Horn(position);
            }

            // Travou com outro carro (cruzamento) ou parado muito tempo: segue mesmo assim.
            if (isCar && blockedTime > 3f || !isPlayer && blockedTime > 6f)
            {
                ignoreObstaclesUntil = Time.time + 2f;
            }
        }
        else
        {
            blockedTime = 0f;
        }

        speed = Mathf.MoveTowards(speed, wanted, (wanted < speed ? Braking : Acceleration) * dt);

        Vector3 toTarget = target - position;
        float step = speed * dt;
        if (toTarget.magnitude <= Mathf.Max(step, 0.35f))
        {
            pathIndex++;
            if (pathIndex >= path.Count)
            {
                if (finalLeg)
                {
                    Park();
                    return;
                }

                PlanLeg(target);
            }

            return;
        }

        Vector3 direction = toTarget.normalized;
        body.MovePosition(position + direction * step);
        Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);
        body.MoveRotation(Quaternion.Slerp(body.rotation, facing, 8f * dt));
    }

    // ---------- Rota ----------

    // Monta o próximo trecho: reta até perto do cruzamento + curva para a próxima rua.
    private void PlanLeg(Vector3 start)
    {
        // Missão: entrou no trecho do crime → só falta parar no ponto.
        if (hasMission && fromNode == goalFrom && toNode == goalTo)
        {
            legFrom = fromNode;
            legTo = toNode;
            StartFinalLeg();
            return;
        }

        path.Clear();
        pathSpeeds.Clear();
        pathIndex = 0;
        legFrom = fromNode;
        legTo = toNode;

        Vector3 a = system.Node(fromNode);
        Vector3 b = system.Node(toNode);
        Vector3 dirIn = (b - a).normalized;
        Vector3 rightIn = Vector3.Cross(Vector3.up, dirIn);
        float lane = system.LaneOffset;
        float clearance = system.Clearance;

        Vector3 approach = b - dirIn * clearance + rightIn * lane;
        path.Add(approach);
        pathSpeeds.Add(system.CruiseSpeed);

        // Próxima rua: qualquer vizinho menos de onde veio; sem opção = retorno.
        IReadOnlyList<int> options = system.Neighbors(toNode);
        List<int> choices = new List<int>();
        foreach (int option in options)
        {
            if (option != fromNode)
            {
                choices.Add(option);
            }
        }

        int next = choices.Count > 0 ? choices[Random.Range(0, choices.Count)] : fromNode;
        if (hasMission)
        {
            next = toNode == goalFrom ? goalTo : system.NextHop(fromNode, toNode, goalFrom);
        }
        Vector3 dirOut = (system.Node(next) - b).normalized;
        Vector3 rightOut = Vector3.Cross(Vector3.up, dirOut);
        Vector3 exit = b + dirOut * clearance + rightOut * lane;

        Vector3 control;
        float dot = Vector3.Dot(dirIn, dirOut);
        if (dot > 0.9f)
        {
            control = (approach + exit) * 0.5f;                 // reto
        }
        else if (dot < -0.9f)
        {
            control = b + dirIn * (lane * 1.2f);                 // retorno
        }
        else
        {
            control = b + rightIn * lane + rightOut * lane;      // curva de 90°
        }

        bool straight = dot > 0.9f;
        const int samples = 8;
        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector3 point = (1 - t) * (1 - t) * approach + 2f * (1 - t) * t * control + t * t * exit;
            path.Add(point);
            pathSpeeds.Add(straight ? system.CruiseSpeed : system.TurnSpeed);
        }

        // Reduz antes da curva.
        if (!straight)
        {
            pathSpeeds[0] = system.TurnSpeed + 1.5f;
        }

        fromNode = toNode;
        toNode = next;
    }

    private void StartFinalLeg()
    {
        path.Clear();
        pathSpeeds.Clear();
        pathIndex = 0;
        path.Add(goalStop);
        pathSpeeds.Add(system.CruiseSpeed);
        finalLeg = true;
    }

    private void Park()
    {
        IsParked = true;
        hasMission = false;
        finalLeg = false;
        speed = 0f;
        Arrived?.Invoke();
    }

    // ---------- Obstáculos ----------

    private bool CheckObstacle(out bool isPlayer, out bool isCar)
    {
        isPlayer = false;
        isCar = false;
        Vector3 forward = body.rotation * Vector3.forward;
        Vector3 origin = body.position + Vector3.up * 0.9f + forward * 2.2f;
        int count = Physics.BoxCastNonAlloc(origin, new Vector3(0.9f, 0.45f, 0.2f), forward, hits, body.rotation, LookAhead,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        bool ignoring = Time.time < ignoreObstaclesUntil;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider other = hits[i].collider;
            if (other.transform.IsChildOf(transform))
            {
                continue;
            }

            bool player = other is CharacterController;
            if (!player && other.attachedRigidbody == null)
            {
                continue; // coisas fixas (casas, muretas, paredes invisíveis) não estão na faixa
            }

            if (ignoring && !player)
            {
                continue;
            }

            found = true;
            isPlayer |= player;
            isCar |= other.attachedRigidbody != null && other.attachedRigidbody.GetComponent<TrafficCar>() != null;
        }

        return found;
    }

    // ---------- Virar carro comum ----------

    // Chamado quando a telecinese levanta o carro ou a Onda de Impacto acerta.
    public void Abandon()
    {
        if (abandoned)
        {
            return;
        }

        abandoned = true;
        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        Vector3 velocity = transform.forward * speed;
        body.isKinematic = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Viatura: os dois policiais são ejetados (a PoliceCar cuida disso).
        if (TryGetComponent(out PoliceCar police))
        {
            police.OnAbandoned(velocity);
            return;
        }

        if (driver != null)
        {
            Vector3 seat = driver.transform.position + Vector3.up * 1.6f;
            driver.SetActive(false);
            if (PopulationSpawner.Instance != null)
            {
                PopulationSpawner.Instance.SpawnEjectedDriver(seat, velocity);
            }
        }
    }
}
