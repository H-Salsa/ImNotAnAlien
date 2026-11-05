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

    private void Awake()
    {
        Instance = this;
        CurrentHealth = maxHealth;
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
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
