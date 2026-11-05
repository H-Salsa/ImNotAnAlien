using UnityEngine;

// Gera estilhaços, som e tremida quando este objeto bate em algo com força.
// A telecinese adiciona este componente automaticamente em tudo que ela levanta,
// e os alienígenas já o recebem ao nascer; não é preciso colocar à mão.
[DisallowMultipleComponent]
public sealed class ImpactFeedback : MonoBehaviour
{
    [Tooltip("Intervalo mínimo (s) entre dois efeitos deste objeto, para não virar barulho contínuo.")]
    [SerializeField, Min(0f)] private float cooldown = 0.12f;

    private Color color = Color.white;
    private float nextAllowedTime;

    private void Awake()
    {
        Renderer firstRenderer = GetComponentInChildren<Renderer>();
        if (firstRenderer != null && firstRenderer.sharedMaterial != null)
        {
            color = firstRenderer.sharedMaterial.color;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (Time.time < nextAllowedTime || collision.contactCount == 0)
        {
            return;
        }

        nextAllowedTime = Time.time + cooldown;
        GameFeel.Impact(collision.GetContact(0).point, collision.relativeVelocity.magnitude, color);
    }
}
