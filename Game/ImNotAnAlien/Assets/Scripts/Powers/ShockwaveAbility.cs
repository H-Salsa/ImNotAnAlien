using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Onda de Impacto (evolução da telecinese). Tecla 2 (ou botão B/círculo do controle).
// Arremessa para longe tudo o que tem física ao redor do personagem: caixas, carros e alienígenas.
// Alienígenas atingidos levam dano direto e ainda podem se machucar ao bater em algo.
// Começa bloqueada; o EvolutionSystem chama SetLevel() quando a evolução é comprada.
[DisallowMultipleComponent]
public sealed class ShockwaveAbility : MonoBehaviour
{
    [System.Serializable]
    private struct LevelStats
    {
        public float radius;
        [Tooltip("Impulso da explosão. Objetos pesados voam menos.")]
        public float power;
        public float damage;
    }

    [Tooltip("Valores por nível (o nível 0 é bloqueado e não usa esta lista).")]
    [SerializeField] private LevelStats[] levels =
    {
        new LevelStats { radius = 8f, power = 900f, damage = 20f },
        new LevelStats { radius = 12f, power = 1400f, damage = 35f },
    };

    [SerializeField, Min(0f)] private float cooldown = 6f;
    [Tooltip("Quanto a explosão empurra para cima (1 = bastante).")]
    [SerializeField, Min(0f)] private float upwardsModifier = 1.2f;
    [SerializeField] private Color waveColor = new Color(0.55f, 0.4f, 1f, 1f);

    private readonly Collider[] overlaps = new Collider[128];
    private readonly HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();

    private InputAction shockwaveAction;
    private int level;
    private float readyTime;

    public bool Unlocked => level >= 1;

    // Segundos até poder usar de novo (0 = pronta).
    public float CooldownRemaining => Mathf.Max(0f, readyTime - Time.time);

    private void Awake()
    {
        shockwaveAction = new InputAction("OndaDeImpacto", InputActionType.Button, "<Keyboard>/2");
        shockwaveAction.AddBinding("<Gamepad>/buttonEast");
    }

    private void OnEnable()
    {
        shockwaveAction.Enable();
    }

    private void OnDisable()
    {
        shockwaveAction.Disable();
    }

    private void OnDestroy()
    {
        shockwaveAction.Dispose();
    }

    public void SetLevel(int newLevel)
    {
        level = Mathf.Clamp(newLevel, 0, levels.Length);
    }

    private void Update()
    {
        if (!Unlocked || GamePause.IsPaused || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        if (shockwaveAction.WasPressedThisFrame() && CooldownRemaining <= 0f)
        {
            Fire();
        }
    }

    private void Fire()
    {
        LevelStats stats = levels[level - 1];
        readyTime = Time.time + cooldown;

        // Se estiver segurando algo com a telecinese, solta antes de explodir.
        if (TryGetComponent(out TelekinesisPower telekinesis) && telekinesis.HeldBody != null)
        {
            telekinesis.ReleaseHeld();
        }

        Vector3 center = transform.position;
        int count = Physics.OverlapSphereNonAlloc(center, stats.radius, overlaps, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        pushed.Clear();
        for (int i = 0; i < count; i++)
        {
            Rigidbody body = overlaps[i].attachedRigidbody;
            if (body == null || body.transform.IsChildOf(transform) || !pushed.Add(body))
            {
                continue;
            }

            Push(body, center, stats);
        }

        GameFeel.Shockwave(center, stats.radius, waveColor);
    }

    private void Push(Rigidbody body, Vector3 center, LevelStats stats)
    {
        float distance = Vector3.Distance(center, body.worldCenterOfMass);
        float falloff = Mathf.Clamp01(1f - distance / stats.radius);

        AlienHealth alien = body.GetComponent<AlienHealth>();
        if (alien != null && !alien.IsDead)
        {
            // Vira corpo físico (como na telecinese) e se levanta sozinho depois.
            if (body.TryGetComponent(out AlienBrain brain) && brain.enabled)
            {
                brain.OnTelekinesisGrab();
                brain.OnTelekinesisRelease();
            }

            alien.TakeDamage(stats.damage * Mathf.Lerp(0.4f, 1f, falloff));
        }

        if (body.isKinematic)
        {
            return; // algo fixo que não deve voar
        }

        if (!body.TryGetComponent(out ImpactFeedback _))
        {
            body.gameObject.AddComponent<ImpactFeedback>();
        }

        body.AddExplosionForce(stats.power, center, stats.radius, upwardsModifier, ForceMode.Impulse);
    }
}
