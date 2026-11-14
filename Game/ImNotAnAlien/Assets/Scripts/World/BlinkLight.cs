using UnityEngine;

// Luzinha que pisca (topo das antenas, Missão 20). Liga e desliga o próprio Renderer.
[DisallowMultipleComponent]
public sealed class BlinkLight : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float period = 1.4f;
    [SerializeField, Range(0.05f, 0.95f)] private float onFraction = 0.35f;

    private Renderer target;
    private float offset;

    private void Awake()
    {
        target = GetComponent<Renderer>();
        offset = Random.value * period; // as antenas não piscam todas juntas
    }

    private void Update()
    {
        if (target != null)
        {
            target.enabled = Mathf.Repeat(Time.time + offset, period) < period * onFraction;
        }
    }
}
