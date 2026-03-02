using UnityEngine;

public class TransformPanel : MonoBehaviour
{
    public SelectionManager selection;
    public StepRecorder recorder;

    public float moveStep = 0.02f;     // 每次移动多少
    public float rotateStep = 5f;      // 每次旋转多少度

    void ApplyToSelected(System.Action<Transform> op)
    {
        if (selection == null || selection.index == null) return;

        bool any = false;
        foreach (var key in selection.SelectedKeys)
        {
            if (selection.index.map.TryGetValue(key, out var t))
            {
                op(t);
                any = true;
            }
        }

        if (any && recorder != null)
            recorder.MarkDirtySelected();
    }

    // ====== Move ======
    public void MoveXPlus()  => ApplyToSelected(t => t.localPosition += new Vector3(+moveStep, 0, 0));
    public void MoveXMinus() => ApplyToSelected(t => t.localPosition += new Vector3(-moveStep, 0, 0));
    public void MoveYPlus()  => ApplyToSelected(t => t.localPosition += new Vector3(0, +moveStep, 0));
    public void MoveYMinus() => ApplyToSelected(t => t.localPosition += new Vector3(0, -moveStep, 0));
    public void MoveZPlus()  => ApplyToSelected(t => t.localPosition += new Vector3(0, 0, +moveStep));
    public void MoveZMinus() => ApplyToSelected(t => t.localPosition += new Vector3(0, 0, -moveStep));

    // ====== Rotate ======
    public void RotYawPlus()  => ApplyToSelected(t => t.localEulerAngles += new Vector3(0, +rotateStep, 0));
    public void RotYawMinus() => ApplyToSelected(t => t.localEulerAngles += new Vector3(0, -rotateStep, 0));
    public void RotPitchPlus()=> ApplyToSelected(t => t.localEulerAngles += new Vector3(+rotateStep, 0, 0));
    public void RotPitchMinus()=>ApplyToSelected(t => t.localEulerAngles += new Vector3(-rotateStep, 0, 0));
    public void RotRollPlus() => ApplyToSelected(t => t.localEulerAngles += new Vector3(0, 0, +rotateStep));
    public void RotRollMinus()=> ApplyToSelected(t => t.localEulerAngles += new Vector3(0, 0, -rotateStep));

    // ====== UI Slider 回调 ======
    public void SetMoveStep(float v) => moveStep = Mathf.Clamp(v, 0.001f, 1f);
    public void SetRotateStep(float v)=> rotateStep = Mathf.Clamp(v, 0.5f, 45f);
    public void SetDefaultDuration(float v)
    {
        if (recorder != null) recorder.defaultDuration = Mathf.Clamp(v, 0.1f, 5f);
    }
}
