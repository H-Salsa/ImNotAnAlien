using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Casa modelada no Blender (RetroHouse) que quebra de verdade, aos poucos.
// - Intacta: uma malha só (LODs) e uma caixa de colisão.
// - 1º golpe forte: a casa é trocada, sem ninguém notar, pela versão pré-fraturada (cacos sólidos
//   parados, cada um com colisão). Os cacos perto do golpe se soltam e voam na direção do impacto,
//   com força proporcional à velocidade de quem bateu: abre um BURACO na parede.
//   O carro "atravessa" (recupera parte da velocidade que perdeu na batida).
// - Novos golpes abrem mais buracos. Sem parede suficiente (ou vida zerada), desaba tudo:
//   paredes cedem primeiro, o telhado despenca um instante depois.
// - Os cacos mais baixos, a caixa de correio e a lixeira ficam como entulho; o resto afunda e some.
// A base (fundação) e o chão da casa (pedras, entrada) ficam no lugar.
[DisallowMultipleComponent]
public sealed class HouseBreakable : MonoBehaviour
{
    public GameObject source; // modelo (FBX) da casa ou do prédio
    public int palette;
    public bool mirror;
    public Transform visual;

    private const float RubbleShare = 0.35f;
    private const float CollapseWhenWallsLost = 0.45f;

    private sealed class Live
    {
        public RetroHouse.Piece piece;
        public Rigidbody body;
        public bool released;
        public bool rubble;
    }

    private readonly List<Live> live = new List<Live>();
    private bool fractured;
    private bool collapsed;
    private int wallCount;
    private int wallsReleased;

    public bool Collapsed => collapsed;

    // Troca a casa intacta pelos cacos parados (uma vez só).
    private void Fracture()
    {
        if (fractured)
        {
            return;
        }

        fractured = true;
        Material material = RetroHouse.HouseMaterial;

        // Some a casa inteira (LODs) e a caixa de colisão.
        if (TryGetComponent(out LODGroup group))
        {
            Destroy(group);
        }

        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
            Destroy(renderer.gameObject);
        }

        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("Colisor_"))
            {
                child.GetComponent<Collider>().enabled = false;
                Destroy(child.gameObject);
            }
        }

        // Chão e fundação ficam.
        Mesh ground = RetroHouse.GroundFor(this);
        if (ground != null)
        {
            GameObject g = new GameObject("Chao_Casa");
            g.transform.SetParent(transform, false);
            g.transform.SetPositionAndRotation(visual.position, visual.rotation);
            g.AddComponent<MeshFilter>().sharedMesh = ground;
            g.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        List<RetroHouse.Piece> pieces = RetroHouse.PiecesFor(this);
        List<RetroHouse.Piece> byHeight = new List<RetroHouse.Piece>(pieces);
        byHeight.Sort((a, b) => a.center.y.CompareTo(b.center.y));
        HashSet<RetroHouse.Piece> low = new HashSet<RetroHouse.Piece>();
        for (int i = 0; i < Mathf.RoundToInt(byHeight.Count * RubbleShare); i++)
        {
            low.Add(byHeight[i]);
        }

        foreach (RetroHouse.Piece p in pieces)
        {
            GameObject go = new GameObject(p.prop ? "Objeto_Casa" : "Caco");
            go.transform.SetParent(transform, true);
            go.transform.SetPositionAndRotation(visual.TransformPoint(p.center), visual.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.center = p.mesh.bounds.center;
            box.size = Vector3.Max(p.mesh.bounds.size * 0.92f, Vector3.one * 0.25f);

            Rigidbody body = go.AddComponent<Rigidbody>();
            Vector3 size = p.mesh.bounds.size;
            body.mass = Mathf.Clamp(size.x * size.y * size.z * 40f, 15f, 500f);
            body.maxDepenetrationVelocity = 1.5f; // cacos encostados: sem isso a física os "dispara"
            body.isKinematic = true;              // parado até ser solto
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (!p.prop)
            {
                go.AddComponent<StructurePart>(); // continua levando golpes enquanto está de pé
                if (!p.roof) wallCount++;
            }

            live.Add(new Live { piece = p, body = body, rubble = p.prop || low.Contains(p) });
        }

        Physics.SyncTransforms(); // os colisores novos já valem para achar o ponto mais perto do golpe
    }

    // Golpe que não derrubou a casa: abre um buraco onde bateu.
    // impact = direção × velocidade de quem bateu; severity = fração da vida tirada (0..1).
    public void Hit(Vector3 point, Vector3 impact, float severity, Rigidbody attacker)
    {
        if (collapsed)
        {
            return;
        }

        Fracture();
        float radius = Mathf.Lerp(1.6f, 4.5f, Mathf.Clamp01(severity * 2.5f));
        int opened = 0;
        foreach (Live l in live)
        {
            if (l.released || l.piece.roof)
            {
                continue;
            }

            if (Vector3.Distance(ClosestPoint(l, point), point) <= radius)
            {
                Release(l, point, impact, 1f);
                opened++;
            }
        }

        if (opened > 0)
        {
            PunchThrough(attacker, impact, 0.55f);
            DestructionDust.Spawn(point, 1.6f, 0.8f);
        }

        if (wallCount > 0 && wallsReleased >= wallCount * CollapseWhenWallsLost)
        {
            Collapse(point, impact * 0.5f, null);
        }
    }

    // Explosão: os cacos no alcance são arrancados para longe do centro (o telhado também voa).
    // Quanto mais perto, mais rápido. Se a casa perder parede demais (ou a vida acabar), o resto desaba.
    public void Blast(Vector3 center, float radius, float severity, bool lethal)
    {
        if (collapsed)
        {
            return;
        }

        // Explosão DENTRO da casa (carro que entrou pelo buraco, barril na garagem): a casa inteira
        // estoura de dentro para fora — todos os cacos voam e o telhado sobe antes de cair.
        Bounds body = RetroHouse.BodyFor(this);
        body.Expand(new Vector3(0.6f, 2f, 0.6f));
        bool inside = body.Contains(visual.InverseTransformPoint(center));

        Fracture();
        DestructionDust.Spawn(center, Mathf.Max(2f, radius * 0.45f), 1.4f);
        float reach = inside ? 100f : radius * Mathf.Lerp(0.9f, 1.5f, Mathf.Clamp01(severity * 1.5f));
        float scale = inside ? 10f : reach;
        if (inside)
        {
            GameFeel.Explosion(visual.TransformPoint(RetroHouse.BodyFor(this).center), radius * 1.3f);
        }

        foreach (Live l in live)
        {
            if (l.released || l.body == null)
            {
                continue;
            }

            float distance = Vector3.Distance(ClosestPoint(l, center), center);
            if (distance > reach)
            {
                continue;
            }

            float t = Mathf.Clamp01(distance / scale);
            Vector3 outward = l.body.worldCenterOfMass - center;
            outward.y = Mathf.Max(outward.y, 0f);
            float up = inside ? (l.piece.roof ? 1.6f : 0.7f) : Mathf.Lerp(0.9f, 0.35f, t);
            Vector3 direction = (outward.normalized + Vector3.up * up).normalized;
            float speed = (inside ? Mathf.Lerp(28f, 14f, t) : Mathf.Lerp(24f, 6f, t)) * Random.Range(0.8f, 1.2f);
            Release(l, direction * speed, Random.insideUnitSphere * Mathf.Lerp(10f, 3f, t));
        }

        if (inside || lethal || (wallCount > 0 && wallsReleased >= wallCount * CollapseWhenWallsLost))
        {
            Collapse(center, Vector3.zero, null);
        }
    }

    // Vida zerada (ou parede demais faltando): desaba tudo.
    public void Collapse(Vector3 point, Vector3 impact, Rigidbody attacker)
    {
        if (collapsed)
        {
            return;
        }

        collapsed = true;
        Fracture();
        PunchThrough(attacker, impact, 0.75f);
        CollapseDust();
        // Desaba de baixo para cima: cada andar cede um pouco depois do de baixo e o de cima despenca
        // sobre ele (casa: o telhado cai ~0,5 s depois; prédio de 7 andares: o topo ~1,4 s depois).
        float top = Mathf.Max(1f, RetroHouse.BodyFor(this).max.y);
        float span = Mathf.Clamp(0.25f + top * 0.055f, 0.45f, 1.5f);
        foreach (Live l in live)
        {
            if (l.released)
            {
                continue;
            }

            float height = Mathf.Clamp01(l.piece.center.y / top);
            float near = 1f / (1f + Vector3.Distance(l.body.position, point) * 0.25f);
            float delay = height * span + Random.Range(0f, 0.1f) * (1f - near);
            if (l.piece.roof)
            {
                StartCoroutine(ReleaseLater(l, Mathf.Max(delay, 0.3f), point, impact * 0.3f, 0.6f));
            }
            else
            {
                StartCoroutine(ReleaseLater(l, delay, point, impact, 0.8f));
            }
        }
    }

    // Poeira em cada andar enquanto desaba (uma nuvem por andar, até 6) + uma grande no chão.
    private void CollapseDust()
    {
        Bounds body = RetroHouse.BodyFor(this);
        float top = Mathf.Max(1f, body.max.y);
        float radius = Mathf.Clamp(Mathf.Max(body.size.x, body.size.z) * 0.32f, 2f, 5f);
        float span = Mathf.Clamp(0.25f + top * 0.055f, 0.45f, 1.5f);
        int clouds = Mathf.Clamp(Mathf.RoundToInt(top / 3f), 1, 6);
        for (int i = 0; i < clouds; i++)
        {
            float y = (i + 0.5f) * top / clouds;
            Vector3 position = visual.TransformPoint(new Vector3(body.center.x, y, body.center.z));
            DestructionDust.Spawn(position, radius, span + 1.2f);
        }

        DestructionDust.Spawn(visual.TransformPoint(new Vector3(body.center.x, 0.5f, body.center.z)), radius * 1.3f, span + 2.5f);
    }

    private IEnumerator ReleaseLater(Live l, float delay, Vector3 point, Vector3 impact, float strength)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        if (!l.released)
        {
            Release(l, point, impact, strength);
        }
    }

    // Solta um caco: ganha a velocidade do impacto (mais forte perto do golpe), um empurrão para fora
    // do ponto atingido e giro. Ao pousar, vira entulho ou afunda e some (CollapsingFragment).
    private void Release(Live l, Vector3 point, Vector3 impact, float strength)
    {
        if (l.body == null)
        {
            l.released = true;
            return;
        }

        Vector3 position = l.body.worldCenterOfMass;
        Vector3 away = position - point;
        float distance = away.magnitude;
        float falloff = strength / (1f + distance * 0.45f);
        Vector3 direction = impact.sqrMagnitude > 0.01f ? impact : away;
        float speed = Mathf.Clamp(impact.magnitude, 4f, 30f);
        Vector3 velocity = direction.normalized * speed * 0.6f * falloff
                           + away.normalized * 2.5f * falloff
                           + Vector3.up * Random.Range(0.5f, 2.5f) * falloff;
        Release(l, velocity, Random.insideUnitSphere * (1f + 5f * falloff));
    }

    private void Release(Live l, Vector3 velocity, Vector3 spin)
    {
        l.released = true;
        if (!l.piece.roof && !l.piece.prop) wallsReleased++;
        if (l.body == null)
        {
            return;
        }

        if (l.body.TryGetComponent(out StructurePart part))
        {
            Destroy(part); // caco solto não machuca mais a casa
        }

        l.body.transform.SetParent(null, true);
        l.body.isKinematic = false;
        l.body.linearVelocity = velocity;
        l.body.angularVelocity = spin;

        CollapsingFragment fragment = l.body.gameObject.AddComponent<CollapsingFragment>();
        if (l.rubble)
        {
            fragment.StayAsRubble(transform);
        }
    }

    // Quem bateu atravessa: recupera parte da velocidade perdida no choque.
    private static void PunchThrough(Rigidbody attacker, Vector3 impact, float keep)
    {
        if (attacker == null || attacker.isKinematic)
        {
            return;
        }

        Vector3 wanted = impact * keep;
        if (attacker.linearVelocity.sqrMagnitude < wanted.sqrMagnitude)
        {
            attacker.linearVelocity = Vector3.Lerp(attacker.linearVelocity, wanted, 0.8f);
        }
    }

    private static Vector3 ClosestPoint(Live l, Vector3 point)
    {
        Collider c = l.body != null ? l.body.GetComponent<Collider>() : null;
        return c != null && c.enabled ? c.ClosestPoint(point) : l.body.position;
    }
}
