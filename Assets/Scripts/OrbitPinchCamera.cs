using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.EnhancedTouch;
using TouchET = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhaseET = UnityEngine.InputSystem.TouchPhase;
#endif

public class OrbitPinchCamera : MonoBehaviour
{
    [Header("Target & Camera")]
    public Transform target;                // ImportedModel（轨道中心 fallback）
    public Transform cameraTransform;       // Main Camera

    [Header("UI Guard")]
    public bool ignoreInputOverUI = true;

    [Header("Rotate")]
    public float rotateSpeed = 0.2f;
    public float minPitch = -20f;
    public float maxPitch = 80f;

    [Header("Zoom")]
    public float zoomSpeed = 0.02f;
    public float minDistance = 1f;
    public float maxDistance = 20f;

    [Header("Orbit Center (Dynamic)")]
    [Tooltip("是否启用动态轨道中心")]
    public bool useDynamicOrbitCenter = true;
    [Tooltip("平滑过渡速度，0 = 瞬间切换")]
    public float orbitCenterSmoothSpeed = 5f;
    [Tooltip("选中零件时，轨道中心相对零件中心的垂直偏移")]
    public float selectionVerticalOffset = 0f;

    float yaw = 0f;
    float pitch = 20f;
    float distance = 6f;

    Vector2 lastPos;
    bool ignoreDrag = false;

    // 动态轨道中心
    Vector3 _targetOrbitCenter;     // 目标中心（外部设置）
    Vector3 _currentOrbitCenter;    // 当前插值中心
    bool _orbitCenterSet = false;   // 是否已被外部设置过
    Transform _followTarget;        // 当前选中的零件

    void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        EnhancedTouchSupport.Enable();
#endif
    }

    void Start()
    {
        AutoFindCamera();
        InitOrbitCenter();
        if (cameraTransform != null) UpdateCam();
    }

    void InitOrbitCenter()
    {
        // 初始中心：target 位置（兼容旧行为）
        Vector3 initial = target != null ? target.position : Vector3.zero;
        _targetOrbitCenter = initial;
        _currentOrbitCenter = initial;
    }

    void AutoFindCamera()
    {
        if (cameraTransform != null) return;

        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
            return;
        }

        var c = GetComponentInChildren<Camera>(true);
        if (c != null) cameraTransform = c.transform;
    }

    void Update()
    {
        AutoFindCamera();
        if (cameraTransform == null) return;

        if (_followTarget != null)
            _targetOrbitCenter = _followTarget.position + Vector3.up * selectionVerticalOffset;

        // 平滑过渡轨道中心
        if (useDynamicOrbitCenter && _orbitCenterSet)
        {
            if (orbitCenterSmoothSpeed > 0f)
                _currentOrbitCenter = Vector3.Lerp(_currentOrbitCenter, _targetOrbitCenter,
                    Time.deltaTime * orbitCenterSmoothSpeed);
            else
                _currentOrbitCenter = _targetOrbitCenter;

            UpdateCam();
        }
        else if (target == null)
        {
            return;
        }

#if ENABLE_INPUT_SYSTEM
        HandleInput_NewInputSystem();
#else
        HandleInput_OldInput();
#endif
    }

    /// <summary>
    /// 设置动态轨道中心（手动指定中心点）
    /// </summary>
    public void SetOrbitCenter(Vector3 worldCenter)
    {
        _followTarget = null;
        _targetOrbitCenter = worldCenter;
        if (!_orbitCenterSet)
            _currentOrbitCenter = worldCenter;
        _orbitCenterSet = true;
        Debug.Log($"[OrbitCamera] SetOrbitCenter: {worldCenter}");
    }

    /// <summary>
    /// 聚焦到指定零件，之后轨道中心会持续跟随该零件中心
    /// </summary>
    public void FocusOnTransform(Transform focusTarget)
    {
        if (focusTarget == null) return;
        _followTarget = focusTarget;
        _targetOrbitCenter = focusTarget.position + Vector3.up * selectionVerticalOffset;
        if (!_orbitCenterSet)
            _currentOrbitCenter = _targetOrbitCenter;
        _orbitCenterSet = true;
        Debug.Log($"[OrbitCamera] FocusOnTransform: {focusTarget.name}");
    }

    /// <summary>
    /// 清除零件跟随，回退到模型整体 target
    /// </summary>
    public void ClearFollowTarget()
    {
        _followTarget = null;
        ClearOrbitCenter();
    }

    /// <summary>
    /// 清除动态中心，回退到 target
    /// </summary>
    public void ClearOrbitCenter()
    {
        _orbitCenterSet = false;
        if (target != null)
        {
            _targetOrbitCenter = target.position;
            _currentOrbitCenter = target.position;
        }
    }

    /// <summary>
    /// 获取当前实际使用的轨道中心
    /// </summary>
    Vector3 GetOrbitCenter()
    {
        if (useDynamicOrbitCenter && _orbitCenterSet)
            return _currentOrbitCenter;

        return target != null ? target.position : Vector3.zero;
    }

#if ENABLE_INPUT_SYSTEM
    void HandleInput_NewInputSystem()
    {
        var touches = TouchET.activeTouches;
        int count = touches.Count;

        // 1 指旋转
        if (count == 1)
        {
            var t = touches[0];
            int pointerId = t.touchId;
            Vector2 pos = t.screenPosition;

            if (t.phase == TouchPhaseET.Began)
            {
                ignoreDrag = ignoreInputOverUI && EventSystem.current != null &&
                             EventSystem.current.IsPointerOverGameObject(pointerId);
                lastPos = pos;
            }

            if (ignoreDrag) return;

            if (t.phase == TouchPhaseET.Moved)
            {
                Vector2 delta = pos - lastPos;
                lastPos = pos;

                yaw += delta.x * rotateSpeed;
                pitch -= delta.y * rotateSpeed;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
                UpdateCam();
            }
        }

        // 2 指缩放
        if (count >= 2)
        {
            var t0 = touches[0];
            var t1 = touches[1];

            if (ignoreInputOverUI && EventSystem.current != null &&
                (EventSystem.current.IsPointerOverGameObject(t0.touchId) ||
                 EventSystem.current.IsPointerOverGameObject(t1.touchId)))
                return;

            Vector2 p0 = t0.screenPosition;
            Vector2 p1 = t1.screenPosition;

            Vector2 p0Prev = p0 - t0.delta;
            Vector2 p1Prev = p1 - t1.delta;

            float prevDist = Vector2.Distance(p0Prev, p1Prev);
            float currDist = Vector2.Distance(p0, p1);
            float delta = currDist - prevDist;

            distance -= delta * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            UpdateCam();
        }

#if UNITY_EDITOR
        HandleMouseInEditor();
#endif
    }
#endif

    void HandleInput_OldInput()
    {
        if (Input.touchCount == 1)
        {
            var t = Input.GetTouch(0);

            if (t.phase == TouchPhase.Began)
            {
                ignoreDrag = ignoreInputOverUI && EventSystem.current != null &&
                             EventSystem.current.IsPointerOverGameObject(t.fingerId);
                lastPos = t.position;
            }

            if (ignoreDrag) return;

            if (t.phase == TouchPhase.Moved)
            {
                Vector2 delta = t.position - lastPos;
                lastPos = t.position;

                yaw += delta.x * rotateSpeed;
                pitch -= delta.y * rotateSpeed;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
                UpdateCam();
            }
        }

        if (Input.touchCount == 2)
        {
            UnityEngine.Touch t0 = Input.GetTouch(0);
            UnityEngine.Touch t1 = Input.GetTouch(1);
            if (ignoreInputOverUI && EventSystem.current != null &&
                (EventSystem.current.IsPointerOverGameObject(t0.fingerId) ||
                 EventSystem.current.IsPointerOverGameObject(t1.fingerId)))
                return;

            Vector2 t0Prev = t0.position - t0.deltaPosition;
            Vector2 t1Prev = t1.position - t1.deltaPosition;

            float prevDist = Vector2.Distance(t0Prev, t1Prev);
            float currDist = Vector2.Distance(t0.position, t1.position);
            float delta = currDist - prevDist;

            distance -= delta * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            UpdateCam();
        }

#if UNITY_EDITOR
        HandleMouseInEditor();
#endif
    }

#if UNITY_EDITOR
    void HandleMouseInEditor()
    {
        if (Input.GetMouseButton(0))
        {
            if (ignoreInputOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");
            yaw += dx * 6f;
            pitch -= dy * 6f;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            UpdateCam();
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.001f)
        {
            distance -= scroll * 0.3f;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            UpdateCam();
        }
    }
#endif

    void UpdateCam()
    {
        if (cameraTransform == null) return;

        Vector3 center = GetOrbitCenter();

        // 绕中心旋转
        transform.position = center;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        cameraTransform.position = transform.position - transform.forward * distance;
        cameraTransform.LookAt(center);
    }

    public void ForceUpdateOnce()
    {
        AutoFindCamera();
        if (!_orbitCenterSet && target != null)
        {
            _targetOrbitCenter = target.position;
            _currentOrbitCenter = target.position;
        }
        UpdateCam();
    }
}
