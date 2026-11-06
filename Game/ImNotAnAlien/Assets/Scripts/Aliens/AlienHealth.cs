using System;
using UnityEngine;
using UnityEngine.AI;

// Vida dos alienígenas.
// - Dano por impacto depende da VELOCIDADE e do PESO do que bateu: caixa machuca pouco,
//   carro machuca muito, parede (objeto fixo) machuca bem.
// - Ao morrer, solta orbes de DNA (DnaOrbSystem) e o corpo fica acinzentado, "drenado".
// - Ferido (vida baixa): pode ser devorado (DevourAbility), rendendo o dobro de DNA.
// Teste rápido: durante o Play, clique com o botão direito no título deste
// componente no Inspector e escolha "Teste: derrotar este alienígena".
[DisallowMultipleComponent]
public sealed class AlienHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    [Header("Dano por impacto")]
    [Tooltip("Batidas mais lentas que isso (m/s) não causam dano.")]
    [SerializeField, Min(0f)] private float minImpactSpeed = 6f;
    [Tooltip("Dano por cada m/s acima da velocidade mínima, para um objeto de peso igual ao do alienígena.")]
    [SerializeField, Min(0f)] private float impactDamageScale = 2f;
    [Tooltip("Peso (kg) de referência: objetos com esse peso causam o dano 'normal'.")]
    [SerializeField, Min(1f)] private float referenceMass = 60f;
    [Tooltip("Multiplicador de dano ao bater em algo fixo (parede, chão, prédio).")]
    [SerializeField, Min(0f)] private float staticImpactMultiplier = 1.5f;
    [Tooltip("Limites do multiplicador por peso (objetos muito leves/pesados).")]
    [SerializeField] private Vector2 massMultiplierRange = new Vector2(0.3f, 3f);

    [Header("DNA")]
    [Tooltip("DNA total que este alienígena rende ao ser consumido.")]
    [SerializeField, Min(0)] private int dnaValue = 10;
    [Tooltip("Em quantos orbes o DNA é dividido.")]
    [SerializeField, Min(1)] private int orbCount = 5;

    [Header("Devorar")]
    [Tooltip("Abaixo desta fração da vida (0,5 = metade) o alienígena está ferido e pode ser devorado.")]
    [SerializeField, Range(0.05f, 1f)] private float woundedThreshold = 0.5f;

    [Header("Corpo drenado")]
    [SerializeField] private Color drainedTint = new Color(0.45f, 0.45f, 0.5f, 1f);
    [SerializeField, Range(0f, 1f)] private float drainedAmount = 0.65f;

    private const float CorpseFallOutHeight = -50f;

    private AlienBrain brain;

    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public float HealthFraction => CurrentHealth / maxHealth;
    public bool IsDead { get; private set; }
    public bool IsWounded => !IsDead && CurrentHealth <= maxHealth * woundedThreshold;
    public int DnaValue => dnaValue;

    // Cota (população dinâmica): setor de origem (-1 = descobrir pela posição ao morrer)
    // e se este alienígena conta para a cota. Definidos por quem cria o alienígena.
    public int QuotaSector { get; set; } = -1;
    public bool CountsForQuota { get; set; } = true;

    // Avisa outros sistemas (contador, DNA, etc.) quando este alienígena é derrotado.
    public event Action<AlienHealth> Died;

    private void Awake()
    {
        CurrentHealth = maxHealth;
        brain = GetComponent<AlienBrain>();
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
        // Sendo segurado pela telecinese: esbarrões no caminho não machucam.
        // O dano vale quando ele é arremessado/solto, ou quando outro objeto bate nele.
        if (brain != null && brain.IsHeld)
        {
            return;
        }

        float speed = collision.relativeVelocity.magnitude;
        if (speed <= minImpactSpeed)
        {
            return;
        }

        Rigidbody other = collision.rigidbody;
        float massMultiplier = other == null || other.isKinematic
            ? staticImpactMultiplier
            : Mathf.Clamp(other.mass / referenceMass, massMultiplierRange.x, massMultiplierRange.y);

        TakeDamage((speed - minImpactSpeed) * impactDamageScale * massMultiplier);
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

    [ContextMenu("Teste: ferir este alienígena")]
    private void DebugWound()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("Este teste só funciona durante o Play.");
            return;
        }

        TakeDamage(Mathf.Max(0f, CurrentHealth - maxHealth * woundedThreshold * 0.8f));
    }

    // Devorar: consome na hora, sem orbes. Retorna o DNA que o alienígena rende (sem o bônus).
    // O DevourAbility cuida da animação e de remover o corpo.
    public int Devour()
    {
        if (IsDead)
        {
            return 0;
        }

        IsDead = true;
        CurrentHealth = 0f;
        if (brain != null)
        {
            brain.OnDevoured();
            brain.enabled = false;
        }

        Died?.Invoke(this);
        return dnaValue;
    }

    private void Die()
    {
        IsDead = true;

        if (brain != null)
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
