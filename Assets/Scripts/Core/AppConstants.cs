/// <summary>
/// 应用常量配置
/// 集中管理所有魔法数字和配置值，便于后续 Android 集成时统一调整
/// </summary>
public static class AppConstants
{
    // ==================== 网络配置 ====================
    
    /// <summary>默认网络请求超时（秒）</summary>
    public const int DEFAULT_TIMEOUT_SEC = 30;
    
    /// <summary>上传模型超时（秒）</summary>
    public const int UPLOAD_TIMEOUT_SEC = 120;
    
    /// <summary>下载模型超时（秒）</summary>
    public const int DOWNLOAD_TIMEOUT_SEC = 60;
    
    // ==================== UI 配置 ====================
    
    /// <summary>错误消息显示时长（秒）</summary>
    public const float ERROR_MESSAGE_DISPLAY_SEC = 1.5f;
    
    /// <summary>最小刷新间隔（秒）</summary>
    public const float MIN_REFRESH_INTERVAL_SEC = 0.4f;
    
    /// <summary>最小提交间隔（秒）- 防止重复提交</summary>
    public const float MIN_SUBMIT_INTERVAL_SEC = 5f;
    
    /// <summary>UI 列表项默认高度</summary>
    public const float UI_ITEM_HEIGHT = 36f;
    
    /// <summary>UI 列表项间距</summary>
    public const float UI_ITEM_SPACING = 8f;
    
    // ==================== 录制配置 ====================
    
    /// <summary>默认步骤动画时长（秒）</summary>
    public const float DEFAULT_STEP_DURATION = 0.6f;
    
    /// <summary>默认轨迹采样率（Hz）</summary>
    public const float DEFAULT_SAMPLE_RATE = 24f;
    
    /// <summary>最小步骤时长（秒）</summary>
    public const float MIN_STEP_DURATION = 0.02f;
    
    // ==================== 移动阈值 ====================
    
    /// <summary>位置变化阈值（米）</summary>
    public const float MOVE_POS_THRESHOLD = 0.001f;
    
    /// <summary>旋转变化阈值（度）</summary>
    public const float MOVE_ROT_THRESHOLD_DEG = 0.8f;
    
    /// <summary>缩放变化阈值</summary>
    public const float MOVE_SCALE_THRESHOLD = 0.001f;
    
    // ==================== 相机配置 ====================
    
    /// <summary>相机旋转速度</summary>
    public const float CAMERA_ROTATE_SPEED = 0.2f;
    
    /// <summary>相机缩放速度</summary>
    public const float CAMERA_ZOOM_SPEED = 0.02f;
    
    /// <summary>相机最小距离</summary>
    public const float CAMERA_MIN_DISTANCE = 1f;
    
    /// <summary>相机最大距离</summary>
    public const float CAMERA_MAX_DISTANCE = 20f;
    
    /// <summary>相机最小俯仰角</summary>
    public const float CAMERA_MIN_PITCH = -20f;
    
    /// <summary>相机最大俯仰角</summary>
    public const float CAMERA_MAX_PITCH = 80f;
    
    // ==================== XR 配置 ====================
    
    /// <summary>XR 模型放置距离（米）</summary>
    public const float XR_PLACE_DISTANCE = 1.2f;
    
    /// <summary>XR 模型垂直偏移（米）</summary>
    public const float XR_PLACE_VERTICAL_OFFSET = -0.1f;
    
    /// <summary>XR 模型目标最大尺寸（米）</summary>
    public const float XR_TARGET_MAX_SIZE = 0.5f;
    
    /// <summary>XR 相机异常 Y 阈值</summary>
    public const float XR_ABNORMAL_CAMERA_Y_THRESHOLD = 20f;
    
    // ==================== PlayerPrefs Keys ====================
    
    /// <summary>手机端最后的任务 ID</summary>
    public const string PREF_PHONE_LAST_TASK_ID = "phone.lastTaskId";
    
    /// <summary>手机端最后的录制 ID</summary>
    public const string PREF_PHONE_LAST_RECORDING_ID = "phone.lastRecordingId";
    
    /// <summary>Quest 端最后的任务 ID</summary>
    public const string PREF_QUEST_LAST_TASK_ID = "quest.lastTaskId";
    
    /// <summary>Quest 端最后的模型 ID</summary>
    public const string PREF_QUEST_LAST_MODEL_ID = "quest.lastModelId";
    
    /// <summary>Quest 端最后的模型路径</summary>
    public const string PREF_QUEST_LAST_MODEL_PATH = "quest.lastModelPath";
    
    // ==================== 默认值 ====================
    
    /// <summary>默认任务标题</summary>
    public const string DEFAULT_TASK_TITLE = "Assembly Task";
    
    /// <summary>默认录制标题前缀</summary>
    public const string DEFAULT_RECORDING_TITLE_PREFIX = "Recording_";
}
