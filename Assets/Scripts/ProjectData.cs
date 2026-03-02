using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PoseData {
    public Vector3 pos;
    public Vector3 euler;
    public Vector3 scale;

    public static PoseData From(Transform t) => new PoseData {
        pos = t.localPosition,
        euler = t.localEulerAngles,
        scale = t.localScale
    };

    public void ApplyTo(Transform t) {
        t.localPosition = pos;
        t.localEulerAngles = euler;
        t.localScale = scale;
    }
}

[Serializable]
public class StepItem {
    public string partKey;
    public PoseData from;
    public PoseData to;
}

[Serializable]
public class StepData {
    public string name;
    public float duration = 0.6f;
    public List<StepItem> items = new List<StepItem>();
}

[Serializable]
public class ModelProject {
    public string modelId;
    public string modelFileName; // persistentDataPath 下的文件名，如 test.glb
    public List<StepData> steps = new List<StepData>();
}
