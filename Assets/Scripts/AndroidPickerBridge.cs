using UnityEngine;

public class AndroidPickerBridge : MonoBehaviour
{
    public ImportManager importManager;
    public PhoneCreateTask phoneCreateTask;

#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaClass plugin;
#endif

    void Awake()
    {
        gameObject.name = "AndroidPickerBridge";

#if UNITY_ANDROID && !UNITY_EDITOR
        plugin = new AndroidJavaClass("com.demo.picker.PickerPlugin");
        plugin.CallStatic("setUnityReceiver", gameObject.name, "OnFilePicked");
#endif
    }

    // UI 按钮绑定这个：打开系统文件选择器
    public void ImportByPicker()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log("[PICKER] openDocumentPicker");
        plugin.CallStatic("openDocumentPicker");
#else
        Debug.LogWarning("[PICKER] Only works on Android device.");
#endif
    }

    // ✅ Java 会 UnitySendMessage 到这里：现在传回来的不是 content://，而是“真实路径”
    public void OnFilePicked(string path)
    {
        Debug.Log("[PICKER] OnFilePicked path=" + path);

        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[PICKER] cancelled or failed");
            return;
        }

        if (importManager != null)
            importManager.LoadGlbFromPath(path);
        else
            Debug.LogWarning("[PICKER] importManager is NULL");

        // 把路径塞给手机端“创建任务”脚本（让你不用手填 localGlbPath）
        if (phoneCreateTask == null)
            phoneCreateTask = FindFirstObjectByType<PhoneCreateTask>();

        if (phoneCreateTask != null)
        {
            phoneCreateTask.localGlbPath = path;
            Debug.Log("[PICKER] phoneCreateTask.localGlbPath set => " + path);
        }
        else
        {
            Debug.LogWarning("[PICKER] PhoneCreateTask not found in scene");
        }
    }
}