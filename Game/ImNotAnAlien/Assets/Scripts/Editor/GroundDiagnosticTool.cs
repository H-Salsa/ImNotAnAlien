using System.Text;
using UnityEditor;
using UnityEngine;

// Diagnóstico do chão (20/11): com o jogo rodando, aponte a câmera do jogo para um lugar estranho
// (ex.: uma mancha de cor com borda reta) e use o menu ImNotAnAlien > Diagnóstico > O que tem no centro da tela.
// Escreve no Console (e no Editor.log, que o Claude lê) o que existe ali: bloco, tipo, altura do chão,
// água, grama e todos os objetos visíveis naquele ponto.
public static class GroundDiagnosticTool
{
    [MenuItem("ImNotAnAlien/Diagnóstico/O que tem no centro da tela")]
    private static void Run()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Diagnóstico] Dê Play primeiro.");
            return;
        }

        Camera cam = Camera.main;
        WorldStreamer streamer = WorldStreamer.Instance;
        if (cam == null || streamer == null)
        {
            Debug.LogWarning("[Diagnóstico] Sem câmera principal ou sem WorldStreamer.");
            return;
        }

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        StringBuilder sb = new StringBuilder(2048);
        sb.AppendLine("[Diagnóstico] Centro da tela:");

        Vector3 point;
        if (Physics.Raycast(ray, out RaycastHit hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            sb.AppendLine($"  Raio bateu em: {Path(hit.collider.transform)} (y {hit.point.y:0.00}, distância {hit.distance:0} m)");
        }
        else
        {
            // Sem colisor: usa o plano y = 0.
            float t = ray.direction.y < -0.001f ? -ray.origin.y / ray.direction.y : 100f;
            point = ray.origin + ray.direction * t;
            sb.AppendLine("  Raio não bateu em nada (usando y = 0).");
        }

        Vector3 logical = WorldOrigin.ToLogical(point);
        sb.AppendLine($"  Ponto lógico: {logical.x:0.0}, {logical.y:0.00}, {logical.z:0.0}");
        sb.AppendLine($"  PlanetTerrain.Height aqui: {PlanetTerrain.Height(logical.x, logical.z):0.00}  (nível da água {PlanetTerrain.WaterLevel:0.00})");

        foreach (WorldBlock block in streamer.LoadedBlocks)
        {
            if (block == null)
            {
                continue;
            }

            Rect rect = streamer.BlockRectWorld(block.Coord);
            if (!rect.Contains(new Vector2(point.x, point.z)))
            {
                continue;
            }

            BlockType type = block.HasSpec ? block.Spec.type : BlockType.Bairro;
            sb.AppendLine($"  Bloco: {block.name}  coord {block.Coord}  tipo {(block.HasSpec ? type.ToString() : "?")}  " +
                          $"bioma {PlanetTerrain.BiomeOfBlock(block.Coord)}  modo do chão {PlanetTerrain.GroundMode(block.Coord, type)}");

            Transform ground = block.StaticRoot != null ? block.StaticRoot.Find("Chao") : null;
            if (ground != null && ground.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                Bounds b = filter.sharedMesh.bounds;
                Renderer r = ground.GetComponent<Renderer>();
                sb.AppendLine($"  Chão: altura de {b.min.y:0.00} a {b.max.y:0.00}  material {(r != null && r.sharedMaterial != null ? r.sharedMaterial.name + " / " + r.sharedMaterial.shader.name : "-")}");
            }

            Transform sea = block.StaticRoot != null ? block.StaticRoot.Find("Agua_Mar") : null;
            sb.AppendLine(sea != null
                ? $"  Água do bloco (Agua_Mar): SIM, y {sea.position.y:0.00}, ativa {sea.gameObject.activeInHierarchy}"
                : "  Água do bloco (Agua_Mar): não");

            GrassField grass = block.GetComponent<GrassField>();
            sb.AppendLine($"  Grama de triângulos: {(grass != null ? "sim" : "não")}");
        }

        // Tudo o que é desenhado em cima desse ponto (caixa do objeto passa por ele).
        sb.AppendLine("  Objetos desenhados neste ponto (nome · material/shader · fila · altura da caixa):");
        int count = 0;
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer)
            {
                continue;
            }

            Bounds b = r.bounds;
            if (point.x < b.min.x || point.x > b.max.x || point.z < b.min.z || point.z > b.max.z)
            {
                continue;
            }

            if (b.size.x < 20f && b.size.z < 20f)
            {
                continue; // só coisas grandes (chão, água, faixas)
            }

            Material m = r.sharedMaterial;
            sb.AppendLine($"    {Path(r.transform)} · {(m != null ? m.name + " / " + m.shader.name : "-")} · fila {(m != null ? m.renderQueue : 0)} · y {b.min.y:0.00} a {b.max.y:0.00}");
            if (++count >= 25)
            {
                break;
            }
        }

        Debug.Log(sb.ToString());
    }

    private static string Path(Transform t)
    {
        string path = t.name;
        for (int i = 0; i < 4 && t.parent != null; i++)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}
