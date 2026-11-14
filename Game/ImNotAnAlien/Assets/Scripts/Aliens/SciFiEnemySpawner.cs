using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Cria e remove os inimigos do Sci-Fi Kit (Missão 20). Nasce sozinho quando a cena começa.
// - Drones da polícia: 1 com 4 estrelas, 2 com 5. Chegam voando de longe; somem quando o
//   procurado cai.
// - Robô de 4 patas do exército: 1 por vez com 5 estrelas, chega andando de uns 45 m.
// - Trilobites: até 3 por perto quando o jogador está na Floresta Chiclete, na Cratera Funda ou
//   nas Dunas Roxas (só em blocos de mato: floresta e campo). Os que ficam longe somem.
// Os prefabs ficam em Resources/Enemies (feitos pela ferramenta "Preparar Sci-Fi Kit").
public sealed class SciFiEnemySpawner : MonoBehaviour
{
    private const float Tick = 1f;

    private static SciFiEnemySpawner instance;

    private GameObject dronePrefab;
    private GameObject quadPrefab;
    private GameObject trilobitePrefab;

    private readonly List<EnemyDrone> drones = new List<EnemyDrone>();
    private EnemyQuadShell quad;
    private readonly List<EnemyTrilobite> creatures = new List<EnemyTrilobite>();

    private float timer;
    private float droneCooldown;
    private float quadCooldown;
    private float creatureCooldown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null)
        {
            return;
        }

        GameObject go = new GameObject("Inimigos_SciFi");
        instance = go.AddComponent<SciFiEnemySpawner>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        dronePrefab = Resources.Load<GameObject>("Enemies/Drone");
        quadPrefab = Resources.Load<GameObject>("Enemies/QuadShell");
        trilobitePrefab = Resources.Load<GameObject>("Enemies/Trilobite");
    }

    private void Update()
    {
        droneCooldown -= Time.deltaTime;
        quadCooldown -= Time.deltaTime;
        creatureCooldown -= Time.deltaTime;
        timer -= Time.deltaTime;
        if (timer > 0f)
        {
            return;
        }

        timer = Tick;
        PlayerHealth player = PlayerHealth.Instance;
        if (player == null || player.IsDead)
        {
            return;
        }

        int stars = WantedSystem.Instance != null ? WantedSystem.Instance.Stars : 0;
        UpdateDrones(player.transform, stars);
        UpdateQuad(player.transform, stars);
        UpdateCreatures(player.transform);
    }

    // ---------- Drones ----------

    private void UpdateDrones(Transform player, int stars)
    {
        drones.RemoveAll(d => d == null);
        int wanted = stars >= AlienAppearance.ArmyFromStars ? 2 : stars >= AlienAppearance.HelmetFromStars ? 1 : 0;
        int active = 0;
        foreach (EnemyDrone drone in drones)
        {
            if (drone.IsLeaving)
            {
                continue;
            }

            if (active >= wanted && drone.IsAlive)
            {
                drone.Leave();
                continue;
            }

            active++;
        }

        if (dronePrefab == null || active >= wanted || droneCooldown > 0f)
        {
            return;
        }

        Vector2 dir = Random.insideUnitCircle.normalized;
        Vector3 spawn = player.position + new Vector3(dir.x, 0f, dir.y) * 35f + Vector3.up * 18f;
        GameObject go = Instantiate(dronePrefab, spawn, Quaternion.identity);
        go.name = "Drone_Policia";
        drones.Add(go.GetComponent<EnemyDrone>());
        droneCooldown = 5f;
    }

    // ---------- Robô de 4 patas ----------

    private void UpdateQuad(Transform player, int stars)
    {
        bool wanted = stars >= AlienAppearance.ArmyFromStars;
        if (quad != null)
        {
            if (!wanted && quad.IsAlive && Vector3.Distance(quad.transform.position, player.position) > 30f)
            {
                quad.Leave();
                quad = null;
            }
            else if (!quad.IsAlive)
            {
                quad = null;
                quadCooldown = 30f; // destruído: o próximo demora
            }

            return;
        }

        if (!wanted || quadPrefab == null || quadCooldown > 0f)
        {
            return;
        }

        if (TryGroundPoint(player.position, 40f, 55f, false, out Vector3 point))
        {
            Vector3 look = player.position - point;
            look.y = 0f;
            GameObject go = Instantiate(quadPrefab, point, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            go.name = "Robo_Exercito";
            quad = go.GetComponent<EnemyQuadShell>();
            quadCooldown = 5f;
        }
    }

    // ---------- Trilobites ----------

    private void UpdateCreatures(Transform player)
    {
        for (int i = creatures.Count - 1; i >= 0; i--)
        {
            EnemyTrilobite creature = creatures[i];
            if (creature == null)
            {
                creatures.RemoveAt(i);
                continue;
            }

            AlienHealth health = creature.GetComponent<AlienHealth>();
            if (health != null && health.IsDead)
            {
                creatures.RemoveAt(i); // o corpo fica (DNA); some sozinho depois
                continue;
            }

            if (Vector3.Distance(creature.transform.position, player.position) > 130f)
            {
                Destroy(creature.gameObject);
                creatures.RemoveAt(i);
            }
        }

        if (trilobitePrefab == null || creatures.Count >= 3 || creatureCooldown > 0f || !Planet.Active)
        {
            return;
        }

        if (!CreatureBiome(WorldOrigin.ToLogical(player.position)))
        {
            return;
        }

        if (TryGroundPoint(player.position, 35f, 60f, true, out Vector3 point))
        {
            GameObject go = Instantiate(trilobitePrefab, point, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            go.name = "Trilobite";
            creatures.Add(go.GetComponent<EnemyTrilobite>());
            creatureCooldown = 6f;
        }
    }

    private static bool CreatureBiome(Vector3 logical)
    {
        Vector2Int block = Planet.PlanetBlockAt(logical);
        if (!Planet.InsideLatitude(block.y))
        {
            return false;
        }

        Planet.Biome biome = Planet.RegionBiome(Planet.RegionAt(block));
        return biome == Planet.Biome.Floresta || biome == Planet.Biome.Cratera || biome == Planet.Biome.Dunas;
    }

    // Ponto no NavMesh a uma distância do jogador, fora da vista se possível.
    private static bool TryGroundPoint(Vector3 around, float min, float max, bool wildOnly, out Vector3 point)
    {
        Camera camera = Camera.main;
        WorldStreamer streamer = WorldStreamer.Instance;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            Vector3 guess = around + new Vector3(dir.x, 0f, dir.y) * Random.Range(min, max);
            guess.y = PlanetTerrain.SurfaceY(guess);
            if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                continue;
            }

            if (streamer != null && !streamer.IsLoaded(hit.position, 10f))
            {
                continue;
            }

            Vector3 logical = WorldOrigin.ToLogical(hit.position);
            if (wildOnly)
            {
                if (!CreatureBiome(logical) || streamer == null || streamer.Layout == null)
                {
                    continue;
                }

                BlockType type = streamer.Layout.Get(Planet.PlanetBlockAt(logical)).type;
                if (!PlanetTerrain.IsWild(type))
                {
                    continue;
                }
            }

            if (Planet.Active && Planet.TryGetBase(BaseIdNear(logical), out _))
            {
                continue; // nada surge nas bases
            }

            if (camera != null)
            {
                Vector3 viewport = camera.WorldToViewportPoint(hit.position + Vector3.up);
                if (viewport.z > 0f && viewport.x > 0f && viewport.x < 1f && viewport.y > 0f && viewport.y < 1f && attempt < 7)
                {
                    continue; // prefere surgir fora da tela
                }
            }

            point = hit.position;
            return true;
        }

        point = default;
        return false;
    }

    // Id da base no bloco (ou vazio): bases são zona segura.
    private static string BaseIdNear(Vector3 logical)
    {
        Vector2Int block = Planet.PlanetBlockAt(logical);
        foreach (Planet.BaseInfo info in Planet.Bases)
        {
            if (info.coord == block)
            {
                return info.id;
            }
        }

        return "";
    }
}
