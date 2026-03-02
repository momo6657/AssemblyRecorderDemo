using UnityEngine;

/// <summary>
/// 服务定位器：统一管理场景中的核心组件引用，避免频繁的 FindFirstObjectByType 调用
/// 使用方式：ServiceLocator.Instance.apiClient
/// </summary>
public class ServiceLocator : MonoBehaviour
{
    private static ServiceLocator _instance;
    
    public static ServiceLocator Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<ServiceLocator>();
                if (_instance == null)
                {
                    var go = new GameObject("ServiceLocator");
                    _instance = go.AddComponent<ServiceLocator>();
                    Debug.LogWarning("[ServiceLocator] Created new instance automatically.");
                }
            }
            return _instance;
        }
    }

    [Header("Core Services - 在 Inspector 中拖入或自动查找")]
    public ApiClient apiClient;
    public ImportManager importManager;
    public ModelIndex modelIndex;
    public SelectionManager selectionManager;
    public StepPlayer stepPlayer;
    public StepRecorder stepRecorder;
    public ProjectManager projectManager;
    
    [Header("Phone Services")]
    public PhoneStepPlayback phoneStepPlayback;
    public PhoneCreateTask phoneCreateTask;
    
    [Header("Quest Services")]
    public QuestStepSession questStepSession;
    public QuestPollTask questPollTask;
    
    [Header("UI Services")]
    public UIController uiController;
    public RecordingListUI recordingListUI;
    public StepListUI stepListUI;
    public TransformPanel transformPanel;
    
    [Header("Camera & Input")]
    public OrbitPinchCamera orbitPinchCamera;

    void Awake()
    {
        // 单例模式
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[ServiceLocator] Duplicate instance detected, destroying.");
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
        
        // 自动查找所有服务（如果 Inspector 中未设置）
        AutoFindServices();
        
        Debug.Log("[ServiceLocator] Initialized with " + CountServices() + " services.");
    }

    void AutoFindServices()
    {
        if (apiClient == null) apiClient = FindFirstObjectByType<ApiClient>();
        if (importManager == null) importManager = FindFirstObjectByType<ImportManager>();
        if (modelIndex == null) modelIndex = FindFirstObjectByType<ModelIndex>();
        if (selectionManager == null) selectionManager = FindFirstObjectByType<SelectionManager>();
        if (stepPlayer == null) stepPlayer = FindFirstObjectByType<StepPlayer>();
        if (stepRecorder == null) stepRecorder = FindFirstObjectByType<StepRecorder>();
        if (projectManager == null) projectManager = FindFirstObjectByType<ProjectManager>();
        if (phoneStepPlayback == null) phoneStepPlayback = FindFirstObjectByType<PhoneStepPlayback>();
        if (phoneCreateTask == null) phoneCreateTask = FindFirstObjectByType<PhoneCreateTask>();
        if (questStepSession == null) questStepSession = FindFirstObjectByType<QuestStepSession>();
        if (questPollTask == null) questPollTask = FindFirstObjectByType<QuestPollTask>();
        if (uiController == null) uiController = FindFirstObjectByType<UIController>();
        if (recordingListUI == null) recordingListUI = FindFirstObjectByType<RecordingListUI>();
        if (stepListUI == null) stepListUI = FindFirstObjectByType<StepListUI>();
        if (transformPanel == null) transformPanel = FindFirstObjectByType<TransformPanel>();
        if (orbitPinchCamera == null) orbitPinchCamera = FindFirstObjectByType<OrbitPinchCamera>();
    }

    int CountServices()
    {
        int count = 0;
        if (apiClient != null) count++;
        if (importManager != null) count++;
        if (modelIndex != null) count++;
        if (selectionManager != null) count++;
        if (stepPlayer != null) count++;
        if (stepRecorder != null) count++;
        if (projectManager != null) count++;
        if (phoneStepPlayback != null) count++;
        if (phoneCreateTask != null) count++;
        if (questStepSession != null) count++;
        if (questPollTask != null) count++;
        if (uiController != null) count++;
        if (recordingListUI != null) count++;
        if (stepListUI != null) count++;
        if (transformPanel != null) count++;
        if (orbitPinchCamera != null) count++;
        return count;
    }

    /// <summary>
    /// 便捷方法：获取 API 客户端
    /// </summary>
    public static ApiClient GetApiClient() => Instance.apiClient;
    
    /// <summary>
    /// 便捷方法：获取导入管理器
    /// </summary>
    public static ImportManager GetImportManager() => Instance.importManager;
    
    /// <summary>
    /// 便捷方法：获取模型索引
    /// </summary>
    public static ModelIndex GetModelIndex() => Instance.modelIndex;
}
