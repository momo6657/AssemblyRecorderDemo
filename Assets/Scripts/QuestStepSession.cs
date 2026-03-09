using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[Serializable]
public class PartState
{
    public string id;
    public Vector3 localPos;
    public Quaternion localRot;
    public Vector3 localScale;

    public static PartState From(string id, Transform t)
    {
        return new PartState
        {
            id = id,
            localPos = t.localPosition,
            localRot = t.localRotation,
            localScale = t.localScale
        };
    }
}

[Serializable]
public class TrajectorySample
{
    public float t;
    public Vector3 localPos;
    public Quaternion localRot;
    public Vector3 localScale;

    public static TrajectorySample From(float time, Transform t)
    {
        return new TrajectorySample
        {
            t = time,
            localPos = t.localPosition,
            localRot = t.localRotation,
            localScale = t.localScale
        };
    }
}

[Serializable]
public class PartTrajectory
{
    public string id;
    public PartState from;
    public PartState to;
    public List<TrajectorySample> samples = new List<TrajectorySample>();
}

[Serializable]
public class StepFrame
{
    public float duration = 0.6f;
    public List<PartState> parts = new List<PartState>();
    public List<PartTrajectory> trajectories = new List<PartTrajectory>();
}

[Serializable]
public class StepsData
{
    public string modelId;
    public List<StepFrame> steps = new List<StepFrame>();
}

public class QuestStepSession : MonoBehaviour
{
    [Header("Refs")]
    public ModelIndex modelIndex;
    public ApiClient apiClient;
    public QuestPollTask questPollTask;

    [Header("Optional UI")]
    public TMP_Text statusText;

    [Header("Behavior")]
    public bool clearStepsWhenTaskChanged = true;

    [Header("Upload / Saved Recording")]
    public bool useNamedRecordingApi = true;
    public bool allowLegacyFallbackWhenNamedApiFails = false;
    public string recordingTitleOverride = "";
    public string recordingUserId = "";
    public string currentRecordingId = "";

    [Header("Trajectory Recording")]
    public bool recordTrajectory = true;
    [Range(1f, 60f)] public float sampleRateHz = AppConstants.DEFAULT_SAMPLE_RATE;
    public bool recordOnlyMovedParts = true;
    public float movePosThreshold = AppConstants.MOVE_POS_THRESHOLD;
    public float moveRotThresholdDeg = AppConstants.MOVE_ROT_THRESHOLD_DEG;
    public float moveScaleThreshold = AppConstants.MOVE_SCALE_THRESHOLD;

    [Header("Runtime")]
    public StepsData data = new StepsData();
    public int currentIndex = -1;
    
    // ✅ 新增：记录开始录制时的状态（不是导入时的初始状态）
    readonly Dictionary<string, PartState> _recordingStartSnapshot = new Dictionary<string, PartState>();
    bool _hasRecordingStartSnapshot = false;

    string _boundTaskId;
    string _boundModelId;

    bool _segmentPrepared;
    float _segmentStartTime;
    float _nextSampleAt;
    readonly Dictionary<string, PartState> _segmentStartSnapshot = new Dictionary<string, PartState>();
    readonly Dictionary<string, List<TrajectorySample>> _segmentSamples = new Dictionary<string, List<TrajectorySample>>();

    Coroutine _playCo;

    void Awake()
    {
        EnsureRefs();
        BindTaskEvent();
    }

    void OnEnable()
    {
        BindTaskEvent();
    }

    void OnDisable()
    {
        UnbindTaskEvent();
    }

    void Start()
    {
        SyncTaskContext(clearStepsWhenChanged: false);
    }

    void Update()
    {
        if (!_segmentPrepared &&
            modelIndex != null &&
            modelIndex.map != null &&
            modelIndex.map.Count > 0 &&
            !string.IsNullOrEmpty(_boundTaskId))
        {
            PrepareSegmentFromCurrentPose();
        }

        if (!recordTrajectory || !_segmentPrepared) return;
        if (Time.unscaledTime < _nextSampleAt) return;

        SampleCurrentPose(force: false);
        _nextSampleAt = Time.unscaledTime + GetSampleInterval();
    }

    public void BeginTaskSession(string taskId, string modelId, bool clearExistingSteps = true)
    {
        bool changed = !string.Equals(_boundTaskId, taskId, StringComparison.Ordinal) ||
                       !string.Equals(_boundModelId, modelId, StringComparison.Ordinal);

        _boundTaskId = taskId;
        _boundModelId = modelId;
        data.modelId = modelId;
        if (changed) currentRecordingId = "";

        if (clearExistingSteps && changed)
        {
            ClearRecordedSteps();
            ResetToBasePose();
            SetStatus("[QuestStepSession] New task session: cleared previous steps.");
        }

        if (changed || !_segmentPrepared)
            PrepareSegmentFromCurrentPose();
    }

    public void ClearRecordedSteps()
    {
        if (data == null) data = new StepsData();
        if (data.steps == null) data.steps = new List<StepFrame>();
        data.steps.Clear();
        currentIndex = -1;
        _segmentPrepared = false;
        
        // ✅ 修复：清除录制开始快照
        _recordingStartSnapshot.Clear();
        _hasRecordingStartSnapshot = false;
        Debug.Log("[QuestStepSession] Cleared recording start snapshot");
    }

    public void ResetToBasePose()
    {
        if (modelIndex == null) return;
        modelIndex.RestoreBaseAll();
        currentIndex = -1;
        PrepareSegmentFromCurrentPose();
    }

    public void RecordStep()
    {
        EnsureRefs();
        SyncTaskContext(clearStepsWhenChanged: clearStepsWhenTaskChanged);

        if (modelIndex == null)
        {
            SetStatus("[QuestStepSession] modelIndex is null");
            return;
        }

        if (modelIndex.map == null || modelIndex.map.Count == 0)
        {
            SetStatus("[QuestStepSession] modelIndex.map empty - import/build model first.");
            return;
        }

        if (string.IsNullOrEmpty(_boundTaskId))
        {
            SetStatus("[QuestStepSession] no active taskId - poll task first.");
            return;
        }

        if (string.IsNullOrEmpty(_boundModelId) && questPollTask != null)
            _boundModelId = questPollTask.currentModelId;

        // ✅ 修复：如果这是第一次录制步骤，保存当前状态作为"录制开始状态"
        if (!_hasRecordingStartSnapshot)
        {
            // 使用 _segmentStartSnapshot 作为录制开始状态
            // 因为 PrepareSegmentFromCurrentPose 会在第一次录制前被调用
            if (_segmentStartSnapshot.Count > 0)
            {
                _recordingStartSnapshot.Clear();
                foreach (var kv in _segmentStartSnapshot)
                {
                    _recordingStartSnapshot[kv.Key] = CloneState(kv.Value);
                }
                _hasRecordingStartSnapshot = true;
                Debug.Log($"[QuestStepSession] Captured recording start snapshot with {_recordingStartSnapshot.Count} parts (from segment start)");
            }
        }

        // Important: only initialize baseline when there is none.
        // If we always rebuild baseline here, movement delta becomes zero.
        if (!_segmentPrepared && !PrepareSegmentFromCurrentPose())
        {
            SetStatus("[QuestStepSession] segment baseline not ready.");
            return;
        }

        SampleCurrentPose(force: true);

        data.modelId = _boundModelId;
        if (data.steps == null) data.steps = new List<StepFrame>();

        float duration = Mathf.Max(0.02f, Time.unscaledTime - _segmentStartTime);
        var frame = new StepFrame { duration = duration };

        bool anyMoved = false;
        var keys = new List<string>(modelIndex.map.Keys);
        keys.Sort(StringComparer.Ordinal);

        var nowSnapshot = CaptureCurrentSnapshot();

        for (int i = 0; i < keys.Count; i++)
        {
            var id = keys[i];
            if (!nowSnapshot.TryGetValue(id, out var to)) continue;

            frame.parts.Add(CloneState(to));

            if (!_segmentStartSnapshot.TryGetValue(id, out var from))
                from = CloneState(to);

            bool moved = HasMoved(from, to);
            if (moved) anyMoved = true;

            if (!recordTrajectory) continue;
            
            // ✅ 修复：即使零件没有移动，也要记录 trajectory
            // 这样回放时可以确保所有零件都从正确的位置开始
            // 只有当 recordOnlyMovedParts=true 且零件没有移动时才跳过
            // 但如果有录制开始快照，就必须记录，以确保从正确位置开始
            bool shouldRecord = moved || !recordOnlyMovedParts || _hasRecordingStartSnapshot;
            if (!shouldRecord) continue;

            var traj = BuildTrajectory(id, from, to, duration);
            if (traj != null) frame.trajectories.Add(traj);
        }

        if (!anyMoved)
        {
            SetStatus("[QuestStepSession] no movement detected, step ignored.");
            ResetSegmentBaseline(nowSnapshot);
            return;
        }

        data.steps.Add(frame);
        currentIndex = data.steps.Count - 1;
        SetStatus($"[QuestStepSession] Recorded step {currentIndex + 1}/{data.steps.Count}, dur={duration:F2}s, tracks={frame.trajectories.Count}");

        ResetSegmentBaseline(nowSnapshot);
    }

    // Optional explicit two-button flow:
    // 1) BeginStep() before you start moving parts
    // 2) EndStep() after movement complete
    public void BeginStep()
    {
        EnsureRefs();
        SyncTaskContext(clearStepsWhenChanged: clearStepsWhenTaskChanged);

        if (modelIndex == null || modelIndex.map == null || modelIndex.map.Count == 0)
        {
            SetStatus("[QuestStepSession] BeginStep failed: model not ready.");
            return;
        }

        if (string.IsNullOrEmpty(_boundTaskId))
        {
            SetStatus("[QuestStepSession] BeginStep failed: no active taskId.");
            return;
        }
        
        // ✅ 修复：如果这是第一次开始录制，保存当前状态作为"录制开始状态"
        if (!_hasRecordingStartSnapshot)
        {
            _recordingStartSnapshot.Clear();
            var snapshot = CaptureCurrentSnapshot();
            Debug.Log($"[QuestStepSession] BeginStep: Capturing snapshot with {snapshot.Count} parts");
            
            foreach (var kv in snapshot)
            {
                _recordingStartSnapshot[kv.Key] = CloneState(kv.Value);
                Debug.Log($"[QuestStepSession] BeginStep: {kv.Key} pos={kv.Value.localPos}");
            }
            _hasRecordingStartSnapshot = true;
            Debug.Log($"[QuestStepSession] Captured recording start snapshot with {_recordingStartSnapshot.Count} parts");
        }
        else
        {
            Debug.Log($"[QuestStepSession] BeginStep: Already have recording start snapshot with {_recordingStartSnapshot.Count} parts");
        }

        if (!PrepareSegmentFromCurrentPose())
        {
            SetStatus("[QuestStepSession] BeginStep failed: cannot prepare baseline.");
            return;
        }

        SetStatus("[QuestStepSession] BeginStep OK. Move parts, then press EndStep.");
    }

    public void EndStep()
    {
        RecordStep();
    }

    public void JumpTo(int index)
    {
        if (modelIndex == null) return;
        if (data.steps == null || data.steps.Count == 0) return;

        if (_playCo != null)
        {
            StopCoroutine(_playCo);
            _playCo = null;
        }

        if (index < 0)
        {
            // ✅ 修复：跳到"录制开始状态"而不是"初始状态"
            if (_hasRecordingStartSnapshot && _recordingStartSnapshot.Count > 0)
            {
                Debug.Log("[QuestStepSession] JumpTo recording start pose");
                
                // 先恢复父级关系
                foreach (var kv in modelIndex.map)
                {
                    var id = kv.Key;
                    var t = kv.Value;
                    if (t == null) continue;
                    
                    if (modelIndex.TryGetOriginalParent(id, out var originalParent))
                    {
                        if (t.parent != originalParent)
                        {
                            t.SetParent(originalParent, true);
                        }
                    }
                }
                
                // 应用录制开始时的状态
                foreach (var kv in _recordingStartSnapshot)
                {
                    var id = kv.Key;
                    var ps = kv.Value;
                    if (!modelIndex.map.TryGetValue(id, out var t) || t == null) continue;
                    t.localPosition = ps.localPos;
                    t.localRotation = ps.localRot;
                    t.localScale = ps.localScale;
                }
                
                SetStatus("[QuestStepSession] JumpTo recording start pose");
            }
            else
            {
                // 如果没有录制开始快照，才恢复到初始状态
                ResetToBasePose();
                SetStatus("[QuestStepSession] JumpTo base pose");
            }
            currentIndex = -1;
            return;
        }

        index = Mathf.Clamp(index, 0, data.steps.Count - 1);
        var frame = data.steps[index];

        // ✅ 修复：在应用步骤之前，先恢复所有零件的父级关系
        if (modelIndex != null)
        {
            foreach (var kv in modelIndex.map)
            {
                var id = kv.Key;
                var t = kv.Value;
                if (t == null) continue;
                
                // 恢复到原始父级
                if (modelIndex.TryGetOriginalParent(id, out var originalParent))
                {
                    if (t.parent != originalParent)
                    {
                        Debug.Log($"[QuestStepSession] JumpTo: restoring parent for {id}");
                        t.SetParent(originalParent, true);
                    }
                }
            }
        }

        // 应用步骤的状态
        for (int i = 0; i < frame.parts.Count; i++)
        {
            var ps = frame.parts[i];
            if (!modelIndex.map.TryGetValue(ps.id, out var t) || t == null) continue;
            t.localPosition = ps.localPos;
            t.localRotation = ps.localRot;
            t.localScale = ps.localScale;
        }

        currentIndex = index;
        SetStatus("[QuestStepSession] JumpTo " + (currentIndex + 1));
    }

    public void PrevStep() => JumpTo(currentIndex - 1);
    public void NextStep() => PlayStep(currentIndex + 1);

    public void PlayStep(int index)
    {
        if (index < 0)
        {
            JumpTo(-1);
            return;
        }
        if (data.steps == null || index >= data.steps.Count) return;

        if (_playCo != null) StopCoroutine(_playCo);
        _playCo = StartCoroutine(CoPlayStep(index));
    }

    IEnumerator CoPlayStep(int index)
    {
        // ✅ 修复：每一步播放都从录制开始状态开始，而不是从前一步结束状态开始
        
        // ✅ 锁定模型根节点位置，确保动画在当前位置播放
        Transform modelRoot = modelIndex?.modelRoot;
        
        if (modelRoot == null)
        {
            Debug.LogWarning("[QuestStepSession] ModelRoot is null, cannot play step");
            _playCo = null;
            yield break;
        }
        
        Vector3 lockedPosition = modelRoot.position;
        Quaternion lockedRotation = modelRoot.rotation;
        
        // ✅ 修复：始终跳到录制开始状态（-1），而不是前一步
        // 这样每一步都从相同的起始位置开始
        JumpTo(-1);
        
        // ✅ JumpTo 后重新锁定位置
        modelRoot = modelIndex?.modelRoot;
        if (modelRoot != null)
        {
            modelRoot.position = lockedPosition;
            modelRoot.rotation = lockedRotation;
        }

        var frame = data.steps[index];
        float duration = Mathf.Max(0.02f, frame.duration);

        if (frame.trajectories == null || frame.trajectories.Count == 0)
        {
            JumpTo(index);
            // ✅ JumpTo 后再次恢复位置
            modelRoot = modelIndex?.modelRoot;
            if (modelRoot != null)
            {
                modelRoot.position = lockedPosition;
                modelRoot.rotation = lockedRotation;
            }
            _playCo = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp(elapsed, 0f, duration);

            // ✅ 每帧强制锁定模型根节点位置
            modelRoot = modelIndex?.modelRoot;
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
        // ✅ 最后再次恢复位置
        modelRoot = modelIndex?.modelRoot;
        if (modelRoot != null)
        {
            modelRoot.position = lockedPosition;
            modelRoot.rotation = lockedRotation;
        }
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

    public async void UploadSteps()
    {
        try
        {
            EnsureRefs();
            SyncTaskContext(clearStepsWhenChanged: false);

            if (apiClient == null) throw new Exception("apiClient is null");
            if (string.IsNullOrEmpty(_boundTaskId)) throw new Exception("currentTaskId is empty");
            if (data.steps == null || data.steps.Count == 0) throw new Exception("no steps recorded");

            data.modelId = _boundModelId;
            if (useNamedRecordingApi)
            {
                if (string.IsNullOrEmpty(_boundModelId))
                    throw new Exception("modelId is empty, cannot create recording");

                string title = BuildRecordingTitle();
                var createReq = new CreateRecordingReq
                {
                    taskId = _boundTaskId,
                    modelId = _boundModelId,
                    title = title,
                    userId = ResolveRecordingUserId()
                };

                string createJson = JsonUtility.ToJson(createReq);
                string createResText = await apiClient.PostJson("/createRecording", createJson);
                var createRes = JsonUtility.FromJson<CreateRecordingRes>(createResText);
                string rid = createRes != null ? createRes.GetRecordingId() : "";
                if (string.IsNullOrEmpty(rid))
                    throw new Exception("createRecording missing recordingId. raw=" + createResText);

                var uploadReq = new UploadRecordingStepsReq
                {
                    recordingId = rid,
                    steps = data
                };

                string uploadJson = JsonUtility.ToJson(uploadReq);
                string uploadResText = await apiClient.PostJson("/uploadRecordingSteps", uploadJson);
                var uploadRes = JsonUtility.FromJson<UploadRecordingStepsRes>(uploadResText);
                string uploadedRid = uploadRes != null ? uploadRes.GetRecordingId() : rid;
                currentRecordingId = string.IsNullOrEmpty(uploadedRid) ? rid : uploadedRid;

                SetStatus("[QuestStepSession] uploadRecordingSteps ok, recordingId=" + currentRecordingId);
                return;
            }

            var payload = new UploadStepsPayload
            {
                taskId = _boundTaskId,
                steps = data
            };
            string json = JsonUtility.ToJson(payload);
            string res = await apiClient.PostJson("/uploadSteps", json);
            SetStatus("[QuestStepSession] uploadSteps (legacy) => " + res);
        }
        catch (Exception e)
        {
            if (allowLegacyFallbackWhenNamedApiFails && useNamedRecordingApi)
            {
                try
                {
                    var payload = new UploadStepsPayload
                    {
                        taskId = _boundTaskId,
                        steps = data
                    };
                    string json = JsonUtility.ToJson(payload);
                    string res = await apiClient.PostJson("/uploadSteps", json);
                    SetStatus("[QuestStepSession] named API failed, fallback /uploadSteps => " + res);
                    Debug.LogWarning("[QuestStepSession] named API failed, fallback /uploadSteps. reason=" + e.Message);
                    return;
                }
                catch (Exception fallbackEx)
                {
                    Debug.LogError("[QuestStepSession] fallback /uploadSteps also failed: " + fallbackEx);
                }
            }

            SetStatus("[QuestStepSession] Upload failed: " + e.Message);
            Debug.LogError("[QuestStepSession] Upload failed: " + e);
        }
    }

    bool PrepareSegmentFromCurrentPose()
    {
        if (modelIndex == null || modelIndex.map == null || modelIndex.map.Count == 0)
        {
            _segmentPrepared = false;
            return false;
        }

        var snap = CaptureCurrentSnapshot();
        if (snap.Count == 0)
        {
            _segmentPrepared = false;
            return false;
        }

        ResetSegmentBaseline(snap);
        return true;
    }

    void ResetSegmentBaseline(Dictionary<string, PartState> baseline)
    {
        _segmentStartSnapshot.Clear();
        _segmentSamples.Clear();

        foreach (var kv in baseline)
        {
            _segmentStartSnapshot[kv.Key] = CloneState(kv.Value);

            var list = new List<TrajectorySample>
            {
                new TrajectorySample
                {
                    t = 0f,
                    localPos = kv.Value.localPos,
                    localRot = kv.Value.localRot,
                    localScale = kv.Value.localScale
                }
            };
            _segmentSamples[kv.Key] = list;
        }

        _segmentStartTime = Time.unscaledTime;
        _nextSampleAt = Time.unscaledTime + GetSampleInterval();
        _segmentPrepared = true;
    }

    Dictionary<string, PartState> CaptureCurrentSnapshot()
    {
        var dict = new Dictionary<string, PartState>();
        if (modelIndex == null || modelIndex.map == null) return dict;

        foreach (var kv in modelIndex.map)
        {
            var id = kv.Key;
            var t = kv.Value;
            if (t == null) continue;
            dict[id] = CaptureStableState(id, t);
        }

        return dict;
    }

    void SampleCurrentPose(bool force)
    {
        if (!_segmentPrepared) return;
        if (modelIndex == null || modelIndex.map == null) return;

        float time = Mathf.Max(0f, Time.unscaledTime - _segmentStartTime);

        foreach (var kv in modelIndex.map)
        {
            var id = kv.Key;
            var t = kv.Value;
            if (t == null) continue;

            if (!_segmentSamples.TryGetValue(id, out var list))
            {
                list = new List<TrajectorySample>();
                _segmentSamples[id] = list;
            }

            var sample = CaptureStableSample(id, time, t);
            if (list.Count == 0)
            {
                list.Add(sample);
                continue;
            }

            var last = list[list.Count - 1];
            bool changed = HasMoved(last, sample);
            if (!changed)
            {
                if (force) list[list.Count - 1] = sample;
                continue;
            }

            list.Add(sample);
        }
    }

    PartState CaptureStableState(string id, Transform t)
    {
        if (t == null) return null;

        Vector3 localPos = t.localPosition;
        Quaternion localRot = t.localRotation;
        Vector3 localScale = t.localScale;

        if (modelIndex != null)
            modelIndex.TryGetStableLocalPose(id, t, out localPos, out localRot, out localScale);

        return new PartState
        {
            id = id,
            localPos = localPos,
            localRot = localRot,
            localScale = localScale
        };
    }

    TrajectorySample CaptureStableSample(string id, float time, Transform t)
    {
        if (t == null) return null;

        Vector3 localPos = t.localPosition;
        Quaternion localRot = t.localRotation;
        Vector3 localScale = t.localScale;

        if (modelIndex != null)
            modelIndex.TryGetStableLocalPose(id, t, out localPos, out localRot, out localScale);

        return new TrajectorySample
        {
            t = time,
            localPos = localPos,
            localRot = localRot,
            localScale = localScale
        };
    }

    PartTrajectory BuildTrajectory(string id, PartState from, PartState to, float duration)
    {
        // ✅ 修复：如果有录制开始快照，使用它作为 from 状态
        // 这样可以确保每一步的起始位置都是 BeginStep 时的位置
        if (_hasRecordingStartSnapshot && _recordingStartSnapshot.TryGetValue(id, out var recordingStart))
        {
            Debug.Log($"[QuestStepSession] BuildTrajectory: {id} using recording start pos={recordingStart.localPos} (was {from.localPos})");
            from = CloneState(recordingStart);
        }
        else
        {
            Debug.LogWarning($"[QuestStepSession] BuildTrajectory: {id} NO recording start snapshot! Using from={from.localPos}");
        }
        
        var traj = new PartTrajectory
        {
            id = id,
            from = CloneState(from),
            to = CloneState(to)
        };

        if (_segmentSamples.TryGetValue(id, out var samples) && samples != null)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                traj.samples.Add(new TrajectorySample
                {
                    t = Mathf.Clamp(s.t, 0f, duration),
                    localPos = s.localPos,
                    localRot = s.localRot,
                    localScale = s.localScale
                });
            }
        }

        if (traj.samples.Count == 0)
        {
            traj.samples.Add(new TrajectorySample
            {
                t = 0f,
                localPos = from.localPos,
                localRot = from.localRot,
                localScale = from.localScale
            });
        }

        // ✅ 修复：确保第一个 sample 使用录制开始状态
        var first = traj.samples[0];
        if (HasMoved(first, from))
        {
            traj.samples.Insert(0, new TrajectorySample
            {
                t = 0f,
                localPos = from.localPos,
                localRot = from.localRot,
                localScale = from.localScale
            });
        }
        else
        {
            first.t = 0f;
            first.localPos = from.localPos;
            first.localRot = from.localRot;
            first.localScale = from.localScale;
            traj.samples[0] = first;
        }

        var last = traj.samples[traj.samples.Count - 1];
        if (HasMoved(last, to) || last.t < duration - 0.0001f)
        {
            traj.samples.Add(new TrajectorySample
            {
                t = duration,
                localPos = to.localPos,
                localRot = to.localRot,
                localScale = to.localScale
            });
        }
        else
        {
            last.t = duration;
            traj.samples[traj.samples.Count - 1] = last;
        }

        Debug.Log($"[QuestStepSession] BuildTrajectory: {id} from={traj.from.localPos} to={traj.to.localPos} samples={traj.samples.Count}");
        return traj;
    }

    float GetSampleInterval()
    {
        return 1f / Mathf.Max(1f, sampleRateHz);
    }

    bool HasMoved(PartState a, PartState b)
    {
        if (a == null || b == null) return true;
        return Vector3.Distance(a.localPos, b.localPos) > movePosThreshold
               || Quaternion.Angle(a.localRot, b.localRot) > moveRotThresholdDeg
               || Vector3.Distance(a.localScale, b.localScale) > moveScaleThreshold;
    }

    bool HasMoved(TrajectorySample a, TrajectorySample b)
    {
        if (a == null || b == null) return true;
        return Vector3.Distance(a.localPos, b.localPos) > movePosThreshold
               || Quaternion.Angle(a.localRot, b.localRot) > moveRotThresholdDeg
               || Vector3.Distance(a.localScale, b.localScale) > moveScaleThreshold;
    }

    bool HasMoved(TrajectorySample a, PartState b)
    {
        if (a == null || b == null) return true;
        return Vector3.Distance(a.localPos, b.localPos) > movePosThreshold
               || Quaternion.Angle(a.localRot, b.localRot) > moveRotThresholdDeg
               || Vector3.Distance(a.localScale, b.localScale) > moveScaleThreshold;
    }

    static PartState CloneState(PartState src)
    {
        return new PartState
        {
            id = src.id,
            localPos = src.localPos,
            localRot = src.localRot,
            localScale = src.localScale
        };
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
        if (modelIndex == null) modelIndex = FindFirstObjectByType<ModelIndex>();
        if (apiClient == null) apiClient = FindFirstObjectByType<ApiClient>();
        if (questPollTask == null) questPollTask = FindFirstObjectByType<QuestPollTask>();
        if (statusText == null)
        {
            var import = FindFirstObjectByType<ImportManager>();
            if (import != null) statusText = import.statusText;
        }

        BindTaskEvent();
    }

    void SyncTaskContext(bool clearStepsWhenChanged)
    {
        if (questPollTask == null) return;
        if (string.IsNullOrEmpty(questPollTask.currentTaskId)) return;

        BeginTaskSession(questPollTask.currentTaskId, questPollTask.currentModelId, clearStepsWhenChanged);
    }

    void BindTaskEvent()
    {
        if (questPollTask == null) return;
        questPollTask.OnTaskContextChanged -= HandleTaskContextChanged;
        questPollTask.OnTaskContextChanged += HandleTaskContextChanged;
    }

    void UnbindTaskEvent()
    {
        if (questPollTask == null) return;
        questPollTask.OnTaskContextChanged -= HandleTaskContextChanged;
    }

    void HandleTaskContextChanged(string taskId, string modelId)
    {
        BeginTaskSession(taskId, modelId, clearStepsWhenTaskChanged);
    }

    string BuildRecordingTitle()
    {
        if (!string.IsNullOrWhiteSpace(recordingTitleOverride))
            return recordingTitleOverride.Trim();

        if (questPollTask != null && !string.IsNullOrWhiteSpace(questPollTask.currentTaskTitle))
            return questPollTask.currentTaskTitle.Trim();

        return "Recording_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
    }

    string ResolveRecordingUserId()
    {
        if (!string.IsNullOrWhiteSpace(recordingUserId))
            return recordingUserId.Trim();

        if (questPollTask != null && !string.IsNullOrWhiteSpace(questPollTask.currentTaskUserId))
            return questPollTask.currentTaskUserId.Trim();

        return null;
    }

    void SetStatus(string msg)
    {
        Debug.Log(msg);
        if (statusText != null) statusText.text = msg;
    }

    [Serializable]
    class UploadStepsPayload
    {
        public string taskId;
        public StepsData steps;
    }
}
