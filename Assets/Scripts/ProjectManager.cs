using System.IO;
using UnityEngine;

public class ProjectManager : MonoBehaviour
{
    public ModelProject current;

    public string GetProjectPath(string modelId)
        => Path.Combine(Application.persistentDataPath, $"project_{modelId}.json");

    public void NewProject(string modelId, string modelFileName)
    {
        current = new ModelProject {
            modelId = modelId,
            modelFileName = modelFileName
        };
    }

    public void Save()
    {
        if (current == null) return;
        var json = JsonUtility.ToJson(current, true);
        File.WriteAllText(GetProjectPath(current.modelId), json);
        Debug.Log("Saved: " + GetProjectPath(current.modelId));
    }

    public ModelProject Load(string modelId)
    {
        var path = GetProjectPath(modelId);
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonUtility.FromJson<ModelProject>(json);
    }
}
