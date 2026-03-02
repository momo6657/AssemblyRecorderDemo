using TMPro;
using UnityEngine;

public class UIController : MonoBehaviour
{
    [Header("Refs")]
    public StepRecorder recorder;
    public StepPlayer player;
    public ProjectManager projectManager;
    public PhoneStepPlayback phonePlayback;

    [Header("UI")]
    public TextMeshProUGUI statusText;
    public TMP_InputField jumpInput;

    public UnityEngine.UI.Button btnStartRec;
    public UnityEngine.UI.Button btnFinishRec;
    public UnityEngine.UI.Button btnPrev;
    public UnityEngine.UI.Button btnNext;
    public UnityEngine.UI.Button btnJump;
    public UnityEngine.UI.Button btnSave;

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

            if (btnStartRec != null) btnStartRec.interactable = true;
            if (btnFinishRec != null) btnFinishRec.interactable = total > 0;
            if (btnPrev != null) btnPrev.interactable = true;
            if (btnNext != null) btnNext.interactable = true;
            if (btnJump != null) btnJump.interactable = total > 0;
            if (btnSave != null) btnSave.interactable = false;
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

        if (btnStartRec != null) btnStartRec.interactable = hasProject && !recording;
        if (btnFinishRec != null) btnFinishRec.interactable = hasProject && recording;
        if (btnPrev != null) btnPrev.interactable = hasProject && !recording;
        if (btnNext != null) btnNext.interactable = hasProject && !recording;
        if (btnJump != null) btnJump.interactable = hasProject && !recording;
        if (btnSave != null) btnSave.interactable = hasProject && !recording;
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
