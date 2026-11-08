using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Missão 15 — carros estacionados com o modelo 3D.
// Menu: ImNotAnAlien > Trocar Carros Estacionados (Missao 15)
// Rode com a cena aberta (Regiao1_VilarejoDoBilu ou a Prototype), depois de "Montar Carros 3D".
// Cada objeto "Carro" com física (os estacionados; o trânsito não é tocado) continua no mesmo lugar,
// com o mesmo peso e telecinese, mas:
// - as caixas antigas (carroceria, cabine, rodas) dão lugar ao carro flutuante 3D ("Visual");
// - os colisores passam a ter a forma do carro novo;
// - ganha HoverCar (discos apagados, já que está desligado; destruição na explosão),
//   CarPaint (cores sorteadas a cada jogo) e ExplosiveVehicle (se ainda não tinha).
// Pode rodar de novo sem problema: carros já trocados são refeitos.
public static class ParkedCarsSetupTool
{
    private const string ModelPath = "Assets/Art/Vehicles/Carro_Civil.fbx";

    [MenuItem("ImNotAnAlien/Trocar Carros Estacionados (Missao 15)")]
    public static void Setup()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            EditorUtility.DisplayDialog("Carros estacionados",
                "Não achei " + ModelPath + ".\nExporte o carro do Blender e rode \"Montar Carros 3D\" antes.", "OK");
            return;
        }

        int converted = 0;
        foreach (Rigidbody body in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            GameObject car = body.gameObject;
            if (!car.name.StartsWith("Carro") || car.GetComponent<TrafficCar>() != null || !car.scene.IsValid())
            {
                continue;
            }

            Convert(car, model);
            converted++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        string message = converted > 0
            ? $"{converted} carro(s) estacionado(s) agora usam o carro 3D.\n\nSALVE A CENA (Ctrl+S) e dê Play."
            : "Nenhum carro estacionado encontrado nesta cena.";
        Debug.Log(message);
        EditorUtility.DisplayDialog("Carros estacionados", message, "OK");
    }

    private static void Convert(GameObject car, GameObject model)
    {
        Undo.RegisterFullObjectHierarchyUndo(car, "Trocar carro estacionado");

        // Some com o visual antigo (caixas e rodas) ou com o carro 3D de uma troca anterior.
        List<GameObject> oldParts = new List<GameObject>();
        foreach (Transform child in car.transform)
        {
            oldParts.Add(child.gameObject);
        }

        foreach (GameObject part in oldParts)
        {
            Undo.DestroyObjectImmediate(part);
        }

        foreach (BoxCollider old in car.GetComponents<BoxCollider>())
        {
            Undo.DestroyObjectImmediate(old);
        }

        // Os carros antigos são compridos no eixo X do objeto: o carro 3D é girado para a frente
        // ficar no +X (o FBX vem com a frente no -Z).
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model, car.transform);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.Euler(0f, 270f, 0f);
        visual.transform.localScale = Vector3.one;
        foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.isStatic = false;
        }

        foreach (Collider extra in visual.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(extra);
        }

        Undo.RegisterCreatedObjectUndo(visual, "Carro 3D");

        // Casco: da base dos discos (apoiados no chão) até a linha da janela; cabine por cima.
        BoxCollider hull = Undo.AddComponent<BoxCollider>(car);
        hull.center = new Vector3(0f, 0.615f, 0f);
        hull.size = new Vector3(3.2f, 0.89f, 1.76f);
        BoxCollider cabin = Undo.AddComponent<BoxCollider>(car);
        cabin.center = new Vector3(-0.2f, 1.38f, 0f);
        cabin.size = new Vector3(2.35f, 0.72f, 1.56f);

        if (car.GetComponent<HoverCar>() == null)
        {
            Undo.AddComponent<HoverCar>(car);
        }

        if (car.GetComponent<CarPaint>() == null)
        {
            Undo.AddComponent<CarPaint>(car);
        }

        if (car.GetComponent<ExplosiveVehicle>() == null)
        {
            Undo.AddComponent<ExplosiveVehicle>(car);
        }
    }
}
