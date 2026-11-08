using System;
using System.IO;
using UnityEngine;

// Arquivos dos jogos salvos (Missão 17). Três espaços: jogo_1.json, jogo_2.json, jogo_3.json,
// na pasta de dados do jogo (Application.persistentDataPath/Saves).
// Grava primeiro num arquivo temporário e guarda a versão anterior como .bak, para um
// salvamento interrompido (queda de energia) não estragar o jogo.
public static class SaveSystem
{
    public const int SlotCount = 3;

    // Espaço escolhido no menu (1 a 3). 0 = nenhum (Play direto pela cena do jogo no editor).
    public static int CurrentSlot { get; set; }

    // O menu pediu "Novo jogo" neste espaço.
    public static bool PendingNewGame { get; set; }

    public static string Folder => Path.Combine(Application.persistentDataPath, "Saves");

    public static string PathFor(int slot) => Path.Combine(Folder, $"jogo_{slot}.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        CurrentSlot = 0;
        PendingNewGame = false;
    }

    public static bool Exists(int slot) => File.Exists(PathFor(slot)) || File.Exists(PathFor(slot) + ".bak");

    public static SaveData Load(int slot)
    {
        string path = PathFor(slot);
        SaveData data = TryRead(path);
        if (data == null)
        {
            data = TryRead(path + ".bak");
            if (data != null)
            {
                Debug.LogWarning($"Jogo {slot}: arquivo principal com problema; usei a cópia de segurança.");
            }
        }

        return data;
    }

    public static bool Write(int slot, SaveData data)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = PathFor(slot);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data, true));

            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", true);
                File.Delete(path);
            }

            File.Move(temp, path);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Não consegui salvar o jogo {slot}: {exception.Message}");
            return false;
        }
    }

    public static void Delete(int slot)
    {
        string path = PathFor(slot);
        foreach (string file in new[] { path, path + ".bak", path + ".tmp" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private static SaveData TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            return data != null && data.version > 0 ? data : null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Não consegui ler {path}: {exception.Message}");
            return null;
        }
    }
}
