#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;

public class SafImportPicker : MonoBehaviour
{
    public ImportManager importManager;
    public string targetModelId = "imported";

    const int REQ_CODE = 4242;
    AndroidJavaObject activity;

    void Awake()
    {
        using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
    }

    // UI按钮调用
    public void OpenPicker()
    {
        Debug.Log("[SAF] OpenPicker");

        try
        {
            using var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.OPEN_DOCUMENT");
            intent.Call<AndroidJavaObject>("addCategory", "android.intent.category.OPENABLE");
            intent.Call<AndroidJavaObject>("setType", "*/*");

            // 只给 glb/gltf 作为参考（不同系统不一定完全按mime过滤，但够用）
            string[] mimeTypes = new string[] { "model/gltf-binary", "model/gltf+json", "application/octet-stream" };
            intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.MIME_TYPES", mimeTypes);

            activity.Call("startActivityForResult", intent, REQ_CODE);
        }
        catch (Exception e)
        {
            Debug.LogError("[SAF] OpenPicker failed: " + e);
        }
    }

    // 需要 UnitySendMessage 调用：我们用一个 AndroidJavaProxy 方式在 Update 里轮询结果
    // 为了最小实现：用 UnityPlayerActivity 的 onActivityResult 我们拿不到，所以下面用一个“结果轮询桥”
    // => 最简单：改成使用 NativeFilePicker 或者写 Kotlin 插件
    // 但你当前设备会杀回调，所以建议你直接用 B2（Kotlin插件）会最稳
}
#endif