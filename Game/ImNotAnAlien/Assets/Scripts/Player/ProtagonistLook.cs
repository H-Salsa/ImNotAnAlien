using System.Collections.Generic;
using UnityEngine;

// Visual do protagonista "Fugitivo H-19" (decisão D24).
// - Marcas do experimento no braço da telecinese, em 4 estágios (antebraço, braço, pescoço, rosto):
//   aparecem conforme a evolução.
// - As marcas brilham com a cor do poder em uso (forte enquanto o poder está ativo, fraco parado).
// - A cabeça cresce a cada poder novo (forma "Cabeca_Alien"), até virar um bulbo de alien no último;
//   no fim o cabelo some (careca como os aliens).
// Hoje o jogo só tem a telecinese: use SetPowersUnlocked/SetActivePower quando os outros poderes
// existirem. Para testar, mude "Poderes liberados" e "Poder ativo" no Inspector durante o Play.
[DisallowMultipleComponent]
public sealed class ProtagonistLook : MonoBehaviour
{
    public enum Power { Telecinese, Bioeletricidade, Supervelocidade, Superforca, Metamorfose }

    [Header("Evolução")]
    [Tooltip("Quantos poderes o jogador já tem (1 a 5). Cada poder novo: mais marcas e cabeça maior.")]
    [SerializeField, Range(1, 5)] private int powersUnlocked = 1;
    [SerializeField] private Power activePower = Power.Telecinese;

    [Header("Brilho das marcas")]
    [SerializeField] private Color[] powerColors =
    {
        new Color(0.37f, 0.95f, 0.85f),   // telecinese: ciano
        new Color(1.00f, 0.88f, 0.25f),   // bioeletricidade: amarelo
        new Color(0.95f, 0.97f, 1.00f),   // supervelocidade: branco
        new Color(1.00f, 0.55f, 0.20f),   // superforça: laranja
        new Color(0.70f, 0.40f, 1.00f),   // metamorfose: roxo
    };
    [Tooltip("Brilho das marcas com o poder parado e em uso.")]
    [SerializeField, Min(0f)] private float idleGlow = 0.35f;
    [SerializeField, Min(0f)] private float activeGlow = 3.5f;
    [SerializeField, Min(0.1f)] private float glowSpeed = 6f;

    private readonly List<GameObject> markStages = new List<GameObject>();
    private readonly List<(SkinnedMeshRenderer renderer, int index)> headShapes = new List<(SkinnedMeshRenderer, int)>();
    private GameObject hair;
    private Material marksMaterial;
    private Material lockLight;      // luz da trava da algema (pisca)
    private Color lockColor;
    private TelekinesisPower telekinesis;
    private float glow;
    private float headWeight;

    public int PowersUnlocked => powersUnlocked;

    public void SetPowersUnlocked(int count)
    {
        powersUnlocked = Mathf.Clamp(count, 1, 5);
    }

    public void SetActivePower(Power power)
    {
        activePower = power;
    }

    private void Awake()
    {
        telekinesis = GetComponentInParent<TelekinesisPower>();
        if (telekinesis == null)
        {
            telekinesis = FindFirstObjectByType<TelekinesisPower>();
        }

        for (int i = 1; i <= 4; i++)
        {
            Transform stage = FindDeep(transform, "H19_Marcas_" + i);
            markStages.Add(stage != null ? stage.gameObject : null);
            if (stage != null && stage.TryGetComponent(out Renderer r))
            {
                if (marksMaterial == null)
                {
                    marksMaterial = new Material(r.sharedMaterial);
                }

                r.sharedMaterial = marksMaterial;
            }
        }

        Transform cuff = FindDeep(transform, "H19_Algema");
        if (cuff != null && cuff.TryGetComponent(out Renderer cuffRenderer))
        {
            Material[] mats = cuffRenderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].name.Contains("H19_Luz"))
                {
                    lockLight = new Material(mats[i]);
                    lockColor = lockLight.GetColor("_BaseColor");
                    mats[i] = lockLight;
                }
            }

            cuffRenderer.sharedMaterials = mats;
        }

        foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            int index = r.sharedMesh != null ? r.sharedMesh.GetBlendShapeIndex("Cabeca_Alien") : -1;
            if (index >= 0)
            {
                headShapes.Add((r, index));
            }

            if (r.name.StartsWith("Hair_SimpleParted"))
            {
                hair = r.gameObject;
            }
        }

        headWeight = HeadTarget();
    }

    private void Update()
    {
        // Marcas: estágio 1 com o primeiro poder, mais um estágio a cada poder novo (até o rosto).
        for (int i = 0; i < markStages.Count; i++)
        {
            if (markStages[i] != null)
            {
                markStages[i].SetActive(i < powersUnlocked);
            }
        }

        // Cabeça cresce aos poucos até o tamanho do estágio atual.
        headWeight = Mathf.MoveTowards(headWeight, HeadTarget(), 0.5f * Time.deltaTime);
        foreach ((SkinnedMeshRenderer r, int index) in headShapes)
        {
            if (r != null)
            {
                r.SetBlendShapeWeight(index, headWeight * 100f);
            }
        }

        if (hair != null)
        {
            hair.SetActive(headWeight < 0.98f);
        }

        // Brilho: forte enquanto o poder está em uso.
        bool using_ = activePower == Power.Telecinese && telekinesis != null && telekinesis.HeldBody != null;
        glow = Mathf.MoveTowards(glow, using_ ? activeGlow : idleGlow, glowSpeed * Time.deltaTime);
        // Luz da trava: pisca devagar (a algema continua "ligada", presa no pulso).
        if (lockLight != null)
        {
            float blink = Mathf.PingPong(Time.time * 1.6f, 1f) > 0.75f ? 3.5f : 0.4f;
            lockLight.SetColor("_EmissionColor", lockColor * blink);
        }

        if (marksMaterial != null)
        {
            Color c = powerColors != null && powerColors.Length > (int)activePower ? powerColors[(int)activePower] : Color.cyan;
            marksMaterial.SetColor("_BaseColor", c);
            marksMaterial.SetColor("_EmissionColor", c * glow);
        }
    }

    private float HeadTarget()
    {
        return (powersUnlocked - 1) / 4f;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                return t;
            }
        }

        return null;
    }
}
