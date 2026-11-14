using UnityEngine;

// Pedaço de uma estrutura destruída.
// 1. Cai com física até POUSAR e ficar parado (o desmoronamento precisa ser visto).
// 2. Depois perde a colisão, afunda no chão encolhendo e é apagado.
// Assim os pedaços nunca se acumulam e não pesam o computador.
// Segurança: se ficar preso em algum lugar (ex.: telhado) e não parar, some depois de um tempo máximo.
// Missão 20: cacos marcados como escombro (StayAsRubble) não afundam: ao pousar, ficam parados no lugar
// (como parte do monte de entulho da casa) e somem junto com o bloco.
public sealed class CollapsingFragment : MonoBehaviour
{
    private const float MinFallTime = 1f;          // tempo mínimo com física, mesmo que pouse antes
    private const float RestTimeToSink = 0.35f;    // quanto tempo parado no chão antes de afundar
    private const float RestSpeed = 0.6f;          // abaixo disso (m/s) conta como "parado"
    private const float MaxPhysicsTime = 6f;       // segurança
    private const float SinkSpeed = 0.9f;
    private const float SinkDuration = 2.2f;

    private Rigidbody body;
    private float age;
    private float restTime;
    private float sinkStart = -1f;
    private Vector3 startScale;
    private float extraDelay;
    private bool rubble;
    private Transform rubbleParent;

    public void StayAsRubble(Transform parent)
    {
        rubble = true;
        rubbleParent = parent;
    }

    private void Start()
    {
        body = GetComponent<Rigidbody>();
        startScale = transform.localScale;
        extraDelay = Random.Range(0f, 0.8f); // nem todos afundam ao mesmo tempo
    }

    private void Update()
    {
        age += Time.deltaTime;

        if (sinkStart < 0f)
        {
            bool resting = body == null || body.linearVelocity.magnitude < RestSpeed;
            restTime = resting ? restTime + Time.deltaTime : 0f;

            bool landed = age >= MinFallTime + extraDelay && restTime >= RestTimeToSink;
            if (landed || age >= MaxPhysicsTime)
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
            Destroy(gameObject);
        }
    }

    // Vira escombro fixo: sem física (pode pisar em cima), preso ao lugar da casa.
    private void Settle()
    {
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
