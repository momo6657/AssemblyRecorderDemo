using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Michsky.MUIP;

public class RecordingListUI : MonoBehaviour
{
    [Header("Refs")]
    public PhoneStepPlayback phonePlayback;
    public Transform content;
    public GameObject itemPrefab;   // 改为 GameObject，兼容任何 UI 框架的按钮
    public TMP_Text statusText;

    [Header("Optional Filters")]
    public TMP_InputField taskIdInput;
    public bool filterByCurrentTask = true;
    public bool alwaysShowAllRecordings = true;

    [Header("Behavior")]
    public bool autoRefreshOnEnable = true;
    public bool clearIfNoTask = true;
    public float minRefreshIntervalSec = AppConstants.MIN_REFRESH_INTERVAL_SEC;
    public bool showRefreshButton = true;

    bool _busy;
    float _lastRefreshAt = -999f;
    readonly List<GameObject> _spawned = new List<GameObject>();

    // 公开方法供 UI Button 调用
    public void ForceRefresh()
    {
        _lastRefreshAt = -999f; // 重置时间限制
        RefreshList();
    }

    void OnEnable()
    {
        if (autoRefreshOnEnable) RefreshList();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        // 当应用重新获得焦点时刷新列表（例如从后台返回）
        if (hasFocus && autoRefreshOnEnable)
        {
            RefreshList();
        }
    }

    public async void RefreshList()
    {
        if (_busy)
        {
            SetStatus("[RecordingListUI] Refresh already in progress...");
            return;
        }
        if (Time.unscaledTime - _lastRefreshAt < minRefreshIntervalSec)
        {
            SetStatus("[RecordingListUI] Please wait before refreshing again.");
            return;
        }
        _lastRefreshAt = Time.unscaledTime;
        _busy = true;

        try
        {
            SetStatus("[RecordingListUI] Refreshing...");
            
            // 使用 ServiceLocator 获取服务
            if (phonePlayback == null)
                phonePlayback = ServiceLocator.Instance?.phoneStepPlayback;
            
            if (phonePlayback == null)
            {
                SetStatus("[RecordingListUI] PhoneStepPlayback missing.");
                return;
            }
            if (content == null)
            {
                SetStatus("[RecordingListUI] content is null.");
                return;
            }
            if (itemPrefab == null)
            {
                SetStatus("[RecordingListUI] itemPrefab is null.");
                return;
            }

            string taskFilter = ResolveTaskFilter();
            if (!alwaysShowAllRecordings && filterByCurrentTask && string.IsNullOrWhiteSpace(taskFilter))
            {
                if (clearIfNoTask) ClearList();
                SetStatus("[RecordingListUI] no taskId, list cleared.");
                return;
            }
            if (alwaysShowAllRecordings)
                taskFilter = null;

            Debug.Log($"[RecordingListUI] Requesting recordings with taskFilter={(taskFilter ?? "NULL (all)")}");
            
            var items = await phonePlayback.ListRecordings(taskFilter, null);
            
            Debug.Log($"[RecordingListUI] Received {items?.Length ?? 0} recordings from server.");
            
            Rebuild(items);
        }
        catch (Exception e)
        {
            SetStatus("[RecordingListUI] refresh failed: " + e.Message);
            Debug.LogError("[RecordingListUI] refresh failed: " + e);
        }
        finally
        {
            _busy = false;
        }
    }

    public async void LoadSelectedByInput()
    {
        if (phonePlayback == null)
            phonePlayback = ServiceLocator.Instance?.phoneStepPlayback;
        
        if (phonePlayback == null) return;
        await phonePlayback.LoadRecording(phonePlayback.recordingIdInput != null ? phonePlayback.recordingIdInput.text : phonePlayback.recordingId);
    }

    void Rebuild(RecordingData[] items)
    {
        ClearList();

        if (items == null || items.Length == 0)
        {
            SetStatus("[RecordingListUI] no recordings.");
            return;
        }

        Array.Sort(items, CompareByUpdatedTimeDesc);

        bool hasLayout =
            content.GetComponent<VerticalLayoutGroup>() != null ||
            content.GetComponent<HorizontalLayoutGroup>() != null ||
            content.GetComponent<GridLayoutGroup>() != null;

        float itemHeight = AppConstants.UI_ITEM_HEIGHT;
        float spacing = AppConstants.UI_ITEM_SPACING;

        for (int i = 0; i < items.Length; i++)
        {
            var rec = items[i];
            if (rec == null) continue;

            var go = Instantiate(itemPrefab, content);
            _spawned.Add(go);

            // 构建标签文本
            string labelText = BuildItemLabel(rec);

            // 优先通过 MUIP ButtonManager.buttonText 设置文字（避免被 MUIP 覆盖）
            var muipBtnLabel = go.GetComponent<ButtonManager>();
            if (muipBtnLabel == null) muipBtnLabel = go.GetComponentInChildren<ButtonManager>();
            if (muipBtnLabel != null)
            {
                muipBtnLabel.buttonText = labelText;
                muipBtnLabel.UpdateUI();
            }
            else
            {
                var label = go.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = labelText;
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

            string rid = rec.GetRecordingId();

            // 注册点击事件：优先用 MUIP ButtonManager，否则用原生 Button
            // muipBtnLabel 已在上方查找过，复用变量
            if (muipBtnLabel != null)
            {
                muipBtnLabel.onClick.RemoveAllListeners();
                muipBtnLabel.onClick.AddListener(() => OnRecordingClicked(rid));
            }
            else
            {
                var btn = go.GetComponent<Button>();
                if (btn == null) btn = go.GetComponentInChildren<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => OnRecordingClicked(rid));
                }
                else
                {
                    Debug.LogWarning($"[RecordingListUI] itemPrefab '{go.name}' has no ButtonManager or Button component");
                }
            }
        }

        if (!hasLayout)
        {
            var contentRt = content as RectTransform;
            if (contentRt != null)
            {
                float needed = _spawned.Count > 0 ? (_spawned.Count * itemHeight + (_spawned.Count - 1) * spacing) : 0f;
                var sz = contentRt.sizeDelta;
                if (needed > sz.y) sz.y = needed;
                contentRt.sizeDelta = sz;
            }
        }

        var sr = content.GetComponentInParent<ScrollRect>();
        if (sr != null) sr.verticalNormalizedPosition = 1f;

        SetStatus("[RecordingListUI] loaded " + _spawned.Count + " recordings.");
    }

    async void OnRecordingClicked(string recordingId)
    {
        if (string.IsNullOrWhiteSpace(recordingId)) return;

        // 使用 ServiceLocator
        if (phonePlayback == null)
            phonePlayback = ServiceLocator.Instance?.phoneStepPlayback;
        
        if (phonePlayback == null) return;

        if (phonePlayback.recordingIdInput != null)
            phonePlayback.recordingIdInput.text = recordingId;
        phonePlayback.recordingId = recordingId;

        SetStatus("[RecordingListUI] Loading " + recordingId + "...");

        bool ok = await phonePlayback.LoadRecording(recordingId);
        if (ok)
        {
            SetStatus("[RecordingListUI] loaded " + recordingId);
        }
        else
        {
            SetStatus("[RecordingListUI] Failed to load " + recordingId + ". It may have been deleted. Refreshing list...");
            Debug.LogWarning("[RecordingListUI] Recording " + recordingId + " failed to load, refreshing list.");
            
            // 使用 AppConstants 定义的延迟时间
            await System.Threading.Tasks.Task.Delay((int)(AppConstants.ERROR_MESSAGE_DISPLAY_SEC * 1000));
            RefreshList();
        }
    }

    void ClearList()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] != null) Destroy(_spawned[i]);
        }
        _spawned.Clear();

        if (content == null) return;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i);
            if (child == null) continue;
            if (itemPrefab != null && child.gameObject == itemPrefab) continue;
            Destroy(child.gameObject);
        }
    }

    static int CompareByUpdatedTimeDesc(RecordingData a, RecordingData b)
    {
        long ta = a != null ? (a.updatedAt > 0 ? a.updatedAt : a.createdAt) : 0;
        long tb = b != null ? (b.updatedAt > 0 ? b.updatedAt : b.createdAt) : 0;
        if (ta == tb) return 0;
        return ta > tb ? -1 : 1;
    }

    static string BuildItemLabel(RecordingData rec)
    {
        if (rec == null) return "(null)";

        string title = string.IsNullOrWhiteSpace(rec.title) ? rec.GetRecordingId() : rec.title.Trim();
        int steps = rec.GetStepsCount();
        string time = FormatTime(rec.updatedAt > 0 ? rec.updatedAt : rec.createdAt);
        return $"{title}  |  steps:{steps}  |  {time}";
    }

    static string FormatTime(long ms)
    {
        if (ms <= 0) return "-";
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("MM-dd HH:mm");
        }
        catch
        {
            return ms.ToString();
        }
    }

    string ResolveTaskFilter()
    {
        if (!filterByCurrentTask) return null;

        if (taskIdInput != null && !string.IsNullOrWhiteSpace(taskIdInput.text))
            return taskIdInput.text.Trim();

        if (phonePlayback != null && !string.IsNullOrWhiteSpace(phonePlayback.taskId))
            return phonePlayback.taskId.Trim();

        if (phonePlayback != null && phonePlayback.taskIdInput != null && !string.IsNullOrWhiteSpace(phonePlayback.taskIdInput.text))
            return phonePlayback.taskIdInput.text.Trim();

        return PlayerPrefs.GetString(AppConstants.PREF_PHONE_LAST_TASK_ID, "").Trim();
    }

    // 移除 EnsureRefs - 改用 ServiceLocator
    void SetStatus(string msg)
    {
        Debug.Log(msg);
        if (statusText != null) statusText.text = msg;
    }
}
