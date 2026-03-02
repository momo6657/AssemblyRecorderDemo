using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public static class InputCompat
{
    // 是否按下（这一帧）
    public static bool PointerDownThisFrame(out Vector2 pos)
    {
        pos = default;

#if ENABLE_INPUT_SYSTEM
        // Touch 优先
        if (Touchscreen.current != null)
        {
            var t = Touchscreen.current.primaryTouch;
            if (t.press.wasPressedThisFrame)
            {
                pos = t.position.ReadValue();
                return true;
            }
        }

        // Mouse（编辑器/PC）
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            pos = Mouse.current.position.ReadValue();
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                pos = t.position;
                return true;
            }
        }
        if (Input.GetMouseButtonDown(0))
        {
            pos = Input.mousePosition;
            return true;
        }
#endif
        return false;
    }

    // 单指拖动 delta（用于旋转）
    public static bool PrimaryDragDelta(out Vector2 delta)
    {
        delta = default;

#if ENABLE_INPUT_SYSTEM
        if (Touchscreen.current != null)
        {
            var t = Touchscreen.current.primaryTouch;
            if (t.press.isPressed)
            {
                delta = t.delta.ReadValue();
                return delta.sqrMagnitude > 0.01f;
            }
        }
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            delta = Mouse.current.delta.ReadValue();
            return delta.sqrMagnitude > 0.01f;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved)
            {
                delta = t.deltaPosition;
                return true;
            }
        }
        if (Input.GetMouseButton(0))
        {
            delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
            return delta.sqrMagnitude > 0.01f;
        }
#endif
        return false;
    }
}