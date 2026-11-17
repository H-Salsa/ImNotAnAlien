using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

// O que os poderes elementais deixam num alienígena (Missão 21). Colocado sozinho no alvo ao ser atingido.
// - QUEIMANDO (fogo): perde vida aos poucos, coberto de labaredas e fumaça. Civil sai correndo.
// - COM FRIO (gelo): cada acerto deixa mais lento; no terceiro (ou num disparo carregado) CONGELA:
//   fica duro dentro de um bloco de gelo. Congelado que leva uma pancada (objeto arremessado, queda)
//   ESTILHAÇA e leva dano extra.
// - EM CHOQUE (eletricidade): atordoado, tremendo, com arquinhos elétricos pelo corpo.
// Os elementos se anulam: fogo derrete o gelo (vapor) e gelo apaga o fogo.
[DefaultExecutionOrder(120)] // depois do AlienBrain e da aquática
[DisallowMultipleComponent]
public sealed class ElementalStatus : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Color FrozenTint = new Color(0.62f, 0.86f, 1f);
    private static readonly Color BurnTint = new Color(0.3f, 0.16f, 0.12f);

    private AlienHealth health;
    private AlienBrain brain;
    private NavMeshAgent agent;
    private Animator animator;
    private Behaviour otherAi;       // bichos sem AlienBrain (ex.: Trilobite)
    private bool otherAiPaused;
    private Transform fxRoot;
    private Vector3 center = new Vector3(0f, 1f, 0f);   // meio do corpo (espaço do alienígena)
    private Vector3 extents = new Vector3(0.4f, 1f, 0.4f);

    private float burnTime;
    private float burnDps;
    private float burnTick;
    private ParticleSystem flames;
    private ParticleSystem burnSmoke;

    private int chill;
    private float chillTime;
    private ParticleSystem mist;
    private ParticleSystem flakes;

    private float frozenTime;
    private Transform shell;
    private float shellAge;
    private float savedAnimatorSpeed = 1f;

    private float shockTime;
    private ElementalBolt[] arcs;
    private ParticleSystem sparks;

    private float lastSetSpeed = -1f;
    private float baseSpeed;
    private float deadTimer = -1f;

    private readonly List<Renderer> tinted = new List<Renderer>();
    private readonly List<Color[]> tintedBase = new List<Color[]>();
    private MaterialPropertyBlock block;
    private Color tintColor;
    private float tintAmount;

    public bool IsBurning => burnTime > 0f;
    public bool IsFrozen => frozenTime > 0f;
    public bool IsShocked => shockTime > 0f;
    public int ChillStacks => chill;

    // Pega (ou cria) o estado elemental de um alvo com AlienHealth.
    public static ElementalStatus For(AlienHealth target)
    {
        if (target == null || target.IsDead)
        {
            return null;
        }

        return target.TryGetComponent(out ElementalStatus status) ? status : target.gameObject.AddComponent<ElementalStatus>();
    }

    private void Awake()
    {
        health = GetComponent<AlienHealth>();
        brain = GetComponent<AlienBrain>();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        if (brain == null)
        {
            otherAi = GetComponent<EnemyTrilobite>();
        }

        MeasureBody();
        fxRoot = new GameObject("Efeitos_Elementais").transform;
        fxRoot.SetParent(transform, false);
    }

    // Volume do corpo, a partir dos colisores (para espalhar chamas, gelo e arcos no tamanho certo).
    private void MeasureBody()
    {
        bool found = false;
        Bounds bounds = default;
        foreach (Collider collider in GetComponentsInChildren<Collider>())
        {
            if (collider.isTrigger || !collider.enabled)
            {
                continue;
            }

            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (!found)
        {
            return;
        }

        Vector3 scale = transform.lossyScale;
        center = transform.InverseTransformPoint(bounds.center);
        extents = new Vector3(
            Mathf.Clamp(bounds.extents.x / Mathf.Max(0.01f, scale.x), 0.2f, 3f),
            Mathf.Clamp(bounds.extents.y / Mathf.Max(0.01f, scale.y), 0.2f, 4f),
            Mathf.Clamp(bounds.extents.z / Mathf.Max(0.01f, scale.z), 0.2f, 3f));
        float width = Mathf.Max(extents.x, extents.z);
        extents.x = extents.z = width;
    }

    public Vector3 WorldCenter => transform.TransformPoint(center);

    // ---------- Aplicar ----------

    public void Burn(float dps, float seconds)
    {
        if (health == null || health.IsDead)
        {
            return;
        }

        if (IsFrozen)
        {
            // Fogo no gelo: derrete com vapor e queima só a metade do tempo.
            Thaw(true);
            seconds *= 0.5f;
        }

        if (chill > 0)
        {
            chill = 0;
            ElementalFx.Steam(WorldCenter, 0.7f);
        }

        bool starting = burnTime <= 0f;
        burnTime = Mathf.Max(burnTime, seconds);
        burnDps = Mathf.Max(burnDps, dps);
        if (starting)
        {
            burnTick = 0.25f;
            StartFlames();
            GameFeel.AlienScream(transform.position + Vector3.up * 1.8f);
        }

        if (brain != null)
        {
            brain.Provoke();
        }
    }

    public void Chill(int stacks, float freezeSeconds)
    {
        if (health == null || health.IsDead || stacks <= 0)
        {
            return;
        }

        if (IsBurning)
        {
            // Gelo no fogo: apaga (vapor) e gasta um dos acertos.
            StopFlames();
            burnTime = 0f;
            ElementalFx.Steam(WorldCenter, 0.9f);
            ElementalFxRunner.Play(ElementalFxRunner.Sound.Sizzle, WorldCenter, 0.7f);
            stacks--;
            if (stacks <= 0)
            {
                return;
            }
        }

        ElementalTuning tuning = ElementalTuning.Current;
        chill = Mathf.Min(tuning.freezeStacks, chill + stacks);
        chillTime = tuning.chillSeconds;
        StartMist();
        if (chill >= tuning.freezeStacks)
        {
            Freeze(freezeSeconds);
        }
        else if (brain != null)
        {
            brain.Provoke();
        }
    }

    public void Freeze(float seconds)
    {
        if (health == null || health.IsDead || seconds <= 0f)
        {
            return;
        }

        if (IsBurning)
        {
            StopFlames();
            burnTime = 0f;
            ElementalFx.Steam(WorldCenter, 0.9f);
        }

        bool starting = frozenTime <= 0f;
        frozenTime = Mathf.Max(frozenTime, seconds);
        chill = ElementalTuning.Current.freezeStacks;
        chillTime = frozenTime + 2f;
        StartMist();
        HoldBrain(AlienBrain.Hold.Congelado, frozenTime);
        if (starting)
        {
            BuildShell();
            if (animator != null)
            {
                savedAnimatorSpeed = animator.speed > 0.01f ? animator.speed : 1f;
                animator.speed = 0f;
            }

            ElementalFxRunner.Play(ElementalFxRunner.Sound.Freeze, WorldCenter, 0.9f);
        }
    }

    public void Shock(float seconds)
    {
        if (health == null || health.IsDead || seconds <= 0f)
        {
            return;
        }

        bool starting = shockTime <= 0f;
        shockTime = Mathf.Max(shockTime, seconds);
        if (!IsFrozen)
        {
            HoldBrain(AlienBrain.Hold.Choque, shockTime);
        }

        if (starting)
        {
            StartArcs();
        }
    }

    private void HoldBrain(AlienBrain.Hold kind, float seconds)
    {
        if (brain != null)
        {
            brain.ElementalHold(kind, seconds);
        }
        else if (otherAi != null && !otherAiPaused)
        {
            otherAiPaused = true;
            otherAi.enabled = false;
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
        }
    }

    // ---------- Rotina ----------

    private void Update()
    {
        if (health == null)
        {
            Destroy(this);
            return;
        }

        float dt = Time.deltaTime;
        if (health.IsDead)
        {
            UpdateDead(dt);
            return;
        }

        if (burnTime > 0f)
        {
            burnTime -= dt;
            burnTick -= dt;
            if (burnTick <= 0f)
            {
                burnTick += 0.25f;
                health.TakeDamage(burnDps * 0.25f);
                if (health.IsDead)
                {
                    return; // o próximo quadro cuida do corpo
                }
            }

            if (burnTime <= 0f)
            {
                burnDps = 0f;
                StopFlames();
            }
        }

        if (frozenTime > 0f)
        {
            frozenTime -= dt;
            shellAge += dt;
            if (shell != null)
            {
                float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(shellAge / 0.22f));
                float melt = Mathf.Clamp01(frozenTime / 0.5f); // encolhe no fim
                shell.localScale = Vector3.one * (grow * Mathf.Lerp(0.75f, 1f, melt));
            }

            if (frozenTime <= 0f)
            {
                Thaw(false);
            }
            else if (brain != null && brain.CurrentHold != AlienBrain.Hold.Congelado)
            {
                brain.ElementalHold(AlienBrain.Hold.Congelado, frozenTime); // (ex.: acabou de cair no chão)
            }
        }

        if (shockTime > 0f)
        {
            shockTime -= dt;
            if (shockTime <= 0f)
            {
                StopArcs();
            }
        }

        if (chill > 0 && !IsFrozen)
        {
            chillTime -= dt;
            if (chillTime <= 0f)
            {
                chill = 0;
                StopMist();
            }
        }

        if (!IsFrozen && !IsShocked && otherAiPaused)
        {
            otherAiPaused = false;
            otherAi.enabled = true;
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }
        }

        ApplySlow();
        UpdateTint(dt);
    }

    // Morreu: o gelo estilhaça, os arcos somem e as chamas ainda queimam um pouco.
    private void UpdateDead(float dt)
    {
        if (deadTimer < 0f)
        {
            deadTimer = IsBurning ? 2.5f : 0.1f;
            if (IsFrozen || shell != null)
            {
                ElementalFx.IceShatter(WorldCenter, extents.y);
                DestroyShell();
            }

            frozenTime = 0f;
            shockTime = 0f;
            chill = 0;
            StopArcs();
            StopMist();
            if (animator != null && animator.speed < 0.01f)
            {
                animator.speed = savedAnimatorSpeed;
            }

            ClearTint(); // o corpo "drenado" do AlienHealth manda na cor
        }

        deadTimer -= dt;
        if (deadTimer <= 0f)
        {
            StopFlames();
            Destroy(fxRoot.gameObject, 2f);
            Destroy(this);
        }
    }

    // Frio deixa lento. Com AlienBrain, ele junta este multiplicador com os outros; sem, ajusta o agente.
    private void ApplySlow()
    {
        float factor = IsFrozen ? 1f : Mathf.Clamp(1f - ElementalTuning.Current.slowPerStack * chill, 0.1f, 1f);
        if (brain != null)
        {
            brain.ChillSpeedFactor = factor;
            return;
        }

        if (agent == null || !agent.enabled)
        {
            return;
        }

        if (!Mathf.Approximately(agent.speed, lastSetSpeed))
        {
            baseSpeed = agent.speed;
        }

        float want = baseSpeed * factor;
        if (!Mathf.Approximately(agent.speed, want))
        {
            agent.speed = want;
        }

        lastSetSpeed = agent.speed;
    }

    // Pancada num congelado: o gelo estilhaça e machuca bastante.
    private void OnCollisionEnter(Collision collision)
    {
        if (!IsFrozen || health == null || health.IsDead || collision.relativeVelocity.magnitude < 5f)
        {
            return;
        }

        Shatter();
    }

    public void Shatter()
    {
        if (!IsFrozen)
        {
            return;
        }

        ElementalFx.IceShatter(WorldCenter, extents.y);
        frozenTime = 0f;
        Unfreeze();
        chill = 1;
        chillTime = 2f;
        health.TakeDamage(ElementalTuning.Current.shatterDamage);
    }

    private void Thaw(bool steam)
    {
        frozenTime = 0f;
        if (steam)
        {
            ElementalFx.Steam(WorldCenter, 1.2f);
            ElementalFxRunner.Play(ElementalFxRunner.Sound.Sizzle, WorldCenter, 0.8f);
            chill = 0;
            StopMist();
        }
        else
        {
            chill = 1; // sai do gelo ainda travado de frio por um instante
            chillTime = 2f;
        }

        Unfreeze();
    }

    private void Unfreeze()
    {
        DestroyShell();
        if (animator != null)
        {
            animator.speed = savedAnimatorSpeed;
        }

        if (brain != null && brain.CurrentHold == AlienBrain.Hold.Congelado)
        {
            brain.ReleaseHold();
        }
    }

    private void OnDestroy()
    {
        if (animator != null && animator.speed < 0.01f)
        {
            animator.speed = savedAnimatorSpeed;
        }
    }

    // ---------- Visual: fogo ----------

    private void StartFlames()
    {
        if (flames != null)
        {
            return;
        }

        float width = extents.x;
        Vector3 box = new Vector3(width * 2f, width * 2f, extents.y * 1.7f);
        Quaternion up = Quaternion.Euler(-90f, 0f, 0f); // o "para a frente" do emissor vira "para cima"
        flames = ElementalFx.Emitter("Chamas", fxRoot, center, up, ElementalFx.Additive("fx_flame", 1.6f), new ElementalFx.Spec
        {
            rate = 30f,
            life = new Vector2(0.4f, 0.8f),
            speed = new Vector2(0.6f, 1.8f),
            size = new Vector2(0.5f, 1f) * Mathf.Clamp(width * 2.2f, 0.7f, 2.5f),
            colorA = ElementalFx.Hot(ElementKind.Fire),
            colorB = ElementalFx.Main(ElementKind.Fire),
            shape = ParticleSystemShapeType.Box,
            boxSize = box,
            gravity = -0.5f,
            tilt = 22f,
            loop = true,
            fade = ElementalFx.Fade(Color.white, ElementalFx.Deep(ElementKind.Fire), 0.1f, 0.4f),
            sizeCurve = ElementalFx.Curve(0.5f, 1.1f, 0.3f),
            max = 70,
        });
        burnSmoke = ElementalFx.Emitter("Fumaca", fxRoot, center + Vector3.up * extents.y * 0.6f, up, ElementalFx.Blended("fx_smoke"), new ElementalFx.Spec
        {
            rate = 6f,
            life = new Vector2(1f, 1.8f),
            speed = new Vector2(0.8f, 1.6f),
            size = new Vector2(0.6f, 1.1f),
            colorA = new Color(0.14f, 0.13f, 0.13f),
            colorB = new Color(0.3f, 0.27f, 0.25f),
            shape = ParticleSystemShapeType.Box,
            boxSize = new Vector3(width, width, 0.2f),
            gravity = -0.2f,
            spin = 30f,
            loop = true,
            fade = ElementalFx.Fade(Color.white, Color.white, 0.15f, 0.4f, 0.5f),
            sizeCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f),
            max = 24,
            sortFudge = 2f,
        });
    }

    private void StopFlames()
    {
        ElementalFx.Release(flames);
        ElementalFx.Release(burnSmoke);
        flames = null;
        burnSmoke = null;
    }

    // ---------- Visual: frio e gelo ----------

    private void StartMist()
    {
        if (mist != null)
        {
            return;
        }

        float width = extents.x;
        Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
        mist = ElementalFx.Emitter("Nevoa_Fria", fxRoot, center, up, ElementalFx.Blended("fx_smoke"), new ElementalFx.Spec
        {
            rate = 7f,
            life = new Vector2(0.9f, 1.6f),
            speed = new Vector2(0.05f, 0.3f),
            size = new Vector2(0.6f, 1.1f) * Mathf.Clamp(width * 2.2f, 0.7f, 2.5f),
            colorA = new Color(0.82f, 0.94f, 1f),
            colorB = Color.white,
            shape = ParticleSystemShapeType.Box,
            boxSize = new Vector3(width * 2.2f, width * 2.2f, extents.y * 1.6f),
            gravity = 0.05f,
            spin = 20f,
            loop = true,
            fade = ElementalFx.Fade(Color.white, Color.white, 0.2f, 0.4f, 0.3f),
            sizeCurve = AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f),
            max = 24,
        });
        flakes = ElementalFx.Emitter("Flocos", fxRoot, center + Vector3.up * extents.y * 0.5f, up, ElementalFx.Additive("fx_flake", 1.3f), new ElementalFx.Spec
        {
            rate = 6f,
            life = new Vector2(0.9f, 1.5f),
            speed = new Vector2(0f, 0.3f),
            size = new Vector2(0.08f, 0.18f),
            colorA = ElementalFx.Hot(ElementKind.Ice),
            colorB = ElementalFx.Main(ElementKind.Ice),
            shape = ParticleSystemShapeType.Box,
            boxSize = new Vector3(width * 2.6f, width * 2.6f, extents.y),
            gravity = 0.2f,
            spin = 120f,
            noise = 0.4f,
            loop = true,
            max = 24,
        });
    }

    private void StopMist()
    {
        ElementalFx.Release(mist);
        ElementalFx.Release(flakes);
        mist = null;
        flakes = null;
    }

    // Bloco de gelo: um cristal grande envolvendo o corpo e cristais menores em volta, no chão.
    private void BuildShell()
    {
        DestroyShell();
        shellAge = 0f;
        shell = new GameObject("Bloco_De_Gelo").transform;
        shell.SetParent(fxRoot, false);
        shell.localPosition = new Vector3(center.x, center.y - extents.y, center.z);
        shell.localScale = Vector3.zero;

        Material ice = ElementalFx.IceMaterial();
        float width = extents.x;
        float height = extents.y * 2f;

        Transform big = ElementalFx.MeshObject("Cristal_Central", ElementalFx.Crystal, ice, shell).transform;
        big.localScale = new Vector3(width * 5.6f, height * 1.18f, width * 5.6f);
        big.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        const int count = 7;
        for (int i = 0; i < count; i++)
        {
            float angle = (i + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / count;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Transform spike = ElementalFx.MeshObject("Cristal_" + i, ElementalFx.Crystal, ice, shell).transform;
            spike.localPosition = outward * width * Random.Range(0.9f, 1.25f);
            spike.localRotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up + outward * Random.Range(0.25f, 0.6f)).normalized) *
                                  Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            float tall = height * Random.Range(0.35f, 0.8f);
            float thick = width * Random.Range(1.6f, 2.6f);
            spike.localScale = new Vector3(thick, tall, thick);
        }
    }

    private void DestroyShell()
    {
        if (shell != null)
        {
            Destroy(shell.gameObject);
            shell = null;
        }
    }

    // ---------- Visual: choque ----------

    private void StartArcs()
    {
        if (arcs != null)
        {
            return;
        }

        Vector3 volume = new Vector3(extents.x * 1.15f, extents.y * 0.9f, extents.z * 1.15f);
        arcs = new ElementalBolt[3];
        for (int i = 0; i < arcs.Length; i++)
        {
            arcs[i] = ElementalBolt.Arc(fxRoot, center, volume, 0.05f);
        }

        sparks = ElementalFx.Emitter("Faiscas", fxRoot, center, Quaternion.identity, ElementalFx.Additive("fx_spark", 2.2f), new ElementalFx.Spec
        {
            rate = 16f,
            life = new Vector2(0.15f, 0.4f),
            speed = new Vector2(1f, 4f),
            size = new Vector2(0.12f, 0.3f),
            colorA = ElementalFx.Hot(ElementKind.Lightning),
            colorB = ElementalFx.Main(ElementKind.Lightning),
            radius = Mathf.Max(extents.x, extents.y * 0.6f),
            gravity = 0.8f,
            stretch = 0.05f,
            loop = true,
            max = 30,
        });
    }

    private void StopArcs()
    {
        if (arcs != null)
        {
            foreach (ElementalBolt arc in arcs)
            {
                if (arc != null)
                {
                    arc.Stop();
                }
            }

            arcs = null;
        }

        ElementalFx.Release(sparks);
        sparks = null;
    }

    // ---------- Cor do corpo ----------

    private void UpdateTint(float dt)
    {
        Color wanted = FrozenTint;
        float amount = 0f;
        if (IsFrozen)
        {
            amount = 0.62f;
        }
        else if (IsBurning)
        {
            wanted = BurnTint;
            amount = 0.4f;
        }
        else if (chill > 0)
        {
            amount = 0.2f * chill;
        }

        if (amount <= 0f && tintAmount <= 0f)
        {
            return;
        }

        if (amount > 0f)
        {
            tintColor = tintAmount <= 0.001f ? wanted : Color.Lerp(tintColor, wanted, 1f - Mathf.Exp(-8f * dt));
        }

        float previous = tintAmount;
        tintAmount = Mathf.MoveTowards(tintAmount, amount, 2.5f * dt);
        if (Mathf.Approximately(previous, tintAmount) && amount <= 0f)
        {
            return;
        }

        if (tintAmount <= 0.001f)
        {
            ClearTint();
            return;
        }

        if (tinted.Count == 0)
        {
            CollectRenderers();
        }

        block ??= new MaterialPropertyBlock();
        for (int r = 0; r < tinted.Count; r++)
        {
            Renderer renderer = tinted[r];
            if (renderer == null)
            {
                continue;
            }

            Color[] colors = tintedBase[r];
            for (int i = 0; i < colors.Length; i++)
            {
                renderer.GetPropertyBlock(block, i);
                block.SetColor(BaseColorId, Color.Lerp(colors[i], tintColor, tintAmount));
                renderer.SetPropertyBlock(block, i);
            }
        }
    }

    private void CollectRenderers()
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer) || renderer.transform.IsChildOf(fxRoot) || !renderer.enabled)
            {
                continue;
            }

            Material[] materials = renderer.sharedMaterials;
            Color[] colors = new Color[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                colors[i] = material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
            }

            tinted.Add(renderer);
            tintedBase.Add(colors);
        }
    }

    private void ClearTint()
    {
        tintAmount = 0f;
        for (int r = 0; r < tinted.Count; r++)
        {
            Renderer renderer = tinted[r];
            if (renderer == null)
            {
                continue;
            }

            for (int i = 0; i < tintedBase[r].Length; i++)
            {
                renderer.SetPropertyBlock(null, i);
            }
        }

        tinted.Clear();
        tintedBase.Clear();
    }
}
