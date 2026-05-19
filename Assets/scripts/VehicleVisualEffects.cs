using System.Collections.Generic;
using UnityEngine;

public sealed class VehicleVisualEffects : MonoBehaviour
{
    private sealed class WheelVisual
    {
        public Transform SteerPivot;
        public Transform SpinPivot;
        public bool Steers;
        public float Spin;
        public Quaternion BasePivotLocalRotation;
        public Quaternion BaseSpinPivotLocalRotation;
        public Vector3 SteerAxis;
        public Vector3 SpinAxis;
        public float Radius;
    }

    private sealed class BrakeLightVisual
    {
        public Material Material;
        public Color BaseColor;
        public Color EmissionColor;
    }

    [SerializeField] private ArcadeCarController car;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform frontLeftWheel;
    [SerializeField] private Transform frontRightWheel;
    [SerializeField] private Transform rearLeftWheel;
    [SerializeField] private Transform rearRightWheel;
    [SerializeField] private float fallbackWheelRadius = 0.38f;
    [SerializeField] private float maxSteerAngle = 28f;
    [SerializeField] private Vector3 wheelSpinAxis = Vector3.right;
    [SerializeField] private Vector3 wheelSteerAxis = Vector3.up;
    [SerializeField] private float wheelSpinDirection = 1f;
    [SerializeField] private float combinedWheelMeshMinHalfTrack = 1.15f;
    [SerializeField] private float combinedWheelMeshMinHalfWheelbase = 1.35f;
    [SerializeField] private float brakeLightOffMultiplier = 0.18f;
    [SerializeField] private float brakeEmissionMultiplier = 2.8f;

    private readonly List<WheelVisual> wheels = new List<WheelVisual>();
    private readonly List<BrakeLightVisual> brakeLights = new List<BrakeLightVisual>();
    private Material wheelProxyMaterial;

    public void Initialize(ArcadeCarController newCar, Transform newVisualRoot)
    {
        car = newCar;
        visualRoot = newVisualRoot;
        Rebuild();
    }

    private void Awake()
    {
        if (car == null)
        {
            car = GetComponent<ArcadeCarController>();
        }

        if (visualRoot == null && transform.childCount > 0)
        {
            visualRoot = transform.GetChild(0);
        }
    }

    private void Start()
    {
        if (wheels.Count == 0 && brakeLights.Count == 0)
        {
            Rebuild();
        }
    }

    private void LateUpdate()
    {
        if (car == null)
        {
            return;
        }

        UpdateWheels();
        UpdateBrakeLights();
    }

    private void Rebuild()
    {
        wheels.Clear();
        brakeLights.Clear();

        Transform root = visualRoot != null ? visualRoot : transform;
        ResolveNamedWheelReferences(root);
        if (RegisterExplicitWheels(root))
        {
            RegisterBrakeLights(root);
            SetBrakeLights(false);
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            Material[] materials = renderer.materials;
            bool hasWheelMaterial = false;
            bool hasNonWheelMaterial = false;

            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                {
                    continue;
                }

                string materialName = material.name.ToLowerInvariant();
                if (materialName.Contains("tyre") || materialName.Contains("wheel") || materialName.Contains("rim"))
                {
                    hasWheelMaterial = true;
                }
                else
                {
                    hasNonWheelMaterial = true;
                }

                if (materialName.Contains("back light") || materialName.Contains("red light"))
                {
                    RegisterBrakeLight(material);
                }
            }

            if (IsWheelTransform(renderer.transform) || (hasWheelMaterial && !hasNonWheelMaterial))
            {
                if (LooksLikeCombinedWheelMesh(renderer))
                {
                    RegisterCombinedWheelMesh(renderer, root);
                }
                else
                {
                    RegisterWheel(renderer, root);
                }
            }
        }

        SetBrakeLights(false);
    }

    private bool RegisterExplicitWheels(Transform root)
    {
        bool any = false;
        any |= RegisterWheelTransform(frontLeftWheel, root, true);
        any |= RegisterWheelTransform(frontRightWheel, root, true);
        any |= RegisterWheelTransform(rearLeftWheel, root, false);
        any |= RegisterWheelTransform(rearRightWheel, root, false);
        return any;
    }

    private bool RegisterWheelTransform(Transform wheelTransform, Transform root, bool steers)
    {
        if (wheelTransform == null)
        {
            return false;
        }

        for (int i = 0; i < wheels.Count; i++)
        {
            if (wheels[i].SpinPivot == wheelTransform || wheelTransform.IsChildOf(wheels[i].SpinPivot))
            {
                return false;
            }
        }

        Transform parent = wheelTransform.parent;
        Renderer renderer = wheelTransform.GetComponentInChildren<Renderer>();
        Vector3 wheelCenter = renderer != null ? renderer.bounds.center : wheelTransform.position;
        Quaternion parentRotation = parent != null ? parent.rotation : root.rotation;

        GameObject steerPivotObject = new GameObject(wheelTransform.name + " Steer Pivot");
        Transform steerPivot = steerPivotObject.transform;
        steerPivot.SetParent(parent, false);
        steerPivot.SetPositionAndRotation(wheelCenter, parentRotation);

        GameObject spinPivotObject = new GameObject(wheelTransform.name + " Spin Pivot");
        Transform spinPivot = spinPivotObject.transform;
        spinPivot.SetParent(steerPivot, false);
        spinPivot.localPosition = Vector3.zero;
        spinPivot.localRotation = Quaternion.identity;

        wheelTransform.SetParent(spinPivot, true);

        float radius = renderer != null ? DetectExplicitWheelRadius(renderer) : fallbackWheelRadius;
        wheels.Add(new WheelVisual
        {
            SteerPivot = steerPivot,
            SpinPivot = spinPivot,
            Steers = steers,
            BasePivotLocalRotation = steerPivot.localRotation,
            BaseSpinPivotLocalRotation = spinPivot.localRotation,
            SteerAxis = GetSafeAxis(wheelSteerAxis, Vector3.up),
            SpinAxis = GetSafeAxis(wheelSpinAxis, Vector3.right),
            Radius = radius
        });

        return true;
    }

    private void RegisterWheel(Renderer renderer, Transform root)
    {
        Transform wheelTransform = renderer.transform;
        for (int i = 0; i < wheels.Count; i++)
        {
            if (wheels[i].SpinPivot == wheelTransform || wheelTransform.IsChildOf(wheels[i].SpinPivot))
            {
                return;
            }
        }

        Vector3 localPosition = root.InverseTransformPoint(wheelTransform.position);
        bool steers = localPosition.z > 0f;
        Transform parent = wheelTransform.parent;
        Vector3 wheelCenter = renderer.bounds.center;
        Quaternion parentRotation = parent != null ? parent.rotation : root.rotation;

        GameObject steerPivotObject = new GameObject(wheelTransform.name + " Steer Pivot");
        Transform steerPivot = steerPivotObject.transform;
        steerPivot.SetParent(parent, false);
        steerPivot.SetPositionAndRotation(wheelCenter, parentRotation);

        GameObject spinPivotObject = new GameObject(wheelTransform.name + " Spin Pivot");
        Transform spinPivot = spinPivotObject.transform;
        spinPivot.SetParent(steerPivot, false);
        spinPivot.localPosition = Vector3.zero;
        spinPivot.localRotation = Quaternion.identity;

        wheelTransform.SetParent(spinPivot, true);

        Vector3 spinAxis = DetectWheelSpinAxis(renderer);
        float radius = DetectWheelRadius(renderer, spinAxis);
        wheels.Add(new WheelVisual
        {
            SteerPivot = steerPivot,
            SpinPivot = spinPivot,
            Steers = steers,
            BasePivotLocalRotation = steerPivot.localRotation,
            BaseSpinPivotLocalRotation = spinPivot.localRotation,
            SteerAxis = GetSafeAxis(wheelSteerAxis, Vector3.up),
            SpinAxis = spinAxis,
            Radius = radius
        });
    }

    private void RegisterCombinedWheelMesh(Renderer renderer, Transform root)
    {
        Bounds bounds = renderer.localBounds;
        float sideInset = Mathf.Min(bounds.extents.x * 0.18f, 0.42f);
        float frontInset = Mathf.Min(bounds.extents.z * 0.14f, 0.5f);
        float[] sideSigns = { -1f, 1f };
        float[] forwardSigns = { -1f, 1f };

        renderer.enabled = false;
        EnsureWheelProxyMaterial(renderer);

        for (int sideIndex = 0; sideIndex < sideSigns.Length; sideIndex++)
        {
            for (int forwardIndex = 0; forwardIndex < forwardSigns.Length; forwardIndex++)
            {
                Vector3 localPosition = bounds.center;
                localPosition.x += sideSigns[sideIndex] * Mathf.Max(0.05f, bounds.extents.x - sideInset);
                localPosition.z += forwardSigns[forwardIndex] * Mathf.Max(0.05f, bounds.extents.z - frontInset);
                Vector3 worldPosition = renderer.transform.TransformPoint(localPosition);
                float radius = Mathf.Max(fallbackWheelRadius, bounds.extents.y * Mathf.Abs(renderer.transform.lossyScale.y));
                float thickness = Mathf.Clamp(bounds.extents.x * 0.18f, 0.22f, 0.44f);

                RegisterProxyWheel(root, renderer.transform, worldPosition, radius, thickness);
            }
        }
    }

    private void RegisterProxyWheel(Transform root, Transform sourceTransform, Vector3 worldPosition, float radius, float thickness)
    {
        Vector3 localPosition = root.InverseTransformPoint(worldPosition);
        bool steers = localPosition.z > 0f;

        GameObject pivotObject = new GameObject("Wheel Proxy Steer Pivot");
        Transform pivot = pivotObject.transform;
        pivot.SetParent(sourceTransform.parent, false);
        pivot.SetPositionAndRotation(worldPosition, sourceTransform.parent != null ? sourceTransform.parent.rotation : root.rotation);

        GameObject wheelObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wheelObject.name = "Wheel Proxy";
        wheelObject.transform.SetParent(pivot, false);
        wheelObject.transform.localPosition = Vector3.zero;
        wheelObject.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        wheelObject.transform.localScale = new Vector3(radius * 2f, thickness * 0.5f, radius * 2f);
        Destroy(wheelObject.GetComponent<Collider>());

        MeshRenderer renderer = wheelObject.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = wheelProxyMaterial;
        }

        wheels.Add(new WheelVisual
        {
            SteerPivot = pivot,
            SpinPivot = wheelObject.transform,
            Steers = steers,
            BasePivotLocalRotation = pivot.localRotation,
            BaseSpinPivotLocalRotation = wheelObject.transform.localRotation,
            SteerAxis = GetSafeAxis(wheelSteerAxis, Vector3.up),
            SpinAxis = Vector3.up,
            Radius = radius
        });
    }

    private void RegisterBrakeLights(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].materials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                {
                    continue;
                }

                string materialName = material.name.ToLowerInvariant();
                if (materialName.Contains("back light") || materialName.Contains("red light"))
                {
                    RegisterBrakeLight(material);
                }
            }
        }
    }

    private void RegisterBrakeLight(Material material)
    {
        Color baseColor = GetMaterialColor(material);
        brakeLights.Add(new BrakeLightVisual
        {
            Material = material,
            BaseColor = baseColor,
            EmissionColor = baseColor.linear
        });
    }

    private void UpdateWheels()
    {
        float distance = car.CurrentSpeed * Time.deltaTime;
        float steerAngle = car.VisualSteer * maxSteerAngle;

        for (int i = 0; i < wheels.Count; i++)
        {
            WheelVisual wheel = wheels[i];
            if (wheel.SteerPivot == null || wheel.SpinPivot == null)
            {
                continue;
            }

            float radius = wheel.Radius > 0.01f ? wheel.Radius : fallbackWheelRadius;
            float spinDelta = radius > 0.01f ? distance / radius * Mathf.Rad2Deg * wheelSpinDirection : 0f;
            wheel.Spin += spinDelta;
            Quaternion steerRotation = wheel.Steers ? Quaternion.AngleAxis(steerAngle, GetSafeAxis(wheel.SteerAxis, Vector3.up)) : Quaternion.identity;
            Quaternion spinRotation = Quaternion.AngleAxis(wheel.Spin, GetSafeAxis(wheel.SpinAxis, Vector3.right));
            wheel.SteerPivot.localRotation = steerRotation * wheel.BasePivotLocalRotation;
            wheel.SpinPivot.localRotation = spinRotation * wheel.BaseSpinPivotLocalRotation;
        }
    }

    private void UpdateBrakeLights()
    {
        SetBrakeLights(car.IsBraking);
    }

    private void SetBrakeLights(bool braking)
    {
        for (int i = 0; i < brakeLights.Count; i++)
        {
            BrakeLightVisual light = brakeLights[i];
            if (light.Material == null)
            {
                continue;
            }

            Color color = braking ? light.BaseColor : light.BaseColor * brakeLightOffMultiplier;
            SetMaterialColor(light.Material, color);

            Color emission = braking ? light.EmissionColor * brakeEmissionMultiplier : Color.black;
            if (light.Material.HasProperty("_EmissionColor"))
            {
                light.Material.EnableKeyword("_EMISSION");
                light.Material.SetColor("_EmissionColor", emission);
            }
        }
    }

    private static bool IsWheelTransform(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            string name = current.name.ToLowerInvariant();
            if (name.Contains("wheel") || name.Contains("tyre") || name.Contains("tire") || name.Contains("rim"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void ResolveNamedWheelReferences(Transform root)
    {
        if (frontLeftWheel == null)
        {
            frontLeftWheel = FindChildByName(root, "Front Left Wheel");
        }

        if (frontRightWheel == null)
        {
            frontRightWheel = FindChildByName(root, "Front Right Wheel");
        }

        if (rearLeftWheel == null)
        {
            rearLeftWheel = FindChildByName(root, "Rear Left Wheel");
        }

        if (rearRightWheel == null)
        {
            rearRightWheel = FindChildByName(root, "Rear Right Wheel");
        }
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == childName)
            {
                return children[i];
            }
        }

        return null;
    }

    private bool LooksLikeCombinedWheelMesh(Renderer renderer)
    {
        Bounds bounds = renderer.localBounds;
        Vector3 scale = renderer.transform.lossyScale;
        float halfTrack = bounds.extents.x * Mathf.Abs(scale.x);
        float halfWheelbase = bounds.extents.z * Mathf.Abs(scale.z);
        return halfTrack >= combinedWheelMeshMinHalfTrack && halfWheelbase >= combinedWheelMeshMinHalfWheelbase;
    }

    private void EnsureWheelProxyMaterial(Renderer sourceRenderer)
    {
        if (wheelProxyMaterial != null)
        {
            return;
        }

        Material source = sourceRenderer.sharedMaterial;
        if (source != null)
        {
            wheelProxyMaterial = new Material(source);
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        wheelProxyMaterial = new Material(shader)
        {
            name = "Wheel Proxy Material",
            color = new Color(0.025f, 0.023f, 0.021f)
        };
    }

    private Vector3 DetectWheelSpinAxis(Renderer renderer)
    {
        Bounds bounds = renderer.localBounds;
        Vector3 extents = bounds.extents;

        if (extents.x <= extents.y && extents.x <= extents.z)
        {
            return Vector3.right;
        }

        if (extents.y <= extents.x && extents.y <= extents.z)
        {
            return Vector3.up;
        }

        return Vector3.forward;
    }

    private float DetectWheelRadius(Renderer renderer, Vector3 spinAxis)
    {
        Vector3 extents = renderer.localBounds.extents;
        Vector3 scale = renderer.transform.lossyScale;

        float radiusX = extents.x * Mathf.Abs(scale.x);
        float radiusY = extents.y * Mathf.Abs(scale.y);
        float radiusZ = extents.z * Mathf.Abs(scale.z);

        if (Mathf.Abs(spinAxis.x) > 0.5f)
        {
            return Mathf.Max(radiusY, radiusZ, fallbackWheelRadius);
        }

        if (Mathf.Abs(spinAxis.y) > 0.5f)
        {
            return Mathf.Max(radiusX, radiusZ, fallbackWheelRadius);
        }

        return Mathf.Max(radiusX, radiusY, fallbackWheelRadius);
    }

    private float DetectExplicitWheelRadius(Renderer renderer)
    {
        Vector3 extents = renderer.localBounds.extents;
        Vector3 scale = renderer.transform.lossyScale;
        float radiusX = extents.x * Mathf.Abs(scale.x);
        float radiusY = extents.y * Mathf.Abs(scale.y);
        float radiusZ = extents.z * Mathf.Abs(scale.z);

        if (Mathf.Abs(wheelSpinAxis.x) > 0.5f)
        {
            return Mathf.Max(radiusY, radiusZ, fallbackWheelRadius);
        }

        if (Mathf.Abs(wheelSpinAxis.y) > 0.5f)
        {
            return Mathf.Max(radiusX, radiusZ, fallbackWheelRadius);
        }

        return Mathf.Max(radiusX, radiusY, fallbackWheelRadius);
    }

    private static Vector3 GetSafeAxis(Vector3 axis, Vector3 fallback)
    {
        return axis.sqrMagnitude > 0.0001f ? axis.normalized : fallback;
    }

    private static Color GetMaterialColor(Material material)
    {
        if (material.HasProperty("_BaseColor"))
        {
            return material.GetColor("_BaseColor");
        }

        if (material.HasProperty("_Color"))
        {
            return material.GetColor("_Color");
        }

        return Color.white;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }
}
