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
    public Transform target;                // ImportedModel
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

    float yaw = 0f;
    float pitch = 20f;
    float distance = 6f;

    Vector2 lastPos;
    bool ignoreDrag = false;

    void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        EnhancedTouchSupport.Enable();
#endif
    }

    void Start()
    {
        AutoFindCamera();
        if (target != null && cameraTransform != null) UpdateCam();
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
        if (target == null) return;

        AutoFindCamera();
        if (cameraTransform == null) return;

#if ENABLE_INPUT_SYSTEM
        HandleInput_NewInputSystem();
#else
        HandleInput_OldInput();
#endif
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
        if (target == null || cameraTransform == null) return;

        transform.position = target.position;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        cameraTransform.position = transform.position - transform.forward * distance;
        cameraTransform.LookAt(target.position);
    }

    public void ForceUpdateOnce()
    {
        AutoFindCamera();
        UpdateCam();
    }
}