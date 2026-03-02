using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class ApiClient : MonoBehaviour
{
    [Header("改成你电脑IP，比如 http://192.168.1.50:5000")]
    public string baseUrl = "http://192.168.1.50:5000";

    public async Task<string> GetText(string path)
    {
        using var req = UnityWebRequest.Get(baseUrl + path);
        req.timeout = 30;
        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        if (req.result != UnityWebRequest.Result.Success) throw new Exception(req.error);
        return req.downloadHandler.text;
    }

    public async Task<byte[]> GetBytes(string path)
    {
        using var req = UnityWebRequest.Get(baseUrl + path);
        req.timeout = 60;
        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        if (req.result != UnityWebRequest.Result.Success) throw new Exception(req.error);
        return req.downloadHandler.data;
    }

    public async Task<string> PostJson(string path, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var req = new UnityWebRequest(baseUrl + path, "POST");
        req.uploadHandler = new UploadHandlerRaw(bytes);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
        req.timeout = 30;

        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        if (req.result != UnityWebRequest.Result.Success) throw new Exception(req.error);
        return req.downloadHandler.text;
    }

    // 适配你现在的 /uploadModel：二进制 body + X-Model-Id
    public async Task<string> UploadModelBytes(string modelId, byte[] glbBytes)
    {
        using var req = new UnityWebRequest(baseUrl + "/uploadModel", "POST");
        req.uploadHandler = new UploadHandlerRaw(glbBytes);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/octet-stream");
        req.SetRequestHeader("X-Model-Id", modelId);
        req.timeout = 120;

        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        if (req.result != UnityWebRequest.Result.Success) throw new Exception(req.error);
        return req.downloadHandler.text;
    }
}