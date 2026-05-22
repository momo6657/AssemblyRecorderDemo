using UnityEngine;

public enum ServerEnvironment
{
    Local = 0,
    Cloud = 1,
}

[CreateAssetMenu(fileName = "ServerConfig", menuName = "Assembly Recorder/Server Config")]
public class ServerConfig : ScriptableObject
{
    [Header("当前环境")]
    public ServerEnvironment environment = ServerEnvironment.Cloud;

    [Header("本地开发地址（同局域网/热点测试）")]
    public string localBaseUrl = "http://192.168.1.50:5000";

    [Header("云端发布地址")]
    public string cloudBaseUrl = "https://yunjingzhilian.asia";

    [Header("API Key（服务器未设置则留空）")]
    public string apiKey = "";

    [Header("是否允许组件级覆盖")]
    public bool allowInspectorOverride = true;
}
