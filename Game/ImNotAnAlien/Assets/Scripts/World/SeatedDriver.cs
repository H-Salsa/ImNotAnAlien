using UnityEngine;

// Motorista do trânsito com o modelo 3D do alien (Missão 15).
// A animação de dirigir é "sentada", mas o quadril muda de lugar de um modelo para outro;
// aqui o modelo é movido para o quadril ficar sempre sobre o banco do motorista.
// No carro 3D, as pernas também são dobradas (IK de duas articulações) para os pés ficarem
// nos pedais, em vez de atravessarem o assoalho.
[DisallowMultipleComponent]
public sealed class SeatedDriver : MonoBehaviour
{
    [Tooltip("Onde fica o quadril, em relação ao carro (x = lado, y = altura, z = frente).")]
    [SerializeField] private Vector3 hipsSeat = new Vector3(-0.34f, 0.73f, 0.02f);
    [Tooltip("Ponto entre os dois pedais, em relação ao carro.")]
    [SerializeField] private Vector3 pedals = new Vector3(-0.34f, 0.53f, 0.66f);
    [Tooltip("Distância de cada pé até o meio dos pedais (para os lados).")]
    [SerializeField, Min(0f)] private float footSpread = 0.12f;

    private bool fullSeat;   // carro 3D: posiciona o quadril em x/y/z e dobra as pernas
    private Transform hips;
    private Transform car;
    private Transform[] leftLeg;
    private Transform[] rightLeg;

    // Carro antigo (caixas): só ajusta a altura do quadril.
    public void Init(float seatHeight)
    {
        hipsSeat.y = seatHeight;
        fullSeat = false;
    }

    // Carro 3D: quadril no banco e pés nos pedais.
    public void Init(Vector3 seat, Vector3 pedalPoint)
    {
        hipsSeat = seat;
        pedals = pedalPoint;
        fullSeat = true;
    }

    private void Start()
    {
        car = transform.parent;
        if (TryGetComponent(out Animator animator) && animator.isHuman)
        {
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            leftLeg = new[]
            {
                animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),
                animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                animator.GetBoneTransform(HumanBodyBones.LeftFoot),
            };
            rightLeg = new[]
            {
                animator.GetBoneTransform(HumanBodyBones.RightUpperLeg),
                animator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
                animator.GetBoneTransform(HumanBodyBones.RightFoot),
            };
        }
    }

    private void LateUpdate()
    {
        if (hips == null || car == null)
        {
            return;
        }

        // Move o modelo para o quadril cair no lugar certo do banco.
        Vector3 hipsLocal = car.InverseTransformPoint(hips.position);
        Vector3 local = transform.localPosition;
        local.y += hipsSeat.y - hipsLocal.y;
        if (fullSeat)
        {
            local.x += hipsSeat.x - hipsLocal.x;
            local.z += hipsSeat.z - hipsLocal.z;
        }

        transform.localPosition = local;

        if (fullSeat)
        {
            Vector3 side = car.right * footSpread;
            Vector3 pedal = car.TransformPoint(pedals);
            SolveLeg(leftLeg, pedal - side);
            SolveLeg(rightLeg, pedal + side); // pé direito no acelerador
        }
    }

    // IK de duas articulações: coxa e canela se dobram (joelho para cima) até o pé chegar no alvo.
    private void SolveLeg(Transform[] leg, Vector3 target)
    {
        if (leg == null || leg[0] == null || leg[1] == null || leg[2] == null)
        {
            return;
        }

        Transform thigh = leg[0], shin = leg[1], foot = leg[2];
        Vector3 a = thigh.position;
        float upper = Vector3.Distance(a, shin.position);
        float lower = Vector3.Distance(shin.position, foot.position);
        Vector3 toTarget = target - a;
        float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upper - lower) + 0.01f, upper + lower - 0.001f);
        Vector3 dir = toTarget.normalized;

        // Joelho no plano "para cima" do carro.
        Vector3 pole = Vector3.ProjectOnPlane(car.up, dir).normalized;
        float cosA = Mathf.Clamp((upper * upper + distance * distance - lower * lower) / (2f * upper * distance), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 knee = a + dir * (upper * cosA) + pole * (upper * sinA);

        thigh.rotation = Quaternion.FromToRotation(shin.position - a, knee - a) * thigh.rotation;
        Vector3 footTarget = a + dir * distance;
        shin.rotation = Quaternion.FromToRotation(foot.position - shin.position, footTarget - shin.position) * shin.rotation;
    }
}
