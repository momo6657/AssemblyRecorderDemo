using System;

using System.IO;

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



    public string currentTaskId;

    public Button submitButton;

    bool _busy;

    public float minSubmitIntervalSec = AppConstants.MIN_SUBMIT_INTERVAL_SEC;

    float _lastSubmitAt = -999f;



    public async void UploadAndCreate()

    {

        Debug.Log("[PHONE] UploadAndCreate tapped at " + DateTime.Now.ToString("HH:mm:ss.fff"));



        if (Time.unscaledTime - _lastSubmitAt < minSubmitIntervalSec)

        {

            Debug.Log("[PHONE] UploadAndCreate ignored: submit cooldown.");

            return;

        }



        if (_busy)

        {

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



            // 1) upload model

            byte[] glb = File.ReadAllBytes(localGlbPath);

            string up = await api.UploadModelBytes(modelId, glb);

            Debug.Log("[PHONE] uploadModel => " + up);



            // 2) create task（CreateTaskReq / CreateTaskRes 来自 NetModels.cs）

            string title = titleInput != null ? titleInput.text : "";

            if (string.IsNullOrWhiteSpace(title))

                title = defaultTaskTitle + " " + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string uid = userIdInput != null ? userIdInput.text : "";

            uid = string.IsNullOrWhiteSpace(uid) ? "" : uid.Trim();

            var req = new CreateTaskReq { modelId = modelId, title = title, userId = uid };

            string resText = await api.PostJson("/createTask", JsonUtility.ToJson(req));

            Debug.Log("[PHONE] createTask => " + resText);



            var res = JsonUtility.FromJson<CreateTaskRes>(resText);

            if (res == null || !res.ok || res.task == null) throw new Exception("createTask failed");

            currentTaskId = res.task.GetTaskId();

            if (string.IsNullOrEmpty(currentTaskId))

                throw new Exception("createTask returned empty taskId");

            PlayerPrefs.SetString(AppConstants.PREF_PHONE_LAST_TASK_ID, currentTaskId ?? "");

            PlayerPrefs.Save();



            Debug.Log("[PHONE] Task created: " + currentTaskId);

        }

        catch (Exception e)

        {

            Debug.LogError("[PHONE] " + e);

        }

        finally

        {

            _busy = false;

            if (submitButton != null) submitButton.interactable = true;

        }

    }

}

