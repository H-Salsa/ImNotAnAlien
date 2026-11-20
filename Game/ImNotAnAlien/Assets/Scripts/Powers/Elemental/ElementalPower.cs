using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// PODER ELEMENTAL do protagonista (Missão 21): fogo, gelo e eletricidade no BOTÃO ESQUERDO do mouse.
// (A telecinese continua no botão direito.)
// - Toque rápido: disparo fraco e barato.
// - Segurar: carrega (a mão brilha, a mira fecha); soltar dispara forte, em área.
// - Roda do mouse ou Q: troca o elemento (ícones no canto de baixo, à direita).
// - Barra de ENERGIA: cada disparo gasta; volta sozinha e ao absorver DNA.
// Controle de videogame: gatilho direito dispara, botão de ombro direito troca.
// Este componente entra sozinho no jogador ao dar Play (não precisa colocar na cena).
// Os números ficam em Assets/Resources/Elemental/ElementalTuning.asset.
// A animação do braço é tocada pelo PlayerAnimator; os efeitos, pelo ElementalFx.
[DisallowMultipleComponent]
public sealed class ElementalPower : MonoBehaviour
{
    private const int ElementCount = 3;

    public static ElementalPower Instance { get; private set; }

    private readonly RaycastHit[] hits = new RaycastHit[24];
    private readonly List<Vector3> jumps = new List<Vector3>();
    private readonly bool[] unlocked = { true, true, true };

    private InputAction fireAction;
    private InputAction nextAction;
    private Transform cameraTransform;
    private PrototypeThirdPersonController mover;
    private PlayerHealth health;
    private TelekinesisPower telekinesis;
    private Animator animator;
    private Transform rightHand;
    private Transform leftHand;
    private ElementalTuning tuning;
    private DnaInventory inventory;

    private bool pressing;
    private float pressTime;
    private bool cursorWasLocked;
    private float nextShotTime;
    private float lastUseTime = -10f;
    private float lastCastTime = -10f;
    private float scrollCooldown;

    private bool shotPending;
    private float shotTime;
    private float shotCharge;
    private ElementKind shotKind;

    private Transform chargeRoot;
    private readonly GameObject[] chargeVisuals = new GameObject[ElementCount];
    private readonly ParticleSystem[] chargeGlows = new ParticleSystem[ElementCount];
    private Transform fireCore;
    private Transform iceCore;
    private ElementalBolt[] chargeArcs;
    private Light chargeLight;

    // ---------- Para os outros scripts (animação, HUD) ----------

    public ElementKind Element { get; private set; } = ElementKind.Fire;
    public float Energy { get; private set; }
    public float Energy01 => tuning != null ? Mathf.Clamp01(Energy / tuning.maxEnergy) : 1f;
    public bool IsCharging { get; private set; }
    public float Charge01 { get; private set; }
    public int CastCount { get; private set; }      // sobe a cada disparo (gatilho da animação)
    public int SwitchCount { get; private set; }    // sobe a cada troca de elemento (pulso no HUD)
    public int DenyCount { get; private set; }      // sobe quando falta energia (barra pisca)
    public Vector3 AimPoint { get; private set; }

    // O corpo deve virar para a mira: carregando ou logo depois de disparar.
    public bool AimActive => pressing || IsCharging || Time.time - lastCastTime < 0.6f;

    // Quanto o braço deve apontar direto para a mira (1 durante o gesto do disparo).
    public float ArmAim01
    {
        get
        {
            float t = Time.time - lastCastTime;
            return t < 0.4f ? 1f : Mathf.Clamp01(1f - (t - 0.4f) / 0.25f);
        }
    }

    public bool IsUnlocked(ElementKind kind) => unlocked[(int)kind];

    // Para a história: liberar um elemento ao consumir aliens de certo bioma (ainda não usado).
    public void SetUnlocked(ElementKind kind, bool value)
    {
        unlocked[(int)kind] = value;
        if (!value && Element == kind)
        {
            Cycle(1);
        }
    }

    // ---------- Entrada automática no jogador ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Attach();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Attach();
    }

    private static void Attach()
    {
        PrototypeThirdPersonController player = FindFirstObjectByType<PrototypeThirdPersonController>();
        if (player != null && !player.TryGetComponent(out ElementalPower _))
        {
            player.gameObject.AddComponent<ElementalPower>();
        }
    }

    // ---------- Vida do componente ----------

    private void Awake()
    {
        Instance = this;
        tuning = ElementalTuning.Current;
        Energy = tuning.maxEnergy;
        mover = GetComponent<PrototypeThirdPersonController>();
        health = GetComponent<PlayerHealth>();
        telekinesis = GetComponent<TelekinesisPower>();

        fireAction = new InputAction("Poder_Elemental", InputActionType.Button, "<Mouse>/leftButton");
        fireAction.AddBinding("<Gamepad>/rightTrigger");
        nextAction = new InputAction("Trocar_Elemento", InputActionType.Button, "<Keyboard>/q");
        nextAction.AddBinding("<Gamepad>/rightShoulder");

        GameObject hud = new GameObject("HUD_Elemental");
        hud.transform.SetParent(transform, false);
        hud.AddComponent<ElementalHud>().Bind(this);
    }

    private void Start()
    {
        // 18/11: deixa a caixa de efeitos cheia já no carregamento (o primeiro tiro não trava).
        ElementalFx.Prewarm(64);

        inventory = DnaInventory.Instance;
        if (inventory != null)
        {
            inventory.Changed += OnDnaChanged;
        }
    }

    private void OnEnable()
    {
        fireAction.Enable();
        nextAction.Enable();
    }

    private void OnDisable()
    {
        fireAction.Disable();
        nextAction.Disable();
        CancelCharge();
        shotPending = false;
        ElementalFxRunner.StopHum();
        if (chargeRoot != null)
        {
            chargeRoot.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        fireAction.Dispose();
        nextAction.Dispose();
        if (inventory != null)
        {
            inventory.Changed -= OnDnaChanged;
        }

        if (chargeRoot != null)
        {
            Destroy(chargeRoot.gameObject);
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    // Absorver DNA devolve energia (a "energia genética" do protagonista).
    private void OnDnaChanged(int total, int delta)
    {
        if (delta > 0)
        {
            Energy = Mathf.Min(tuning.maxEnergy, Energy + delta * tuning.energyPerDna);
        }
    }

    private void FindBody()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            }
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || GamePause.IsPaused)
        {
            if (GamePause.IsPaused)
            {
                CancelCharge(); // menu de evoluções ou mapa aberto: a carga não fica guardada
            }

            return;
        }

        FindBody();
        tuning = ElementalTuning.Current;

        // Energia volta sozinha depois de um instante sem usar.
        if (!IsCharging && Time.time - lastUseTime >= tuning.regenDelay)
        {
            Energy = Mathf.Min(tuning.maxEnergy, Energy + tuning.regenPerSecond * dt);
        }

        bool blocked = cameraTransform == null || !cursorWasLocked ||
                       (health != null && (health.IsDead || health.IsArrested)) ||
                       (mover != null && (!mover.enabled || mover.IsSwimming));
        if (blocked)
        {
            CancelCharge();
            shotPending = false;
            return;
        }

        UpdateSwitch(dt);
        UpdateAim();
        UpdateFireButton();

        if (shotPending && Time.time >= shotTime)
        {
            shotPending = false;
            Shoot(shotKind, shotCharge);
        }
    }

    private void LateUpdate()
    {
        cursorWasLocked = Cursor.lockState == CursorLockMode.Locked; // o clique que trava o cursor não dispara
        UpdateChargeVisual();
    }

    // ---------- Troca de elemento ----------

    private void UpdateSwitch(float dt)
    {
        scrollCooldown -= dt;
        int step = 0;
        if (nextAction.WasPressedThisFrame())
        {
            step = 1;
        }
        else if (scrollCooldown <= 0f && Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                step = scroll > 0f ? -1 : 1;
                scrollCooldown = 0.14f;
            }
        }

        if (step != 0)
        {
            Cycle(step);
        }
    }

    private void Cycle(int step)
    {
        int index = (int)Element;
        for (int i = 0; i < ElementCount; i++)
        {
            index = (index + step + ElementCount) % ElementCount;
            if (unlocked[index])
            {
                break;
            }
        }

        if (index == (int)Element)
        {
            return;
        }

        CancelCharge();
        Element = (ElementKind)index;
        SwitchCount++;
        Vector3 hand = HandPoint(Element);
        ElementalFxRunner.Play(ElementalFxRunner.Sound.Switch, hand, 0.6f, 0.85f + 0.2f * index);
        ElementalFx.Muzzle(Element, hand, Vector3.up, 0f);
    }

    // ---------- Botão de disparo ----------

    private void UpdateFireButton()
    {
        if (fireAction.WasPressedThisFrame())
        {
            pressing = true;
            pressTime = Time.time;
        }

        if (!pressing)
        {
            return;
        }

        float held = Time.time - pressTime;
        if (!IsCharging && held >= tuning.holdToCharge)
        {
            if (Energy >= tuning.tapCost)
            {
                IsCharging = true;
            }
            else
            {
                Deny();
                pressing = false;
                return;
            }
        }

        if (IsCharging)
        {
            // A carga sobe com o tempo, até onde a energia alcança.
            float byTime = Mathf.Clamp01((held - tuning.holdToCharge) / tuning.fullChargeTime);
            float byEnergy = tuning.chargedCost > tuning.tapCost
                ? Mathf.Clamp01((Energy - tuning.tapCost) / (tuning.chargedCost - tuning.tapCost))
                : 1f;
            Charge01 = Mathf.Min(byTime, byEnergy);
            lastUseTime = Time.time;
        }

        if (!fireAction.IsPressed())
        {
            pressing = false;
            float charge = IsCharging ? Charge01 : 0f;
            IsCharging = false;
            Charge01 = 0f;
            Release(charge);
        }
    }

    private void Release(float charge)
    {
        if (shotPending)
        {
            return; // já tem um disparo saindo
        }

        float cost = tuning.Cost(charge);
        if (Energy < cost)
        {
            Deny();
            return;
        }

        Energy -= cost;
        lastUseTime = Time.time;
        shotPending = true;
        shotKind = Element;
        shotCharge = charge;
        // Cliques muito seguidos esperam o intervalo mínimo; o gesto da mão sempre vem antes do disparo.
        float start = Mathf.Max(Time.time, nextShotTime);
        shotTime = start + tuning.castDelay;
        nextShotTime = start + tuning.shotInterval;
        lastCastTime = start;
        CastCount++;
    }

    private void Deny()
    {
        DenyCount++;
        Vector3 hand = HandPoint(Element);
        ElementalFx.Fizzle(Element, hand);
        ElementalFxRunner.Play(ElementalFxRunner.Sound.Deny, hand, 0.7f);
    }

    private void CancelCharge()
    {
        pressing = false;
        IsCharging = false;
        Charge01 = 0f;
    }

    // ---------- Mira ----------

    private void UpdateAim()
    {
        if (!AimActive && !shotPending)
        {
            return;
        }

        AimPoint = FindAimPoint(out _);
    }

    // Ponto que o centro da tela aponta, ignorando o jogador, o que a telecinese segura
    // e o que estiver entre a câmera e o personagem.
    private Vector3 FindAimPoint(out RaycastHit nearest)
    {
        nearest = default;
        Vector3 origin = cameraTransform.position;
        Vector3 forward = cameraTransform.forward;
        float minDistance = Mathf.Max(0f, Vector3.Dot(transform.position + Vector3.up * 1.3f - origin, forward));
        Rigidbody heldBody = telekinesis != null ? telekinesis.HeldBody : null;

        int count = Physics.RaycastNonAlloc(origin, forward, hits, tuning.range + minDistance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.distance < minDistance || hit.collider.transform.IsChildOf(transform) ||
                (heldBody != null && hit.collider.attachedRigidbody == heldBody))
            {
                continue;
            }

            if (hit.distance < best)
            {
                best = hit.distance;
                nearest = hit;
                found = true;
            }
        }

        return found ? nearest.point : origin + forward * (tuning.range + minDistance);
    }

    // De onde o disparo sai: a mão (ou o meio das duas, no gelo).
    private Vector3 HandPoint(ElementKind kind)
    {
        Vector3 forward = cameraTransform != null
            ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized
            : transform.forward;
        if (rightHand == null || leftHand == null)
        {
            return transform.position + Vector3.up * 1.35f + forward * 0.6f;
        }

        switch (kind)
        {
            case ElementKind.Ice: return (rightHand.position + leftHand.position) * 0.5f + forward * 0.18f;
            case ElementKind.Fire: return rightHand.position + forward * 0.12f;
            default: return rightHand.position + forward * 0.2f;
        }
    }

    // ---------- Disparo ----------

    private void Shoot(ElementKind kind, float charge)
    {
        Vector3 aim = FindAimPoint(out _);
        AimPoint = aim;
        Vector3 chest = transform.position + Vector3.up * 1.3f;
        Vector3 origin = HandPoint(kind);

        // Mão atrás de uma parede (encostado nela): sai do peito.
        if (Physics.Linecast(chest, origin, out RaycastHit wall, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            !wall.collider.transform.IsChildOf(transform))
        {
            origin = chest;
        }

        Vector3 direction = aim - origin;
        Vector3 cameraForward = cameraTransform.forward;
        if (direction.sqrMagnitude < 0.25f || Vector3.Dot(direction.normalized, cameraForward) < 0.2f)
        {
            direction = cameraForward; // alvo colado no personagem: vai para onde a câmera olha
        }

        direction.Normalize();
        ElementalFx.Muzzle(kind, origin, direction, charge);
        CameraShake.AddTrauma(Mathf.Lerp(0.04f, 0.22f, charge));
        Rigidbody heldBody = telekinesis != null ? telekinesis.HeldBody : null;

        switch (kind)
        {
            case ElementKind.Fire:
                ElementalFxRunner.Play(ElementalFxRunner.Sound.FireShot, origin, Mathf.Lerp(0.7f, 1f, charge), Mathf.Lerp(1.15f, 0.85f, charge));
                ElementalProjectile.Launch(kind, origin, direction, charge, transform, heldBody);
                break;
            case ElementKind.Ice:
                ElementalFxRunner.Play(ElementalFxRunner.Sound.IceShot, origin, Mathf.Lerp(0.7f, 1f, charge), Mathf.Lerp(1.1f, 0.85f, charge));
                ElementalProjectile.Launch(kind, origin, direction, charge, transform, heldBody);
                break;
            default:
                ShootBolt(origin, direction, charge, heldBody);
                break;
        }
    }

    // Raio: instantâneo. Acerta o primeiro obstáculo na linha e pula para os alienígenas vizinhos.
    private void ShootBolt(Vector3 origin, Vector3 direction, float charge, Rigidbody heldBody)
    {
        float range = tuning.range;
        int count = Physics.SphereCastNonAlloc(origin, 0.3f, direction, hits, range, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        RaycastHit nearest = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider.transform.IsChildOf(transform) || (heldBody != null && hit.collider.attachedRigidbody == heldBody))
            {
                continue;
            }

            if (hit.distance < best)
            {
                best = hit.distance;
                nearest = hit;
                found = true;
            }
        }

        bool overlapping = found && nearest.distance <= 0f; // já estava encostado no alvo
        Vector3 end = !found ? origin + direction * range : overlapping ? origin + direction * 0.5f : nearest.point;
        Vector3 normal = found && !overlapping ? nearest.normal : -direction;

        float width = Mathf.Lerp(0.12f, 0.24f, charge);
        float seconds = Mathf.Lerp(0.16f, 0.34f, charge);
        ElementalBolt.Strike(origin, end, width, seconds, Mathf.RoundToInt(Mathf.Lerp(2f, 5f, charge)), rightHand);
        ElementalFxRunner.Play(charge >= 0.5f ? ElementalFxRunner.Sound.BoltThunder : ElementalFxRunner.Sound.BoltZap, origin,
            Mathf.Lerp(0.75f, 1f, charge));
        if (!found)
        {
            return;
        }

        ElementalFx.BoltHit(end, normal, charge);
        ElementalCombat.LightningImpact(end, normal, nearest.collider, charge, transform, jumps);
        for (int i = 0; i + 1 < jumps.Count; i += 2)
        {
            ElementalBolt.Strike(jumps[i], jumps[i + 1], width * 0.7f, seconds, 1);
        }
    }

    // ---------- Brilho na mão enquanto carrega ----------

    private void UpdateChargeVisual()
    {
        ElementalFxRunner.Instance.SetHum(Element, IsCharging ? Mathf.Max(0.05f, Charge01) : 0f);
        if (!IsCharging)
        {
            if (chargeRoot != null && chargeRoot.gameObject.activeSelf)
            {
                chargeRoot.gameObject.SetActive(false);
            }

            return;
        }

        if (chargeRoot == null)
        {
            BuildChargeVisuals();
        }

        if (!chargeRoot.gameObject.activeSelf)
        {
            chargeRoot.gameObject.SetActive(true);
        }

        int index = (int)Element;
        for (int i = 0; i < ElementCount; i++)
        {
            if (chargeVisuals[i].activeSelf != (i == index))
            {
                chargeVisuals[i].SetActive(i == index);
            }
        }

        // Onde fica: na palma (fogo), entre as mãos (gelo), no punho erguido (raio).
        Vector3 anchor;
        if (rightHand == null || leftHand == null)
        {
            anchor = transform.position + Vector3.up * 1.4f + transform.forward * 0.5f;
        }
        else if (Element == ElementKind.Ice)
        {
            anchor = (rightHand.position + leftHand.position) * 0.5f;
        }
        else
        {
            anchor = rightHand.position + Vector3.up * (Element == ElementKind.Fire ? 0.16f : 0.12f);
        }

        chargeRoot.position = anchor;
        float charge = Charge01;
        float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 26f);
        Color main = ElementalFx.Main(Element);
        chargeLight.color = main;
        chargeLight.intensity = Mathf.Lerp(1.5f, 5.5f, charge) * pulse;
        chargeLight.range = Mathf.Lerp(3f, 7f, charge);

        ParticleSystem.MainModule glowMain = chargeGlows[index].main;
        float glowSize = Mathf.Lerp(0.45f, 1.5f, charge) * pulse;
        glowMain.startSize = new ParticleSystem.MinMaxCurve(glowSize, glowSize);

        switch (Element)
        {
            case ElementKind.Fire:
                fireCore.localScale = Vector3.one * Mathf.Lerp(0.1f, 0.34f, charge) * pulse;
                break;
            case ElementKind.Ice:
                float size = Mathf.Lerp(0.14f, 0.42f, charge);
                iceCore.localScale = new Vector3(size * 0.8f, size, size * 0.8f);
                iceCore.Rotate(0f, 200f * Time.deltaTime, 0f, Space.World);
                break;
            default:
                float reach = Mathf.Lerp(0.12f, 0.38f, charge);
                foreach (ElementalBolt arc in chargeArcs)
                {
                    arc.SetArcVolume(Vector3.zero, Vector3.one * reach);
                }

                break;
        }
    }

    private void BuildChargeVisuals()
    {
        chargeRoot = new GameObject("Carga_Elemental").transform;
        chargeLight = chargeRoot.gameObject.AddComponent<Light>();
        chargeLight.type = LightType.Point;
        chargeLight.shadows = LightShadows.None;

        for (int i = 0; i < ElementCount; i++)
        {
            ElementKind kind = (ElementKind)i;
            GameObject visual = new GameObject("Carga_" + ElementalFx.Name(kind));
            visual.transform.SetParent(chargeRoot, false);
            chargeVisuals[i] = visual;
            Color main = ElementalFx.Main(kind);
            Color hot = ElementalFx.Hot(kind);

            chargeGlows[i] = ElementalFx.Emitter("Halo", visual.transform, Vector3.zero, Quaternion.identity, ElementalFx.Additive("fx_glow", 1.6f), new ElementalFx.Spec
            {
                rate = 40f,
                life = new Vector2(0.08f, 0.12f),
                speed = Vector2.zero,
                size = Vector2.one * 0.6f,
                colorA = main,
                colorB = main,
                radius = 0.001f,
                local = true,
                loop = true,
                fade = ElementalFx.Fade(Color.white, Color.white, 0.2f, 0.6f, 0.5f),
                sizeCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f),
                max = 12,
            });

            switch (kind)
            {
                case ElementKind.Fire:
                    fireCore = ElementalFx.MeshObject("Miolo", ElementalFx.Sphere,
                        ElementalFx.Solid("Fogo_Miolo", new Color(1.6f, 1.25f, 0.55f, 1f), new Color(2.4f, 0.7f, 0.1f, 1f), 1.6f, true),
                        visual.transform).transform;
                    ElementalFx.Emitter("Labaredas", visual.transform, Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), ElementalFx.Additive("fx_flame", 1.5f), new ElementalFx.Spec
                    {
                        rate = 34f,
                        life = new Vector2(0.25f, 0.5f),
                        speed = new Vector2(0.5f, 1.4f),
                        size = new Vector2(0.18f, 0.42f),
                        colorA = hot,
                        colorB = main,
                        shape = ParticleSystemShapeType.Cone,
                        angle = 12f,
                        radius = 0.08f,
                        tilt = 18f,
                        loop = true,
                        fade = ElementalFx.Fade(Color.white, ElementalFx.Deep(kind), 0.1f, 0.4f),
                        sizeCurve = ElementalFx.Curve(0.6f, 1.1f, 0.2f),
                        max = 40,
                    });
                    break;

                case ElementKind.Ice:
                    iceCore = ElementalFx.MeshObject("Cristal", ElementalFx.Crystal, ElementalFx.IceMaterial(), visual.transform).transform;
                    iceCore.localPosition = new Vector3(0f, -0.1f, 0f);
                    ElementalFx.Emitter("Flocos", visual.transform, Vector3.zero, Quaternion.identity, ElementalFx.Additive("fx_flake", 1.4f), new ElementalFx.Spec
                    {
                        rate = 22f,
                        life = new Vector2(0.4f, 0.8f),
                        speed = new Vector2(0.2f, 0.9f),
                        size = new Vector2(0.05f, 0.12f),
                        colorA = hot,
                        colorB = main,
                        radius = 0.22f,
                        gravity = 0.15f,
                        spin = 160f,
                        noise = 0.5f,
                        loop = true,
                        max = 40,
                    });
                    ElementalFx.Emitter("Nevoa", visual.transform, Vector3.zero, Quaternion.identity, ElementalFx.Blended("fx_smoke"), new ElementalFx.Spec
                    {
                        rate = 9f,
                        life = new Vector2(0.5f, 0.9f),
                        speed = new Vector2(0.05f, 0.3f),
                        size = new Vector2(0.3f, 0.55f),
                        colorA = new Color(0.85f, 0.95f, 1f),
                        colorB = Color.white,
                        radius = 0.12f,
                        gravity = 0.1f,
                        spin = 30f,
                        loop = true,
                        fade = ElementalFx.Fade(Color.white, Color.white, 0.2f, 0.4f, 0.3f),
                        sizeCurve = AnimationCurve.Linear(0f, 0.6f, 1f, 1.5f),
                        max = 20,
                    });
                    break;

                default:
                    chargeArcs = new ElementalBolt[4];
                    for (int a = 0; a < chargeArcs.Length; a++)
                    {
                        chargeArcs[a] = ElementalBolt.Arc(visual.transform, Vector3.zero, Vector3.one * 0.2f, 0.035f);
                    }

                    ElementalFx.Emitter("Faiscas", visual.transform, Vector3.zero, Quaternion.identity, ElementalFx.Additive("fx_spark", 2.2f), new ElementalFx.Spec
                    {
                        rate = 20f,
                        life = new Vector2(0.12f, 0.3f),
                        speed = new Vector2(0.8f, 3f),
                        size = new Vector2(0.1f, 0.26f),
                        colorA = hot,
                        colorB = main,
                        radius = 0.12f,
                        stretch = 0.05f,
                        loop = true,
                        max = 30,
                    });
                    break;
            }
        }
    }
}
