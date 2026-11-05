using System;
using UnityEngine;
using UnityEngine.AI;

// Vida dos alienígenas. Batidas fortes (telecinese) causam dano.
// Ao morrer, solta orbes de DNA (DnaOrbSystem) e o corpo fica acinzentado, "drenado".
// Teste rápido: durante o Play, clique com o botão direito no título deste
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

    [Header("DNA")]
    [Tooltip("DNA total que este alienígena rende ao ser consumido.")]
    [SerializeField, Min(0)] private int dnaValue = 10;
    [Tooltip("Em quantos orbes o DNA é dividido.")]
    [SerializeField, Min(1)] private int orbCount = 5;

    [Header("Corpo drenado")]
    [SerializeField] private Color drainedTint = new Color(0.45f, 0.45f, 0.5f, 1f);
    [SerializeField, Range(0f, 1f)] private float drainedAmount = 0.65f;

    private const float CorpseFallOutHeight = -50f;

    public float MaxHealth => maxHealth;
    public int DnaValue => dnaValue;
    public float CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }

    // Avisa outros sistemas (contador, DNA, etc.) quando este alienígena é derrotado.
    public event Action<AlienHealth> Died;

    private void Awake()
    {
        CurrentHealth = maxHealth;
    }

    private void Update()
    {
        // Corpo que caiu para fora do mapa: remove (o DNA já foi liberado).
        if (IsDead && transform.position.y < CorpseFallOutHeight)
        {
            Destroy(gameObject);
        }
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

        DnaOrbSystem.SpawnFrom(this, transform.position + Vector3.up * 1.2f, dnaValue, orbCount);
        TintAsDrained();

        Debug.Log($"{name} foi derrotado.", this);
        Died?.Invoke(this);
    }

    // Deixa o corpo acinzentado: o DNA saiu dele.
    private void TintAsDrained()
    {
        foreach (Renderer part in GetComponentsInChildren<Renderer>(true))
        {
            Material material = part.material; // cria uma cópia só para este corpo
            material.color = Color.Lerp(material.color, drainedTint, drainedAmount);
        }
    }
}
