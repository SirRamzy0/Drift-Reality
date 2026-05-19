using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class FollowCamera : MonoBehaviour
{
    [System.Serializable]
    public struct Settings
    {
        public Vector3 Offset;
        public float PositionSharpness;
        public float RotationSharpness;
        public float BaseFieldOfView;
        public float SpeedFieldOfView;

        public static Settings Default => new Settings
        {
            Offset = new Vector3(0f, 4.8f, -7.4f),
            PositionSharpness = 9f,
            RotationSharpness = 11f,
            BaseFieldOfView = 64f,
            SpeedFieldOfView = 7f
        };
    }

    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = Settings.Default.Offset;
    [SerializeField] private float positionSharpness = Settings.Default.PositionSharpness;
    [SerializeField] private float rotationSharpness = Settings.Default.RotationSharpness;
    [SerializeField] private float baseFieldOfView = Settings.Default.BaseFieldOfView;
    [SerializeField] private float speedFieldOfView = Settings.Default.SpeedFieldOfView;

    private Camera cameraComponent;
    private ArcadeCarController targetCar;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        targetCar = target != null ? target.GetComponent<ArcadeCarController>() : null;
    }

    public void ApplySettings(Settings settings)
    {
        offset = settings.Offset;
        positionSharpness = Mathf.Max(0.1f, settings.PositionSharpness);
        rotationSharpness = Mathf.Max(0.1f, settings.RotationSharpness);
        baseFieldOfView = Mathf.Clamp(settings.BaseFieldOfView, 35f, 95f);
        speedFieldOfView = Mathf.Clamp(settings.SpeedFieldOfView, 0f, 30f);

        if (cameraComponent != null)
        {
            cameraComponent.fieldOfView = baseFieldOfView;
        }
    }

    private void Awake()
    {
        cameraComponent = GetComponent<Camera>();
        cameraComponent.fieldOfView = baseFieldOfView;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 desiredPosition = target.TransformPoint(offset);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, 1f - Mathf.Exp(-positionSharpness * Time.deltaTime));

        Vector3 lookAt = target.position + Vector3.up * 1.35f + target.forward * 5f;
        Quaternion desiredRotation = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime));

        float speed01 = targetCar != null ? targetCar.NormalizedSpeed : 0f;
        cameraComponent.fieldOfView = Mathf.Lerp(cameraComponent.fieldOfView, baseFieldOfView + speedFieldOfView * speed01, 7f * Time.deltaTime);
    }
}
