using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Missão 20 — casas modeladas no Blender (Assets/Art/Houses/*.fbx, fonte em C:\WorkSpace\ImNotAnAlien\Blender).
// Menu: ImNotAnAlien > Preparar Casas (Missao 20)
// 1. Importação: malhas legíveis (o jogo troca as cores/espelha e quebra a casa a partir delas),
//    sem animação, câmera ou luz.
// 2. Liga os modelos ao Kit de Blocos (houseModels): o bairro passa a usar essas casas.
// Cada FBX traz LOD0_* (casa inteira), LOD1, LOD2 e Caco_* (versão pré-fraturada).
// Pode rodar de novo (ex.: depois de exportar uma casa nova do Blender).
public static class HouseSetupTool
{
    private const string Folder = "Assets/Art/Houses";
    private const string BuildingFolder = "Assets/Art/Buildings";
    private const string BlockKitPath = "Assets/Art/BlockKit.asset";

    [MenuItem("ImNotAnAlien/Preparar Casas (Missao 20)")] // casas (Assets/Art/Houses) e prédios (Assets/Art/Buildings)
    public static void Run()
    {
        List<GameObject> models = Prepare(Folder);
        List<GameObject> buildings = Prepare(BuildingFolder);

        BlockKit kit = AssetDatabase.LoadAssetAtPath<BlockKit>(BlockKitPath);
        if (kit != null)
        {
            kit.houseModels = models.OrderBy(m => m.name).ToArray();
            kit.buildingModels = buildings.OrderBy(m => m.name).ToArray();
            kit.retroHouses = true;
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
        }

        EditorUtility.DisplayDialog("Casas e prédios",
            $"{models.Count} casa(s): {string.Join(", ", models.Select(m => m.name))}.\n" +
            $"{buildings.Count} prédio(s): {string.Join(", ", buildings.Select(m => m.name))}." +
            (kit != null ? "\nLigados ao Kit de Blocos: comece um jogo novo." : "\nNão achei o Kit de Blocos em " + BlockKitPath + "."),
            "OK");
    }

    private static List<GameObject> Prepare(string folder)
    {
        List<GameObject> models = new List<GameObject>();
        if (!AssetDatabase.IsValidFolder(folder))
        {
            return models;
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                continue;
            }

            importer.isReadable = true;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshVertices = false;
            importer.weldVertices = false;
            importer.addCollider = false;
            importer.SaveAndReimport();

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model != null && model.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("LOD0_")))
            {
                models.Add(model);
            }
            else
            {
                Debug.LogWarning($"Casas: {path} não tem partes LOD0_* (exportado do Blender pelo script da casa?).");
            }
        }

        return models;
    }
}
