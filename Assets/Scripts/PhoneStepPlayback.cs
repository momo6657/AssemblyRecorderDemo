using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class PhoneStepPlayback : MonoBehaviour
{
    [Header("Refs")]
    public ApiClient api;
    public ImportManager importManager;
    public ModelIndex modelIndex;

    [Header("Input/UI")]
    public TMP_InputField taskIdInput;
    public TMP_InputField recordingIdInput;
    public TMP_InputField userIdInput;
    public TMP_Text statusText;
    public string taskId;
    public string recordingId;
    public string userId;

    [Header("Behavior")]
    public bool autoLoadModelFromSteps = true;
    public bool useTrajectoryPlayback = true;
    public bool preferRecordingApi = true;

    [Header("Runtime")]
    public StepsData data;
    public int currentIndex = -1;

    Coroutine _playCo;
    string _loadedTaskId;
    string _loadedRecordingId;
    bool _busy;

    public async void DownloadAndPrepare()
    {
        recordingId = ResolveRecordingIdForPlayback();
        if (!string.IsNullOrWhiteSpace(recordingId))
        {
            await LoadRecording(recordingId);
            return;
        }

        taskId = ResolveTaskIdForPlayback();

        if (string.IsNullOrWhiteSpace(taskId))
        {
            SetStatus("[PHONE] taskId/recordingId is empty.");
            return;
        }

        await LoadTask(taskId);
    }

    public async Task<bool> LoadTask(string id)
    {
        EnsureRefs();

        taskId = id;
        if (taskIdInput != null) taskIdInput.text = id;

        if (preferRecordingApi)
        {
            bool loadedFromRecording = await TryLoadLatestRecordingByTask(id);
            if (loadedFromRecording) return true;
        }

        if (_busy) return false;
        _busy = true;
        try
        {
            if (api == null)
            {
                SetStatus("[PHONE] ApiClient is null.");
                return false;
            }

            string path = "/downloadSteps?taskId=" + UnityWebRequest.EscapeURL(id);
            string json = await api.GetText(path);
            if (string.IsNullOrEmpty(json))
            {
                SetStatus("[PHONE] downloadSteps empty response.");
                return false;
            }

            data = TryParseSteps(json);

            if (data == null || data.steps == null)
            {
                SetStatus("[PHONE] parse steps failed.");
                return false;
            }

            _loadedRecordingId = "";
            recordingId = "";
            _loadedTaskId = id;
            PlayerPrefs.SetString(AppConstants.PREF_PHONE_LAST_TASK_ID, _loadedTaskId);
            PlayerPrefs.SetString(AppConstants.PREF_PHONE_LAST_RECORDING_ID, "");
            PlayerPrefs.Save();
            currentIndex = -1;
            int trackCount = 0;
            int sampleCount = 0;
            CountTrajectoryStats(data, out trackCount, out sampleCount);
            SetStatus($"[PHONE] steps loaded. count={data.steps.Count}, tracks={trackCount}, samples={sampleCount}");

            if (autoLoadModelFromSteps)
                await EnsureModelLoaded();

            return true;
        }
        catch (Exception e)
        {
            SetStatus("[PHONE] load task failed: " + e.Message);
            Debug.LogError("[PHONE] load task failed: " + e);
            return false;
        }
        finally
        {
            _busy = false;
        }
    }

    public async Task<bool> LoadRecording(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            SetStatus("[PHONE] recordingId is empty.");
            return false;
        }

        if (_busy) return false;
        _busy = true;
        try
        {
            EnsureRefs();

            if (api == null)
            {
                SetStatus("[PHONE] ApiClient is null.");
                return false;
            }

            id = id.Trim();
            recordingId = id;
            if (recordingIdInput != null) recordingIdInput.text = id;

            string modelIdFromMeta = "";

            try
            {
                string metaPath = "/downloadRecording?recordingId=" + UnityWebRequest.EscapeURL(id);
                string metaJson = await api.GetText(metaPath);
                
                if (string.IsNullOrWhiteSpace(metaJson))
                {
                    SetStatus("[PHONE] Recording not found (empty response). It may have been deleted.");
                    Debug.LogWarning("[PHONE] /downloadRecording returned empty for recordingId=" + id);
                    return false;
                }
                
                var metaRes = JsonUtility.FromJson<DownloadRecordingRes>(metaJson);
                if (metaRes != null && !metaRes.ok)
                {
                    string errMsg = !string.IsNullOrEmpty(metaRes.error) ? metaRes.error : "unknown error";
                    SetStatus("[PHONE] Recording not found: " + errMsg);
                    Debug.LogWarning("[PHONE] /downloadRecording failed: " + errMsg);
                    return false;
                }
                
                if (metaRes != null && metaRes.ok && metaRes.recording != null)
                {
                    string t = metaRes.recording.GetTaskId();
                    if (!string.IsNullOrEmpty(t))
                    {
                        taskId = t;
                        if (taskIdInput != null) taskIdInput.text = t;
                    }
                    modelIdFromMeta = metaRes.recording.GetModelId();
                }
            }
            catch (Exception ex)
            {
                SetStatus("[PHONE] Recording not found or network error: " + ex.Message);
                Debug.LogWarning("[PHONE] /downloadRecording unavailable or parse failed: " + ex.Message);
                return false;
            }

            string stepsPath = "/downloadRecordingSteps?recordingId=" + UnityWebRequest.EscapeURL(id);
            string stepsJson = await api.GetText(stepsPath);
            if (string.IsNullOrEmpty(stepsJson))
            {
                SetStatus("[PHONE] Recording steps not found (empty response).");
                Debug.LogWarning("[PHONE] /downloadRecordingSteps returned empty for recordingId=" + id);
                return false;
            }

            data = TryParseSteps(stepsJson);
            if (data == null || data.steps == null)
            {
                SetStatus("[PHONE] Failed to parse recording steps.");
                Debug.LogWarning("[PHONE] parse recording steps failed for recordingId=" + id);
                return false;
            }

            if (string.IsNullOrEmpty(data.modelId) && !string.IsNullOrEmpty(modelIdFromMeta))
                data.modelId = modelIdFromMeta;

            _loadedRecordingId = id;
            _loadedTaskId = taskId;
            PlayerPrefs.SetString(AppConstants.PREF_PHONE_LAST_RECORDING_ID, _loadedRecordingId ?? "");
            PlayerPrefs.SetString(AppConstants.PREF_PHONE_LAST_TASK_ID, _loadedTaskId ?? "");
            PlayerPrefs.Save();
            currentIndex = -1;

            int trackCount = 0;
            int sampleCount = 0;
            CountTrajectoryStats(data, out trackCount, out sampleCount);
            SetStatus($"[PHONE] recording loaded. rid={id}, steps={data.steps.Count}, tracks={trackCount}, samples={sampleCount}");

            if (autoLoadModelFromSteps)
                await EnsureModelLoaded();

            return true;
        }
        catch (Exception e)
        {
            SetStatus("[PHONE] load recording failed: " + e.Message);
            Debug.LogError("[PHONE] load recording failed: " + e);
            return false;
        }
        finally
        {
            _busy = false;
        }
    }

    StepsData TryParseSteps(string json)
    {
        json = NormalizeJson(json);
        StepsData parsed = TryParseStepsObject(json);
        if (HasAnySteps(parsed)) return parsed;

        var envString = JsonUtility.FromJson<DownloadStepsEnvelopeString>(json);
        if (envString != null && !string.IsNullOrWhiteSpace(envString.steps))
        {
            string nested = NormalizeJson(envString.steps);
            var nestedParsed = TryParseStepsObject(nested);
            if (HasAnySteps(nestedParsed)) return nestedParsed;
        }

        if (!string.IsNullOrEmpty(json))
        {
            int idx = json.IndexOf("\"steps\":\"", StringComparison.Ordinal);
            if (idx >= 0)
            {
                int start = idx + "\"steps\":\"".Length;
                int end = json.LastIndexOf('"');
                if (end > start)
                {
                    string escaped = json.Substring(start, end - start);
                    string nested = NormalizeJson(escaped);
                    var nestedParsed = TryParseStepsObject(nested);
                    if (HasAnySteps(nestedParsed)) return nestedParsed;
                }
            }
        }

        Debug.LogWarning("[PHONE] parse fallback still empty. jsonLen=" + (json != null ? json.Length : 0));
        if (!string.IsNullOrEmpty(json))
        {
            int n = Mathf.Min(420, json.Length);
            Debug.LogWarning("[PHONE] downloadSteps json head => " + json.Substring(0, n));
        }
        return parsed ?? new StepsData();
    }

    StepsData TryParseStepsObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        var env = JsonUtility.FromJson<DownloadStepsEnvelope>(json);
        var envParsed = env != null ? env.steps : null;
        if (HasAnySteps(envParsed) && HasTrajectoryData(envParsed))
            return envParsed;

        var recEnv = JsonUtility.FromJson<DownloadRecordingStepsEnvelope>(json);
        var recParsed = recEnv != null ? recEnv.steps : null;
        if (HasAnySteps(recParsed) && HasTrajectoryData(recParsed))
            return recParsed;

        var direct = JsonUtility.FromJson<StepsData>(json);
        if (HasAnySteps(direct) && HasTrajectoryData(direct))
            return direct;

        var liteEnv = JsonUtility.FromJson<DownloadStepsEnvelopeLite>(json);
        StepsData liteEnvConverted = null;
        if (liteEnv != null && HasAnySteps(liteEnv.steps))
            liteEnvConverted = ConvertLite(liteEnv.steps);
        if (HasAnySteps(liteEnvConverted))
            return liteEnvConverted;

        var recLiteEnv = JsonUtility.FromJson<DownloadRecordingStepsEnvelopeLite>(json);
        StepsData recLiteConverted = null;
        if (recLiteEnv != null && HasAnySteps(recLiteEnv.steps))
            recLiteConverted = ConvertLite(recLiteEnv.steps);
        if (HasAnySteps(recLiteConverted))
            return recLiteConverted;

        var liteDirect = JsonUtility.FromJson<StepsDataLite>(json);
        StepsData liteDirectConverted = null;
        if (HasAnySteps(liteDirect))
            liteDirectConverted = ConvertLite(liteDirect);
        if (HasAnySteps(liteDirectConverted))
            return liteDirectConverted;

        if (HasAnySteps(envParsed)) return envParsed;
        if (HasAnySteps(recParsed)) return recParsed;
        if (HasAnySteps(direct)) return direct;

        return direct ?? env?.steps ?? recEnv?.steps;
    }

    static bool HasAnySteps(StepsData d)
    {
        return d != null && d.steps != null && d.steps.Count > 0;
    }

    static bool HasAnySteps(StepsDataLite d)
    {
        return d != null && d.steps != null && d.steps.Count > 0;
    }

    static bool HasTrajectoryData(StepsData d)
    {
        if (d == null || d.steps == null) return false;
        for (int i = 0; i < d.steps.Count; i++)
        {
            var s = d.steps[i];
            if (s == null || s.trajectories == null) continue;
            for (int j = 0; j < s.trajectories.Count; j++)
            {
                var t = s.trajectories[j];
                if (t == null) continue;
                if (t.samples != null && t.samples.Count > 0) return true;
                if (t.from != null || t.to != null) return true;
            }
        }
        return false;
    }

    static string NormalizeJson(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        s = s.Trim();

        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
        {
            s = s.Substring(1, s.Length - 2);
            s = s.Replace("\\\"", "\"")
                 .Replace("\\\\", "\\")
                 .Replace("\\n", "")
                 .Replace("\\r", "")
                 .Replace("\\t", "");
        }

        return s.Trim('\uFEFF', '\u200B', ' ', '\r', '\n', '\t');
    }

    static void CountTrajectoryStats(StepsData d, out int tracks, out int samples)
    {
        tracks = 0;
        samples = 0;
        if (d == null || d.steps == null) return;

        for (int i = 0; i < d.steps.Count; i++)
        {
            var step = d.steps[i];
            if (step == null || step.trajectories == null) continue;
            tracks += step.trajectories.Count;
            for (int j = 0; j < step.trajectories.Count; j++)
            {
                var tr = step.trajectories[j];
                if (tr == null || tr.samples == null) continue;
                samples += tr.samples.Count;
            }
        }
    }

    static StepsData ConvertLite(StepsDataLite lite)
    {
        var outData = new StepsData
        {
            modelId = lite != null ? lite.modelId : null,
            steps = new List<StepFrame>()
        };

        if (lite == null || lite.steps == null) return outData;

        for (int i = 0; i < lite.steps.Count; i++)
        {
            var srcStep = lite.steps[i];
            if (srcStep == null) continue;

            var dstStep = new StepFrame
            {
                duration = srcStep.duration,
                parts = new List<PartState>(),
                trajectories = new List<PartTrajectory>()
            };
            var partIds = new HashSet<string>(StringComparer.Ordinal);

            if (srcStep.parts != null)
            {
                for (int j = 0; j < srcStep.parts.Count; j++)
                {
                    var p = srcStep.parts[j];
                    if (p == null) continue;
                    var ps = ConvertPartState(p);
                    if (ps == null) continue;
                    dstStep.parts.Add(ps);
                    if (!string.IsNullOrEmpty(ps.id)) partIds.Add(ps.id);
                }
            }

            if (srcStep.trajectories != null)
            {
                for (int j = 0; j < srcStep.trajectories.Count; j++)
                {
                    var tr = srcStep.trajectories[j];
                    if (tr == null || string.IsNullOrEmpty(tr.id)) continue;

                    var dstTr = new PartTrajectory
                    {
                        id = tr.id,
                        from = ConvertPartState(tr.from),
                        to = ConvertPartState(tr.to),
                        samples = new List<TrajectorySample>()
                    };

                    if (tr.samples != null)
                    {
                        for (int k = 0; k < tr.samples.Count; k++)
                        {
                            var s = tr.samples[k];
                            if (s == null) continue;
                            dstTr.samples.Add(new TrajectorySample
                            {
                                t = s.t,
                                localPos = ToVec3(s.localPos, Vector3.zero),
                                localRot = ToQuat(s.localRot),
                                localScale = ToVec3(s.localScale, Vector3.one)
                            });
                        }
                    }

                    dstStep.trajectories.Add(dstTr);

                    if (!partIds.Contains(tr.id))
                    {
                        PartState finalState = dstTr.to;
                        if (finalState == null && dstTr.samples != null && dstTr.samples.Count > 0)
                        {
                            var last = dstTr.samples[dstTr.samples.Count - 1];
                            finalState = new PartState
                            {
                                id = tr.id,
                                localPos = last.localPos,
                                localRot = last.localRot,
                                localScale = last.localScale
                            };
                        }

                        if (finalState != null)
                        {
                            dstStep.parts.Add(finalState);
                            partIds.Add(tr.id);
                        }
                    }
                }
            }

            outData.steps.Add(dstStep);
        }

        return outData;
    }

    static PartState ConvertPartState(PartStateLite p)
    {
        if (p == null || string.IsNullOrEmpty(p.id)) return null;
        return new PartState
        {
            id = p.id,
            localPos = ToVec3(p.localPos, Vector3.zero),
            localRot = ToQuat(p.localRot),
            localScale = ToVec3(p.localScale, Vector3.one)
        };
    }

    static Vector3 ToVec3(Vec3Lite v, Vector3 def)
    {
        if (v == null) return def;
        return new Vector3(v.x, v.y, v.z);
    }

    static Quaternion ToQuat(QuatLite q)
    {
        if (q == null) return Quaternion.identity;
        return new Quaternion(q.x, q.y, q.z, q.w);
    }

    // Compatibility methods for legacy phone UI button bindings.
    public void PlayPrev() => PrevStep();
    public void PlayNext() => NextStep();
    public void StartRecordingStep() => DownloadAndPrepare();
    public void FinishRecordingStep() => NextStep();
    public async void LoadRecordingByInput()
    {
        string rid = recordingIdInput != null ? recordingIdInput.text : recordingId;
        if (string.IsNullOrWhiteSpace(rid)) return;
        rid = rid.Trim();
        await LoadRecording(rid);
    }

    async Task EnsureModelLoaded()
    {
        EnsureRefs();
        if (importManager == null || api == null) return;
        if (data == null || string.IsNullOrEmpty(data.modelId)) return;

        string localPath = Path.Combine(Application.persistentDataPath, data.modelId + ".glb");
        if (!File.Exists(localPath))
        {
            byte[] glb = await api.GetBytes("/downloadModel?modelId=" + UnityWebRequest.EscapeURL(data.modelId));
            if (glb != null && glb.Length > 0)
                File.WriteAllBytes(localPath, glb);
        }

        if (File.Exists(localPath))
        {
            bool ok = await importManager.LoadGlbFromPathAsync(localPath);
            if (!ok)
                SetStatus("[PHONE] model load failed.");
            else
                SetStatus("[PHONE] model ready for steps.");
        }
    }

    public void JumpTo(int index)
    {
        EnsureRefs();
        if (modelIndex == null || data == null || data.steps == null || data.steps.Count == 0) return;

        if (_playCo != null)
        {
            StopCoroutine(_playCo);
            _playCo = null;
        }

        if (index < 0)
        {
            modelIndex.RestoreBaseAll();
            currentIndex = -1;
            SetStatus("[PHONE] Jump base");
            return;
        }

        index = Mathf.Clamp(index, 0, data.steps.Count - 1);
        var frame = data.steps[index];

        for (int i = 0; i < frame.parts.Count; i++)
        {
            var ps = frame.parts[i];
            if (!modelIndex.map.TryGetValue(ps.id, out var t) || t == null) continue;
            t.localPosition = ps.localPos;
            t.localRotation = ps.localRot;
            t.localScale = ps.localScale;
        }

        currentIndex = index;
        SetStatus("[PHONE] JumpTo " + (currentIndex + 1) + "/" + data.steps.Count + " taskId=" + _loadedTaskId);
    }

    bool HasLoadedSteps()
    {
        return data != null && data.steps != null && data.steps.Count > 0;
    }

    async Task<bool> EnsureStepsLoadedForPlayback()
    {
        if (HasLoadedSteps()) return true;

        string rid = ResolveRecordingIdForPlayback();
        if (!string.IsNullOrWhiteSpace(rid))
        {
            recordingId = rid;
            bool okRecording = await LoadRecording(rid);
            if (okRecording && HasLoadedSteps()) return true;
        }

        string id = ResolveTaskIdForPlayback();

        if (string.IsNullOrWhiteSpace(id))
        {
            SetStatus("[PHONE] no taskId/recordingId. Please CreateTask or input a recordingId.");
            return false;
        }

        taskId = id;
        bool ok = await LoadTask(id);
        if (!ok) return false;

        if (!HasLoadedSteps())
        {
            SetStatus("[PHONE] no steps loaded for taskId=" + id);
            return false;
        }

        return true;
    }

    public async void PrevStep()
    {
        if (!await EnsureStepsLoadedForPlayback()) return;
        JumpTo(currentIndex - 1);
    }

    public async void NextStep()
    {
        if (!await EnsureStepsLoadedForPlayback()) return;
        int target = currentIndex + 1;
        if (!useTrajectoryPlayback) JumpTo(target);
        else PlayStep(target);
    }

    public async void PlayStep(int index)
    {
        if (!await EnsureStepsLoadedForPlayback()) return;
        EnsureRefs();
        if (modelIndex == null || data == null || data.steps == null) return;
        if (index < 0 || index >= data.steps.Count) return;

        if (_playCo != null) StopCoroutine(_playCo);
        _playCo = StartCoroutine(CoPlayStep(index));
    }

    IEnumerator CoPlayStep(int index)
    {
        JumpTo(index - 1);
        var frame = data.steps[index];
        float duration = Mathf.Max(0.02f, frame.duration);

        if (frame.trajectories == null || frame.trajectories.Count == 0)
        {
            JumpTo(index);
            _playCo = null;
            yield break;
        }

        // ✅ 修复：锁定模型根节点位置，防止动画播放时模型跳动
        Transform modelRoot = modelIndex?.modelRoot;
        Vector3 lockedPosition = modelRoot != null ? modelRoot.position : Vector3.zero;
        Quaternion lockedRotation = modelRoot != null ? modelRoot.rotation : Quaternion.identity;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp(elapsed, 0f, duration);

            // ✅ 每帧强制锁定模型根节点位置，确保动画在当前位置播放
            if (modelRoot != null)
            {
                modelRoot.position = lockedPosition;
                modelRoot.rotation = lockedRotation;
            }

            for (int i = 0; i < frame.trajectories.Count; i++)
            {
                var track = frame.trajectories[i];
                if (!modelIndex.map.TryGetValue(track.id, out var tr) || tr == null) continue;
                ApplyTrackAtTime(track, tr, t, duration);
            }

            yield return null;
        }

        JumpTo(index);
        _playCo = null;
    }

    void ApplyTrackAtTime(PartTrajectory track, Transform target, float t, float duration)
    {
        if (track == null || target == null) return;
        var samples = track.samples;

        if (samples == null || samples.Count == 0)
        {
            if (track.to != null) ApplyState(track.to, target);
            return;
        }

        if (samples.Count == 1)
        {
            ApplySample(samples[0], target);
            return;
        }

        float clamped = Mathf.Clamp(t, 0f, duration);
        if (clamped <= samples[0].t)
        {
            ApplySample(samples[0], target);
            return;
        }

        for (int i = 0; i < samples.Count - 1; i++)
        {
            var a = samples[i];
            var b = samples[i + 1];
            if (clamped > b.t) continue;

            float seg = Mathf.Max(0.0001f, b.t - a.t);
            float u = Mathf.Clamp01((clamped - a.t) / seg);

            target.localPosition = Vector3.Lerp(a.localPos, b.localPos, u);
            target.localRotation = Quaternion.Slerp(a.localRot, b.localRot, u);
            target.localScale = Vector3.Lerp(a.localScale, b.localScale, u);
            return;
        }

        ApplySample(samples[samples.Count - 1], target);
    }

    static void ApplyState(PartState src, Transform t)
    {
        if (src == null || t == null) return;
        t.localPosition = src.localPos;
        t.localRotation = src.localRot;
        t.localScale = src.localScale;
    }

    static void ApplySample(TrajectorySample src, Transform t)
    {
        if (src == null || t == null) return;
        t.localPosition = src.localPos;
        t.localRotation = src.localRot;
        t.localScale = src.localScale;
    }

    void EnsureRefs()
    {
        if (api == null) api = FindFirstObjectByType<ApiClient>();
        if (importManager == null) importManager = FindFirstObjectByType<ImportManager>();
        if (modelIndex == null) modelIndex = FindFirstObjectByType<ModelIndex>();
    }

    async Task<bool> TryLoadLatestRecordingByTask(string taskIdForList)
    {
        try
        {
            var items = await QueryRecordings(taskIdForList, ResolveUserIdForPlayback());
            if (items == null || items.Length == 0) return false;

            string latestRid = ChooseLatestRecordingId(items);
            if (string.IsNullOrEmpty(latestRid)) return false;

            return await LoadRecording(latestRid);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PHONE] listRecordings fallback failed: " + e.Message);
            return false;
        }
    }

    async Task<RecordingData[]> QueryRecordings(string taskIdForList, string userIdForList)
    {
        EnsureRefs();
        if (api == null) return Array.Empty<RecordingData>();

        string path = BuildListRecordingsPath(taskIdForList, userIdForList);
        string json = await api.GetText(path);
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<RecordingData>();

        var res = JsonUtility.FromJson<ListRecordingsRes>(json);
        if (res == null || !res.ok) return Array.Empty<RecordingData>();
        return res.GetItems();
    }

    // taskIdForList:
    // - null/empty => no task filter (list all recordings)
    // - non-empty  => filter by that taskId
    public async Task<RecordingData[]> ListRecordings(string taskIdForList = null, string userIdForList = null)
    {
        string taskFilter = string.IsNullOrWhiteSpace(taskIdForList) ? null : taskIdForList.Trim();
        string userFilter = string.IsNullOrWhiteSpace(userIdForList) ? ResolveUserIdForPlayback() : userIdForList;
        return await QueryRecordings(taskFilter, userFilter);
    }

    string BuildListRecordingsPath(string taskIdForList, string userIdForList)
    {
        string path = "/listRecordings";
        string sep = "?";

        if (!string.IsNullOrWhiteSpace(taskIdForList))
        {
            path += sep + "taskId=" + UnityWebRequest.EscapeURL(taskIdForList.Trim());
            sep = "&";
        }

        if (!string.IsNullOrWhiteSpace(userIdForList))
            path += sep + "userId=" + UnityWebRequest.EscapeURL(userIdForList.Trim());

        return path;
    }

    static string ChooseLatestRecordingId(RecordingData[] items)
    {
        if (items == null || items.Length == 0) return "";

        int best = -1;
        long bestTime = long.MinValue;
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            if (it == null) continue;
            string rid = it.GetRecordingId();
            if (string.IsNullOrEmpty(rid)) continue;
            long ts = it.updatedAt > 0 ? it.updatedAt : it.createdAt;
            if (best < 0 || ts > bestTime)
            {
                best = i;
                bestTime = ts;
            }
        }

        if (best < 0) return "";
        return items[best].GetRecordingId();
    }

    string ResolveFallbackTaskId()
    {
        var create = FindFirstObjectByType<PhoneCreateTask>();
        if (create != null && !string.IsNullOrWhiteSpace(create.currentTaskId))
            return create.currentTaskId.Trim();

        string cached = PlayerPrefs.GetString("phone.lastTaskId", "");
        if (!string.IsNullOrWhiteSpace(cached))
            return cached.Trim();

        return "";
    }

    string ResolveRecordingIdForPlayback()
    {
        if (recordingIdInput != null && !string.IsNullOrWhiteSpace(recordingIdInput.text))
            return recordingIdInput.text.Trim();

        if (!string.IsNullOrWhiteSpace(recordingId))
            return recordingId.Trim();

        if (!string.IsNullOrWhiteSpace(_loadedRecordingId))
            return _loadedRecordingId.Trim();

        string cached = PlayerPrefs.GetString(AppConstants.PREF_PHONE_LAST_RECORDING_ID, "");
        if (!string.IsNullOrWhiteSpace(cached))
            return cached.Trim();

        return "";
    }

    string ResolveUserIdForPlayback()
    {
        if (userIdInput != null && !string.IsNullOrWhiteSpace(userIdInput.text))
            return userIdInput.text.Trim();

        if (!string.IsNullOrWhiteSpace(userId))
            return userId.Trim();

        return "";
    }

    string ResolveTaskIdForPlayback()
    {
        var create = FindFirstObjectByType<PhoneCreateTask>();
        if (create != null && !string.IsNullOrWhiteSpace(create.currentTaskId))
            return create.currentTaskId.Trim();

        if (taskIdInput != null && !string.IsNullOrWhiteSpace(taskIdInput.text))
            return taskIdInput.text.Trim();

        if (!string.IsNullOrWhiteSpace(taskId))
            return taskId.Trim();

        if (!string.IsNullOrWhiteSpace(_loadedTaskId))
            return _loadedTaskId.Trim();

        string cached = PlayerPrefs.GetString(AppConstants.PREF_PHONE_LAST_TASK_ID, "");
        if (!string.IsNullOrWhiteSpace(cached))
            return cached.Trim();

        return "";
    }

    void SetStatus(string msg)
    {
        Debug.Log(msg);
        if (statusText != null) statusText.text = msg;
    }

    [Serializable]
    class DownloadStepsEnvelope
    {
        public string taskId;
        public StepsData steps;
    }

    [Serializable]
    class DownloadRecordingStepsEnvelope
    {
        public bool ok;
        public string recordingId;
        public string recording_id;
        public StepsData steps;
    }

    [Serializable]
    class DownloadStepsEnvelopeLite
    {
        public string taskId;
        public StepsDataLite steps;
    }

    [Serializable]
    class DownloadRecordingStepsEnvelopeLite
    {
        public bool ok;
        public string recordingId;
        public string recording_id;
        public StepsDataLite steps;
    }

    [Serializable]
    class DownloadStepsEnvelopeString
    {
        public string taskId;
        public string steps;
    }

    [Serializable]
    class StepsDataLite
    {
        public string modelId;
        public List<StepFrameLite> steps;
    }

    [Serializable]
    class StepFrameLite
    {
        public float duration;
        public List<PartStateLite> parts;
        public List<PartTrajectoryLite> trajectories;
    }

    [Serializable]
    class PartStateLite
    {
        public string id;
        public Vec3Lite localPos;
        public QuatLite localRot;
        public Vec3Lite localScale;
    }

    [Serializable]
    class PartTrajectoryLite
    {
        public string id;
        public PartStateLite from;
        public PartStateLite to;
        public List<TrajectorySampleLite> samples;
    }

    [Serializable]
    class TrajectorySampleLite
    {
        public float t;
        public Vec3Lite localPos;
        public QuatLite localRot;
        public Vec3Lite localScale;
    }

    [Serializable]
    class Vec3Lite
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    class QuatLite
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }
}
