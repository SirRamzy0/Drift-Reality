using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class RaceBootstrap : MonoBehaviour
{
    [Header("Cars")]
    [SerializeField] private Object[] carPrefabs;
    [SerializeField] private int opponentCount = 5;

    [Header("Race")]
    [SerializeField] private float spawnDistance = 9f;
    [SerializeField] private float startSpacing = 5.2f;

    [Header("Road Settings")]
    [SerializeField] private ProceduralRoadGenerator.Settings roadSettings = ProceduralRoadGenerator.Settings.Default;

    [Header("Camera Settings")]
    [SerializeField] private FollowCamera.Settings cameraSettings = FollowCamera.Settings.Default;

    [Header("Car Handling Settings")]
    [SerializeField] private CarHandlingProfile playerHandlingProfile;
    [SerializeField] private CarHandlingProfile.Settings playerHandling = CarHandlingProfile.Settings.Balanced;
    [SerializeField] private CarHandlingProfile[] opponentHandlingProfiles;
    [SerializeField] private CarHandlingProfile.Settings opponentHandling = CarHandlingProfile.Settings.Balanced;
    [SerializeField] private bool generateOpponentVariations = true;

    private ProceduralRoadGenerator road;
    private Material roadMaterial;
    private Material shoulderMaterial;
    private Material markerMaterial;

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
        roadMaterial = CreateMaterial("Road Asphalt", new Color(0.055f, 0.06f, 0.062f), 0.45f);
        shoulderMaterial = CreateMaterial("Road Edge", new Color(0.9f, 0.82f, 0.18f), 0.25f);
        markerMaterial = CreateMaterial("Race Marker", new Color(0.95f, 0.12f, 0.08f), 0.15f);

        BuildRoad();
        BuildMarkers();

        GameObject player = SpawnCar("Player", 0, spawnDistance, 0f, true);
        SpawnOpponents();
        SetupCamera(player.transform);
        SetupLighting();
    }

    private void BuildRoad()
    {
        GameObject roadObject = new GameObject("Procedural Road");
        road = roadObject.AddComponent<ProceduralRoadGenerator>();
        road.Configure(roadSettings, roadMaterial, shoulderMaterial);
        road.Generate();
    }

    private void SpawnOpponents()
    {
        int prefabCount = Mathf.Max(carPrefabs != null ? carPrefabs.Length : 0, ImportedCarPaths.Length);
        int count = Mathf.Clamp(opponentCount, 0, prefabCount > 0 ? prefabCount - 1 : opponentCount);
        float laneStep = road.RoadWidth / 10f;
        float[] lanes =
        {
            -laneStep * 3f,
            laneStep * 3f,
            -laneStep,
            laneStep,
            -laneStep * 4f,
            laneStep * 4f,
            -laneStep * 2f,
            laneStep * 2f
        };

        for (int i = 0; i < count; i++)
        {
            float distance = spawnDistance - ((i / 2) + 1) * startSpacing;
            float lane = lanes[i % lanes.Length];
            SpawnCar("Opponent " + (i + 1), i + 1, Mathf.Max(1.5f, distance), lane, false);
        }
    }

    private GameObject SpawnCar(string objectName, int prefabIndex, float distance, float laneOffset, bool isPlayer)
    {
        Pose pose = road.GetPoseAtDistance(distance, laneOffset);
        GameObject root = new GameObject(objectName);
        root.transform.SetPositionAndRotation(pose.position + Vector3.up * 0.18f, pose.rotation);

        bool hasVisual = false;
        GameObject prefab = ResolveCarPrefab(prefabIndex);
        if (prefab != null)
        {
            GameObject visual = Instantiate(prefab, root.transform);
            visual.name = prefab.name + " Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            hasVisual = true;
        }

        if (!hasVisual)
        {
            GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallback.name = "Fallback Car Visual";
            fallback.transform.SetParent(root.transform, false);
            fallback.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            fallback.transform.localScale = new Vector3(2f, 0.85f, 4f);
            Destroy(fallback.GetComponent<Collider>());
        }

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = isPlayer ? 920f : 880f;

        root.AddComponent<BoxCollider>();
        ArcadeCarController car = root.AddComponent<ArcadeCarController>();
        car.Initialize(road, isPlayer);
        car.ApplyHandling(ResolveHandling(prefabIndex, isPlayer));

        if (!isPlayer)
        {
            AiCarController ai = root.AddComponent<AiCarController>();
            ai.Initialize(road, laneOffset);
        }

        return root;
    }

    private CarHandlingProfile.Settings ResolveHandling(int prefabIndex, bool isPlayer)
    {
        if (isPlayer)
        {
            return playerHandlingProfile != null ? playerHandlingProfile.RuntimeSettings : playerHandling.Validated();
        }

        int opponentIndex = Mathf.Max(0, prefabIndex - 1);
        if (opponentHandlingProfiles != null && opponentHandlingProfiles.Length > 0)
        {
            CarHandlingProfile profile = opponentHandlingProfiles[opponentIndex % opponentHandlingProfiles.Length];
            if (profile != null)
            {
                return profile.RuntimeSettings;
            }
        }

        CarHandlingProfile.Settings baseHandling = opponentHandling.Validated();
        return generateOpponentVariations ? baseHandling.CreateOpponentVariant(opponentIndex + 1) : baseHandling;
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

    private void SetupCamera(Transform target)
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

        followCamera.SetTarget(target);
        followCamera.ApplySettings(cameraSettings);
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

    private void BuildMarkers()
    {
        CreateLineMarker("Start Line", 4f, new Color(0.05f, 0.95f, 0.45f));
        CreateLineMarker("Finish Line", road.TotalLength - 8f, new Color(0.95f, 0.12f, 0.08f));
        CreateFinishTrigger();
    }

    private void CreateLineMarker(string objectName, float distance, Color color)
    {
        Pose pose = road.GetPoseAtDistance(distance, 0f);
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = objectName;
        marker.transform.SetPositionAndRotation(pose.position + Vector3.up * 0.08f, pose.rotation);
        marker.transform.localScale = new Vector3(road.RoadWidth + 1.2f, 0.12f, 0.55f);

        Material material = CreateMaterial(objectName + " Material", color, 0.2f);
        marker.GetComponent<MeshRenderer>().sharedMaterial = material;
        Destroy(marker.GetComponent<Collider>());
    }

    private void CreateFinishTrigger()
    {
        Pose pose = road.GetPoseAtDistance(road.TotalLength - 8f, 0f);
        GameObject trigger = new GameObject("Finish Trigger");
        trigger.transform.SetPositionAndRotation(pose.position + Vector3.up * 1.8f, pose.rotation);

        BoxCollider collider = trigger.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(road.RoadWidth + 4f, 4f, 2.5f);

        trigger.AddComponent<FinishLineTrigger>();

        GameObject leftPillar = CreateFinishPrimitive("Finish Left Pillar", trigger.transform, new Vector3(-(road.RoadWidth * 0.5f + 1.2f), 0.7f, 0f), new Vector3(0.45f, 3.8f, 0.45f));
        GameObject rightPillar = CreateFinishPrimitive("Finish Right Pillar", trigger.transform, new Vector3(road.RoadWidth * 0.5f + 1.2f, 0.7f, 0f), new Vector3(0.45f, 3.8f, 0.45f));
        GameObject topBeam = CreateFinishPrimitive("Finish Top Beam", trigger.transform, new Vector3(0f, 2.6f, 0f), new Vector3(road.RoadWidth + 3f, 0.45f, 0.45f));

        leftPillar.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
        rightPillar.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
        topBeam.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
    }

    private GameObject CreateFinishPrimitive(string objectName, Transform parent, Vector3 localPosition, Vector3 localScale)
    {
        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        primitive.name = objectName;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localRotation = Quaternion.identity;
        primitive.transform.localScale = localScale;
        Destroy(primitive.GetComponent<Collider>());
        return primitive;
    }

    private Material CreateMaterial(string materialName, Color color, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.color = color;
        material.SetFloat("_Smoothness", smoothness);
        return material;
    }
}
