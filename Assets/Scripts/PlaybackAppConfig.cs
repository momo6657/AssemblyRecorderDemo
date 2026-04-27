using UnityEngine;

public enum AppBuildMode
{
    PlayerLocked = 0,
    DebugAll = 1,
}

[CreateAssetMenu(fileName = "PlaybackAppConfig", menuName = "Assembly Recorder/Playback App Config")]
public class PlaybackAppConfig : ScriptableObject
{
    [Header("打包版本模型绑定")]
    public string logicalModelId = "";
    public string modelType = "";
    public string modelHash = "";

    [Header("录制列表过滤")]
    public bool lockRecordingsToConfiguredModel = false;
    public AppBuildMode buildMode = AppBuildMode.PlayerLocked;

    public static PlaybackAppConfig Load()
    {
        return Resources.Load<PlaybackAppConfig>("PlaybackAppConfig");
    }
}
