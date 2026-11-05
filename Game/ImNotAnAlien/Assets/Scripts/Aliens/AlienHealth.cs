using System;
using UnityEngine;
using UnityEngine.AI;

// Vida dos alienígenas. Na Missão 4, a telecinese vai causar dano chamando TakeDamage.
// Para testar agora: durante o Play, clique com o botão direito no título deste
// componente no Inspector e escolha "Teste: derrotar este alienígena".
[DisallowMultipleComponent]
public sealed class AlienHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    [Header("Dano por impacto")]
    [Tooltip("Batidas mais lentas que isso (m/s) não causam dano.")]
    [SerializeField, Min(0f)] private float minImpactSpeed = 6f;
    [Tooltip("Dano por cada m/s acima da velocidade mínima.")]
    [SerializeField, Min(0f)] private float damagePerImpactSpeed = 8f;

    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }

    // Avisa outros sistemas (contador, DNA, etc.) quando este alienígena é derrotado.
    public event Action<AlienHealth> Died;

    private void Awake()
    {
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        if (CurrentHealth <= 0f)
        {
            Die();
        }
    }

    // Arremessado contra algo, ou atingido por um objeto arremessado.
    private void OnCollisionEnter(Collision collision)
    {
        float speed = collision.relativeVelocity.magnitude;
        if (speed > minImpactSpeed)
        {
            TakeDamage((speed - minImpactSpeed) * damagePerImpactSpeed);
        }
    }

    [ContextMenu("Teste: derrotar este alienígena")]
    private void DebugKill()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("Este teste só funciona durante o Play.");
            return;
        }

        TakeDamage(CurrentHealth);
    }

    private void Die()
    {
        IsDead = true;

        if (TryGetComponent(out AlienBrain brain))
        {
            brain.enabled = false;
        }

        if (TryGetComponent(out NavMeshAgent agent))
        {
            agent.enabled = false;
        }

        // Vira um corpo físico e tomba de um jeito meio cômico.
        if (!TryGetComponent(out Rigidbody body))
        {
            body = gameObject.AddComponent<Rigidbody>();
        }

        body.isKinematic = false;
        body.mass = 60f;
        body.AddForce(Vector3.up * 3f + UnityEngine.Random.insideUnitSphere * 1.5f, ForceMode.VelocityChange);
        body.AddTorque(UnityEngine.Random.insideUnitSphere * 6f, ForceMode.VelocityChange);

        Renderer skin = GetComponentInChildren<Renderer>();
        GameFeel.AlienDefeated(transform.position, skin != null && skin.sharedMaterial != null ? skin.sharedMaterial.color : Color.white);

        Debug.Log($"{name} foi derrotado.", this);
        Died?.Invoke(this);
    }
}
