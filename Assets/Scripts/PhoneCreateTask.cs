using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PhoneCreateTask : MonoBehaviour
{
    public ApiClient api;

    [Header("你手机端已经复制到 persistent 的 glb 路径（完整路径）")]
    public string localGlbPath;

    [Header("本次上传使用的 modelId（自动生成）")]
    public string modelId;
    public TMP_InputField titleInput;
    public TMP_InputField userIdInput;
    public string defaultTaskTitle = AppConstants.DEFAULT_TASK_TITLE;

    [Header("UI Toolkit Compatibility")]
    public string uiTaskTitle = "";
    public string uiUserId = "";

    public string currentTaskId;
    public Button submitButton;
    bool _busy;
    string _lastStatus = "";
    public float minSubmitIntervalSec = AppConstants.MIN_SUBMIT_INTERVAL_SEC;
    float _lastSubmitAt = -999f;

    public string LastStatus => _lastStatus;

    public void SetUiTaskTitle(string value)
    {
        uiTaskTitle = value != null ? value.Trim() : "";
    }

    public void SetUiUserId(string value)
    {
        uiUserId = value != null ? value.Trim() : "";
    }

    public async void UploadAndCreate()
    {
        SetStatus("[PHONE] Creating task...");
        Debug.Log("[PHONE] UploadAndCreate tapped at " + DateTime.Now.ToString("HH:mm:ss.fff"));

        if (Time.unscaledTime - _lastSubmitAt < minSubmitIntervalSec)
        {
            SetStatus("[PHONE] UploadAndCreate ignored: submit cooldown.");
            Debug.Log("[PHONE] UploadAndCreate ignored: submit cooldown.");
            return;
        }

        if (_busy)
        {
            SetStatus("[PHONE] UploadAndCreate ignored: request already in progress.");
            Debug.Log("[PHONE] UploadAndCreate ignored: request already in progress.");
            return;
        }

        _busy = true;
        _lastSubmitAt = Time.unscaledTime;
        modelId = "m_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        if (submitButton == null) submitButton = GetComponent<Button>();
        if (submitButton != null) submitButton.interactable = false;

        try
        {
            if (api == null) throw new Exception("ApiClient not set");
            if (string.IsNullOrEmpty(localGlbPath) || !File.Exists(localGlbPath))
                throw new Exception("localGlbPath invalid");

            byte[] glb = File.ReadAllBytes(localGlbPath);
            string computedModelHash = ComputeSha256Hex(glb);
            string up = await api.UploadModelBytes(modelId, glb);
            Debug.Log("[PHONE] uploadModel => " + up);

            string title = ResolveTaskTitle();
            if (string.IsNullOrWhiteSpace(title))
                title = defaultTaskTitle + " " + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string uid = ResolveUserId();
            uid = string.IsNullOrWhiteSpace(uid) ? "" : uid.Trim();

            PlaybackAppConfig config = PlaybackAppConfig.Load();
            string configuredHash = config != null && !string.IsNullOrWhiteSpace(config.modelHash) ? config.modelHash.Trim() : "";
            if (!string.IsNullOrEmpty(configuredHash) && !string.Equals(configuredHash, computedModelHash, StringComparison.OrdinalIgnoreCase))
                Debug.LogWarning($"[PHONE] PlaybackAppConfig.modelHash differs from uploaded GLB SHA256. config={configuredHash}, computed={computedModelHash}");

            string logicalModelId = config != null && !string.IsNullOrWhiteSpace(config.logicalModelId) ? config.logicalModelId.Trim() : "";
            if (string.IsNullOrEmpty(logicalModelId) && !string.IsNullOrEmpty(computedModelHash))
                logicalModelId = "lm_" + computedModelHash.Substring(0, Math.Min(16, computedModelHash.Length));
            string modelType = config != null && !string.IsNullOrWhiteSpace(config.modelType) ? config.modelType.Trim() : "";
            string modelHash = computedModelHash;

            var req = new CreateTaskReq
            {
                modelId = modelId,
                title = title,
                userId = uid,
                logicalModelId = logicalModelId,
                modelType = modelType,
                modelHash = modelHash
            };
            string resText = await api.PostJson("/createTask", JsonUtility.ToJson(req));
            Debug.Log("[PHONE] createTask => " + resText);

            var res = JsonUtility.FromJson<CreateTaskRes>(resText);
            if (res == null || !res.ok || res.task == null) throw new Exception("createTask failed");
            currentTaskId = res.task.GetTaskId();
            if (string.IsNullOrEmpty(currentTaskId))
                throw new Exception("createTask returned empty taskId");
            PlayerPrefs.SetString(AppConstants.PREF_PHONE_LAST_TASK_ID, currentTaskId ?? "");
            PlayerPrefs.Save();

            SetStatus("[PHONE] Task created: " + currentTaskId);
            Debug.Log("[PHONE] Task created: " + currentTaskId);
        }
        catch (Exception e)
        {
            SetStatus("[PHONE] " + e.Message);
            Debug.LogError("[PHONE] " + e);
        }
        finally
        {
            _busy = false;
            if (submitButton != null) submitButton.interactable = true;
        }
    }

    string ResolveTaskTitle()
    {
        if (titleInput != null && !string.IsNullOrWhiteSpace(titleInput.text))
            return titleInput.text.Trim();
        if (!string.IsNullOrWhiteSpace(uiTaskTitle))
            return uiTaskTitle.Trim();
        return "";
    }

    string ResolveUserId()
    {
        if (userIdInput != null && !string.IsNullOrWhiteSpace(userIdInput.text))
            return userIdInput.text.Trim();
        if (!string.IsNullOrWhiteSpace(uiUserId))
            return uiUserId.Trim();
        return "";
    }

    static string ComputeSha256Hex(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return "";
        using (var sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }

    void SetStatus(string msg)
    {
        _lastStatus = msg ?? "";
        Debug.Log(msg);
    }
}

