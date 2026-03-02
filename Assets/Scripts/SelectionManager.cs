using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

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

    [Header("UI Guard")]
    public bool ignoreTapOverUI = true;

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

        if (ignoreTapOverUI && EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject(t.touchId))
            return;

        TryPick(t.screenPosition);
    }
#endif

    void HandleTap_OldInput()
    {
        if (Input.touchCount != 1) return;

        var t = Input.GetTouch(0);
        if (t.phase != TouchPhase.Began) return;

        if (ignoreTapOverUI && EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject(t.fingerId))
            return;

        TryPick(t.position);
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

        if (selected.Contains(key))
        {
            selected.Remove(key);
            Restore(t, key);
        }
        else
        {
            selected.Add(key);
            Highlight(t, key);
        }
    }

    void Highlight(Transform t, string key)
    {
        var r = t.GetComponent<Renderer>();
        if (r == null) return;

        if (!original.ContainsKey(key))
            original[key] = r.materials;

        if (highlightMat != null)
        {
            var mats = new Material[r.materials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = highlightMat;
            r.materials = mats;
        }
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