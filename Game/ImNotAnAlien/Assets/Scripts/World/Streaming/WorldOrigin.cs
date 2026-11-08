using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Origem flutuante (Missão 16).
// Longe da origem (0, 0, 0) os números de posição perdem precisão e a câmera começa a tremer.
// De tempos em tempos este componente "recentraliza" o mundo: tudo anda junto, de uma vez,
// para o jogador voltar para perto da origem. Na tela nada muda.
//
// Duas coordenadas:
// - MUNDO: a posição de verdade na Unity (transform.position). Muda quando o mundo recentraliza.
// - LÓGICA: a posição fixa no mapa (a que aparece nos setores, nas ruas e na grade de blocos).
//   lógica = mundo + Offset. Sem este componente na cena, Offset = 0 e as duas são iguais.
//
// Quem guarda uma posição de MUNDO num campo (ponto de renascer, último lugar visto etc.)
// assina o evento Shifted e subtrai o deslocamento. Transforms, corpos físicos, NavMesh,
// agentes do NavMesh, partículas e rastros já são ajustados aqui.
//
// Teste: tecla F9 recentraliza na hora (nada deve mudar na tela; o Console mostra o deslocamento).
[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class WorldOrigin : MonoBehaviour
{
    public static WorldOrigin Instance { get; private set; }

    // Deslocamento acumulado: lógica = mundo + Offset.
    public static Vector3 Offset { get; private set; }

    // Chamado depois de recentralizar. O valor é o deslocamento: tudo andou "-delta".
    public static event Action<Vector3> Shifted;

    [Tooltip("Recentraliza quando o jogador passa desta distância da origem (m), no plano X/Z.")]
    [SerializeField, Min(100f)] private float recenterDistance = 1000f;
    [Tooltip("O deslocamento é arredondado para múltiplos deste valor (o tamanho do bloco), para a grade continuar alinhada.")]
    [SerializeField, Min(1f)] private float snap = 100f;
    [Tooltip("F9 recentraliza na hora, para testar.")]
    [SerializeField] private bool testKey = true;

    private Transform player;
    private bool pending;
    private bool pendingForce;

    public static Vector3 ToLogical(Vector3 world) => world + Offset;
    public static Vector3 ToWorld(Vector3 logical) => logical - Offset;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Offset = Vector3.zero;
        Shifted = null;
        Instance = null;
    }

    private void Awake()
    {
        Instance = this;
        Offset = Vector3.zero;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            Offset = Vector3.zero;
        }
    }

    private void Start()
    {
        PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
        player = controller != null ? controller.transform : null;
    }

    private void Update()
    {
        if (player == null)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (testKey && keyboard != null && keyboard.f9Key.wasPressedThisFrame)
        {
            pending = true;
            pendingForce = true;
        }

        Vector3 p = player.position;
        if (new Vector2(p.x, p.z).magnitude > recenterDistance)
        {
            pending = true;
        }

        if (pending)
        {
            Recenter(pendingForce);
        }
    }

    // Recentraliza o mundo em volta do jogador. "force": mesmo perto da origem (tecla de teste).
    public void Recenter(bool force)
    {
        // Não mexe no NavMesh enquanto ele está sendo montado: tenta de novo no próximo quadro.
        if (WorldNavMesh.Instance != null && WorldNavMesh.Instance.IsBuilding)
        {
            pending = true;
            pendingForce |= force;
            return;
        }

        pending = false;
        pendingForce = false;

        Vector3 p = player != null ? player.position : Vector3.zero;
        Vector3 delta = new Vector3(Mathf.Round(p.x / snap) * snap, 0f, Mathf.Round(p.z / snap) * snap);
        if (delta == Vector3.zero)
        {
            if (!force)
            {
                return;
            }

            delta = new Vector3(snap, 0f, 0f); // teste perto da origem: anda um bloco
        }

        Shift(delta);
    }

    private void Shift(Vector3 delta)
    {
        // 1. Agentes do NavMesh: guarda para onde iam.
        List<(NavMeshAgent agent, bool hadPath, Vector3 destination)> agents = new List<(NavMeshAgent, bool, Vector3)>();
        foreach (NavMeshAgent agent in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agents.Add((agent, agent.hasPath || agent.pathPending, agent.destination));
            }
        }

        // 2. Move tudo: cada objeto raiz de cada cena aberta.
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                root.transform.position -= delta;
            }
        }

        // 3. Corpos físicos: "teletransporta" (sem a interpolação puxar de volta).
        foreach (Rigidbody body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            body.position = body.transform.position;
        }

        Physics.SyncTransforms();

        // 4. Partículas soltas no mundo, rastros e linhas.
        ShiftParticles(delta);
        foreach (TrailRenderer trail in FindObjectsByType<TrailRenderer>(FindObjectsSortMode.None))
        {
            trail.Clear();
        }

        foreach (LineRenderer line in FindObjectsByType<LineRenderer>(FindObjectsSortMode.None))
        {
            if (!line.useWorldSpace)
            {
                continue;
            }

            Vector3[] points = new Vector3[line.positionCount];
            line.GetPositions(points);
            for (int i = 0; i < points.Length; i++)
            {
                points[i] -= delta;
            }

            line.SetPositions(points);
        }

        Offset += delta;

        // 5. NavMesh anda junto e os agentes voltam para cima dele, indo para o mesmo lugar.
        if (WorldNavMesh.Instance != null)
        {
            WorldNavMesh.Instance.OnOriginShifted(delta);
        }

        foreach ((NavMeshAgent agent, bool hadPath, Vector3 destination) in agents)
        {
            if (agent == null || !agent.enabled)
            {
                continue;
            }

            agent.Warp(agent.transform.position);
            if (hadPath && agent.isOnNavMesh)
            {
                agent.SetDestination(destination - delta);
            }
        }

        // 6. Quem guarda posições de mundo em campos ajusta agora.
        Shifted?.Invoke(delta);

        Debug.Log($"Mundo recentralizado: deslocamento ({delta.x:0}, {delta.z:0}). Origem lógica agora em ({Offset.x:0}, {Offset.z:0}).");
    }

    private static void ShiftParticles(Vector3 delta)
    {
        ParticleSystem.Particle[] buffer = null;
        foreach (ParticleSystem system in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            if (system.main.simulationSpace != ParticleSystemSimulationSpace.World || system.particleCount == 0)
            {
                continue;
            }

            if (buffer == null || buffer.Length < system.particleCount)
            {
                buffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(system.particleCount)];
            }

            int count = system.GetParticles(buffer);
            for (int i = 0; i < count; i++)
            {
                buffer[i].position -= delta;
            }

            system.SetParticles(buffer, count);
        }
    }
}
