using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Planetas no céu (Missão 20, pedido do autor: "ar de planeta alienígena, vários planetas no céu").
// Os planetas do Ultimate Space Kit ficam longe (perto do limite de visão da câmera) e acompanham a
// câmera — nunca chegam mais perto, como lua e estrelas. Um grande e alguns menores, em direções e
// alturas sorteadas pela semente do jogo (cada jogo tem o seu céu), girando devagar.
// Material "ImNotAnAlien/Sky Planet": sem névoa, iluminado pelo sol, brilho de atmosfera na borda.
// Criado pelo WorldStreamer ao montar o planeta (precisa do Kit de Natureza com "skyPlanets").
public sealed class SkyPlanets : MonoBehaviour
{
    private struct Body
    {
        public Transform pivot;
        public Vector3 direction;
        public float angle;      // tamanho no céu (graus)
        public float spin;       // graus por segundo
    }

    private readonly List<Body> bodies = new List<Body>();
    private Camera cam;

    public static SkyPlanets Create(NatureKit kit, int seed)
    {
        if (kit == null || kit.skyPlanets == null || kit.skyPlanets.Length == 0 || kit.skyPlanetMaterial == null)
        {
            return null;
        }

        SkyPlanets old = FindFirstObjectByType<SkyPlanets>();
        if (old != null)
        {
            Destroy(old.gameObject);
        }

        GameObject root = new GameObject("Ceu_Planetas");
        SkyPlanets sky = root.AddComponent<SkyPlanets>();
        sky.Build(kit, seed);
        return sky;
    }

    private void Build(NatureKit kit, int seed)
    {
        System.Random rng = new System.Random(seed * 7919 + 17);
        List<GameObject> pool = new List<GameObject>(kit.skyPlanets);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        // Um gigante, dois médios e dois/três pequenos, espalhados em volta.
        float[] sizes = { 16f, 8f, 7f, 4f, 3.2f, 2.6f };
        int count = Mathf.Min(pool.Count, 5 + rng.Next(2));
        float azimuth = (float)rng.NextDouble() * 360f;
        Color[] rims =
        {
            new Color(0.55f, 0.8f, 1f), new Color(1f, 0.65f, 0.85f), new Color(0.7f, 1f, 0.75f),
            new Color(1f, 0.85f, 0.55f), new Color(0.8f, 0.7f, 1f),
        };

        for (int i = 0; i < count; i++)
        {
            azimuth += 360f / count + (float)(rng.NextDouble() * 40.0 - 20.0);
            float elevation = i == 0 ? 22f + (float)rng.NextDouble() * 18f : 9f + (float)rng.NextDouble() * 40f;
            Vector3 direction = Quaternion.Euler(-elevation, azimuth, 0f) * Vector3.forward;

            Transform pivot = new GameObject("Planeta_" + (i + 1)).transform;
            pivot.SetParent(transform, false);
            GameObject model = Instantiate(pool[i], pivot, false);
            model.name = "Modelo";
            Normalize(model.transform);

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetFloat("_Haze", Mathf.Lerp(0.12f, 0.45f, 1f - sizes[i] / 16f));
            block.SetColor("_RimColor", rims[rng.Next(rims.Length)]);
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    mats[m] = kit.skyPlanetMaterial;
                }

                r.sharedMaterials = mats;
                r.SetPropertyBlock(block);
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
            {
                Destroy(c);
            }

            pivot.rotation = Quaternion.Euler((float)rng.NextDouble() * 40f - 20f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 40f - 20f);
            bodies.Add(new Body
            {
                pivot = pivot,
                direction = direction,
                angle = sizes[i] * (0.85f + (float)rng.NextDouble() * 0.3f),
                spin = ((float)rng.NextDouble() * 1.2f + 0.3f) * (rng.Next(2) == 0 ? 1f : -1f),
            });
        }
    }

    // Deixa o modelo com 1 m de diâmetro e centrado no pivô.
    private static void Normalize(Transform model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
        {
            bounds.Encapsulate(r.bounds);
        }

        float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float s = size > 0.0001f ? 1f / size : 1f;
        model.localScale *= s;
        model.position -= (bounds.center - model.parent.position) * s;
    }

    private void LateUpdate()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null)
            {
                return;
            }
        }

        Vector3 eye = cam.transform.position;
        float distance = Mathf.Min(cam.farClipPlane * 0.8f, 2500f);
        foreach (Body body in bodies)
        {
            if (body.pivot == null)
            {
                continue;
            }

            body.pivot.position = eye + body.direction * distance;
            body.pivot.localScale = Vector3.one * (2f * distance * Mathf.Tan(body.angle * 0.5f * Mathf.Deg2Rad));
            body.pivot.Rotate(Vector3.up, body.spin * Time.deltaTime, Space.Self);
        }
    }
}
