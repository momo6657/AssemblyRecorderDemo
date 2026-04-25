using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class ApiClient : MonoBehaviour
{
    private const string BaseUrlOverridePlayerPrefsKey = "ApiClient.BaseUrlOverride";
    private const string ServerConfigResourcePath = "ServerConfig";

    private static ServerConfig _cachedServerConfig;

    [Header("是否启用当前组件上的 baseUrl 覆盖")]
    public bool useInspectorOverride = false;

    [Header("组件级覆盖地址（仅当上方勾选且全局配置允许时生效）")]
    public string baseUrl = "";

    [Header("组件级 API Key 覆盖（留空则使用全局配置）")]
    public string apiKey = "";

    public static void SetRuntimeBaseUrlOverride(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            PlayerPrefs.DeleteKey(BaseUrlOverridePlayerPrefsKey);
        }
        else
        {
            PlayerPrefs.SetString(BaseUrlOverridePlayerPrefsKey, url.Trim());
        }

        PlayerPrefs.Save();
    }

    public static void ClearRuntimeBaseUrlOverride()
    {
        PlayerPrefs.DeleteKey(BaseUrlOverridePlayerPrefsKey);
        PlayerPrefs.Save();
    }

    public static ServerConfig GetServerConfig()
    {
        if (_cachedServerConfig == null)
        {
            _cachedServerConfig = Resources.Load<ServerConfig>(ServerConfigResourcePath);
        }

        return _cachedServerConfig;
    }

    private string GetResolvedBaseUrl()
    {
        string runtimeOverride = PlayerPrefs.GetString(BaseUrlOverridePlayerPrefsKey, "").Trim();
        if (!string.IsNullOrEmpty(runtimeOverride))
        {
            return NormalizeBaseUrl(runtimeOverride);
        }

        ServerConfig config = GetServerConfig();
        if (config == null)
        {
            throw new Exception("[ApiClient] Missing Resources/ServerConfig asset.");
        }

        if (config.allowInspectorOverride && useInspectorOverride && !string.IsNullOrWhiteSpace(baseUrl))
        {
            return NormalizeBaseUrl(baseUrl);
        }

        string configuredBaseUrl = config.environment == ServerEnvironment.Cloud
            ? config.cloudBaseUrl
            : config.localBaseUrl;

        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            throw new Exception($"[ApiClient] ServerConfig base URL is empty for environment: {config.environment}");
        }

        return NormalizeBaseUrl(configuredBaseUrl);
    }

    private string GetResolvedApiKey()
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey.Trim();
        }

        ServerConfig config = GetServerConfig();
        return config != null ? (config.apiKey ?? string.Empty).Trim() : string.Empty;
    }

    private string BuildUrl(string path)
    {
        string resolvedBaseUrl = GetResolvedBaseUrl();
        if (string.IsNullOrEmpty(path)) return resolvedBaseUrl;
        if (path.StartsWith("/")) return resolvedBaseUrl + path;
        return resolvedBaseUrl + "/" + path;
    }

    private string NormalizeBaseUrl(string raw)
    {
        string value = raw.Trim().TrimEnd('/');
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        bool looksLikeLocal = value.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                              value.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                              value.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase) ||
                              value.StartsWith("10.", StringComparison.OrdinalIgnoreCase) ||
                              value.StartsWith("172.", StringComparison.OrdinalIgnoreCase);

        return (looksLikeLocal ? "http://" : "https://") + value;
    }

    private void SetCommonHeaders(UnityWebRequest req)
    {
        string resolvedApiKey = GetResolvedApiKey();
        if (!string.IsNullOrEmpty(resolvedApiKey))
        {
            req.SetRequestHeader("X-Api-Key", resolvedApiKey);
        }
    }

    public async Task<string> GetText(string path)
    {
        using var req = UnityWebRequest.Get(BuildUrl(path));
        req.timeout = 30;
        SetCommonHeaders(req);
        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        if (req.result != UnityWebRequest.Result.Success) throw new Exception(req.error);
        return req.downloadHandler.text;
    }

    public async Task<byte[]> GetBytes(string path)
    {
        using var req = UnityWebRequest.Get(BuildUrl(path));
        req.timeout = 60;
        SetCommonHeaders(req);
        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        if (req.result != UnityWebRequest.Result.Success) throw new Exception(req.error);
        return req.downloadHandler.data;
    }

    public async Task<string> PostJson(string path, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var req = new UnityWebRequest(BuildUrl(path), "POST");
        req.uploadHandler = new UploadHandlerRaw(bytes);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
        req.timeout = 30;
        SetCommonHeaders(req);

        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        if (req.result != UnityWebRequest.Result.Success) throw new Exception(req.error);
        return req.downloadHandler.text;
    }

    public async Task<string> UploadModelBytes(string modelId, byte[] glbBytes)
    {
        using var req = new UnityWebRequest(BuildUrl("/uploadModel"), "POST");
        req.uploadHandler = new UploadHandlerRaw(glbBytes);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/octet-stream");
        req.SetRequestHeader("X-Model-Id", modelId);
        req.timeout = 120;
        SetCommonHeaders(req);

        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        if (req.result != UnityWebRequest.Result.Success) throw new Exception(req.error);
        return req.downloadHandler.text;
    }
}
