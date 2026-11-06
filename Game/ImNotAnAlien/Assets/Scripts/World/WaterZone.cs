using System.Collections.Generic;
using UnityEngine;

// Água rasa (rio). Fica numa caixa "gatilho" (trigger) do tamanho do rio.
// - Jogador dentro da água anda mais devagar e sai andando pelas margens (decisão D18: "nada até a margem").
// - Qualquer coisa que cai na água faz respingos e "tchibum".
// - Objetos com física ficam mais "lentos" dentro da água (parecem boiar).
[RequireComponent(typeof(Collider))]
public sealed class WaterZone : MonoBehaviour
{
    [Tooltip("Velocidade do jogador dentro da água (0,5 = metade).")]
    [SerializeField, Range(0.1f, 1f)] private float playerSpeedMultiplier = 0.5f;
    [Tooltip("Resistência da água para objetos com física.")]
    [SerializeField, Min(0f)] private float waterDrag = 2.5f;

    private readonly Dictionary<Rigidbody, float> originalDrag = new Dictionary<Rigidbody, float>();

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        Vector3 point = other.bounds.center;
        point.y = GetComponent<Collider>().bounds.max.y;

        PrototypeThirdPersonController player = other.GetComponentInParent<PrototypeThirdPersonController>();
        if (player != null)
        {
            player.SpeedMultiplier = playerSpeedMultiplier;
            GameFeel.Splash(point, 0.5f);
            return;
        }

        Rigidbody body = other.attachedRigidbody;
        if (body == null || body.isKinematic || originalDrag.ContainsKey(body))
        {
            return;
        }

        GameFeel.Splash(point, Mathf.InverseLerp(2f, 25f, body.linearVelocity.magnitude));
        originalDrag[body] = body.linearDamping;
        body.linearDamping = waterDrag;
    }

    private void OnTriggerExit(Collider other)
    {
        PrototypeThirdPersonController player = other.GetComponentInParent<PrototypeThirdPersonController>();
        if (player != null)
        {
            player.SpeedMultiplier = 1f;
            return;
        }

        Rigidbody body = other.attachedRigidbody;
        if (body != null && originalDrag.TryGetValue(body, out float drag))
        {
            body.linearDamping = drag;
            originalDrag.Remove(body);
        }
    }
}
