using TMPro;
using UnityEngine;
using Michsky.MUIP;

public class UIController : MonoBehaviour
{
    [Header("Refs")]
    public StepRecorder recorder;
    public StepPlayer player;
    public ProjectManager projectManager;
    public PhoneStepPlayback phonePlayback;

    [Header("UI")]
    public TextMeshProUGUI statusText;
    public TMPro.TMP_InputField jumpInput;

    // Modern UI Pack ButtonManager 字段
    // 如果未使用 MUIP 可以保持为 null，将自动回退到原生 Button
    public ButtonManager btnStartRec;
    public ButtonManager btnFinishRec;
    public ButtonManager btnPrev;
    public ButtonManager btnNext;
    public ButtonManager btnJump;
    public ButtonManager btnSave;

    void Awake()
    {
        EnsureRefs();
    }

    void Update()
    {
        EnsureRefs();

        bool phoneMode = phonePlayback != null;

        if (phoneMode)
        {
            int total = phonePlayback.data != null && phonePlayback.data.steps != null
                ? phonePlayback.data.steps.Count
                : 0;
            int cur = phonePlayback.currentIndex;

            if (statusText != null)
            {
                statusText.text =
                    $"TaskId: {phonePlayback.taskId}\n" +
                    $"RecordingId: {phonePlayback.recordingId}\n" +
                    $"Loaded Steps: {total}\n" +
                    $"Current Step: {cur + 1} / {total}\n" +
                    (total > 0 ? "" : "Hint: Prev/Next auto-load latest; input RecordingId + Jump loads a specific one.");
            }

            SetInteractable(btnStartRec, true);
            SetInteractable(btnFinishRec, total > 0);
            SetInteractable(btnPrev, true);
            SetInteractable(btnNext, true);
            SetInteractable(btnJump, total > 0);
            SetInteractable(btnSave, false);
            return;
        }

        if (statusText == null || recorder == null || player == null) return;

        int totalLegacy = recorder.GetRecordedStepCount();
        int curLegacy = player.currentStepIndex;
        string rec = recorder.isRecording ? "YES" : "NO";

        statusText.text =
            $"Recording: {rec}\n" +
            $"Recorded Steps: {totalLegacy}\n" +
            $"Current Completed Step: {curLegacy + 1} / {totalLegacy}";

        bool hasProject = player.project != null;
        bool recording = recorder.isRecording;

        SetInteractable(btnStartRec, hasProject && !recording);
        SetInteractable(btnFinishRec, hasProject && recording);
        SetInteractable(btnPrev, hasProject && !recording);
        SetInteractable(btnNext, hasProject && !recording);
        SetInteractable(btnJump, hasProject && !recording);
        SetInteractable(btnSave, hasProject && !recording);
    }

    /// 使用 MUIP ButtonManager.Interactable() 设置可交互性
    void SetInteractable(ButtonManager btn, bool value)
    {
        if (btn != null) btn.Interactable(value);
    }

    public void JumpToStepFromInput()
    {
        if (jumpInput == null) return;
        if (!int.TryParse(jumpInput.text, out int oneBased)) return;

        int stepIndex = oneBased - 1;

        if (phonePlayback != null)
        {
            int total = phonePlayback.data != null && phonePlayback.data.steps != null
                ? phonePlayback.data.steps.Count
                : 0;
            if (stepIndex < 0 || stepIndex >= total) return;
            phonePlayback.JumpTo(stepIndex);
            return;
        }

        if (player == null || recorder == null) return;

        int totalLegacy = recorder.GetRecordedStepCount();
        if (stepIndex < 0 || stepIndex >= totalLegacy) return;
        player.JumpTo(stepIndex);
    }

    public void SaveProject()
    {
        if (phonePlayback != null) return;
        if (projectManager != null) projectManager.Save();
    }

    void EnsureRefs()
    {
        if (phonePlayback == null) phonePlayback = FindFirstObjectByType<PhoneStepPlayback>();
        if (player == null) player = FindFirstObjectByType<StepPlayer>();
        if (recorder == null) recorder = FindFirstObjectByType<StepRecorder>();
        if (projectManager == null) projectManager = FindFirstObjectByType<ProjectManager>();
    }
}
