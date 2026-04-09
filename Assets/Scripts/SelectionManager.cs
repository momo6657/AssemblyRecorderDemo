using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.EnhancedTouch;
using TouchET = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhaseET = UnityEngine.InputSystem.TouchPhase;
#endif

public class SelectionManager : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;
    public Material highlightMat;
    public OrbitPinchCamera orbitCamera;
    public PhonePlaybackUIDocumentController playbackUiController;

    [Header("UI Guard")]
    public bool ignoreTapOverUI = true;

    [Header("Highlight")]
    [Range(0f, 1f)] public float highlightBlend = 0.18f;
    public Color highlightTint = new Color(0.85f, 0.92f, 1f, 1f);

    [Header("Runtime")]
    public Transform modelRoot;     // ImportedModel
    public ModelIndex index;        // Scene ModelIndex

    readonly HashSet<string> selected = new HashSet<string>();

    // 兼容旧脚本：TransformPanel / StepRecorder 还在用这个
    public IReadOnlyCollection<string> SelectedKeys => selected;
    public IReadOnlyCollection<string> Selected => selected;
    readonly Dictionary<string, Material[]> original = new Dictionary<string, Material[]>();

    void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        EnhancedTouchSupport.Enable();
#endif
    }

    void Start()
    {
        if (cam == null) cam = Camera.main;
        if (index == null) index = FindAnyObjectByType<ModelIndex>();
        if (orbitCamera == null) orbitCamera = FindAnyObjectByType<OrbitPinchCamera>();
        if (playbackUiController == null) playbackUiController = FindAnyObjectByType<PhonePlaybackUIDocumentController>();
    }

    public void BindModel(Transform root)
    {
        modelRoot = root;

        if (index == null) index = FindAnyObjectByType<ModelIndex>();
        if (index == null)
        {
            Debug.LogWarning("[SelectionManager] ModelIndex not found in scene");
            return;
        }

        // 避免重复 rebuild：如果 ImportManager 已经 SetModelRoot 了，这里就不再强制 rebuild
        if (index.modelRoot != modelRoot)
            index.SetModelRoot(modelRoot, rebuild: true);

        EnsureColliders(modelRoot);

        ClearSelection();
        if (orbitCamera == null) orbitCamera = FindAnyObjectByType<OrbitPinchCamera>();
        if (orbitCamera != null) orbitCamera.ClearFollowTarget();
        Debug.Log("[SelectionManager] Bound modelRoot=" + modelRoot.name + " parts=" + (index.map != null ? index.map.Count : 0));
    }

    void Update()
    {
        if (modelRoot == null || index == null) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

#if ENABLE_INPUT_SYSTEM
        HandleTap_NewInputSystem();
#else
        HandleTap_OldInput();
#endif

#if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
        {
            if (ignoreTapOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;
            TryPick(Input.mousePosition);
        }
#endif
    }

#if ENABLE_INPUT_SYSTEM
    void HandleTap_NewInputSystem()
    {
        var touches = TouchET.activeTouches;
        if (touches.Count != 1) return;

        var t = touches[0];
        if (t.phase != TouchPhaseET.Began) return;

        if (ignoreTapOverUI && IsTouchOverInteractiveUi(t.screenPosition, t.touchId))
            return;

        TryPick(t.screenPosition);
    }
#endif

    void HandleTap_OldInput()
    {
        if (Input.touchCount != 1) return;

        var t = Input.GetTouch(0);
        if (t.phase != TouchPhase.Began) return;

        if (ignoreTapOverUI && IsTouchOverInteractiveUi(t.position, t.fingerId))
            return;

        TryPick(t.position);
    }

    bool IsTouchOverInteractiveUi(Vector2 screenPosition, int pointerId)
    {
        if (playbackUiController == null) playbackUiController = FindAnyObjectByType<PhonePlaybackUIDocumentController>();
        if (playbackUiController != null)
            return playbackUiController.IsPointerOverInteractiveUi(screenPosition);

        return false;
    }

    void TryPick(Vector2 screenPos)
    {
        var ray = cam.ScreenPointToRay(screenPos);

        if (Physics.Raycast(ray, out var hit, 1000f))
        {
            var t = hit.collider != null ? hit.collider.transform : null;
            if (t == null) return;

            string key = index.BuildPath(t);
            if (string.IsNullOrEmpty(key)) return;

            Toggle(key);
        }
    }

    public void Toggle(string key)
    {
        if (index == null || index.map == null) return;
        if (!index.map.TryGetValue(key, out var t) || t == null) return;

        bool alreadySelected = selected.Contains(key);
        ClearSelection();

        if (alreadySelected)
        {
            if (orbitCamera != null)
                orbitCamera.ClearFollowTarget();
            return;
        }

        selected.Add(key);
        Highlight(t, key);

        if (orbitCamera == null) orbitCamera = FindAnyObjectByType<OrbitPinchCamera>();
        if (orbitCamera != null)
            orbitCamera.FocusOnTransform(t);
    }

    void Highlight(Transform t, string key)
    {
        var r = t.GetComponent<Renderer>();
        if (r == null) return;

        if (!original.ContainsKey(key))
            original[key] = r.materials;

        var sourceMats = original[key];
        var mats = new Material[sourceMats.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            var src = sourceMats[i];
            if (src == null) continue;

            mats[i] = new Material(src);
            if (mats[i].HasProperty("_BaseColor"))
            {
                Color baseColor = mats[i].GetColor("_BaseColor");
                mats[i].SetColor("_BaseColor", Color.Lerp(baseColor, highlightTint, highlightBlend));
            }
            else if (mats[i].HasProperty("_Color"))
            {
                Color baseColor = mats[i].GetColor("_Color");
                mats[i].SetColor("_Color", Color.Lerp(baseColor, highlightTint, highlightBlend));
            }
        }
        r.materials = mats;
    }

    void Restore(Transform t, string key)
    {
        var r = t.GetComponent<Renderer>();
        if (r == null) return;

        if (original.TryGetValue(key, out var mats))
            r.materials = mats;
    }

    public void ClearSelection()
    {
        if (index != null && index.map != null)
        {
            foreach (var key in selected)
                if (index.map.TryGetValue(key, out var t) && t != null)
                    Restore(t, key);
        }
        selected.Clear();
        original.Clear();
    }

    void EnsureColliders(Transform root)
    {
        if (root == null) return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            var go = r.gameObject;
            if (go.GetComponent<Collider>() == null)
                go.AddComponent<BoxCollider>();
        }
    }
}