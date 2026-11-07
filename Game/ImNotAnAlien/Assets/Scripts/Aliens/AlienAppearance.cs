using System.Collections.Generic;
using UnityEngine;

// Aparência de cada alienígena (Missão 15, parte 2).
// O prefab tem dois modelos dentro de "Corpo": Visual_Alien_M (masculino) e Visual_Alien_F
// (feminino), cada um com todas as roupas. Ao nascer, este script:
// - sorteia o corpo (masculino ou feminino) e apaga o outro;
// - civis: sorteia parte de cima (camiseta, regata ou manga longa) e de baixo (calça ou bermuda),
//   sempre com tênis, e pinta cada peça de uma cor;
// - policiais: macacão, colete, cinto, coldre e botas; boné normalmente e CAPACETE quando ele surge
//   com o nível de procurado alto (os mais agressivos) — decisão D20;
// - pinta a pele (civis: uma de várias cores, com pequena variação; policiais: sempre verde-azulado)
//   e as listras da cabeça num tom mais escuro da mesma cor.
[DefaultExecutionOrder(-50)] // antes do AlienAnimator, que usa o modelo escolhido
[DisallowMultipleComponent]
public sealed class AlienAppearance : MonoBehaviour
{
    [Header("Corpo")]
    [Tooltip("Tamanho geral dos alienígenas (1 = tamanho do modelo). Com 1,1 o masculino fica com ~2,17 m e a feminina ~2,09 m; o protagonista tem ~1,81 m.")]
    [SerializeField, Range(0.8f, 1.5f)] private float alienScale = 1.1f;
    [SerializeField, Range(0f, 1f)] private float femaleChance = 0.5f;
    [Tooltip("Variação de tamanho (0,05 = até 5% maior ou menor).")]
    [SerializeField, Range(0f, 0.2f)] private float sizeVariation = 0.05f;

    [Header("Pele")]
    [SerializeField] private Color policeSkin = new Color(0.247f, 0.647f, 0.608f);
    [Tooltip("Cores de pele dos civis (verde-azulado fica de fora: é exclusivo da polícia).")]
    [SerializeField] private Color[] civilSkins =
    {
        Hex("B79BE8"), Hex("F29BC0"), Hex("F2A65A"), Hex("E8D25A"), Hex("A6D95B"), Hex("9B6BD9"),
        Hex("E8736B"), Hex("8FB8F0"), Hex("D96BC8"), Hex("F2C29B"), Hex("A8A0C8"), Hex("C9E86B"),
        Hex("7FD98F"), Hex("E89B5A"), Hex("B8B8E8"),
    };
    [Tooltip("Pequena variação em cada alien (tom e brilho), para nenhum ser igual ao outro.")]
    [SerializeField, Range(0f, 0.15f)] private float skinJitter = 0.06f;
    [Tooltip("Quanto as listras são mais escuras que a pele (0,7 = 30% mais escuras).")]
    [SerializeField, Range(0.3f, 1f)] private float stripeShade = 0.7f;

    [Header("Polícia")]
    [Tooltip("A partir deste nível de procurado, os policiais que surgem vêm de capacete.")]
    [SerializeField, Range(1, 5)] private int helmetFromStars = 3;

    [Header("Cores das roupas dos civis")]
    [SerializeField] private Color[] topColors =
    {
        Hex("E0B13A"), Hex("D9534F"), Hex("4F86C6"), Hex("F2F2F2"), Hex("6BBF59"),
        Hex("E8894A"), Hex("9F7FD9"), Hex("2B2B33"), Hex("F28AB2"),
    };
    [SerializeField] private Color[] bottomColors =
    {
        Hex("3A3A44"), Hex("2E4A7A"), Hex("6B5B4B"), Hex("8C8C8C"), Hex("2B2B33"), Hex("5A6B3A"),
    };
    [SerializeField] private Color[] shoeColors =
    {
        Hex("EDE4D0"), Hex("F2F2F2"), Hex("2B2B33"), Hex("D9534F"), Hex("4F86C6"),
    };

    private static readonly string[] Tops = { "Camiseta", "Regata", "MangaLonga" };
    private static readonly string[] Bottoms = { "Calca", "Bermuda" };
    private static readonly string[] CivilPieces = { "Camiseta", "Regata", "MangaLonga", "Calca", "Bermuda", "Tenis" };
    private static readonly string[] PolicePieces =
        { "Pol_Macacao", "Pol_Colete", "Pol_Cinto", "Pol_Coldre", "Pol_Botas", "Pol_Bone", "Pol_Capacete" };

    private Transform model;
    private bool police;

    public bool IsFemale { get; private set; }

    private void Awake()
    {
        Transform body = transform.Find("Corpo");
        Transform male = body != null ? body.Find("Visual_Alien_M") : null;
        Transform female = body != null ? body.Find("Visual_Alien_F") : null;
        if (male == null && female == null)
        {
            return;
        }

        // O alien inteiro cresce (modelo, colisores, ícone de alerta e arma juntos).
        transform.localScale = Vector3.one * alienScale;

        IsFemale = female != null && (male == null || Random.value < femaleChance);
        model = IsFemale ? female : male;
        Transform other = IsFemale ? male : female;
        if (other != null)
        {
            other.gameObject.SetActive(false); // some já, para o AlienAnimator achar o modelo certo
            Destroy(other.gameObject);
        }

        model.gameObject.SetActive(true);
        model.localScale = Vector3.one * (1f + Random.Range(-sizeVariation, sizeVariation));

        police = TryGetComponent(out AlienBrain brain) && brain.AlienRole == AlienBrain.Role.Policial;
        HashSet<string> wear = new HashSet<string>();
        if (police)
        {
            wear.UnionWith(new[] { "Pol_Macacao", "Pol_Colete", "Pol_Cinto", "Pol_Coldre", "Pol_Botas", "Pol_Bone" });
        }
        else
        {
            wear.Add(Tops[Random.Range(0, Tops.Length)]);
            wear.Add(Bottoms[Random.Range(0, Bottoms.Length)]);
            wear.Add("Tenis");
        }

        foreach (string piece in CivilPieces)
        {
            SetPiece(piece, wear.Contains(piece));
        }

        foreach (string piece in PolicePieces)
        {
            SetPiece(piece, wear.Contains(piece));
        }
    }

    private void Start()
    {
        if (model == null)
        {
            return;
        }

        // Policiais que surgem com o nível de procurado alto vêm de capacete.
        if (police && WantedSystem.Instance != null && WantedSystem.Instance.Stars >= helmetFromStars)
        {
            SetPiece("Pol_Bone", false);
            SetPiece("Pol_Capacete", true);
        }

        // Civis: uma cor da lista com uma variaçãozinha. A cápsula antiga (escondida) recebe a mesma
        // cor, porque é dela que saem as cores das partículas e do DNA quando o alien morre.
        Color skin = police ? policeSkin : Jitter(Pick(civilSkins));
        PaintOldParts(skin);
        Color top = Pick(topColors);
        Color bottom = Pick(bottomColors);
        Color shoes = Pick(shoeColors);

        foreach (SkinnedMeshRenderer part in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Material[] shared = part.sharedMaterials;
            Material[] own = part.materials; // cópias só deste alien
            string pieceName = part.gameObject.name;
            for (int i = 0; i < own.Length; i++)
            {
                string matName = shared[i] != null ? shared[i].name : "";
                if (matName.Contains("Listra"))
                {
                    own[i].color = new Color(skin.r * stripeShade, skin.g * stripeShade, skin.b * stripeShade, 1f);
                }
                else if (matName.Contains("Pele"))
                {
                    own[i].color = skin;
                }
                else if (matName.Contains("Roupa"))
                {
                    own[i].color = pieceName == "Tenis" ? shoes : System.Array.IndexOf(Bottoms, pieceName) >= 0 ? bottom : top;
                }
            }

            part.materials = own;
        }
    }

    private void SetPiece(string pieceName, bool on)
    {
        Transform piece = model.Find(pieceName);
        if (piece != null)
        {
            piece.gameObject.SetActive(on);
        }
    }

    private void PaintOldParts(Color skin)
    {
        foreach (string part in new[] { "Corpo/Tronco", "Corpo/Cabeca" })
        {
            Transform t = transform.Find(part);
            if (t != null && t.TryGetComponent(out Renderer renderer))
            {
                renderer.material.color = skin;
            }
        }
    }

    private Color Jitter(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        h = Mathf.Repeat(h + Random.Range(-skinJitter, skinJitter) * 0.5f, 1f);
        s = Mathf.Clamp01(s + Random.Range(-skinJitter, skinJitter));
        v = Mathf.Clamp01(v + Random.Range(-skinJitter, skinJitter));
        return Color.HSVToRGB(h, s, v);
    }

    private static Color Pick(Color[] colors)
    {
        return colors == null || colors.Length == 0 ? Color.white : colors[Random.Range(0, colors.Length)];
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }
}
