using UnityEngine;
using UnityEngine.Rendering;

// Um RAIO desenhado com linhas (Missão 21): tronco em zigue-zague, brilho largo por baixo e galhos.
// O desenho é sorteado de novo várias vezes por segundo, então o raio "treme" enquanto existe.
// - ElementalBolt.Strike(...): raio de um ponto a outro, que some sozinho (disparo e saltos entre alvos).
// - ElementalBolt.Arc(...): arquinho preso a um objeto, sorteando pontos em volta dele (mão carregando,
//   corpo levando choque). Fica ligado até alguém destruir ou chamar Stop().
[DisallowMultipleComponent]
public sealed class ElementalBolt : MonoBehaviour
{
    private const int MaxPoints = 28;

    private LineRenderer core;
    private LineRenderer glow;
    private LineRenderer[] branches;
    private readonly Vector3[] points = new Vector3[MaxPoints];
    private readonly Vector3[] branchPoints = new Vector3[6];
    private int pointCount;

    private Vector3 from;
    private Vector3 to;
    private Transform fromAnchor;
    private float width;
    private float jitter;
    private float life;
    private float age;
    private float rejigTimer;
    private float rejigInterval = 0.035f;

    // Modo "arquinho": sorteia os dois pontos numa casca em volta do dono.
    private bool arcMode;
    private Vector3 arcCenter;
    private Vector3 arcExtents;
    private bool stopping;

    public static ElementalBolt Strike(Vector3 from, Vector3 to, float width, float seconds, int branchCount, Transform followFrom = null)
    {
        GameObject go = new GameObject("Raio");
        ElementalBolt bolt = go.AddComponent<ElementalBolt>();
        bolt.from = from;
        bolt.to = to;
        bolt.fromAnchor = followFrom;
        bolt.width = width;
        bolt.life = seconds;
        bolt.jitter = 1f;
        bolt.Build(branchCount, true);
        bolt.Rejig();
        return bolt;
    }

    // "center" e "extents" no espaço do dono (meia largura, meia altura, meia profundidade).
    public static ElementalBolt Arc(Transform owner, Vector3 center, Vector3 extents, float width)
    {
        GameObject go = new GameObject("Arco_Eletrico");
        go.transform.SetParent(owner, false);
        ElementalBolt bolt = go.AddComponent<ElementalBolt>();
        bolt.arcMode = true;
        bolt.arcCenter = center;
        bolt.arcExtents = extents;
        bolt.width = width;
        bolt.life = float.MaxValue;
        bolt.jitter = 0.6f;
        bolt.rejigInterval = 0.06f;
        bolt.Build(0, false);
        bolt.Rejig();
        return bolt;
    }

    public void SetArcVolume(Vector3 center, Vector3 extents)
    {
        arcCenter = center;
        arcExtents = extents;
    }

    // Apaga em um instante e destrói.
    public void Stop()
    {
        if (!stopping)
        {
            stopping = true;
            life = age + 0.08f;
        }
    }

    private void Build(int branchCount, bool withGlow)
    {
        Material material = ElementalFx.Additive("fx_beam", 3f);
        core = NewLine("Tronco", material, width);
        if (withGlow)
        {
            glow = NewLine("Brilho", ElementalFx.Additive("fx_beam", 1f), width * 5f);
            Color soft = ElementalFx.Main(ElementKind.Lightning);
            soft.a = 0.35f;
            glow.startColor = glow.endColor = soft;
        }

        branches = new LineRenderer[branchCount];
        for (int i = 0; i < branchCount; i++)
        {
            branches[i] = NewLine("Galho_" + i, material, width * 0.55f);
        }
    }

    private LineRenderer NewLine(string name, Material material, float lineWidth)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.widthMultiplier = lineWidth;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0.35f));
        line.startColor = ElementalFx.Hot(ElementKind.Lightning);
        line.endColor = ElementalFx.Main(ElementKind.Lightning);
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private void LateUpdate()
    {
        age += Time.deltaTime;
        if (age >= life)
        {
            Destroy(gameObject);
            return;
        }

        if (fromAnchor != null)
        {
            from = fromAnchor.position;
        }

        rejigTimer -= Time.deltaTime;
        if (rejigTimer <= 0f || arcMode)
        {
            if (rejigTimer <= 0f)
            {
                rejigTimer = rejigInterval;
                Rejig();
            }
            else if (arcMode)
            {
                Redraw(); // o dono andou: o arco acompanha sem trocar de desenho
            }
        }

        // Some afinando no fim.
        float remaining = life == float.MaxValue ? 1f : Mathf.Clamp01((life - age) / Mathf.Min(0.12f, life));
        core.widthMultiplier = width * remaining;
        if (glow != null)
        {
            glow.widthMultiplier = width * 5f * remaining;
        }

        for (int i = 0; i < branches.Length; i++)
        {
            branches[i].widthMultiplier = width * 0.55f * remaining;
        }
    }

    private Vector3 arcLocalA;
    private Vector3 arcLocalB;
    private int seed;

    private void Rejig()
    {
        seed = Random.Range(1, 1000000);
        if (arcMode)
        {
            arcLocalA = arcCenter + Vector3.Scale(Random.onUnitSphere, arcExtents);
            arcLocalB = arcCenter + Vector3.Scale(Random.onUnitSphere, arcExtents);
            // De vez em quando o arco some por um instante (pisca).
            core.enabled = Random.value > 0.25f;
        }

        Redraw();
    }

    private void Redraw()
    {
        Vector3 a = from;
        Vector3 b = to;
        if (arcMode)
        {
            Transform owner = transform.parent != null ? transform.parent : transform;
            a = owner.TransformPoint(arcLocalA);
            b = owner.TransformPoint(arcLocalB);
        }

        Random.State saved = Random.state;
        Random.InitState(seed);

        Vector3 axis = b - a;
        float distance = axis.magnitude;
        pointCount = Mathf.Clamp(Mathf.CeilToInt(distance * (arcMode ? 9f : 1.3f)) + 3, 4, MaxPoints);
        Vector3 direction = distance > 0.001f ? axis / distance : Vector3.forward;
        Vector3 side = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        Vector3 up = Vector3.Cross(direction, side);
        float amplitude = Mathf.Min(arcMode ? 0.12f : 0.85f, distance * 0.09f) * jitter;

        Vector2 drift = Vector2.zero;
        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            drift = Vector2.Lerp(drift, Random.insideUnitCircle, 0.75f);
            // Preso nas duas pontas. Conserto 18/11: no último ponto, Sin(PI) dá um número NEGATIVO
            // minúsculo (-0,00000009) e Pow(negativo, 0,6) = NaN: a linha ficava com um ponto inválido
            // e o Unity enchia o Console de "IsFinite(distanceAlongView)". O Max segura em zero.
            float envelope = Mathf.Max(0f, Mathf.Sin(t * Mathf.PI));
            points[i] = Vector3.Lerp(a, b, t) + (side * drift.x + up * drift.y) * amplitude * Mathf.Pow(envelope, 0.6f);
        }

        // As pontas ficam exatamente nos dois pontos.
        points[0] = a;
        points[pointCount - 1] = b;

        core.positionCount = pointCount;
        core.SetPositions(points);
        if (glow != null)
        {
            glow.positionCount = 2;
            glow.SetPosition(0, a);
            glow.SetPosition(1, b);
        }

        // Galhos: saem de um ponto do tronco e morrem no ar.
        for (int i = 0; i < branches.Length; i++)
        {
            int start = Random.Range(1, Mathf.Max(2, pointCount - 2));
            Vector3 origin = points[start];
            Vector3 heading = (direction + Random.onUnitSphere * 0.9f).normalized;
            float length = Mathf.Min(distance * 0.3f, Random.Range(1.2f, 3.5f));
            for (int k = 0; k < branchPoints.Length; k++)
            {
                float t = k / (float)(branchPoints.Length - 1);
                branchPoints[k] = origin + heading * (length * t) + Random.insideUnitSphere * (length * 0.12f * (k == 0 ? 0f : 1f));
            }

            branches[i].positionCount = branchPoints.Length;
            branches[i].SetPositions(branchPoints);
        }

        Random.state = saved;
    }
}
