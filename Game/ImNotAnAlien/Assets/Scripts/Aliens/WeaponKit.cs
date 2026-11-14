using UnityEngine;

// Armas do Sci-Fi Kit (Quaternius, CC0) — Missão 20.
// Fica em Assets/Resources/WeaponKit.asset (criado pela ferramenta "Preparar Sci-Fi Kit").
// Polícia: pistola. Exército (nível 5): rifle (às vezes o sniper).
// No Unity os modelos apontam o cano para +X (o FBX do Blender espelha o eixo X ao importar);
// "gunYaw" vira o cano para a frente do policial. A empunhadura fica perto da origem.
[CreateAssetMenu(menuName = "ImNotAnAlien/Kit de Armas", fileName = "WeaponKit")]
public sealed class WeaponKit : ScriptableObject
{
    public GameObject pistol;
    public GameObject revolver;
    public GameObject rifle;
    public GameObject sniper;

    [Tooltip("Giro da arma na mão (graus). -90 = cano (+X do modelo) para a frente. Se ainda ficar torta, teste 90 ou 180.")]
    public float gunYaw = -90f;
    [Tooltip("Escala da pistola na mão do policial (o modelo tem ~44 cm).")]
    public float pistolScale = 0.6f;
    [Tooltip("Escala do rifle/sniper na mão do soldado.")]
    public float rifleScale = 0.55f;

    private static WeaponKit cached;
    private static bool loaded;

    public static WeaponKit Instance
    {
        get
        {
            if (!loaded)
            {
                cached = Resources.Load<WeaponKit>("WeaponKit");
                loaded = true;
            }

            return cached;
        }
    }

    // Troca o desenho antigo da arma (peças de cubo dentro de "Corpo/Arma") pelo modelo do kit.
    // Chamado pelo AlienAnimator antes de guardar a lista de peças da arma.
    public static void Equip(Transform weapon, bool army)
    {
        WeaponKit kit = Instance;
        if (kit == null || weapon == null || weapon.Find("Modelo_Arma") != null)
        {
            return;
        }

        GameObject model = army ? (Random.value < 0.25f && kit.sniper != null ? kit.sniper : kit.rifle) : kit.pistol;
        if (model == null)
        {
            return;
        }

        // Some com as peças antigas, menos o aviso de tiro (a bolinha que cresce na ponta).
        AlienPoliceWeapon shooter = weapon.GetComponentInParent<AlienPoliceWeapon>();
        Transform warning = shooter != null && shooter.ChargeIndicator != null ? shooter.ChargeIndicator.transform : null;
        foreach (Renderer old in weapon.GetComponentsInChildren<Renderer>(true))
        {
            if (warning != null && old.transform.IsChildOf(warning))
            {
                continue;
            }

            Object.DestroyImmediate(old);
        }

        GameObject gun = Object.Instantiate(model, weapon);
        gun.name = "Modelo_Arma";
        gun.transform.localPosition = Vector3.zero;
        gun.transform.localRotation = Quaternion.Euler(0f, kit.gunYaw, 0f); // cano para a frente (+Z do objeto "Arma")
        // Compensa a escala do objeto "Arma" (pode ser um cubo esticado): a arma fica com o tamanho certo.
        float size = army ? kit.rifleScale : kit.pistolScale;
        Vector3 parent = weapon.lossyScale;
        gun.transform.localScale = new Vector3(
            size / Mathf.Max(0.001f, Mathf.Abs(parent.z)),
            size / Mathf.Max(0.001f, Mathf.Abs(parent.y)),
            size / Mathf.Max(0.001f, Mathf.Abs(parent.x)));
        foreach (Collider extra in gun.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(extra);
        }

        foreach (Renderer part in gun.GetComponentsInChildren<Renderer>(true))
        {
            part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // A ponta do cano (de onde sai o tiro) vai para a ponta do modelo novo.
        Transform muzzle = shooter != null ? shooter.Muzzle : weapon.Find("Ponta");
        MeshFilter mesh = gun.GetComponentInChildren<MeshFilter>();
        if (muzzle != null && mesh != null && mesh.sharedMesh != null)
        {
            // Ponta = a ponta do modelo mais à frente (qualquer que seja o eixo do cano).
            Bounds b = mesh.sharedMesh.bounds;
            Vector3 tip = mesh.transform.TransformPoint(b.center);
            float best = float.MinValue;
            for (int k = 0; k < 6; k++)
            {
                Vector3 local = b.center;
                local[k / 2] += (k % 2 == 0 ? -1f : 1f) * b.extents[k / 2];
                local.y = b.center.y + b.extents.y * 0.4f;
                Vector3 world = mesh.transform.TransformPoint(local);
                float ahead = Vector3.Dot(world - weapon.position, weapon.forward);
                if (ahead > best)
                {
                    best = ahead;
                    tip = world;
                }
            }
            muzzle.position = tip;
            if (warning != null && warning != muzzle && !muzzle.IsChildOf(warning))
            {
                warning.position = tip;
            }
        }
    }
}
