using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class FreeRideBootstrap : MonoBehaviour
{
    [Header("Car")]
    [SerializeField] private Object[] carPrefabs;
    [SerializeField] private CarHandlingProfile playerHandlingProfile;
    [SerializeField] private CarHandlingProfile.Settings playerHandling = CarHandlingProfile.Settings.Balanced;

    [Header("Track")]
    [SerializeField] private ManualTrackWaypoints manualTrack;
    [SerializeField] private float spawnDistance = 4f;

    [Header("Camera")]
    [SerializeField] private FollowCamera.Settings cameraSettings = FollowCamera.Settings.Default;

    [Header("Lighting")]
    [SerializeField] private bool setupLighting = true;

    private IRoadProvider roadProvider;
    private GameObject playerCar;

    private static readonly string[] ImportedCarPaths =
    {
        "Assets/Store InvoGames/Car Asset Pack for Arcade & Demolition Racing Games/Prefabs/Car 1.prefab",
        "Assets/Store InvoGames/Car Asset Pack for Arcade & Demolition Racing Games/Prefabs/Car 2.prefab",
        "Assets/Store InvoGames/Car Asset Pack for Arcade & Demolition Racing Games/Prefabs/Car 3.prefab",
        "Assets/Store InvoGames/Car Asset Pack for Arcade & Demolition Racing Games/Prefabs/Car 4.prefab",
        "Assets/Store InvoGames/Car Asset Pack for Arcade & Demolition Racing Games/Prefabs/Car 5.prefab",
        "Assets/Store InvoGames/Car Asset Pack for Arcade & Demolition Racing Games/Prefabs/Car 6.prefab",
        "Assets/Store InvoGames/Car Asset Pack for Arcade & Demolition Racing Games/Prefabs/Car 7.prefab",
        "Assets/Store InvoGames/Car Asset Pack for Arcade & Demolition Racing Games/Prefabs/Car 8.prefab",
        "Assets/Store InvoGames/Car Asset Pack for Arcade & Demolition Racing Games/Prefabs/Car 9.prefab",
        "Assets/Store InvoGames/Car Asset Pack for Arcade & Demolition Racing Games/Prefabs/Car 10.prefab"
    };

    private void Awake()
    {
        if (manualTrack == null)
        {
            Debug.LogError("FreeRideBootstrap: ManualTrackWaypoints не назначен. Добавь компонент ManualTrackWaypoints на объект с вейпоинтами и перетащи его в поле Manual Track.");
            return;
        }

        roadProvider = manualTrack;

        SpawnPlayerCar();
        SetupCamera();
        SetupInput();

        if (setupLighting)
        {
            SetupLighting();
        }
    }

    private void SpawnPlayerCar()
    {
        Pose spawnPose = roadProvider.GetPoseAtDistance(spawnDistance, 0f);

        GameObject root = new GameObject("Player Car");
        root.transform.SetPositionAndRotation(spawnPose.position + Vector3.up * 0.25f, spawnPose.rotation);

        GameObject prefab = ResolveCarPrefab(0);
        Transform visualRoot = null;

        if (prefab != null)
        {
            GameObject visual = Instantiate(prefab, root.transform);
            visual.name = prefab.name + " Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visualRoot = visual.transform;
        }
        else
        {
            GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallback.name = "Fallback Car Visual";
            fallback.transform.SetParent(root.transform, false);
            fallback.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            fallback.transform.localScale = new Vector3(2f, 0.85f, 4f);
            Destroy(fallback.GetComponent<Collider>());
            visualRoot = fallback.transform;
        }

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 920f;

        root.AddComponent<BoxCollider>();

        ArcadeCarController car = root.AddComponent<ArcadeCarController>();
        car.PlayerControlled = true;
        car.Initialize(roadProvider, true);
        car.ApplyHandling(playerHandlingProfile != null ? playerHandlingProfile.RuntimeSettings : playerHandling.Validated());

        VehicleVisualEffects visualEffects = visualRoot != null ? visualRoot.GetComponentInChildren<VehicleVisualEffects>() : null;
        if (visualEffects == null)
        {
            visualEffects = root.AddComponent<VehicleVisualEffects>();
        }

        visualEffects.Initialize(car, visualRoot);

        playerCar = root;
    }

    private void SetupCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        FollowCamera followCamera = camera.GetComponent<FollowCamera>();
        if (followCamera == null)
        {
            followCamera = camera.gameObject.AddComponent<FollowCamera>();
        }

        followCamera.SetTarget(playerCar.transform);
        followCamera.ApplySettings(cameraSettings);
    }

    private void SetupInput()
    {
        if (playerCar != null)
        {
            ArcadeCarController car = playerCar.GetComponent<ArcadeCarController>();
            if (car != null)
            {
                car.PlayerControlled = true;
            }
        }
    }

    private void SetupLighting()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 90f;
        RenderSettings.fogEndDistance = 520f;
        RenderSettings.fogColor = new Color(0.52f, 0.68f, 0.82f);

        Light sun = FindAnyObjectByType<Light>();
        if (sun != null)
        {
            sun.intensity = 2.35f;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        }
    }

    private GameObject ResolveCarPrefab(int prefabIndex)
    {
        int index = Mathf.Abs(prefabIndex);

        if (carPrefabs != null && carPrefabs.Length > 0)
        {
            Object referenced = carPrefabs[index % carPrefabs.Length];
            if (referenced is GameObject referencedGameObject)
            {
                return referencedGameObject;
            }

            if (referenced is Component referencedComponent)
            {
                return referencedComponent.gameObject;
            }
        }

#if UNITY_EDITOR
        string path = ImportedCarPaths[index % ImportedCarPaths.Length];
        GameObject loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (loaded != null)
        {
            return loaded;
        }
#endif

        return null;
    }

#if UNITY_EDITOR
    [ContextMenu("Auto Setup Manual Track")]
    private void AutoSetupManualTrack()
    {
        if (manualTrack == null)
        {
            manualTrack = FindAnyObjectByType<ManualTrackWaypoints>();
        }

        if (manualTrack != null)
        {
            manualTrack.BuildSamplesFromWaypoints();
        }
    }
#endif
}