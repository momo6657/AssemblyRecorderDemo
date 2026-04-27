using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class PhonePlaybackUIDocumentController : MonoBehaviour
{
    public UIDocument uiDocument;
    public PhoneStepPlayback phonePlayback;
    public PhoneCreateTask phoneCreateTask;
    public AndroidPickerBridge androidPickerBridge;
    public ServiceLocator serviceLocator;
    public bool autoRefreshRecordingsOnEnable = true;
    public float pollUiInterval = 0.15f;

    public bool showImportButton = false;
    public bool showStatusPanel = true;
    public bool showRecordingsPanel = true;
    public bool showTaskPanel = false;
    public bool showTimelinePanel = true;
    public bool showJumpControls = true;
    public bool showNavigationButtons = true;

    VisualElement _root, _topLeftArea, _topRightArea, _statusPanel, _recordingsPanel, _taskPanel, _timelinePanel, _controlsPanel, _navRow, _statusDot;
    VisualElement _backgroundVignette, _backgroundGlow, _leftRailArea, _bottomRightArea;
    Button _btnImport, _btnRefreshRecordings, _btnCreateTask, _btnJump, _btnPrev, _btnNext;
    Button _btnMinStatus, _btnMinLibrary, _btnMinTask, _btnMinSteps, _btnMinJump, _btnMinNav;
    Button _btnRestoreImport, _btnRestoreStatus, _btnRestoreLibrary, _btnRestoreTask, _btnRestoreSteps, _btnRestoreJump, _btnRestoreNav;
    TextField _taskTitleInput, _userIdInput, _stepInput;
    Label _statusBadge, _currentStepValue, _taskRecordingValue, _messageValue;
    ScrollView _recordingsList, _stepsList;

    int _lastStepCount = -1, _lastCurrentIndex = int.MinValue;
    string _lastRecordingId = "", _lastTaskId = "", _lastStatusMessage = "";
    float _nextUiRefreshAt;
    bool _refreshingRecordings;

    bool _importButtonMinimized;
    bool _statusPanelMinimized;
    bool _recordingsPanelMinimized;
    bool _taskPanelMinimized;
    bool _timelinePanelMinimized;
    bool _jumpControlsMinimized;
    bool _navigationButtonsMinimized;

    public bool IsPointerOverInteractiveUi(Vector2 screenPosition)
    {
        if (_root == null || _root.panel == null) return false;

        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(_root.panel, screenPosition);
        VisualElement picked = _root.panel.Pick(panelPosition);
        if (picked == null) return false;

        return picked is Button || picked is TextField || picked is ScrollView || picked.GetFirstAncestorOfType<Button>() != null || picked.GetFirstAncestorOfType<TextField>() != null || picked.GetFirstAncestorOfType<ScrollView>() != null;
    }

    void OnEnable()
    {
        EnsureRefs();
        SyncMinimizedStateWithConfig();
        BindUi();
        ApplyModuleVisibility();
        SyncCreateTaskInputs();
        RefreshStatusArea();
        RefreshStepsListIfNeeded(true);
        if (autoRefreshRecordingsOnEnable) _ = RefreshRecordingsAsync(true);
    }

    void Update()
    {
        if (_root == null || Time.unscaledTime < _nextUiRefreshAt) return;
        _nextUiRefreshAt = Time.unscaledTime + pollUiInterval;
        ApplyModuleVisibility();
        SyncCreateTaskInputs();
        RefreshStatusArea();
        RefreshStepsListIfNeeded(false);
    }

    void OnDisable() => UnbindButtons();

    void OnValidate()
    {
        SyncMinimizedStateWithConfig();
        if (_root != null)
            ApplyModuleVisibility();
    }

    void EnsureRefs()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (serviceLocator == null) serviceLocator = ServiceLocator.Instance;
        if (phonePlayback == null) phonePlayback = serviceLocator != null ? serviceLocator.phoneStepPlayback : FindFirstObjectByType<PhoneStepPlayback>();
        if (phoneCreateTask == null) phoneCreateTask = serviceLocator != null ? serviceLocator.phoneCreateTask : FindFirstObjectByType<PhoneCreateTask>();
        if (androidPickerBridge == null) androidPickerBridge = FindFirstObjectByType<AndroidPickerBridge>();
    }

    void BindUi()
    {
        if (uiDocument == null) return;
        _root = uiDocument.rootVisualElement;
        if (_root == null) return;

        _topLeftArea = _root.Q<VisualElement>("top-left-area");
        _topRightArea = _root.Q<VisualElement>("top-right-area");
        _leftRailArea = _root.Q<VisualElement>("left-rail-area");
        _bottomRightArea = _root.Q<VisualElement>("bottom-right-area");
        _backgroundVignette = _root.Q<VisualElement>("background-vignette");
        _backgroundGlow = _root.Q<VisualElement>("background-glow");
        _statusPanel = _root.Q<VisualElement>("status-panel");
        _recordingsPanel = _root.Q<VisualElement>("recordings-panel");
        _taskPanel = _root.Q<VisualElement>("task-panel");
        _timelinePanel = _root.Q<VisualElement>("timeline-panel");
        _controlsPanel = _root.Q<VisualElement>("controls-panel");
        _navRow = _root.Q<VisualElement>("nav-row");
        _statusDot = _root.Q<VisualElement>("status-dot");

        _btnImport = _root.Q<Button>("btn-import");
        _btnRefreshRecordings = _root.Q<Button>("btn-refresh-recordings");
        _btnCreateTask = _root.Q<Button>("btn-create-task");
        _btnJump = _root.Q<Button>("btn-jump");
        _btnPrev = _root.Q<Button>("btn-prev");
        _btnNext = _root.Q<Button>("btn-next");
        _btnMinStatus = _root.Q<Button>("btn-min-status");
        _btnMinLibrary = _root.Q<Button>("btn-min-library");
        _btnMinTask = _root.Q<Button>("btn-min-task");
        _btnMinSteps = _root.Q<Button>("btn-min-steps");
        _btnMinJump = _root.Q<Button>("btn-min-jump");
        _btnMinNav = _root.Q<Button>("btn-min-nav");
        _btnRestoreImport = _root.Q<Button>("btn-restore-import");
        _btnRestoreStatus = _root.Q<Button>("btn-restore-status");
        _btnRestoreLibrary = _root.Q<Button>("btn-restore-library");
        _btnRestoreTask = _root.Q<Button>("btn-restore-task");
        _btnRestoreSteps = _root.Q<Button>("btn-restore-steps");
        _btnRestoreJump = _root.Q<Button>("btn-restore-jump");
        _btnRestoreNav = _root.Q<Button>("btn-restore-nav");

        _taskTitleInput = _root.Q<TextField>("task-title-input");
        _userIdInput = _root.Q<TextField>("user-id-input");
        _stepInput = _root.Q<TextField>("step-input");
        _statusBadge = _root.Q<Label>("status-badge");
        _currentStepValue = _root.Q<Label>("current-step-value");
        _taskRecordingValue = _root.Q<Label>("task-recording-value");
        _messageValue = _root.Q<Label>("message-value");
        _recordingsList = _root.Q<ScrollView>("recordings-list");
        _stepsList = _root.Q<ScrollView>("steps-list");

        DisableHitTestingForPassiveUi();

        UnbindButtons();
        if (_btnImport != null) _btnImport.clicked += OnImportClicked;
        if (_btnRefreshRecordings != null) _btnRefreshRecordings.clicked += OnRefreshRecordingsClicked;
        if (_btnCreateTask != null) _btnCreateTask.clicked += OnCreateTaskClicked;
        if (_btnJump != null) _btnJump.clicked += OnJumpClicked;
        if (_btnPrev != null) _btnPrev.clicked += OnPrevClicked;
        if (_btnNext != null) _btnNext.clicked += OnNextClicked;
        if (_btnMinStatus != null) _btnMinStatus.clicked += OnMinStatusClicked;
        if (_btnMinLibrary != null) _btnMinLibrary.clicked += OnMinLibraryClicked;
        if (_btnMinTask != null) _btnMinTask.clicked += OnMinTaskClicked;
        if (_btnMinSteps != null) _btnMinSteps.clicked += OnMinStepsClicked;
        if (_btnMinJump != null) _btnMinJump.clicked += OnMinJumpClicked;
        if (_btnMinNav != null) _btnMinNav.clicked += OnMinNavClicked;
        if (_btnRestoreImport != null) _btnRestoreImport.clicked += OnRestoreImportClicked;
        if (_btnRestoreStatus != null) _btnRestoreStatus.clicked += OnRestoreStatusClicked;
        if (_btnRestoreLibrary != null) _btnRestoreLibrary.clicked += OnRestoreLibraryClicked;
        if (_btnRestoreTask != null) _btnRestoreTask.clicked += OnRestoreTaskClicked;
        if (_btnRestoreSteps != null) _btnRestoreSteps.clicked += OnRestoreStepsClicked;
        if (_btnRestoreJump != null) _btnRestoreJump.clicked += OnRestoreJumpClicked;
        if (_btnRestoreNav != null) _btnRestoreNav.clicked += OnRestoreNavClicked;
    }

    void UnbindButtons()
    {
        if (_btnImport != null) _btnImport.clicked -= OnImportClicked;
        if (_btnRefreshRecordings != null) _btnRefreshRecordings.clicked -= OnRefreshRecordingsClicked;
        if (_btnCreateTask != null) _btnCreateTask.clicked -= OnCreateTaskClicked;
        if (_btnJump != null) _btnJump.clicked -= OnJumpClicked;
        if (_btnPrev != null) _btnPrev.clicked -= OnPrevClicked;
        if (_btnNext != null) _btnNext.clicked -= OnNextClicked;
        if (_btnMinStatus != null) _btnMinStatus.clicked -= OnMinStatusClicked;
        if (_btnMinLibrary != null) _btnMinLibrary.clicked -= OnMinLibraryClicked;
        if (_btnMinTask != null) _btnMinTask.clicked -= OnMinTaskClicked;
        if (_btnMinSteps != null) _btnMinSteps.clicked -= OnMinStepsClicked;
        if (_btnMinJump != null) _btnMinJump.clicked -= OnMinJumpClicked;
        if (_btnMinNav != null) _btnMinNav.clicked -= OnMinNavClicked;
        if (_btnRestoreImport != null) _btnRestoreImport.clicked -= OnRestoreImportClicked;
        if (_btnRestoreStatus != null) _btnRestoreStatus.clicked -= OnRestoreStatusClicked;
        if (_btnRestoreLibrary != null) _btnRestoreLibrary.clicked -= OnRestoreLibraryClicked;
        if (_btnRestoreTask != null) _btnRestoreTask.clicked -= OnRestoreTaskClicked;
        if (_btnRestoreSteps != null) _btnRestoreSteps.clicked -= OnRestoreStepsClicked;
        if (_btnRestoreJump != null) _btnRestoreJump.clicked -= OnRestoreJumpClicked;
        if (_btnRestoreNav != null) _btnRestoreNav.clicked -= OnRestoreNavClicked;
    }

    void DisableHitTestingForPassiveUi()
    {
        SetPickingIgnored(_root);
        SetPickingIgnored(_backgroundVignette);
        SetPickingIgnored(_backgroundGlow);
        SetPickingIgnored(_topLeftArea);
        SetPickingIgnored(_topRightArea);
        SetPickingIgnored(_leftRailArea);
        SetPickingIgnored(_bottomRightArea);
        SetPickingIgnored(_statusPanel);
        SetPickingIgnored(_recordingsPanel);
        SetPickingIgnored(_taskPanel);
        SetPickingIgnored(_timelinePanel);
        SetPickingIgnored(_controlsPanel);
        SetPickingIgnored(_navRow);
    }

    static void SetPickingIgnored(VisualElement element)
    {
        if (element == null) return;
        element.pickingMode = PickingMode.Ignore;
    }

    void SyncMinimizedStateWithConfig()
    {
        if (!showImportButton) _importButtonMinimized = false;
        if (!showStatusPanel) _statusPanelMinimized = false;
        if (!showRecordingsPanel) _recordingsPanelMinimized = false;
        if (!showTaskPanel) _taskPanelMinimized = false;
        if (!showTimelinePanel) _timelinePanelMinimized = false;
        if (!showJumpControls) _jumpControlsMinimized = false;
        if (!showNavigationButtons) _navigationButtonsMinimized = false;
    }

    void OnMinStatusClicked() { _statusPanelMinimized = true; ApplyModuleVisibility(); }
    void OnMinLibraryClicked() { _recordingsPanelMinimized = true; ApplyModuleVisibility(); }
    void OnMinTaskClicked() { _taskPanelMinimized = true; ApplyModuleVisibility(); }
    void OnMinStepsClicked() { _timelinePanelMinimized = true; ApplyModuleVisibility(); }
    void OnMinJumpClicked() { _jumpControlsMinimized = true; ApplyModuleVisibility(); }
    void OnMinNavClicked() { _navigationButtonsMinimized = true; ApplyModuleVisibility(); }
    void OnRestoreImportClicked() { _importButtonMinimized = false; ApplyModuleVisibility(); }
    void OnRestoreStatusClicked() { _statusPanelMinimized = false; ApplyModuleVisibility(); }
    void OnRestoreLibraryClicked() { _recordingsPanelMinimized = false; ApplyModuleVisibility(); }
    void OnRestoreTaskClicked() { _taskPanelMinimized = false; ApplyModuleVisibility(); }
    void OnRestoreStepsClicked() { _timelinePanelMinimized = false; ApplyModuleVisibility(); }
    void OnRestoreJumpClicked() { _jumpControlsMinimized = false; ApplyModuleVisibility(); }
    void OnRestoreNavClicked() { _navigationButtonsMinimized = false; ApplyModuleVisibility(); }

    void ApplyModuleVisibility()
    {
        SetShown(_btnImport, showImportButton && !_importButtonMinimized);
        SetShown(_btnRestoreImport, showImportButton && _importButtonMinimized);
        SetShown(_statusPanel, showStatusPanel && !_statusPanelMinimized);
        SetShown(_btnRestoreStatus, showStatusPanel && _statusPanelMinimized);
        SetShown(_recordingsPanel, showRecordingsPanel && !_recordingsPanelMinimized);
        SetShown(_btnRestoreLibrary, showRecordingsPanel && _recordingsPanelMinimized);
        SetShown(_taskPanel, showTaskPanel && !_taskPanelMinimized);
        SetShown(_btnRestoreTask, showTaskPanel && _taskPanelMinimized);
        SetShown(_timelinePanel, showTimelinePanel && !_timelinePanelMinimized);
        SetShown(_btnRestoreSteps, showTimelinePanel && _timelinePanelMinimized);
        SetShown(_controlsPanel, showJumpControls && !_jumpControlsMinimized);
        SetShown(_btnRestoreJump, showJumpControls && _jumpControlsMinimized);
        SetShown(_navRow, showNavigationButtons && !_navigationButtonsMinimized);
        SetShown(_btnRestoreNav, showNavigationButtons && _navigationButtonsMinimized);
    }

    static void SetShown(VisualElement e, bool shown)
    {
        if (e == null) return;
        e.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void SyncCreateTaskInputs()
    {
        if (phoneCreateTask == null) return;
        if (_taskTitleInput != null) phoneCreateTask.SetUiTaskTitle(_taskTitleInput.value);
        string userId = _userIdInput != null ? _userIdInput.value : "";
        phoneCreateTask.SetUiUserId(userId);
        if (phonePlayback != null) phonePlayback.userId = string.IsNullOrWhiteSpace(userId) ? "" : userId.Trim();
    }

    void RefreshStatusArea()
    {
        if (phonePlayback == null) return;
        int stepCount = phonePlayback.data != null && phonePlayback.data.steps != null ? phonePlayback.data.steps.Count : 0;
        int currentStep = stepCount > 0 && phonePlayback.currentIndex >= 0 ? phonePlayback.currentIndex + 1 : 0;
        bool loaded = stepCount > 0;
        if (_statusBadge != null) _statusBadge.text = loaded ? "Loaded" : "Idle";
        if (_statusDot != null)
        {
            _statusDot.RemoveFromClassList("is-loaded");
            _statusDot.RemoveFromClassList("is-empty");
            _statusDot.AddToClassList(loaded ? "is-loaded" : "is-empty");
        }
        if (_currentStepValue != null) _currentStepValue.text = $"{currentStep} / {stepCount}";
        if (_taskRecordingValue != null)
        {
            string task = string.IsNullOrWhiteSpace(phonePlayback.taskId) ? "-" : phonePlayback.taskId;
            string recording = string.IsNullOrWhiteSpace(phonePlayback.recordingId) ? "-" : phonePlayback.recordingId;
            _taskRecordingValue.text = $"{task}\n{recording}";
        }
        if (_messageValue != null) _messageValue.text = ResolveMessage();
    }

    void RefreshStepsListIfNeeded(bool force)
    {
        if (_stepsList == null || phonePlayback == null) return;
        int stepCount = phonePlayback.data != null && phonePlayback.data.steps != null ? phonePlayback.data.steps.Count : 0;
        int currentIndex = phonePlayback.currentIndex;
        string recordingId = phonePlayback.recordingId ?? "";
        string taskId = phonePlayback.taskId ?? "";
        string status = phonePlayback.LastStatus ?? "";
        bool changed = force || stepCount != _lastStepCount || currentIndex != _lastCurrentIndex || !string.Equals(recordingId, _lastRecordingId, StringComparison.Ordinal) || !string.Equals(taskId, _lastTaskId, StringComparison.Ordinal) || !string.Equals(status, _lastStatusMessage, StringComparison.Ordinal);
        if (!changed) return;
        _lastStepCount = stepCount; _lastCurrentIndex = currentIndex; _lastRecordingId = recordingId; _lastTaskId = taskId; _lastStatusMessage = status;
        RebuildStepsList(stepCount, currentIndex);
    }

    void RebuildStepsList(int stepCount, int currentIndex)
    {
        _stepsList.Clear();
        if (stepCount <= 0) { _stepsList.Add(BuildEmptyLabel("No tutorial steps loaded")); return; }
        for (int i = 0; i < stepCount; i++)
        {
            int idx = i;
            var btn = new Button(() => OnStepClicked(idx)) { text = $"Step {idx + 1}" };
            btn.AddToClassList("step-item");
            if (idx == currentIndex) btn.AddToClassList("is-active");
            _stepsList.Add(btn);
        }
    }

    async Task RefreshRecordingsAsync(bool silent)
    {
        if (_refreshingRecordings || _recordingsList == null || phonePlayback == null) return;
        _refreshingRecordings = true;
        try
        {
            if (!silent) SetMessage("Refreshing library...");
            string logicalModelId = null;
            string modelType = null;
            string modelHash = null;
            PlaybackAppConfig config = PlaybackAppConfig.Load();
            if (config != null && config.lockRecordingsToConfiguredModel && config.buildMode != AppBuildMode.DebugAll)
            {
                logicalModelId = string.IsNullOrWhiteSpace(config.logicalModelId) ? null : config.logicalModelId.Trim();
                modelType = string.IsNullOrWhiteSpace(config.modelType) ? null : config.modelType.Trim();
                modelHash = string.IsNullOrWhiteSpace(config.modelHash) ? null : config.modelHash.Trim();
                Debug.Log($"[UITK] Refresh recordings filter logicalModelId={logicalModelId ?? ""} modelType={modelType ?? ""} modelHash={modelHash ?? ""}");
                if (string.IsNullOrEmpty(logicalModelId) && string.IsNullOrEmpty(modelType) && string.IsNullOrEmpty(modelHash))
                    Debug.LogWarning("[UITK] lockRecordingsToConfiguredModel is true but logicalModelId/modelType/modelHash are all empty; using unfiltered recordings list.");
            }

            var items = await phonePlayback.ListRecordings(null, GetUserIdFilter(), logicalModelId, modelType, modelHash);
            RebuildRecordingsList(items);
        }
        catch (Exception e)
        {
            Debug.LogError("[UITK] RefreshRecordings failed: " + e);
            SetMessage("Refresh failed: " + e.Message);
        }
        finally { _refreshingRecordings = false; }
    }

    void RebuildRecordingsList(RecordingData[] items)
    {
        _recordingsList.Clear();
        if (items == null || items.Length == 0) { _recordingsList.Add(BuildEmptyLabel("No recordings")); return; }
        Array.Sort(items, CompareByUpdatedTimeDesc);
        foreach (var item in items)
        {
            if (item == null) continue;
            string recordingId = item.GetRecordingId();
            if (string.IsNullOrWhiteSpace(recordingId)) continue;
            string title = string.IsNullOrWhiteSpace(item.title) ? recordingId : item.title;
            int steps = item.GetStepsCount();
            string label = steps > 0 ? $"{title}   ·   {steps} steps" : title;
            var btn = new Button(() => _ = LoadRecordingAsync(recordingId)) { text = label };
            btn.AddToClassList("recording-item");
            if (string.Equals(phonePlayback.recordingId, recordingId, StringComparison.Ordinal)) btn.AddToClassList("is-selected");
            _recordingsList.Add(btn);
        }
    }

    Label BuildEmptyLabel(string text)
    {
        var l = new Label(text);
        l.AddToClassList("muted-body");
        l.style.paddingLeft = 10;
        l.style.paddingTop = 8;
        return l;
    }

    void OnImportClicked()
    {
        if (androidPickerBridge != null) { androidPickerBridge.ImportByPicker(); SetMessage("Opening Android file picker..."); return; }
        SetMessage("AndroidPickerBridge not found in scene.");
        Debug.LogWarning("[UITK] AndroidPickerBridge not found in scene.");
    }

    void OnRefreshRecordingsClicked() => _ = RefreshRecordingsAsync(false);

    void OnCreateTaskClicked()
    {
        if (phoneCreateTask == null) { SetMessage("PhoneCreateTask not found."); return; }
        SyncCreateTaskInputs();
        phoneCreateTask.UploadAndCreate();
        SetMessage("Creating task...");
    }

    void OnJumpClicked()
    {
        if (phonePlayback == null || _stepInput == null) return;
        if (!int.TryParse(_stepInput.value, out int oneBased)) { SetMessage("Invalid step number."); return; }
        phonePlayback.JumpTo(oneBased - 1);
        RefreshStatusArea();
        RefreshStepsListIfNeeded(true);
    }

    void OnPrevClicked() { if (phonePlayback == null) return; phonePlayback.PrevStep(); SetMessage("Previous step"); }
    void OnNextClicked() { if (phonePlayback == null) return; phonePlayback.NextStep(); SetMessage("Playing next step"); }
    void OnStepClicked(int index) { if (phonePlayback == null) return; phonePlayback.PlayStep(index); RefreshStepsListIfNeeded(true); }

    async Task LoadRecordingAsync(string recordingId)
    {
        if (phonePlayback == null || string.IsNullOrWhiteSpace(recordingId)) return;
        SetMessage("Loading recording...");
        if (!await phonePlayback.LoadRecording(recordingId)) { SetMessage("Failed to load recording"); return; }
        RefreshStatusArea();
        RefreshStepsListIfNeeded(true);
        await RefreshRecordingsAsync(true);
    }

    string ResolveMessage()
    {
        if (phonePlayback != null && !string.IsNullOrWhiteSpace(phonePlayback.LastStatus)) return phonePlayback.LastStatus;
        if (phoneCreateTask != null && !string.IsNullOrWhiteSpace(phoneCreateTask.LastStatus)) return phoneCreateTask.LastStatus;
        return "Ready";
    }

    void SetMessage(string msg)
    {
        if (_messageValue != null) _messageValue.text = msg;
    }

    string GetUserIdFilter()
    {
        if (_userIdInput != null && !string.IsNullOrWhiteSpace(_userIdInput.value)) return _userIdInput.value.Trim();
        return null;
    }

    static int CompareByUpdatedTimeDesc(RecordingData a, RecordingData b)
    {
        long ta = a != null ? (a.updatedAt > 0 ? a.updatedAt : a.createdAt) : long.MinValue;
        long tb = b != null ? (b.updatedAt > 0 ? b.updatedAt : b.createdAt) : long.MinValue;
        return tb.CompareTo(ta);
    }
}
