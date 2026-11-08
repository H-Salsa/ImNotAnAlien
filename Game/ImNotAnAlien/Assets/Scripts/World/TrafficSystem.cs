using System;
using System.Collections.Generic;
using UnityEngine;

// Trânsito (Missão 14).
// - As ruas são descritas como retas (Leste-Oeste ou Norte-Sul). O sistema monta sozinho a
//   rede: cruzamentos onde as ruas se encontram e pontas onde elas terminam.
// - Mantém alguns carros circulando perto do jogador. Eles surgem e somem fora da vista.
// - Cada carro (TrafficCar) anda na faixa da direita, faz curvas nos cruzamentos, faz retorno
//   no fim da rua, freia diante de obstáculos e buzina.
// - Carros: o modelo 3D flutuante (carModel) com pintura sorteada; sem modelo, caixas por código.
// - Mundo em blocos (Missão 16): as ruas são LÓGICAS (fixas no mapa); Node() devolve a posição
//   de MUNDO. Carros só surgem sobre blocos carregados e somem com o bloco descarregado.
[DisallowMultipleComponent]
public sealed class TrafficSystem : MonoBehaviour
{
    [Serializable]
    public struct Road
    {
        public string name;
        [Tooltip("Marcado = rua Leste-Oeste (ao longo de X). Desmarcado = Norte-Sul (ao longo de Z).")]
        public bool eastWest;
        [Tooltip("Coordenada fixa da rua: Z para Leste-Oeste, X para Norte-Sul.")]
        public float at;
        public float from;
        public float to;
    }

    public static TrafficSystem Instance { get; private set; }

    [Header("Ruas")]
    [SerializeField] private List<Road> roads = new List<Road>();
    [Tooltip("Distância do centro da rua até o meio da faixa.")]
    [SerializeField, Min(0.5f)] private float laneOffset = 2.5f;
    [Tooltip("Distância do centro do cruzamento onde a curva começa/termina.")]
    [SerializeField, Min(2f)] private float intersectionClearance = 6.5f;

    [Header("Quantidade e distâncias")]
    [SerializeField, Min(0)] private int targetCars = 6;
    [SerializeField, Min(10f)] private float spawnMinDistance = 50f;
    [SerializeField, Min(20f)] private float spawnMaxDistance = 120f;
    [SerializeField, Min(30f)] private float despawnDistance = 140f;

    [Header("Velocidade (m/s)")]
    [SerializeField, Min(1f)] private float cruiseSpeed = 9f;
    [SerializeField, Min(1f)] private float turnSpeed = 4.5f;

    [Header("Aparência")]
    [SerializeField] private Material[] bodyColors;
    [SerializeField] private Material glass;
    [SerializeField] private Material wheel;
    [SerializeField] private Material[] driverSkins;
    [SerializeField] private Material eye;

    [Header("Motoristas (modelo 3D — Missão 15)")]
    [Tooltip("Modelos de alien (Alien_Masculino / Alien_Feminino). Vazio = motorista antigo de cápsula.")]
    [SerializeField] private GameObject[] driverModels;
    [Tooltip("Animator Controller com a animação de dirigir.")]
    [SerializeField] private RuntimeAnimatorController driverController;
    [SerializeField, Range(0.8f, 1.5f)] private float driverScale = 1.1f;
    [Tooltip("Altura do quadril do motorista em relação ao carro (o banco).")]
    [SerializeField] private float driverSeatHeight = 0.95f;

    [Header("Carro 3D (flutuante)")]
    [Tooltip("Modelo do carro (Carro_Civil.fbx). Vazio = carro antigo de caixas.")]
    [SerializeField] private GameObject carModel;
    [Tooltip("Pares de cores (pintura principal, detalhe/faixa de baixo) sorteados para cada carro.")]
    [SerializeField] private Color[] carPaints =
    {
        Hex("F1DFC0"), Hex("E2735A"),   // creme e coral (a arte original)
        Hex("BFEBDD"), Hex("2E8C76"),   // menta e verde
        Hex("D9C9F2"), Hex("7B5BC4"),   // lilás e roxo
        Hex("F4D35E"), Hex("3E6FB0"),   // amarelo e azul
        Hex("F6C1CF"), Hex("A33B5C"),   // rosa e vinho
        Hex("F2F2F2"), Hex("E8894A"),   // branco e laranja
        Hex("A8D8F0"), Hex("2B5D8C"),   // azul-claro e azul
        Hex("E8E1D3"), Hex("4A4F57"),   // areia e grafite
    };
    [Tooltip("Carro 3D: onde fica o quadril do motorista (x = lado, y = altura, z = frente).")]
    [SerializeField] private Vector3 carSeatHips = new Vector3(-0.34f, 0.73f, 0.02f);
    [Tooltip("Carro 3D: ponto entre os pedais (os pés do motorista vão para lá).")]
    [SerializeField] private Vector3 carSeatPedals = new Vector3(-0.34f, 0.53f, 0.66f);

    private readonly List<Vector3> nodes = new List<Vector3>();
    private readonly List<List<int>> neighbors = new List<List<int>>();
    private readonly List<(int a, int b)> edges = new List<(int a, int b)>();
    private readonly List<TrafficCar> cars = new List<TrafficCar>();

    private Transform player;
    private Camera mainCamera;
    private float timer;
    private WorldStreamer streamer;

    public float LaneOffset => laneOffset;
    public float Clearance => intersectionClearance;
    public float CruiseSpeed => cruiseSpeed;
    public float TurnSpeed => turnSpeed;

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

        if (streamer != null)
        {
            streamer.BlockUnloading -= OnBlockUnloading;
        }
    }

    private void Start()
    {
        PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
        player = controller != null ? controller.transform : null;
        mainCamera = Camera.main;

        streamer = WorldStreamer.Instance;
        if (streamer != null)
        {
            streamer.BlockUnloading += OnBlockUnloading;
        }

        // Mundo sorteado (Missão 18): a rede vem do sorteio; senão, das ruas da lista.
        if (streamer != null && streamer.Layout != null)
        {
            BuildGraphFromLayout(streamer.Layout);
        }
        else
        {
            BuildGraph();
        }

        if (player == null || edges.Count == 0)
        {
            Debug.LogWarning("TrafficSystem: sem jogador ou sem ruas configuradas. Rode 'Preparar Transito'.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f)
        {
            return;
        }

        timer = 0.5f;
        Despawn();
        if (CountActiveNearby() < targetCars)
        {
            TrySpawn();
        }
    }

    // ---------- Rede de ruas ----------

    // Posição de MUNDO do nó (a rede é guardada em coordenadas lógicas).
    public Vector3 Node(int index) => WorldOrigin.ToWorld(nodes[index]);

    public IReadOnlyList<int> Neighbors(int index) => neighbors[index];

    private void BuildGraph()
    {
        foreach (Road road in roads)
        {
            List<float> stops = new List<float> { road.from, road.to };
            foreach (Road other in roads)
            {
                bool crosses = other.eastWest != road.eastWest &&
                               other.at >= road.from - 0.1f && other.at <= road.to + 0.1f &&
                               road.at >= other.from - 0.1f && road.at <= other.to + 0.1f;
                if (crosses)
                {
                    stops.Add(other.at);
                }
            }

            stops.Sort();
            int previous = -1;
            float previousStop = float.NaN;
            foreach (float stop in stops)
            {
                if (!float.IsNaN(previousStop) && Mathf.Abs(stop - previousStop) < 0.5f)
                {
                    continue;
                }

                Vector3 point = road.eastWest ? new Vector3(stop, 0.05f, road.at) : new Vector3(road.at, 0.05f, stop);
                int node = FindOrAddNode(point);
                if (previous >= 0)
                {
                    Connect(previous, node);
                }

                previous = node;
                previousStop = stop;
            }
        }
    }

    private void BuildGraphFromLayout(WorldLayout layout)
    {
        List<Vector3> layoutNodes = new List<Vector3>();
        List<(int a, int b)> layoutEdges = new List<(int a, int b)>();
        layout.BuildRoadNetwork(layoutNodes, layoutEdges);
        foreach (Vector3 node in layoutNodes)
        {
            nodes.Add(node);
            neighbors.Add(new List<int>());
        }

        foreach ((int a, int b) in layoutEdges)
        {
            Connect(a, b);
        }

        Debug.Log($"Trânsito: {nodes.Count} cruzamentos e {edges.Count} trechos de rua no mapa sorteado.");
    }

    private int FindOrAddNode(Vector3 point)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if ((nodes[i] - point).sqrMagnitude < 1f)
            {
                return i;
            }
        }

        nodes.Add(point);
        neighbors.Add(new List<int>());
        return nodes.Count - 1;
    }

    private void Connect(int a, int b)
    {
        if (!neighbors[a].Contains(b)) neighbors[a].Add(b);
        if (!neighbors[b].Contains(a)) neighbors[b].Add(a);
        edges.Add((a, b));
    }

    // ---------- Surgir e sumir ----------

    private int CountActiveNearby()
    {
        int count = 0;
        float range = despawnDistance * despawnDistance;
        foreach (TrafficCar car in cars)
        {
            if (car != null && !car.IsAbandoned && (car.transform.position - player.position).sqrMagnitude < range)
            {
                count++;
            }
        }

        return count;
    }

    private void TrySpawn()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            (int a, int b) edge = edges[UnityEngine.Random.Range(0, edges.Count)];
            bool reverse = UnityEngine.Random.value < 0.5f;
            int from = reverse ? edge.b : edge.a;
            int to = reverse ? edge.a : edge.b;

            Vector3 start = Node(from);
            Vector3 end = Node(to);
            Vector3 direction = (end - start).normalized;
            float length = Vector3.Distance(start, end);
            if (length < intersectionClearance * 2f + 4f)
            {
                continue;
            }

            float along = UnityEngine.Random.Range(intersectionClearance + 2f, length - intersectionClearance - 2f);
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            Vector3 position = start + direction * along + right * laneOffset;

            if (streamer != null && !streamer.IsLoaded(position, 15f))
            {
                continue; // rua de um bloco que não está carregado
            }

            float distance = Vector3.Distance(position, player.position);
            if (distance < spawnMinDistance || distance > spawnMaxDistance || IsVisible(position + Vector3.up))
            {
                continue;
            }

            if (Physics.CheckBox(position + Vector3.up, new Vector3(1.2f, 0.8f, 3f), Quaternion.LookRotation(direction),
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && BlockedByMovable(position, direction))
            {
                continue; // já tem algo ali
            }

            TrafficCar car = BuildCar(position, direction);
            car.Init(this, from, to);
            cars.Add(car);
            return;
        }
    }

    private static bool BlockedByMovable(Vector3 position, Vector3 direction)
    {
        foreach (Collider hit in Physics.OverlapBox(position + Vector3.up, new Vector3(1.2f, 0.8f, 3f), Quaternion.LookRotation(direction),
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.attachedRigidbody != null || hit is CharacterController)
            {
                return true;
            }
        }

        return false;
    }

    private void Despawn()
    {
        float range = despawnDistance * despawnDistance;
        for (int i = cars.Count - 1; i >= 0; i--)
        {
            TrafficCar car = cars[i];
            if (car == null)
            {
                cars.RemoveAt(i);
                continue;
            }

            bool far = (car.transform.position - player.position).sqrMagnitude > range;
            bool offMap = streamer != null && !streamer.IsLoaded(car.transform.position);
            if ((far || offMap) && !IsVisible(car.transform.position + Vector3.up, car.transform))
            {
                cars.RemoveAt(i);
                Destroy(car.gameObject);
            }
        }
    }

    // Um bloco vai ser descarregado: os carros que estão nele somem agora.
    private void OnBlockUnloading(Rect worldArea)
    {
        for (int i = cars.Count - 1; i >= 0; i--)
        {
            TrafficCar car = cars[i];
            if (car == null)
            {
                cars.RemoveAt(i);
                continue;
            }

            Vector3 p = car.transform.position;
            if (!worldArea.Contains(new Vector2(p.x, p.z)) || (p - player.position).sqrMagnitude < 30f * 30f)
            {
                continue; // fora do bloco, ou perto do jogador (talvez na mão da telecinese)
            }

            cars.RemoveAt(i);
            Destroy(car.gameObject);
        }
    }

    private bool IsVisible(Vector3 point, Transform target = null)
    {
        if (mainCamera == null)
        {
            return false;
        }

        Vector3 viewport = mainCamera.WorldToViewportPoint(point);
        bool onScreen = viewport.z > 0f && viewport.x > -0.1f && viewport.x < 1.1f && viewport.y > -0.1f && viewport.y < 1.1f;
        if (!onScreen)
        {
            return false;
        }

        if (Physics.Linecast(mainCamera.transform.position, point, out RaycastHit hit, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            Transform hitTransform = hit.collider.transform;
            return hitTransform.IsChildOf(player) || target != null && hitTransform.IsChildOf(target);
        }

        return true;
    }

    // ---------- Montagem do carro (conversível com motorista) ----------

    private TrafficCar BuildCar(Vector3 position, Vector3 direction)
    {
        GameObject car = new GameObject("Carro_Transito");
        car.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
        car.transform.SetParent(transform, true);

        if (carModel != null)
        {
            BuildModelCar(car.transform);
        }
        else
        {
            BuildBoxCar(car.transform);
        }

        // Motorista (do lado esquerdo): o alien 3D dirigindo; sem modelo, o boneco antigo de cápsula.
        Transform driver = BuildModelDriver(car.transform);
        if (driver == null)
        {
            Material skin = Pick(driverSkins);
            driver = new GameObject("Motorista").transform;
            driver.SetParent(car.transform, false);
            driver.localPosition = new Vector3(-0.45f, 0f, -0.3f);
            Part(PrimitiveType.Capsule, "Tronco", driver, new Vector3(0f, 1.35f, 0f), new Vector3(0.55f, 0.4f, 0.55f), skin);
            Part(PrimitiveType.Sphere, "Cabeca", driver, new Vector3(0f, 1.95f, 0f), Vector3.one * 0.62f, skin);
            Part(PrimitiveType.Sphere, "Olho_E", driver, new Vector3(-0.13f, 1.98f, 0.25f), new Vector3(0.18f, 0.24f, 0.1f), eye, new Vector3(0f, 0f, -20f));
            Part(PrimitiveType.Sphere, "Olho_D", driver, new Vector3(0.13f, 1.98f, 0.25f), new Vector3(0.18f, 0.24f, 0.1f), eye, new Vector3(0f, 0f, 20f));
        }

        Rigidbody body = car.AddComponent<Rigidbody>();
        body.mass = 900f;
        body.isKinematic = true; // dirigindo: quem move é o TrafficCar
        body.interpolation = RigidbodyInterpolation.Interpolate;
        car.AddComponent<TelekinesisTarget>();
        car.AddComponent<ExplosiveVehicle>(); // tem gasolina: pode pegar fogo e explodir
        if (carModel != null)
        {
            car.AddComponent<HoverCar>(); // discos acesos, apagam quando o carro "morre"
        }

        TrafficCar trafficCar = car.AddComponent<TrafficCar>();
        trafficCar.SetDriver(driver.gameObject);
        return trafficCar;
    }

    // Carro 3D flutuante (Missão 15): modelo + colisores do tamanho dele + pintura sorteada.
    private void BuildModelCar(Transform car)
    {
        // Casco: de baixo dos discos (17 cm) até a linha da janela; cabine por cima.
        BoxCollider hull = car.gameObject.AddComponent<BoxCollider>();
        hull.center = new Vector3(0f, 0.615f, 0f);
        hull.size = new Vector3(1.76f, 0.89f, 3.2f);
        BoxCollider cabin = car.gameObject.AddComponent<BoxCollider>();
        cabin.center = new Vector3(0f, 1.38f, -0.2f);
        cabin.size = new Vector3(1.56f, 0.72f, 2.35f);

        GameObject visual = Instantiate(carModel, car);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;
        // O FBX do Blender vem com a frente apontando para trás (-Z): gira 180° para a frente
        // do modelo ficar no sentido em que o carro anda (+Z).
        visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        foreach (Collider extra in visual.GetComponentsInChildren<Collider>())
        {
            Destroy(extra);
        }

        int pair = UnityEngine.Random.Range(0, Mathf.Max(1, carPaints.Length / 2));
        Color main = carPaints.Length >= 2 ? carPaints[pair * 2] : Color.white;
        Color detail = carPaints.Length >= 2 ? carPaints[pair * 2 + 1] : Color.gray;
        Material paintMat = null, detailMat = null;
        foreach (Renderer r in visual.GetComponentsInChildren<Renderer>())
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null)
                {
                    continue;
                }

                string n = mats[i].name;
                if (n.Contains("Pintura") || n.Contains("Parachoque"))
                {
                    if (paintMat == null)
                    {
                        paintMat = new Material(mats[i]) { color = main };
                    }

                    mats[i] = paintMat;
                }
                else if (n.Contains("Detalhe"))
                {
                    if (detailMat == null)
                    {
                        detailMat = new Material(mats[i]) { color = detail };
                    }

                    mats[i] = detailMat;
                }
            }

            r.sharedMaterials = mats;
        }
    }

    private void BuildBoxCar(Transform carTransform)
    {
        GameObject car = carTransform.gameObject;
        BoxCollider hull = car.AddComponent<BoxCollider>();
        hull.center = new Vector3(0f, 0.65f, 0f);
        hull.size = new Vector3(2f, 1.3f, 4.2f);

        Material paint = Pick(bodyColors);
        Part(PrimitiveType.Cube, "Carroceria", car.transform, new Vector3(0f, 0.75f, 0f), new Vector3(2f, 0.8f, 4.2f), paint);
        Part(PrimitiveType.Cube, "Capo", car.transform, new Vector3(0f, 1.2f, 1.3f), new Vector3(1.9f, 0.15f, 1.5f), paint);
        Part(PrimitiveType.Cube, "Para_Brisa", car.transform, new Vector3(0f, 1.5f, 0.45f), new Vector3(1.85f, 0.55f, 0.08f), glass,
            new Vector3(-20f, 0f, 0f));

        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                Part(PrimitiveType.Cylinder, "Roda", car.transform, new Vector3(x * 1.0f, 0.4f, z * 1.4f), new Vector3(0.8f, 0.15f, 0.8f),
                    wheel, new Vector3(0f, 0f, 90f));
            }
        }
    }

    private static void Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material,
        Vector3? euler = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        DestroyImmediate(go.GetComponent<Collider>()); // só o colisor do casco conta
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        if (euler.HasValue)
        {
            go.transform.localRotation = Quaternion.Euler(euler.Value);
        }

        if (material != null)
        {
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
    }

    private Transform BuildModelDriver(Transform car)
    {
        if (driverModels == null || driverModels.Length == 0 || driverController == null)
        {
            return null;
        }

        GameObject source = driverModels[UnityEngine.Random.Range(0, driverModels.Length)];
        if (source == null)
        {
            return null;
        }

        GameObject model = Instantiate(source, car);
        model.name = "Motorista";
        model.transform.localPosition = carModel != null
            ? new Vector3(carSeatHips.x, 0.3f, carSeatHips.z)
            : new Vector3(-0.45f, 0.3f, -0.35f);
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * driverScale;

        if (model.TryGetComponent(out Animator animator))
        {
            animator.runtimeAnimatorController = driverController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }

        AlienAppearance.DressRandomCivil(model.transform);
        SeatedDriver seated = model.AddComponent<SeatedDriver>();
        if (carModel != null)
        {
            seated.Init(carSeatHips, carSeatPedals);
        }
        else
        {
            seated.Init(driverSeatHeight);
        }
        return model.transform;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }

    private static Material Pick(Material[] options)
    {
        return options != null && options.Length > 0 ? options[UnityEngine.Random.Range(0, options.Length)] : null;
    }
}
