using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StepListUI : MonoBehaviour
{
    public StepPlayer player;
    public StepRecorder recorder;
    public PhoneStepPlayback phonePlayback;

    public Transform content;
    public Button stepButtonPrefab;
    public ScrollRect scrollRect;

    int lastCount = -1;
    string lastTaskId = "";

    void Update()
    {
        // 使用 ServiceLocator 避免频繁查找
        var phonePlayback = ServiceLocator.Instance?.phoneStepPlayback;
        var player = ServiceLocator.Instance?.stepPlayer;
        var recorder = ServiceLocator.Instance?.stepRecorder;
        
        if (content == null || stepButtonPrefab == null) return;

        int count = GetStepCount(phonePlayback, recorder);
        string taskId = GetTaskId(phonePlayback);
        bool childMismatch = content.childCount != count;

        if (count != lastCount || childMismatch || !string.Equals(taskId, lastTaskId, System.StringComparison.Ordinal))
        {
            Rebuild(count, phonePlayback, player);
            lastCount = count;
            lastTaskId = taskId;
        }
    }

    void Rebuild(int count, PhoneStepPlayback phonePlayback, StepPlayer player)
    {
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        bool hasLayout =
            content.GetComponent<VerticalLayoutGroup>() != null ||
            content.GetComponent<HorizontalLayoutGroup>() != null ||
            content.GetComponent<GridLayoutGroup>() != null;

        float itemHeight = AppConstants.UI_ITEM_HEIGHT;
        float spacing = AppConstants.UI_ITEM_SPACING;

        for (int i = 0; i < count; i++)
        {
            int stepIndex = i;
            var btn = Instantiate(stepButtonPrefab, content);
            var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null) txt.text = $"Step {i + 1}";

            if (!hasLayout)
            {
                var rt = btn.GetComponent<RectTransform>();
                if (rt != null)
                {
                    float h = rt.sizeDelta.y > 1f ? rt.sizeDelta.y : itemHeight;
                    itemHeight = h;
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, -i * (h + spacing));
                    rt.sizeDelta = new Vector2(0f, h);
                }
            }

            btn.onClick.AddListener(() => OnStepClicked(stepIndex, phonePlayback, player));
        }

        if (!hasLayout)
        {
            var contentRt = content as RectTransform;
            if (contentRt != null)
            {
                float needed = count > 0 ? (count * itemHeight + (count - 1) * spacing) : 0f;
                var sz = contentRt.sizeDelta;
                if (needed > sz.y) sz.y = needed;
                contentRt.sizeDelta = sz;
            }
        }

        if (scrollRect == null) scrollRect = GetComponentInParent<ScrollRect>();
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
    }

    void OnStepClicked(int stepIndex, PhoneStepPlayback phonePlayback, StepPlayer player)
    {
        if (phonePlayback != null && phonePlayback.data != null && phonePlayback.data.steps != null && phonePlayback.data.steps.Count > 0)
        {
            phonePlayback.PlayStep(stepIndex);
            return;
        }

        if (player != null) player.PlayStep(stepIndex);
    }

    int GetStepCount(PhoneStepPlayback phonePlayback, StepRecorder recorder)
    {
        if (phonePlayback != null && phonePlayback.data != null && phonePlayback.data.steps != null)
            return phonePlayback.data.steps.Count;

        if (recorder != null) return recorder.GetRecordedStepCount();
        return 0;
    }

    string GetTaskId(PhoneStepPlayback phonePlayback)
    {
        if (phonePlayback != null && !string.IsNullOrWhiteSpace(phonePlayback.taskId))
            return phonePlayback.taskId;
        return "";
    }
}
