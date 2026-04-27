using System;

public static class NetModelStringUtil
{
    public static string FirstNonEmpty(params string[] values)
    {
        if (values == null) return "";
        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrEmpty(values[i])) return values[i];
        }
        return "";
    }
}

[Serializable]
public class TaskData
{
    public string taskId;
    public string modelId;
    public string status;
    public string modelFile;
    public string title;
    public string userId;
    public string logicalModelId;
    public string modelType;
    public string modelHash;

    // compatibility: some backends may return snake_case
    public string task_id;
    public string model_id;
    public string model_file;
    public string user_id;
    public string logical_model_id;
    public string model_type;
    public string model_hash;

    public string GetTaskId()
    {
        if (!string.IsNullOrEmpty(taskId)) return taskId;
        if (!string.IsNullOrEmpty(task_id)) return task_id;
        return "";
    }

    public string GetModelId()
    {
        if (!string.IsNullOrEmpty(modelId)) return modelId;
        if (!string.IsNullOrEmpty(model_id)) return model_id;
        return "";
    }

    public string GetLogicalModelId()
    {
        return NetModelStringUtil.FirstNonEmpty(logicalModelId, logical_model_id);
    }

    public string GetModelType()
    {
        return NetModelStringUtil.FirstNonEmpty(modelType, model_type);
    }

    public string GetModelHash()
    {
        return NetModelStringUtil.FirstNonEmpty(modelHash, model_hash);
    }
}

[Serializable]
public class PollTaskRes
{
    public bool ok;
    public TaskData task;

    // compatibility: some backends may return flat fields
    public string taskId;
    public string modelId;
    public string task_id;
    public string model_id;

    public string GetTaskId()
    {
        if (task != null)
        {
            var t = task.GetTaskId();
            if (!string.IsNullOrEmpty(t)) return t;
        }
        if (!string.IsNullOrEmpty(taskId)) return taskId;
        if (!string.IsNullOrEmpty(task_id)) return task_id;
        return "";
    }

    public string GetModelId()
    {
        if (task != null)
        {
            var m = task.GetModelId();
            if (!string.IsNullOrEmpty(m)) return m;
        }
        if (!string.IsNullOrEmpty(modelId)) return modelId;
        if (!string.IsNullOrEmpty(model_id)) return model_id;
        return "";
    }
}

[Serializable]
public class CreateTaskReq
{
    public string modelId;
    public string title;
    public string userId;
    public string logicalModelId;
    public string modelType;
    public string modelHash;
    public string logical_model_id;
    public string model_type;
    public string model_hash;
}

[Serializable]
public class CreateTaskRes
{
    public bool ok;
    public TaskData task;
}

[Serializable]
public class RecordingData
{
    public string recordingId;
    public string taskId;
    public string modelId;
    public string title;
    public string userId;
    public string logicalModelId;
    public string modelType;
    public string modelHash;
    public int stepsCount;
    public long createdAt;
    public long updatedAt;

    // compatibility: some backends may return snake_case
    public string recording_id;
    public string task_id;
    public string model_id;
    public string user_id;
    public string logical_model_id;
    public string model_type;
    public string model_hash;
    public int steps_count;

    public string GetRecordingId()
    {
        if (!string.IsNullOrEmpty(recordingId)) return recordingId;
        if (!string.IsNullOrEmpty(recording_id)) return recording_id;
        return "";
    }

    public string GetTaskId()
    {
        if (!string.IsNullOrEmpty(taskId)) return taskId;
        if (!string.IsNullOrEmpty(task_id)) return task_id;
        return "";
    }

    public string GetModelId()
    {
        if (!string.IsNullOrEmpty(modelId)) return modelId;
        if (!string.IsNullOrEmpty(model_id)) return model_id;
        return "";
    }

    public string GetLogicalModelId()
    {
        return NetModelStringUtil.FirstNonEmpty(logicalModelId, logical_model_id);
    }

    public string GetModelType()
    {
        return NetModelStringUtil.FirstNonEmpty(modelType, model_type);
    }

    public string GetModelHash()
    {
        return NetModelStringUtil.FirstNonEmpty(modelHash, model_hash);
    }

    public int GetStepsCount()
    {
        if (stepsCount > 0) return stepsCount;
        if (steps_count > 0) return steps_count;
        return 0;
    }
}

[Serializable]
public class CreateRecordingReq
{
    public string taskId;
    public string modelId;
    public string title;
    public string userId;
    public string logicalModelId;
    public string modelType;
    public string modelHash;
    public string logical_model_id;
    public string model_type;
    public string model_hash;
}

[Serializable]
public class CreateRecordingRes
{
    public bool ok;
    public RecordingData recording;
    public string recordingId;
    public string recording_id;
    public string error;

    public string GetRecordingId()
    {
        if (recording != null)
        {
            var id = recording.GetRecordingId();
            if (!string.IsNullOrEmpty(id)) return id;
        }
        if (!string.IsNullOrEmpty(recordingId)) return recordingId;
        if (!string.IsNullOrEmpty(recording_id)) return recording_id;
        return "";
    }
}

[Serializable]
public class UploadRecordingStepsReq
{
    public string recordingId;
    public StepsData steps;
}

[Serializable]
public class UploadRecordingStepsRes
{
    public bool ok;
    public RecordingData recording;
    public string recordingId;
    public string recording_id;
    public string error;

    public string GetRecordingId()
    {
        if (recording != null)
        {
            var id = recording.GetRecordingId();
            if (!string.IsNullOrEmpty(id)) return id;
        }
        if (!string.IsNullOrEmpty(recordingId)) return recordingId;
        if (!string.IsNullOrEmpty(recording_id)) return recording_id;
        return "";
    }
}

[Serializable]
public class DownloadRecordingRes
{
    public bool ok;
    public RecordingData recording;
    public string error;
}

[Serializable]
public class ListRecordingsRes
{
    public bool ok;
    public RecordingData[] items;
    public RecordingData[] recordings;
    public string error;

    public RecordingData[] GetItems()
    {
        if (items != null && items.Length > 0) return items;
        if (recordings != null && recordings.Length > 0) return recordings;
        return Array.Empty<RecordingData>();
    }
}
