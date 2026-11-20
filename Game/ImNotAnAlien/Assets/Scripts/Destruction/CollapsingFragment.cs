using UnityEngine;

// Pedaço de uma estrutura destruída.
// 1. Cai com física até POUSAR e ficar parado (o desmoronamento precisa ser visto).
// 2. Depois perde a colisão, afunda no chão encolhendo e é apagado.
// Assim os pedaços nunca se acumulam e não pesam o computador.
// Segurança: se ficar preso em algum lugar (ex.: telhado) e não parar, some depois de um tempo máximo.
// Missão 20: cacos marcados como escombro (StayAsRubble) não afundam: ao pousar, ficam parados no lugar
// (como parte do monte de entulho da casa) e somem junto com o bloco.
// 18/11 (reaproveitamento): pedaço da caixa (PooledDebris) começa com Begin e, ao sumir, volta para a
// caixa (DebrisPool) em vez de ser apagado.
public sealed class CollapsingFragment : MonoBehaviour
{
    private const float MinFallTime = 1f;          // tempo mínimo com física, mesmo que pouse antes
    private const float RestTimeToSink = 0.35f;    // quanto tempo parado no chão antes de afundar
    private const float RestSpeed = 0.6f;          // abaixo disso (m/s) conta como "parado"
    private const float MaxPhysicsTime = 6f;       // segurança
    private const float SinkSpeed = 0.9f;
    private const float SinkDuration = 2.2f;

    // 20/11 (destruir a capital chegou a 1.095 cacos com física ao mesmo tempo, 60 FPS): acima de
    // Budget cacos caindo, todos ficam "com pressa" — pousam e afundam mais cedo, e a física de cada um
    // é mais simples. Com poucos cacos (uma casa só) nada muda.
    private const int Budget = 300;
    private static int falling;
    private bool counted;

    public static int Falling => falling;

    // 20/11 (pedido do autor): cacos PEQUENOS não batem uns nos outros enquanto caem (continuam batendo no
    // chão, nas paredes, nos cacos grandes, no jogador e nos carros). Montes de cacos pequenos eram a maior
    // parte do trabalho da física. Usa a camada 30 (livre no projeto, sem nome); ao pousar volta à camada dele.
    private const int SmallDebrisLayer = 30;
    private const float SmallSize = 1.6f; // m: maior lado do caco
    private static bool layerReady;
    private int originalLayer = -1;

    // Dominó curto (20/11, decisão do autor): 1 = caco de prédio derrubado pelo jogador (pode derrubar o
    // vizinho); 2 = caco de prédio derrubado por cacos (não derruba mais ninguém). Quem solta o caco define.
    public int Generation { get; set; } = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        falling = 0;
        layerReady = false;
    }

    private void UseSmallLayer()
    {
        if (!layerReady)
        {
            layerReady = true;
            Physics.IgnoreLayerCollision(SmallDebrisLayer, SmallDebrisLayer, true);
        }

        if (originalLayer >= 0 || !TryGetComponent(out Collider own))
        {
            return;
        }

        Vector3 size = own.bounds.size;
        if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < SmallSize)
        {
            originalLayer = gameObject.layer;
            gameObject.layer = SmallDebrisLayer;
        }
    }

    private void RestoreLayer()
    {
        if (originalLayer >= 0)
        {
            gameObject.layer = originalLayer;
            originalLayer = -1;
        }
    }

    private Rigidbody body;
    private float age;
    private float restTime;
    private float sinkStart = -1f;
    private Vector3 startScale;
    private float extraDelay;
    private bool rubble;
    private Transform rubbleParent;
    private bool begun;
    private int puffs;        // quantas fumacinhas este caco já soltou (cada quique solta uma, menor)
    private float nextPuff;   // espera curta entre uma e outra

    public void StayAsRubble(Transform parent)
    {
        rubble = true;
        rubbleParent = parent;
    }

    // Pedaço reaproveitado: começa a cair de novo, do zero.
    public void Begin(bool asRubble, Transform parent)
    {
        rubble = asRubble;
        rubbleParent = asRubble ? parent : null;
        Init();
        enabled = true;
    }

    private void Start()
    {
        if (!begun)
        {
            Init();
        }
    }

    private void Init()
    {
        begun = true;
        body = GetComponent<Rigidbody>();
        startScale = transform.localScale;
        extraDelay = Random.Range(0f, 0.8f); // nem todos afundam ao mesmo tempo
        age = 0f;
        restTime = 0f;
        sinkStart = -1f;
        puffs = 0;
        nextPuff = 0f;
        lastFall = 0f;
        if (body != null)
        {
            // Física mais simples para caco (padrão do Unity: 6 e 1). Montes de cacos custam bem menos.
            body.solverIterations = 3;
            body.solverVelocityIterations = 1;
        }

        if (!counted)
        {
            counted = true;
            falling++;
        }

        UseSmallLayer();
    }

    private void StopCounting()
    {
        RestoreLayer();
        if (counted)
        {
            counted = false;
            falling = Mathf.Max(0, falling - 1);
        }
    }

    private void OnDisable()
    {
        StopCounting();
    }

    private void OnDestroy()
    {
        StopCounting();
    }

    // 20/11: fumacinha quando o caco bate no chão. Em vez de esperar a mensagem de batida da física
    // (os cacos de baixo já nascem encostados no chão e os de cima caem uns sobre os outros), olha a
    // velocidade: vinha caindo rápido e de repente parou de cair = bateu em algo embaixo.
    // 20/11 (pedido do autor): o caco que quica solta fumaça em cada quique forte, cada vez menor
    // (até 3 por caco; quiques fracos, abaixo de ImpactFallSpeed, não soltam).
    private const float ImpactFallSpeed = 2f; // m/s caindo antes da batida
    private const int MaxPuffs = 3;
    private const float PuffGap = 0.2f;
    private float lastFall;

    private void CheckImpact()
    {
        if (puffs >= MaxPuffs || body == null || body.isKinematic)
        {
            return;
        }

        float fall = -body.linearVelocity.y; // > 0 = descendo
        if (lastFall > ImpactFallSpeed && fall < lastFall * 0.35f && age >= nextPuff)
        {
            nextPuff = age + PuffGap;
            float shrink = puffs == 0 ? 1f : puffs == 1 ? 0.7f : 0.5f;
            puffs++;
            Vector3 point = transform.position;
            float size = 0.8f;
            if (TryGetComponent(out Collider own))
            {
                Bounds b = own.bounds;
                point = new Vector3(b.center.x, b.min.y, b.center.z);
                size = (b.size.x + b.size.y + b.size.z) / 3f; // caco grande, fumaça um pouco maior
            }

            DestructionDust.Impact(point, size * shrink);
        }

        lastFall = fall;
    }

    private void Update()
    {
        age += Time.deltaTime;

        if (sinkStart < 0f)
        {
            CheckImpact();
            // Pressa: 0 até Budget cacos caindo, 1 com o dobro ou mais.
            float hurry = Mathf.Clamp01((falling - Budget) / (float)Budget);
            float restSpeed = Mathf.Lerp(RestSpeed, 1.5f, hurry);
            bool resting = body == null || body.linearVelocity.magnitude < restSpeed;
            restTime = resting ? restTime + Time.deltaTime : 0f;

            float minFall = Mathf.Lerp(MinFallTime + extraDelay, 0.4f, hurry);
            bool landed = age >= minFall && restTime >= Mathf.Lerp(RestTimeToSink, 0.1f, hurry);
            if (landed || age >= Mathf.Lerp(MaxPhysicsTime, 2.5f, hurry))
            {
                if (rubble)
                {
                    Settle();
                    return;
                }

                StartSinking();
            }

            return;
        }

        float t = (age - sinkStart) / SinkDuration;
        transform.position += Vector3.down * SinkSpeed * Time.deltaTime;
        transform.localScale = startScale * Mathf.Lerp(1f, 0.6f, t);

        if (t >= 1f)
        {
            if (TryGetComponent(out PooledDebris pooled))
            {
                DebrisPool.Return(pooled);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }

    // Vira escombro fixo: sem física (pode pisar em cima), preso ao lugar da casa.
    private void Settle()
    {
        StopCounting();
        if (body != null)
        {
            body.isKinematic = true;
        }

        if (rubbleParent != null)
        {
            transform.SetParent(rubbleParent, true);
        }

        enabled = false;
    }

    private void StartSinking()
    {
        StopCounting();
        sinkStart = age;
        if (TryGetComponent(out Collider part))
        {
            part.enabled = false;
        }

        if (body != null)
        {
            body.isKinematic = true;
        }
    }
}
