using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class QuestPollTask : MonoBehaviour
{
    // 使用 AppConstants 统一管理
    const string PrefTaskId = AppConstants.PREF_QUEST_LAST_TASK_ID;
    const string PrefModelId = AppConstants.PREF_QUEST_LAST_MODEL_ID;
    const string PrefModelPath = AppConstants.PREF_QUEST_LAST_MODEL_PATH;

    [Header("Refs")]
    public ApiClient api;
    public ImportManager importManager;
    public TMP_Text statusText;

    [Header("Polling")]
    public bool autoPoll = true;
    public bool pollImmediatelyOnStart = true;
    public float pollIntervalSec = 1f;
    public bool stopAutoPollAfterTaskReceived = false;

    [Header("Task Intake Control")]
    public bool holdLoadedTaskUntilManualRelease = false;
    public bool restoreLastModelOnStart = false;
    public bool latchAfterFirstSuccessfulImport = false;

    [Header("Current task context (for QuestStepSession)")]
    public string currentTaskId;
    public string currentModelId;
    public string currentTaskTitle;
    public string currentTaskUserId;
    public string currentLogicalModelId;
    public string currentModelType;
    public string currentModelHash;
    public bool skipReloadIfSameTaskAlreadyLoaded = true;
    public bool skipReloadIfSameModelAlreadyLoaded = true;

    [Header("Debug State")]
    public string lastLoadedTaskId;
    public string lastLoadedModelId;

    public event Action<string, string> OnTaskContextChanged;

    public bool IsBusy => _busy;

    CancellationTokenSource _pollCts;
    bool _busy;
    bool _taskIntakeHeld;
    bool _taskLatched;
    bool _restoreTried;
    string _lastStatus;

    async void Start()
    {
        Application.runInBackground = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Debug.Log("[QUEST] runtime keep-alive enabled (runInBackground + neverSleep).");
        Debug.Log("[QUEST] poll config => autoPoll=" + autoPoll
                  + ", stopAfterLoad=" + stopAutoPollAfterTaskReceived
                  + ", hold=" + holdLoadedTaskUntilManualRelease
                  + ", restore=" + restoreLastModelOnStart
                  + ", latch=" + latchAfterFirstSuccessfulImport);
        var scene = SceneManager.GetActiveScene();
        Debug.Log("[QUEST] active scene => " + scene.path);

        if (statusText == null)
        {
            if (importManager == null) importManager = FindFirstObjectByType<ImportManager>();
            if (importManager != null) statusText = importManager.statusText;
        }

        if (restoreLastModelOnStart)
            await TryRestoreLocalModelAtStartup();

        if (!autoPoll) return;

        _pollCts = new CancellationTokenSource();

        if (pollImmediatelyOnStart)
            PollNow();

        await AutoPollLoop(_pollCts.Token);
    }

    void OnApplicationPause(bool pauseStatus)
    {
        Debug.Log("[QUEST] OnApplicationPause=" + pauseStatus + " at " + DateTime.Now.ToString("HH:mm:ss.fff"));
    }

    void OnApplicationFocus(bool hasFocus)
    {
        Debug.Log("[QUEST] OnApplicationFocus=" + hasFocus + " at " + DateTime.Now.ToString("HH:mm:ss.fff"));
    }

    void OnDisable()
    {
        if (_pollCts == null) return;
        _pollCts.Cancel();
        _pollCts.Dispose();
        _pollCts = null;
    }

    public async void PollNow()
    {
        await PollOnceInternal();
    }

    public void ReleaseTaskIntake()
    {
        _taskIntakeHeld = false;
        _taskLatched = false;
        SetStatus("[QUEST] task intake released.");
    }

    async Task AutoPollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            int delayMs = Mathf.Max(200, Mathf.RoundToInt(pollIntervalSec * 1000f));
            try
            {
                await Task.Delay(delayMs, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (!autoPoll || token.IsCancellationRequested)
                break;

            await PollOnceInternal();
        }
    }

    async Task<bool> PollOnceInternal()
    {
        if (_busy) return false;

        // 使用 ServiceLocator 获取服务（避免频繁 Find）
        if (api == null) api = ServiceLocator.Instance?.apiClient;
        if (importManager == null) importManager = ServiceLocator.Instance?.importManager;
        if (statusText == null && importManager != null) statusText = importManager.statusText;

        if (_taskIntakeHeld && importManager != null && importManager.CurrentModel != null)
        {
            SetStatus("[QUEST] task intake held, skip poll.");
            return false;
        }

        if (_taskIntakeHeld && (importManager == null || importManager.CurrentModel == null))
            _taskIntakeHeld = false;

        if (latchAfterFirstSuccessfulImport && _taskLatched && importManager != null && importManager.CurrentModel != null)
        {
            SetStatus("[QUEST] task latched, skip poll.");
            return false;
        }

        if (!_restoreTried && restoreLastModelOnStart)
            await TryRestoreLocalModelAtStartup();

        if (api == null)
        {
            SetStatus("[QUEST] ApiClient is null on QuestPollTask.");
            return false;
        }

        if (importManager == null)
        {
            SetStatus("[QUEST] ImportManager is null on QuestPollTask.");
            return false;
        }

        if (importManager.IsLoading)
        {
            SetStatus("[QUEST] import in progress, skip poll tick.");
            return false;
        }

        _busy = true;
        try
        {
            string text = await api.GetText("/pollTask?device=quest");
            Debug.Log("[QUEST] /pollTask raw => " + text);
            var res = JsonUtility.FromJson<PollTaskRes>(text);

            if (res == null || !res.ok)
                return false;

            currentTaskId = res.GetTaskId();
            currentModelId = res.GetModelId();
            currentTaskTitle = res.task != null ? res.task.title : "";
            currentTaskUserId = res.task != null
                ? (!string.IsNullOrEmpty(res.task.userId) ? res.task.userId : res.task.user_id)
                : "";
            currentLogicalModelId = res.task != null ? res.task.GetLogicalModelId() : "";
            currentModelType = res.task != null ? res.task.GetModelType() : "";
            currentModelHash = res.task != null ? res.task.GetModelHash() : "";

            if (string.IsNullOrEmpty(currentTaskId) || string.IsNullOrEmpty(currentModelId))
            {
                SetStatus("[QUEST] poll returned empty task/model id, skip download.");
                return false;
            }

            if (skipReloadIfSameTaskAlreadyLoaded &&
                currentTaskId == lastLoadedTaskId &&
                currentModelId == lastLoadedModelId &&
                importManager.CurrentModel != null)
            {
                SetStatus("[QUEST] same task already loaded, skip reload. taskId=" + currentTaskId);
                return false;
            }

            if (skipReloadIfSameModelAlreadyLoaded &&
                currentModelId == lastLoadedModelId &&
                importManager.CurrentModel != null)
            {
                bool taskChanged = !string.Equals(currentTaskId, lastLoadedTaskId, StringComparison.Ordinal);
                lastLoadedTaskId = currentTaskId;

                string cachedPath = PlayerPrefs.GetString(PrefModelPath, "");
                if (string.IsNullOrEmpty(cachedPath))
                    cachedPath = Path.Combine(Application.persistentDataPath, currentModelId + ".glb");
                SaveLastContext(currentTaskId, currentModelId, cachedPath);

                if (taskChanged)
                    OnTaskContextChanged?.Invoke(currentTaskId, currentModelId);

                SetStatus("[QUEST] same model already loaded, updated task context only. taskId=" + currentTaskId);
                return false;
            }

            OnTaskContextChanged?.Invoke(currentTaskId, currentModelId);

            SetStatus("[QUEST] got taskId=" + currentTaskId + " modelId=" + currentModelId
                      + " logicalModelId=" + currentLogicalModelId
                      + " modelType=" + currentModelType
                      + " modelHash=" + currentModelHash);

            byte[] glb = await api.GetBytes("/downloadModel?modelId=" + currentModelId);
            if (glb == null || glb.Length == 0)
                throw new Exception("downloaded model bytes are empty");

            string path = Path.Combine(Application.persistentDataPath, currentModelId + ".glb");
            File.WriteAllBytes(path, glb);
            SetStatus("[QUEST] saved model => " + path);

            bool loaded = await importManager.LoadGlbFromPathAsync(path);
            if (!loaded)
                throw new Exception("ImportManager failed to load model");

            lastLoadedTaskId = currentTaskId;
            lastLoadedModelId = currentModelId;
            SaveLastContext(currentTaskId, currentModelId, path);
            _taskLatched = true;

            if (stopAutoPollAfterTaskReceived)
            {
                autoPoll = false;
                _pollCts?.Cancel();
                SetStatus("[QUEST] auto poll stopped after task received.");
            }

            if (holdLoadedTaskUntilManualRelease)
            {
                _taskIntakeHeld = true;
                SetStatus("[QUEST] task loaded, intake held.");
            }

            return true;
        }
        catch (Exception e)
        {
            SetStatus("[QUEST] Poll failed: " + e.Message);
            Debug.LogError("[QUEST] Poll failed: " + e);
            return false;
        }
        finally
        {
            _busy = false;
        }
    }

    void SetStatus(string msg)
    {
        if (string.Equals(_lastStatus, msg, StringComparison.Ordinal)) return;
        _lastStatus = msg;
        Debug.Log(msg);
        if (statusText != null) statusText.text = msg;
    }

    async Task TryRestoreLocalModelAtStartup()
    {
        _restoreTried = true;

        if (importManager == null) importManager = FindFirstObjectByType<ImportManager>();
        if (importManager == null) return;
        if (importManager.CurrentModel != null) return;

        string cachedModelId = PlayerPrefs.GetString(PrefModelId, "");
        string cachedTaskId = PlayerPrefs.GetString(PrefTaskId, "");
        string cachedPath = PlayerPrefs.GetString(PrefModelPath, "");

        if (string.IsNullOrEmpty(cachedPath) && !string.IsNullOrEmpty(cachedModelId))
            cachedPath = Path.Combine(Application.persistentDataPath, cachedModelId + ".glb");

        if (string.IsNullOrEmpty(cachedPath) || !File.Exists(cachedPath))
            return;

        SetStatus("[QUEST] restoring local model => " + cachedPath);

        bool loaded = await importManager.LoadGlbFromPathAsync(cachedPath);
        if (!loaded) return;

        currentTaskId = cachedTaskId;
        currentModelId = cachedModelId;
        lastLoadedTaskId = cachedTaskId;
        lastLoadedModelId = cachedModelId;
        _taskLatched = true;

        if (holdLoadedTaskUntilManualRelease)
            _taskIntakeHeld = true;

        SetStatus("[QUEST] restored local model success. modelId=" + cachedModelId);
    }

    void SaveLastContext(string taskId, string modelId, string path)
    {
        PlayerPrefs.SetString(PrefTaskId, taskId ?? "");
        PlayerPrefs.SetString(PrefModelId, modelId ?? "");
        PlayerPrefs.SetString(PrefModelPath, path ?? "");
        PlayerPrefs.Save();
    }
}
