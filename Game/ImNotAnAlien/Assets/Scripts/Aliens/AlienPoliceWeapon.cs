using UnityEngine;

// Arma dos alienígenas policiais. Quem decide QUANDO atacar é o AlienBrain;
// esta arma só cuida do tiro.
// Como desviar: quando a ponta da arma acende (aviso), o policial TRAVA a mira no lugar
// onde o jogador estava. Os tiros são rápidos, mas quem se move durante o aviso escapa.
// Cada disparo é uma rajada curta ("pah, pah").
[DisallowMultipleComponent]
public sealed class AlienPoliceWeapon : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Ponto de onde sai o tiro (a ponta da arma).")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Luz/esfera que acende durante o aviso de disparo.")]
    [SerializeField] private GameObject chargeIndicator;
    [SerializeField] private Material projectileMaterial;

    [Header("Disparo")]
    [Tooltip("Tempo (mínimo e máximo, em segundos) entre uma rajada e outra.")]
    [SerializeField] private Vector2 fireIntervalRange = new Vector2(1.6f, 2.4f);
    [Tooltip("Duração do aviso antes da rajada. É a janela para o jogador desviar.")]
    [SerializeField, Min(0f)] private float warningTime = 0.5f;
    [SerializeField, Min(1f)] private float fireRange = 22f;
    [Tooltip("Velocidade do tiro (m/s).")]
    [SerializeField, Min(1f)] private float shotSpeed = 38f;
    [Tooltip("Tiros por rajada (1 = tiro único).")]
    [SerializeField, Min(1)] private int burstCount = 2;
    [Tooltip("Intervalo entre os tiros de uma rajada (s).")]
    [SerializeField, Min(0.02f)] private float burstInterval = 0.15f;
    [SerializeField, Min(0f)] private float damage = 10f;
    [Tooltip("Imprecisão do tiro, em graus.")]
    [SerializeField, Min(0f)] private float spreadDegrees = 1.5f;
    [Tooltip("Os tiros também machucam outros alienígenas (o caos é bem-vindo).")]
    [SerializeField] private bool friendlyFire = true;

    private float cooldown;
    private float warningTimer = -1f;
    private int shotsRemaining;
    private float burstTimer;
    private Vector3 lockedTarget;
    private Vector3 indicatorBaseScale = Vector3.one;

    private void Awake()
    {
        if (muzzle == null)
        {
            muzzle = transform.Find("Corpo/Arma/Ponta");
        }

        if (chargeIndicator != null)
        {
            indicatorBaseScale = chargeIndicator.transform.localScale;
            chargeIndicator.SetActive(false);
        }

        ResetCooldown();
    }

    private void OnDisable()
    {
        CancelCharge();
    }

    private void OnEnable()
    {
        WorldOrigin.Shifted -= OnOriginShifted; // (evita assinar duas vezes)
        WorldOrigin.Shifted += OnOriginShifted;
    }

    private void OnDestroy()
    {
        WorldOrigin.Shifted -= OnOriginShifted;
    }

    // O mundo recentralizou (Missão 16): o alvo travado anda junto.
    private void OnOriginShifted(Vector3 delta)
    {
        lockedTarget -= delta;
    }

    public void ResetCooldown()
    {
        cooldown = Random.Range(fireIntervalRange.x, fireIntervalRange.y) * 0.6f; // a primeira rajada sai um pouco antes
    }

    public void CancelCharge()
    {
        warningTimer = -1f;
        shotsRemaining = 0;
        if (chargeIndicator != null)
        {
            chargeIndicator.SetActive(false);
        }
    }

    // Chamado a cada quadro pelo AlienBrain enquanto o policial está atacando.
    public void Tick(Transform player)
    {
        if (muzzle == null || player == null)
        {
            return;
        }

        // Rajada em andamento: atira no ponto travado.
        if (shotsRemaining > 0)
        {
            burstTimer -= Time.deltaTime;
            if (burstTimer <= 0f)
            {
                Fire(lockedTarget);
                shotsRemaining--;
                burstTimer = burstInterval;
                if (shotsRemaining == 0)
                {
                    cooldown = NextInterval();
                }
            }

            return;
        }

        // Aviso em andamento: a ponta da arma cresce.
        if (warningTimer >= 0f)
        {
            warningTimer -= Time.deltaTime;
            if (chargeIndicator != null)
            {
                float progress = 1f - Mathf.Clamp01(warningTimer / Mathf.Max(warningTime, 0.01f));
                chargeIndicator.transform.localScale = indicatorBaseScale * Mathf.Lerp(0.3f, 1.4f, progress);
            }

            if (warningTimer <= 0f)
            {
                warningTimer = -1f;
                if (chargeIndicator != null)
                {
                    chargeIndicator.SetActive(false);
                }

                shotsRemaining = burstCount;
                burstTimer = 0f;
            }

            return;
        }

        cooldown -= Time.deltaTime;
        if (cooldown > 0f)
        {
            return;
        }

        Vector3 target = player.position + Vector3.up * 0.4f; // altura do peito
        if (Vector3.Distance(muzzle.position, target) > fireRange || !HasLineOfSight(target, player))
        {
            cooldown = 0.3f; // tenta de novo daqui a pouco
            return;
        }

        // Começa o aviso e TRAVA a mira onde o jogador está agora.
        lockedTarget = target;
        warningTimer = warningTime;
        if (chargeIndicator != null)
        {
            chargeIndicator.SetActive(true);
        }

        GameFeel.PoliceCharge(muzzle.position);
    }

    // Quanto mais estrelas de procurado, mais rápido atiram (até 40% mais rápido no nível 5).
    private float NextInterval()
    {
        int stars = WantedSystem.Instance != null ? WantedSystem.Instance.Stars : 0;
        return Random.Range(fireIntervalRange.x, fireIntervalRange.y) * (1f - 0.08f * stars);
    }

    private bool HasLineOfSight(Vector3 target, Transform player)
    {
        if (!Physics.Linecast(muzzle.position, target, out RaycastHit hit, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return hit.collider.transform.IsChildOf(player);
    }

    private void Fire(Vector3 target)
    {
        Vector3 direction = (target - muzzle.position).normalized;
        direction = Quaternion.Euler(Random.Range(-spreadDegrees, spreadDegrees), Random.Range(-spreadDegrees, spreadDegrees), 0f) * direction;

        AlienProjectile.Spawn(projectileMaterial, muzzle.position, direction * shotSpeed, damage, transform, friendlyFire);
        GameFeel.PoliceShot(muzzle.position);
    }
}
