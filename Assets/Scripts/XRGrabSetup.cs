using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class XRGrabSetup : MonoBehaviour
{
    [Tooltip("Root of imported model.")]
    public Transform modelRoot;

    [Header("Part Picking")]
    public bool useTopLevelChildAsPart = true;

    [Header("Collider")]
    public bool preferMeshCollider = true;
    public bool forceConvexMeshCollider = true;
    public bool replaceExistingColliders = true;
    public bool addColliderIfMissing = true;

    [Header("Physics")]
    public bool addRigidbodyIfMissing = true;
    public bool forceKinematicBody = true;

    [Header("Grab")]
    public XRBaseInteractable.MovementType movementType = XRBaseInteractable.MovementType.Kinematic;
    public bool useDynamicAttach = true;
    public bool throwOnDetach = false;

    public void Apply()
    {
        if (modelRoot == null)
        {
            Debug.LogWarning("[XRGrabSetup] modelRoot is null.");
            return;
        }

        var parts = CollectParts();
        if (parts.Count == 0)
        {
            Debug.LogWarning("[XRGrabSetup] no parts found under modelRoot.");
            return;
        }

        int applied = 0;
        int meshColliderCount = 0;
        int fallbackBoxCount = 0;

        foreach (var part in parts)
        {
            if (part == null) continue;
            var go = part.gameObject;

            var colliders = EnsurePartColliders(part, out int partMeshCount, out bool usedFallbackBox);
            meshColliderCount += partMeshCount;
            if (usedFallbackBox) fallbackBoxCount++;

            if (addRigidbodyIfMissing && !go.TryGetComponent<Rigidbody>(out _))
                go.AddComponent<Rigidbody>();

            if (go.TryGetComponent<Rigidbody>(out var body))
            {
                body.useGravity = false;
                body.isKinematic = forceKinematicBody || movementType == XRBaseInteractable.MovementType.Kinematic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = body.isKinematic
                    ? CollisionDetectionMode.ContinuousSpeculative
                    : CollisionDetectionMode.ContinuousDynamic;
            }

            if (!go.TryGetComponent<XRGrabInteractable>(out var grab))
                grab = go.AddComponent<XRGrabInteractable>();

            grab.movementType = movementType;
            grab.useDynamicAttach = useDynamicAttach;
            grab.throwOnDetach = throwOnDetach;

            grab.colliders.Clear();
            for (int i = 0; i < colliders.Count; i++)
            {
                var col = colliders[i];
                if (col == null || !col.enabled) continue;
                if (!grab.colliders.Contains(col))
                    grab.colliders.Add(col);
            }

            if (grab.colliders.Count == 0)
            {
                var allCols = part.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < allCols.Length; i++)
                {
                    var col = allCols[i];
                    if (col == null || !col.enabled) continue;
                    if (!grab.colliders.Contains(col))
                        grab.colliders.Add(col);
                }
            }

            applied++;
        }

        int interactorCount = UnityEngine.Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        ).Length;

        Debug.Log("[XRGrabSetup] Applied to parts=" + applied
                  + ", meshColliders=" + meshColliderCount
                  + ", boxFallbackParts=" + fallbackBoxCount
                  + ", sceneInteractors=" + interactorCount
                  + ", movementType=" + movementType
                  + ", forceKinematicBody=" + forceKinematicBody);
    }

    List<Collider> EnsurePartColliders(Transform part, out int meshColliderCount, out bool usedFallbackBox)
    {
        meshColliderCount = 0;
        usedFallbackBox = false;
        var result = new List<Collider>();

        if (replaceExistingColliders)
        {
            var existing = part.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null) existing[i].enabled = false;
            }
        }

        if (preferMeshCollider)
        {
            AddMeshColliders(part, result, ref meshColliderCount);
        }

        if (result.Count == 0 && addColliderIfMissing)
        {
            var box = AddFittedBoxCollider(part);
            if (box != null)
            {
                box.enabled = true;
                result.Add(box);
                usedFallbackBox = true;
            }
        }

        if (result.Count == 0)
        {
            var existing = part.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                var col = existing[i];
                if (col == null) continue;
                col.enabled = true;
                result.Add(col);
            }
        }

        return result;
    }

    void AddMeshColliders(Transform part, List<Collider> collector, ref int meshColliderCount)
    {
        var seenGo = new HashSet<int>();

        var meshFilters = part.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            var mf = meshFilters[i];
            if (mf == null || mf.sharedMesh == null) continue;

            var go = mf.gameObject;
            if (!seenGo.Add(go.GetInstanceID())) continue;

            var mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();

            mc.sharedMesh = mf.sharedMesh;
            mc.convex = forceConvexMeshCollider;
            mc.enabled = true;
            collector.Add(mc);
            meshColliderCount++;
        }

        var skinned = part.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            var smr = skinned[i];
            if (smr == null || smr.sharedMesh == null) continue;

            var go = smr.gameObject;
            if (!seenGo.Add(go.GetInstanceID())) continue;

            var mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();

            mc.sharedMesh = smr.sharedMesh;
            mc.convex = forceConvexMeshCollider;
            mc.enabled = true;
            collector.Add(mc);
            meshColliderCount++;
        }
    }

    List<Transform> CollectParts()
    {
        var parts = new List<Transform>();
        var seen = new HashSet<int>();

        if (useTopLevelChildAsPart)
        {
            for (int i = 0; i < modelRoot.childCount; i++)
            {
                var child = modelRoot.GetChild(i);
                if (child == null) continue;
                if (child.GetComponentInChildren<Renderer>(true) == null) continue;
                if (seen.Add(child.GetInstanceID()))
                    parts.Add(child);
            }
        }

        if (parts.Count > 0) return parts;

        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;

            Transform part = r.transform;
            while (part.parent != null && part.parent != modelRoot)
                part = part.parent;

            if (seen.Add(part.GetInstanceID()))
                parts.Add(part);
        }

        return parts;
    }

    BoxCollider AddFittedBoxCollider(Transform part)
    {
        var renderers = part.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return part.gameObject.AddComponent<BoxCollider>();
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        var box = part.GetComponent<BoxCollider>();
        if (box == null) box = part.gameObject.AddComponent<BoxCollider>();

        box.center = part.InverseTransformPoint(b.center);

        var lossy = part.lossyScale;
        float sx = Mathf.Abs(lossy.x) > 1e-5f ? Mathf.Abs(lossy.x) : 1f;
        float sy = Mathf.Abs(lossy.y) > 1e-5f ? Mathf.Abs(lossy.y) : 1f;
        float sz = Mathf.Abs(lossy.z) > 1e-5f ? Mathf.Abs(lossy.z) : 1f;
        box.size = new Vector3(b.size.x / sx, b.size.y / sy, b.size.z / sz);
        return box;
    }
}
