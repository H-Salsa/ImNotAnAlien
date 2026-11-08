using UnityEngine;

// Pintura sorteada do carro 3D (Missão 15).
// Os carros estacionados usam este componente: ao começar o jogo, cada um sorteia um par de cores
// (pintura principal + detalhe/faixa de baixo). O trânsito sorteia as cores no TrafficSystem.
// Os materiais "Pintura" e "Parachoque" recebem a cor principal; "Detalhe" recebe a cor do detalhe.
[DisallowMultipleComponent]
public sealed class CarPaint : MonoBehaviour
{
    public static readonly Color[] DefaultPaints =
    {
        Hex("F1DFC0"), Hex("E2735A"),   // creme e coral (a arte original)
        Hex("BFEBDD"), Hex("2E8C76"),   // menta e verde
        Hex("D9C9F2"), Hex("7B5BC4"),   // lilás e roxo
        Hex("F4D35E"), Hex("3E6FB0"),   // amarelo e azul
        Hex("F6C1CF"), Hex("A33B5C"),   // rosa e vinho
        Hex("F2F2F2"), Hex("E8894A"),   // branco e laranja
        Hex("A8D8F0"), Hex("2B5D8C"),   // azul-claro e azul
        Hex("E8E1D3"), Hex("4A4F57"),   // areia e grafite
    };

    [Tooltip("Pares de cores (pintura principal, detalhe) sorteados para este carro.")]
    [SerializeField] private Color[] paints = (Color[])DefaultPaints.Clone();

    private void Start()
    {
        Transform visual = transform.Find("Visual");
        if (visual == null || paints == null || paints.Length < 2)
        {
            return;
        }

        int pair = Random.Range(0, paints.Length / 2);
        Apply(visual, paints[pair * 2], paints[pair * 2 + 1]);
    }

    public static void Apply(Transform visual, Color main, Color detail)
    {
        Material paintMat = null, detailMat = null;
        foreach (Renderer r in visual.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null)
                {
                    continue;
                }

                string n = mats[i].name;
                if (n.Contains("Pintura") || n.Contains("Parachoque"))
                {
                    paintMat ??= new Material(mats[i]) { color = main };
                    mats[i] = paintMat;
                }
                else if (n.Contains("Detalhe"))
                {
                    detailMat ??= new Material(mats[i]) { color = detail };
                    mats[i] = detailMat;
                }
            }

            r.sharedMaterials = mats;
        }
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }
}
