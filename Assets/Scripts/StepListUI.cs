using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Michsky.MUIP;

public class StepListUI : MonoBehaviour
{
    public StepPlayer player;
    public StepRecorder recorder;
    public PhoneStepPlayback phonePlayback;

    public Transform content;
    public GameObject stepButtonPrefab;   // 改为 GameObject，兼容任何 UI 框架的按钮
    public ScrollRect scrollRect;

    int lastCount = -1;
    string lastTaskId = "";
    bool _rebuilding = false;

    void Update()
    {
        // 正在重建时跳过，避免 Destroy 延迟导致的 childMismatch 死循环
        if (_rebuilding) return;

        // 使用 ServiceLocator 避免频繁查找
        var phonePlayback = ServiceLocator.Instance?.phoneStepPlayback;
        var player = ServiceLocator.Instance?.stepPlayer;
        var recorder = ServiceLocator.Instance?.stepRecorder;
        
        if (content == null || stepButtonPrefab == null) return;

        int count = GetStepCount(phonePlayback, recorder);
        string taskId = GetTaskId(phonePlayback);

        if (count != lastCount || !string.Equals(taskId, lastTaskId, System.StringComparison.Ordinal))
        {
            Rebuild(count, phonePlayback, player);
            lastCount = count;
            lastTaskId = taskId;
        }
    }

    void Rebuild(int count, PhoneStepPlayback phonePlayback, StepPlayer player)
    {
        _rebuilding = true;
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
            var go = Instantiate(stepButtonPrefab, content);
            string stepLabel = $"Step {i + 1}";

            // 优先通过 MUIP ButtonManager 设置文字，避免被 MUIP 覆盖
            var muipBtn = go.GetComponent<ButtonManager>();
            if (muipBtn == null) muipBtn = go.GetComponentInChildren<ButtonManager>();
            if (muipBtn != null)
            {
                muipBtn.buttonText = stepLabel;
                muipBtn.UpdateUI();
            }
            else
            {
                var txt = go.GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null) txt.text = stepLabel;
            }

            if (!hasLayout)
            {
                var rt = go.GetComponent<RectTransform>();
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

            // 注册点击事件（先清除旧监听器防止叠加）
            if (muipBtn != null)
            {
                muipBtn.onClick.RemoveAllListeners();
                muipBtn.onClick.AddListener(() => OnStepClicked(stepIndex, phonePlayback, player));
            }
            else
            {
                var btn = go.GetComponent<Button>();
                if (btn == null) btn = go.GetComponentInChildren<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => OnStepClicked(stepIndex, phonePlayback, player));
                }
                else
                    Debug.LogWarning($"[StepListUI] stepButtonPrefab has no Button or ButtonManager on '{go.name}'");
            }
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

        // 延迟一帧后才允许 Update 重新检测，避免 Destroy 延迟触发 childMismatch
        StartCoroutine(ResetRebuildingFlag());
    }

    System.Collections.IEnumerator ResetRebuildingFlag()
    {
        yield return null;
        _rebuilding = false;
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
