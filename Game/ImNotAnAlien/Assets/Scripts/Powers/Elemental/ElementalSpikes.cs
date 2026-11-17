using UnityEngine;

// Espinhos de gelo que brotam do chão onde o disparo carregado de gelo bate (Missão 21).
// Só enfeite: crescem depressa, ficam alguns segundos e derretem (encolhem). Sem colisão.
[DisallowMultipleComponent]
public sealed class ElementalSpikes : MonoBehaviour
{
    private Transform[] spikes;
    private Vector3[] sizes;
    private float[] delays;
    private float age;
    private float life;

    public static void Spawn(Vector3 point, float radius, int count, float seconds)
    {
        GameObject root = new GameObject("Espinhos_De_Gelo");
        root.transform.position = point;
        ElementalSpikes cluster = root.AddComponent<ElementalSpikes>();
        cluster.Build(Mathf.Max(1f, radius), Mathf.Max(1, count), seconds);
    }

    private void Build(float radius, int count, float seconds)
    {
        life = seconds;
        spikes = new Transform[count];
        sizes = new Vector3[count];
        delays = new float[count];
        Material ice = ElementalFx.IceMaterial();

        for (int i = 0; i < count; i++)
        {
            // O primeiro nasce no meio e é o maior; os outros em volta, tombados para fora.
            Vector2 circle = i == 0 ? Vector2.zero : Random.insideUnitCircle.normalized * Random.Range(0.25f, 0.8f) * radius;
            Vector3 position = transform.position + new Vector3(circle.x, 0f, circle.y);
            if (Physics.Raycast(position + Vector3.up * 2.5f, Vector3.down, out RaycastHit ground, 6f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                position.y = ground.point.y;
            }

            Transform spike = ElementalFx.MeshObject("Espinho_" + i, ElementalFx.Crystal, ice, transform).transform;
            spike.position = position - Vector3.up * 0.1f;
            Vector3 outward = new Vector3(circle.x, 0f, circle.y);
            Vector3 lean = (Vector3.up + (outward.sqrMagnitude > 0.01f ? outward.normalized * Random.Range(0.2f, 0.7f) : Vector3.zero)).normalized;
            spike.rotation = Quaternion.FromToRotation(Vector3.up, lean) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            float closeness = 1f - Mathf.Clamp01(outward.magnitude / radius);
            float tall = Mathf.Lerp(0.7f, 1.9f, closeness) * Random.Range(0.8f, 1.2f);
            float thick = tall * Random.Range(1.1f, 1.7f);
            sizes[i] = new Vector3(thick, tall, thick);
            delays[i] = (1f - closeness) * 0.12f;
            spike.localScale = Vector3.zero;
            spikes[i] = spike;
        }
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= life)
        {
            Destroy(gameObject);
            return;
        }

        float melt = Mathf.Clamp01((life - age) / 0.6f);
        for (int i = 0; i < spikes.Length; i++)
        {
            float grow = Mathf.Clamp01((age - delays[i]) / 0.14f);
            // Passa um pouco do tamanho e volta (o "estalo" do gelo brotando).
            float pop = grow < 1f ? Mathf.Sin(grow * Mathf.PI * 0.5f) * 1.12f : 1f + 0.12f * Mathf.Exp(-(age - delays[i] - 0.14f) * 18f);
            spikes[i].localScale = sizes[i] * (pop * melt);
        }
    }
}
