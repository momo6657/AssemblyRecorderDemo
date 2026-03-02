using System.Collections.Generic;
using UnityEngine;

public class StepRecorder : MonoBehaviour
{
    public SelectionManager selection;
    public StepPlayer player;
    public PhoneStepPlayback phonePlayback;

    public bool isRecording = false;
    public float defaultDuration = 0.6f;

    readonly Dictionary<string, PoseData> fromSnapshot = new Dictionary<string, PoseData>();
    readonly HashSet<string> dirty = new HashSet<string>();

    public void StartRecordingStep()
    {
        // Compatibility path: on phone playback UI, this button becomes "load/download steps".
        if (EnsurePhonePlayback() != null)
        {
            phonePlayback.DownloadAndPrepare();
            return;
        }

        if (player == null || player.project == null || selection == null || selection.index == null) return;

        isRecording = true;
        dirty.Clear();
        fromSnapshot.Clear();

        foreach (var kv in selection.index.map)
            fromSnapshot[kv.Key] = PoseData.From(kv.Value);

        Debug.Log("StartRecordingStep");
    }

    public void FinishRecordingStep()
    {
        // Compatibility path: on phone playback UI, this button becomes "next step".
        if (EnsurePhonePlayback() != null)
        {
            phonePlayback.NextStep();
            return;
        }

        if (!isRecording) return;
        isRecording = false;

        if (player == null || player.project == null || selection == null || selection.index == null) return;

        var step = new StepData
        {
            name = $"Step {player.project.steps.Count + 1}",
            duration = defaultDuration
        };

        foreach (var key in dirty)
        {
            if (!selection.index.map.TryGetValue(key, out var tr)) continue;

            step.items.Add(new StepItem
            {
                partKey = key,
                from = fromSnapshot[key],
                to = PoseData.From(tr)
            });
        }

        if (step.items.Count == 0)
        {
            Debug.LogWarning("FinishRecordingStep: no part movement in this step, ignored.");
            return;
        }

        player.project.steps.Add(step);
        Debug.Log("FinishRecordingStep OK, totalSteps=" + player.project.steps.Count);
    }

    public void MarkDirtySelected()
    {
        if (!isRecording || selection == null) return;
        foreach (var key in selection.SelectedKeys)
            dirty.Add(key);
    }

    public int GetRecordedStepCount()
    {
        if (EnsurePhonePlayback(false) != null)
            return phonePlayback.data != null && phonePlayback.data.steps != null ? phonePlayback.data.steps.Count : 0;

        return player != null && player.project != null ? player.project.steps.Count : 0;
    }

    PhoneStepPlayback EnsurePhonePlayback(bool createIfMissing = true)
    {
        if (phonePlayback != null) return phonePlayback;

        phonePlayback = FindFirstObjectByType<PhoneStepPlayback>();
        if (phonePlayback == null && createIfMissing && player != null)
            phonePlayback = player.GetComponent<PhoneStepPlayback>();
        if (phonePlayback == null && createIfMissing && player != null)
        {
            bool xrActive =
#if UNITY_2019_3_OR_NEWER
                UnityEngine.XR.XRSettings.enabled && UnityEngine.XR.XRSettings.isDeviceActive;
#else
                false;
#endif
            if (!xrActive)
                phonePlayback = player.gameObject.AddComponent<PhoneStepPlayback>();
        }

        if (phonePlayback != null)
        {
            if (phonePlayback.api == null) phonePlayback.api = FindFirstObjectByType<ApiClient>();
            if (phonePlayback.importManager == null) phonePlayback.importManager = FindFirstObjectByType<ImportManager>();
            if (phonePlayback.modelIndex == null) phonePlayback.modelIndex = FindFirstObjectByType<ModelIndex>();
        }

        return phonePlayback;
    }
}
