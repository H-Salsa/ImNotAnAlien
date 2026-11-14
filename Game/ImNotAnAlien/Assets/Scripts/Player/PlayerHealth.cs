using System;
using System.Collections;
using UnityEngine;

// Vida do jogador.
// - Regenera sozinha depois de alguns segundos sem levar dano.
// - Ao chegar a zero: "VOCÊ FOI DERROTADO" e renasce no ponto inicial depois de alguns segundos,
//   com a vida cheia e sem perder DNA nem progresso do setor.
[DisallowMultipleComponent]
public sealed class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [Tooltip("Segundos sem levar dano até começar a regenerar.")]
    [SerializeField, Min(0f)] private float regenDelay = 4f;
    [SerializeField, Min(0f)] private float regenPerSecond = 12f;
    [SerializeField, Min(0f)] private float respawnDelay = 3f;
    [Tooltip("Segundos de invulnerabilidade depois de renascer.")]
    [SerializeField, Min(0f)] private float respawnInvulnerability = 2f;

    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float lastDamageTime = -100f;
    private float invulnerableUntil;

    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }

    public event Action Changed;
    public event Action<Vector3> Damaged; // posição de onde veio o dano
    public event Action Died;
    public event Action Respawned;
    public event Action Arrested; // Missão 19: preso pela polícia

    public bool IsArrested { get; private set; }

    private void Awake()
    {
        Instance = this;
        CurrentHealth = maxHealth;
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
    }

    private void OnEnable()
    {
        WorldOrigin.Shifted += OnOriginShifted;
    }

    private void OnDisable()
    {
        WorldOrigin.Shifted -= OnOriginShifted;
    }

    // O mundo recentralizou (Missão 16): o ponto de renascer anda junto.
    private void OnOriginShifted(Vector3 delta)
    {
        spawnPosition -= delta;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (IsDead || CurrentHealth >= maxHealth || Time.time - lastDamageTime < regenDelay)
        {
            return;
        }

        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + regenPerSecond * Time.deltaTime);
        Changed?.Invoke();
    }

    // Evolução "Pele Alienígena": aumenta a vida máxima e cura a diferença.
    // Onde o jogador renasce (Missão 17: continua sendo o ponto inicial, mesmo ao carregar um jogo).
    public void SetSpawnPoint(Vector3 position, Quaternion rotation)
    {
        spawnPosition = position;
        spawnRotation = rotation;
    }

    public Vector3 SpawnPosition => spawnPosition;

    // Base (Missão 19): vida cheia na hora.
    public void HealFull()
    {
        if (IsDead)
        {
            return;
        }

        CurrentHealth = maxHealth;
        Changed?.Invoke();
    }

    // Leva o jogador para outro lugar (viagem rápida entre bases). Posição de MUNDO.
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        CharacterController controller = GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
        }

        transform.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
        if (controller != null)
        {
            controller.enabled = true;
        }

        invulnerableUntil = Time.time + respawnInvulnerability;
    }

    public void SetMaxHealth(float newMaxHealth)
    {
        newMaxHealth = Mathf.Max(1f, newMaxHealth);
        float gained = newMaxHealth - maxHealth;
        maxHealth = newMaxHealth;
        CurrentHealth = Mathf.Clamp(CurrentHealth + Mathf.Max(0f, gained), 0f, maxHealth);
        Changed?.Invoke();
    }

    public void TakeDamage(float amount, Vector3 sourcePosition)
    {
        if (IsDead || amount <= 0f || Time.time < invulnerableUntil)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        lastDamageTime = Time.time;

        GameFeel.PlayerHit(transform.position);
        CameraShake.AddTrauma(0.35f);
        Damaged?.Invoke(sourcePosition);
        Changed?.Invoke();

        if (CurrentHealth <= 0f)
        {
            StartCoroutine(DeathRoutine());
        }
    }

    private IEnumerator DeathRoutine()
    {
        IsDead = true;
        SetControlsEnabled(false);
        Died?.Invoke();

        yield return new WaitForSeconds(respawnDelay);

        // Teletransporta: o CharacterController precisa estar desligado para isso.
        CharacterController controller = GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
        }

        transform.SetPositionAndRotation(spawnPosition, spawnRotation);

        if (controller != null)
        {
            controller.enabled = true;
        }

        CurrentHealth = maxHealth;
        IsDead = false;
        invulnerableUntil = Time.time + respawnInvulnerability;
        SetControlsEnabled(true);
        Respawned?.Invoke();
        Changed?.Invoke();
    }

    // Algemado (Missão 19): trava movimento e poderes sem "morrer".
    public void SetControlsLocked(bool locked)
    {
        if (!IsDead)
        {
            SetControlsEnabled(!locked);
        }
    }

    // Preso: perde parte do DNA, volta ao ponto inicial com a vida cheia e o procurado zera.
    public void Arrest(float dnaLossFraction)
    {
        if (IsDead || IsArrested)
        {
            return;
        }

        StartCoroutine(ArrestRoutine(dnaLossFraction));
    }

    private IEnumerator ArrestRoutine(float dnaLossFraction)
    {
        IsArrested = true;
        SetControlsEnabled(false);
        if (DnaInventory.Instance != null)
        {
            int total = DnaInventory.Instance.Total;
            DnaInventory.Instance.SetTotal(total - Mathf.RoundToInt(total * Mathf.Clamp01(dnaLossFraction)));
        }

        Arrested?.Invoke();
        yield return new WaitForSeconds(respawnDelay);

        CharacterController controller = GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
        }

        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        if (controller != null)
        {
            controller.enabled = true;
        }

        CurrentHealth = maxHealth;
        invulnerableUntil = Time.time + respawnInvulnerability;
        IsArrested = false;
        SetControlsEnabled(true);
        Respawned?.Invoke();
        Changed?.Invoke();
    }

    private void SetControlsEnabled(bool enabled)
    {
        if (TryGetComponent(out PrototypeThirdPersonController movement))
        {
            movement.enabled = enabled;
        }

        if (TryGetComponent(out TelekinesisPower telekinesis))
        {
            telekinesis.enabled = enabled;
        }

        if (TryGetComponent(out DevourAbility devour))
        {
            devour.enabled = enabled;
        }
    }
}
