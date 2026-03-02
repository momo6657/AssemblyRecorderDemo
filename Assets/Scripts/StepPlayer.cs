using System.Collections;
using UnityEngine;

public class StepPlayer : MonoBehaviour
{
    public Transform modelRoot;
    public ModelProject project;
    public int currentStepIndex = -1;
    public PhoneStepPlayback phonePlayback;

    ModelIndex index;
    Coroutine co;

    public void Bind(Transform root, ModelProject proj, ModelIndex idx)
    {
        if (EnsurePhonePlayback(false) != null)
        {
            currentStepIndex = phonePlayback.currentIndex;
            return;
        }

        modelRoot = root;
        project = proj;
        index = idx;
        currentStepIndex = -1;
        ApplyCompletedState(-1);
    }

    public void ApplyCompletedState(int stepInclusive)
    {
        if (EnsurePhonePlayback(false) != null)
        {
            phonePlayback.JumpTo(stepInclusive);
            currentStepIndex = phonePlayback.currentIndex;
            return;
        }

        if (index == null || project == null) return;

        index.RestoreBaseAll();

        int last = Mathf.Clamp(stepInclusive, -1, project.steps.Count - 1);
        for (int i = 0; i <= last; i++)
        {
            foreach (var it in project.steps[i].items)
                if (index.map.TryGetValue(it.partKey, out var t))
                    it.to.ApplyTo(t);
        }

        currentStepIndex = last;
    }

    public void PlayNext()
    {
        if (EnsurePhonePlayback() != null)
        {
            phonePlayback.NextStep();
            currentStepIndex = phonePlayback.currentIndex;
            return;
        }

        PlayStep(currentStepIndex + 1);
    }

    public void PlayPrev()
    {
        if (EnsurePhonePlayback() != null)
        {
            phonePlayback.PrevStep();
            currentStepIndex = phonePlayback.currentIndex;
            return;
        }

        if (project == null) return;
        int prev = Mathf.Clamp(currentStepIndex - 1, -1, project.steps.Count - 1);
        ApplyCompletedState(prev);
    }

    public void JumpTo(int stepIndex)
    {
        if (EnsurePhonePlayback() != null)
        {
            phonePlayback.JumpTo(stepIndex);
            currentStepIndex = phonePlayback.currentIndex;
            return;
        }

        PlayStep(stepIndex);
    }

    public void PlayStep(int stepIndex)
    {
        if (EnsurePhonePlayback() != null)
        {
            phonePlayback.PlayStep(stepIndex);
            currentStepIndex = phonePlayback.currentIndex;
            return;
        }

        if (project == null || index == null) return;
        if (stepIndex < 0 || stepIndex >= project.steps.Count) return;

        if (co != null) StopCoroutine(co);
        co = StartCoroutine(CoPlayStep(stepIndex));
    }

    IEnumerator CoPlayStep(int stepIndex)
    {
        ApplyCompletedState(stepIndex - 1);

        var step = project.steps[stepIndex];
        float dur = Mathf.Max(0.01f, step.duration);

        float t01 = 0f;
        while (t01 < 1f)
        {
            t01 += Time.deltaTime / dur;
            float eased = t01 * t01 * (3f - 2f * t01);

            foreach (var it in step.items)
            {
                if (!index.map.TryGetValue(it.partKey, out var tr)) continue;

                tr.localPosition = Vector3.Lerp(it.from.pos, it.to.pos, eased);
                tr.localRotation = Quaternion.Slerp(Quaternion.Euler(it.from.euler), Quaternion.Euler(it.to.euler), eased);
                tr.localScale = Vector3.Lerp(it.from.scale, it.to.scale, eased);
            }

            yield return null;
        }

        foreach (var it in step.items)
            if (index.map.TryGetValue(it.partKey, out var tr))
                it.to.ApplyTo(tr);

        currentStepIndex = stepIndex;
        co = null;
    }

    PhoneStepPlayback EnsurePhonePlayback(bool createIfMissing = true)
    {
        if (phonePlayback != null) return phonePlayback;

        phonePlayback = FindFirstObjectByType<PhoneStepPlayback>();
        if (phonePlayback == null && createIfMissing)
        {
            bool xrActive =
#if UNITY_2019_3_OR_NEWER
                UnityEngine.XR.XRSettings.enabled && UnityEngine.XR.XRSettings.isDeviceActive;
#else
                false;
#endif
            if (!xrActive)
                phonePlayback = gameObject.AddComponent<PhoneStepPlayback>();
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
