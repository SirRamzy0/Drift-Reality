using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class RaceBootstrap : MonoBehaviour
{
    public static RaceBootstrap Instance { get; private set; }

    [System.Serializable]
    private struct OpponentClassCounts
    {
        public int Min;
        public int Max;

        public int Roll()
        {
            return Random.Range(Mathf.Min(Min, Max), Mathf.Max(Min, Max) + 1);
        }
    }

    private sealed class RaceParticipant
    {
        public string Name;
        public int PrefabIndex;
        public bool IsPlayer;
        public AiCarController.DriverClass DriverClass;
        public ArcadeCarController Car;
        public bool Finished;
        public int FinishPlace;
        public float Progress;
    }

    [Header("Cars")]
    [SerializeField] private Object[] carPrefabs;

    [Header("Tournament")]
    [SerializeField] private int initialCompetitorCount = 50;
    [SerializeField] private int firstRoundQualifierCount = 25;
    [SerializeField] private int secondRoundQualifierCount = 10;
    [SerializeField] private float resultDelay = 2.15f;
    [SerializeField] private string menuSceneName = "Menu";

    [Header("Race")]
    [SerializeField] private float spawnDistance = 9f;
    [SerializeField] private float startSpacing = 5.2f;

    [Header("Road Settings")]
    [SerializeField] private ProceduralRoadGenerator.Settings roadSettings = ProceduralRoadGenerator.Settings.Default;

    [Header("Guardrail Settings")]
    [SerializeField] private RoadGuardrailBuilder.Settings guardrailSettings = RoadGuardrailBuilder.Settings.Default;

    [Header("Camera Settings")]
    [SerializeField] private FollowCamera.Settings cameraSettings = FollowCamera.Settings.Default;

    [Header("Car Handling Settings")]
    [SerializeField] private CarHandlingProfile playerHandlingProfile;
    [SerializeField] private CarHandlingProfile.Settings playerHandling = CarHandlingProfile.Settings.Balanced;
    [SerializeField] private CarHandlingProfile[] opponentHandlingProfiles;
    [SerializeField] private CarHandlingProfile.Settings opponentHandling = CarHandlingProfile.Settings.Balanced;
    [SerializeField] private bool generateOpponentVariations = true;

    [Header("Opponent Mix")]
    [SerializeField] private OpponentClassCounts leaderCount = new OpponentClassCounts { Min = 4, Max = 9 };
    [SerializeField] private OpponentClassCounts packCount = new OpponentClassCounts { Min = 24, Max = 36 };
    [SerializeField] private OpponentClassCounts wildcardCount = new OpponentClassCounts { Min = 5, Max = 12 };

    private const int FinalRoundIndex = 3;

    private readonly List<RaceParticipant> allParticipants = new List<RaceParticipant>();
    private readonly List<RaceParticipant> activeParticipants = new List<RaceParticipant>();
    private readonly List<RaceParticipant> liveOrder = new List<RaceParticipant>();
    private readonly List<ArcadeCarController> finishOrder = new List<ArcadeCarController>();
    private readonly HashSet<ArcadeCarController> finishedCars = new HashSet<ArcadeCarController>();
    private readonly Dictionary<ArcadeCarController, RaceParticipant> participantByCar = new Dictionary<ArcadeCarController, RaceParticipant>();
    private readonly StringBuilder standingsBuilder = new StringBuilder(2048);

    private ProceduralRoadGenerator road;
    private Material roadMaterial;
    private Material shoulderMaterial;
    private Material markerMaterial;
    private Material guardrailMaterial;
    private CarHandlingProfile.Settings resolvedPlayerHandling;
    private RaceParticipant playerParticipant;
    private List<RaceParticipant> pendingNextRoundParticipants;
    private GameObject raceRoot;
    private GameObject playerCar;
    private Canvas resultCanvas;
    private Image resultFade;
    private Text resultText;
    private Canvas hudCanvas;
    private Text standingsText;
    private int levelIndex = 1;
    private int roundIndex = 1;
    private bool roundResolved;
    private float nextStandingsRefresh;

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
        Instance = this;
        roadMaterial = CreateMaterial("Road Asphalt", new Color(0.055f, 0.06f, 0.062f), 0.45f);
        shoulderMaterial = CreateMaterial("Road Edge", new Color(0.9f, 0.82f, 0.18f), 0.25f);
        markerMaterial = CreateMaterial("Race Marker", new Color(0.95f, 0.12f, 0.08f), 0.15f);
        guardrailMaterial = CreateMaterial("Road Guardrail", new Color(0.58f, 0.6f, 0.58f), 0.32f);

        InitializeTournament();
        StartRound();
        SetupLighting();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextStandingsRefresh || roundResolved)
        {
            return;
        }

        nextStandingsRefresh = Time.unscaledTime + 0.15f;
        UpdateStandings(true);
        CheckCutoffElimination();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void NotifyFinished(ArcadeCarController car)
    {
        if (car == null || finishedCars.Contains(car) || !participantByCar.TryGetValue(car, out RaceParticipant participant))
        {
            return;
        }

        finishedCars.Add(car);
        finishOrder.Add(car);
        participant.Finished = true;
        participant.FinishPlace = finishOrder.Count;
        participant.Progress = road != null ? road.TotalLength + activeParticipants.Count - participant.FinishPlace : participant.Progress;
        UpdateStandings(true);

        if (participant.IsPlayer)
        {
            ResolvePlayerFinish(participant.FinishPlace);
        }
        else
        {
            CheckCutoffElimination();
        }
    }

    private void InitializeTournament()
    {
        allParticipants.Clear();
        activeParticipants.Clear();
        liveOrder.Clear();
        participantByCar.Clear();
        finishOrder.Clear();
        finishedCars.Clear();

        int total = Mathf.Max(2, initialCompetitorCount);
        AiCarController.DriverClass[] driverClasses = BuildOpponentMix(total - 1);

        playerParticipant = new RaceParticipant
        {
            Name = "\u0422\u044b",
            PrefabIndex = 0,
            IsPlayer = true,
            DriverClass = AiCarController.DriverClass.Pack
        };

        allParticipants.Add(playerParticipant);
        activeParticipants.Add(playerParticipant);

        for (int i = 1; i < total; i++)
        {
            AiCarController.DriverClass driverClass = driverClasses[Mathf.Clamp(i - 1, 0, driverClasses.Length - 1)];
            RaceParticipant participant = new RaceParticipant
            {
                Name = GetOpponentName(driverClass, i),
                PrefabIndex = i,
                IsPlayer = false,
                DriverClass = driverClass
            };

            allParticipants.Add(participant);
            activeParticipants.Add(participant);
        }
    }

    private void StartRound()
    {
        Time.timeScale = 1f;
        roundResolved = false;
        pendingNextRoundParticipants = null;
        finishOrder.Clear();
        finishedCars.Clear();
        participantByCar.Clear();

        for (int i = 0; i < activeParticipants.Count; i++)
        {
            RaceParticipant participant = activeParticipants[i];
            participant.Car = null;
            participant.Finished = false;
            participant.FinishPlace = 0;
            participant.Progress = 0f;
        }

        if (raceRoot != null)
        {
            Destroy(raceRoot);
        }

        raceRoot = new GameObject("Round " + roundIndex + " Level " + levelIndex);
        BuildRoad();
        BuildMarkers();
        SpawnRaceParticipants();
        SetupCamera(playerCar.transform);
        EnsureRaceHud();
        hudCanvas.gameObject.SetActive(true);
        UpdateStandings(true);

        if (resultCanvas != null)
        {
            resultCanvas.gameObject.SetActive(false);
        }
    }

    private void BuildRoad()
    {
        GameObject roadObject = new GameObject("Procedural Road");
        roadObject.transform.SetParent(raceRoot.transform, false);
        road = roadObject.AddComponent<ProceduralRoadGenerator>();
        ProceduralRoadGenerator.Settings settings = roadSettings;
        settings.Seed += levelIndex - 1;
        road.Configure(settings, roadMaterial, shoulderMaterial);
        road.Generate();

        RoadGuardrailBuilder guardrails = roadObject.AddComponent<RoadGuardrailBuilder>();
        guardrails.Generate(road, guardrailMaterial, settings.Seed, guardrailSettings);
    }

    private void SpawnRaceParticipants()
    {
        int laneCount = 9;
        float laneStep = road.RoadWidth / 10f;
        int rows = Mathf.CeilToInt(activeParticipants.Count / (float)laneCount);
        float frontDistance = Mathf.Max(spawnDistance, 8f + rows * startSpacing);

        for (int i = 0; i < activeParticipants.Count; i++)
        {
            RaceParticipant participant = activeParticipants[i];
            int row = i / laneCount;
            int lane = i % laneCount;
            float laneOffset = (lane - (laneCount - 1) * 0.5f) * laneStep;
            float distance = Mathf.Max(4f, frontDistance - row * startSpacing);
            GameObject carObject = SpawnCar(participant, distance, laneOffset);

            if (participant.IsPlayer)
            {
                playerCar = carObject;
            }
        }
    }

    private GameObject SpawnCar(RaceParticipant participant, float distance, float laneOffset)
    {
        Pose pose = road.GetPoseAtDistance(distance, laneOffset);
        GameObject root = new GameObject(participant.Name);
        root.transform.SetParent(raceRoot.transform, false);
        root.transform.SetPositionAndRotation(pose.position + Vector3.up * 0.18f, pose.rotation);

        bool hasVisual = false;
        Transform visualRoot = null;
        GameObject prefab = ResolveCarPrefab(participant.PrefabIndex);
        if (prefab != null)
        {
            GameObject visual = Instantiate(prefab, root.transform);
            visual.name = prefab.name + " Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visualRoot = visual.transform;
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
            visualRoot = fallback.transform;
        }

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = participant.IsPlayer ? 920f : 880f;

        root.AddComponent<BoxCollider>();
        ArcadeCarController car = root.AddComponent<ArcadeCarController>();
        car.Initialize(road, participant.IsPlayer);
        car.ApplyHandling(ResolveHandling(participant));

        VehicleVisualEffects visualEffects = visualRoot != null ? visualRoot.GetComponentInChildren<VehicleVisualEffects>() : null;
        if (visualEffects == null)
        {
            visualEffects = root.AddComponent<VehicleVisualEffects>();
        }

        visualEffects.Initialize(car, visualRoot);

        participant.Car = car;
        participantByCar[car] = participant;

        if (!participant.IsPlayer)
        {
            AiCarController ai = root.AddComponent<AiCarController>();
            ai.ConfigureDriverClass(participant.DriverClass);
            ai.Initialize(road, laneOffset);
        }

        return root;
    }

    private AiCarController.DriverClass[] BuildOpponentMix(int targetCount)
    {
        if (targetCount <= 0)
        {
            return new AiCarController.DriverClass[0];
        }

        int leaders = Mathf.Clamp(leaderCount.Roll(), 0, targetCount);
        int wildcards = Mathf.Clamp(wildcardCount.Roll(), 0, targetCount - leaders);
        int pack = Mathf.Clamp(packCount.Roll(), 0, targetCount - leaders - wildcards);

        while (leaders + pack + wildcards < targetCount)
        {
            float roll = Random.value;
            if (roll < 0.24f)
            {
                leaders++;
            }
            else if (roll < 0.42f)
            {
                wildcards++;
            }
            else
            {
                pack++;
            }
        }

        AiCarController.DriverClass[] result = new AiCarController.DriverClass[targetCount];
        int index = 0;

        for (int i = 0; i < leaders && index < result.Length; i++)
        {
            result[index++] = AiCarController.DriverClass.Leader;
        }

        for (int i = 0; i < pack && index < result.Length; i++)
        {
            result[index++] = AiCarController.DriverClass.Pack;
        }

        for (int i = 0; i < wildcards && index < result.Length; i++)
        {
            result[index++] = AiCarController.DriverClass.Wildcard;
        }

        for (int i = 1; i < result.Length; i++)
        {
            int swapIndex = Random.Range(0, i + 1);
            (result[i], result[swapIndex]) = (result[swapIndex], result[i]);
        }

        return result;
    }

    private CarHandlingProfile.Settings ResolveHandling(RaceParticipant participant)
    {
        if (participant.IsPlayer)
        {
            resolvedPlayerHandling = playerHandlingProfile != null ? playerHandlingProfile.RuntimeSettings : playerHandling.Validated();
            return resolvedPlayerHandling;
        }

        int opponentIndex = Mathf.Max(0, participant.PrefabIndex - 1);
        if (opponentHandlingProfiles != null && opponentHandlingProfiles.Length > 0)
        {
            CarHandlingProfile profile = opponentHandlingProfiles[opponentIndex % opponentHandlingProfiles.Length];
            if (profile != null)
            {
                return profile.RuntimeSettings.CreateOpponentVariant(opponentIndex + 1, resolvedPlayerHandling.MaxSpeed, participant.DriverClass);
            }
        }

        CarHandlingProfile.Settings baseHandling = opponentHandling.Validated();
        return generateOpponentVariations
            ? baseHandling.CreateOpponentVariant(opponentIndex + 1, resolvedPlayerHandling.MaxSpeed, participant.DriverClass)
            : baseHandling;
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

    private void ResolvePlayerFinish(int playerPlace)
    {
        if (roundResolved)
        {
            return;
        }

        if (roundIndex < FinalRoundIndex)
        {
            int qualifierCount = GetCurrentQualifierCount();
            bool qualified = playerPlace <= qualifierCount;
            if (qualified)
            {
                pendingNextRoundParticipants = BuildQualifiers(qualifierCount);
            }

            StartCoroutine(HandleRoundResult(playerPlace, qualified, false));
            return;
        }

        StartCoroutine(HandleFinalResult(playerPlace));
    }

    private void CheckCutoffElimination()
    {
        if (roundResolved || roundIndex >= FinalRoundIndex || playerParticipant == null || playerParticipant.Finished)
        {
            return;
        }

        int qualifierCount = GetCurrentQualifierCount();
        if (finishOrder.Count < qualifierCount)
        {
            return;
        }

        int place = Mathf.Max(qualifierCount + 1, GetCurrentPlayerPlace());
        StartCoroutine(HandleRoundResult(place, false, true));
    }

    private List<RaceParticipant> BuildQualifiers(int qualifierCount)
    {
        UpdateStandings(false);
        int count = Mathf.Clamp(qualifierCount, 1, liveOrder.Count);
        List<RaceParticipant> qualifiers = new List<RaceParticipant>(count);

        for (int i = 0; i < count; i++)
        {
            qualifiers.Add(liveOrder[i]);
        }

        if (!qualifiers.Contains(playerParticipant))
        {
            qualifiers[Mathf.Max(0, qualifiers.Count - 1)] = playerParticipant;
        }

        return qualifiers;
    }

    private int GetCurrentQualifierCount()
    {
        return roundIndex == 1 ? firstRoundQualifierCount : secondRoundQualifierCount;
    }

    private int GetCurrentPlayerPlace()
    {
        UpdateStandings(false);
        int index = liveOrder.IndexOf(playerParticipant);
        return index >= 0 ? index + 1 : activeParticipants.Count;
    }

    private void UpdateStandings(bool updateText)
    {
        liveOrder.Clear();

        for (int i = 0; i < activeParticipants.Count; i++)
        {
            RaceParticipant participant = activeParticipants[i];
            if (!participant.Finished && participant.Car != null && road != null)
            {
                ProceduralRoadGenerator.RoadSample sample = road.GetNearestSample(participant.Car.transform.position, out _, out _);
                participant.Progress = sample.Distance;
            }

            liveOrder.Add(participant);
        }

        liveOrder.Sort(CompareParticipants);

        if (updateText && standingsText != null)
        {
            BuildStandingsText();
        }
    }

    private int CompareParticipants(RaceParticipant a, RaceParticipant b)
    {
        if (a.Finished && b.Finished)
        {
            return a.FinishPlace.CompareTo(b.FinishPlace);
        }

        if (a.Finished)
        {
            return -1;
        }

        if (b.Finished)
        {
            return 1;
        }

        return b.Progress.CompareTo(a.Progress);
    }

    private void BuildStandingsText()
    {
        standingsBuilder.Length = 0;
        standingsBuilder.Append("\u0420\u0430\u0443\u043d\u0434 ");
        standingsBuilder.Append(roundIndex);
        standingsBuilder.Append(" / ");
        standingsBuilder.Append(FinalRoundIndex);
        standingsBuilder.Append("   ");
        standingsBuilder.Append("\u0443\u0447\u0430\u0441\u0442\u043d\u0438\u043a\u0438: ");
        standingsBuilder.Append(activeParticipants.Count);
        standingsBuilder.Append('\n');

        if (roundIndex < FinalRoundIndex)
        {
            standingsBuilder.Append("\u041f\u0440\u043e\u0445\u043e\u0434\u044f\u0442: ");
            standingsBuilder.Append(GetCurrentQualifierCount());
            standingsBuilder.Append('\n');
        }
        else
        {
            standingsBuilder.Append("\u0424\u0438\u043d\u0430\u043b\n");
        }

        for (int i = 0; i < liveOrder.Count; i++)
        {
            RaceParticipant participant = liveOrder[i];
            standingsBuilder.Append(i + 1 < 10 ? "0" : string.Empty);
            standingsBuilder.Append(i + 1);
            standingsBuilder.Append(". ");
            standingsBuilder.Append(participant.IsPlayer ? "> " : "  ");
            standingsBuilder.Append(participant.Name);

            if (participant.Finished)
            {
                standingsBuilder.Append("  F");
            }

            standingsBuilder.Append('\n');
        }

        standingsText.text = standingsBuilder.ToString();
    }

    private string GetOpponentName(AiCarController.DriverClass driverClass, int index)
    {
        switch (driverClass)
        {
            case AiCarController.DriverClass.Leader:
                return "Leader " + index.ToString("00");
            case AiCarController.DriverClass.Wildcard:
                return "Wildcard " + index.ToString("00");
            default:
                return "Racer " + index.ToString("00");
        }
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
        marker.transform.SetParent(raceRoot.transform, false);
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
        trigger.transform.SetParent(raceRoot.transform, false);
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

    private IEnumerator HandleRoundResult(int place, bool qualified, bool eliminatedByCutoff)
    {
        roundResolved = true;
        EnsureResultOverlay();
        resultCanvas.gameObject.SetActive(true);
        hudCanvas.gameObject.SetActive(false);

        if (qualified)
        {
            resultText.text = "\u0420\u0430\u0443\u043d\u0434 " + roundIndex + ": \u043c\u0435\u0441\u0442\u043e " + place + "\n\u041f\u0440\u043e\u0448\u0435\u043b \u0434\u0430\u043b\u044c\u0448\u0435";
        }
        else
        {
            resultText.text = eliminatedByCutoff
                ? "\u0422\u044b \u0432\u044b\u0431\u044b\u043b \u0432 \u044d\u0442\u043e\u043c \u0440\u0430\u0443\u043d\u0434\u0435\n\u043c\u0435\u0441\u0442\u043e " + place
                : "\u0422\u044b \u0432\u044b\u0431\u044b\u043b \u0432 \u044d\u0442\u043e\u043c \u0440\u0430\u0443\u043d\u0434\u0435\n\u043c\u0435\u0441\u0442\u043e " + place;
        }

        yield return FadeResultIn();

        if (!qualified)
        {
            yield return new WaitForSecondsRealtime(resultDelay);
            Time.timeScale = 1f;
            SceneManager.LoadScene(menuSceneName);
            yield break;
        }

        yield return new WaitForSecondsRealtime(resultDelay);

        roundIndex++;
        levelIndex++;
        activeParticipants.Clear();
        activeParticipants.AddRange(pendingNextRoundParticipants);
        Time.timeScale = 1f;
        resultCanvas.gameObject.SetActive(false);
        StartRound();
    }

    private IEnumerator HandleFinalResult(int place)
    {
        roundResolved = true;
        EnsureResultOverlay();
        resultCanvas.gameObject.SetActive(true);
        hudCanvas.gameObject.SetActive(false);
        resultText.text = place == 1
            ? "\u0422\u044b \u0437\u0430\u043d\u044f\u043b 1 \u043c\u0435\u0441\u0442\u043e\n\u041f\u043e\u0437\u0434\u0440\u0430\u0432\u043b\u044f\u0435\u043c!"
            : "\u0422\u044b \u0437\u0430\u043d\u044f\u043b " + place + " \u043c\u0435\u0441\u0442\u043e";

        yield return FadeResultIn();

        if (place > 1)
        {
            yield return new WaitForSecondsRealtime(resultDelay);
            Time.timeScale = 1f;
            SceneManager.LoadScene(menuSceneName);
        }
    }

    private IEnumerator FadeResultIn()
    {
        float duration = 1.35f;
        float elapsed = 0f;
        Color fadeColor = Color.black;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Time.timeScale = Mathf.Lerp(1f, 0.12f, t);
            fadeColor.a = Mathf.Lerp(0f, 0.78f, t);
            resultFade.color = fadeColor;
            resultText.color = new Color(1f, 1f, 1f, t);
            yield return null;
        }
    }

    private void EnsureRaceHud()
    {
        if (hudCanvas != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("Race HUD Canvas");
        hudCanvas = canvasObject.AddComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 30;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject panelObject = new GameObject("Standings Panel");
        panelObject.transform.SetParent(canvasObject.transform, false);
        Image panel = panelObject.AddComponent<Image>();
        panel.color = new Color(0f, 0f, 0f, 0.48f);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(1f, 0.03f);
        panelRect.anchorMax = new Vector2(1f, 0.97f);
        panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.sizeDelta = new Vector2(245f, 0f);
        panelRect.anchoredPosition = new Vector2(-12f, 0f);

        GameObject textObject = new GameObject("Standings Text");
        textObject.transform.SetParent(panelObject.transform, false);
        standingsText = textObject.AddComponent<Text>();
        standingsText.alignment = TextAnchor.UpperLeft;
        standingsText.fontSize = 13;
        standingsText.lineSpacing = 0.86f;
        standingsText.color = Color.white;
        standingsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (standingsText.font == null)
        {
            standingsText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        RectTransform textRect = standingsText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 8f);
        textRect.offsetMax = new Vector2(-8f, -8f);
    }

    private void EnsureResultOverlay()
    {
        if (resultCanvas != null)
        {
            resultFade.color = new Color(0f, 0f, 0f, 0f);
            resultText.color = new Color(1f, 1f, 1f, 0f);
            return;
        }

        GameObject canvasObject = new GameObject("Race Result Canvas");
        resultCanvas = canvasObject.AddComponent<Canvas>();
        resultCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        resultCanvas.sortingOrder = 100;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject fadeObject = new GameObject("Fade");
        fadeObject.transform.SetParent(canvasObject.transform, false);
        resultFade = fadeObject.AddComponent<Image>();
        resultFade.color = new Color(0f, 0f, 0f, 0f);
        RectTransform fadeRect = resultFade.rectTransform;
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.offsetMin = Vector2.zero;
        fadeRect.offsetMax = Vector2.zero;

        GameObject textObject = new GameObject("Result Text");
        textObject.transform.SetParent(canvasObject.transform, false);
        resultText = textObject.AddComponent<Text>();
        resultText.alignment = TextAnchor.MiddleCenter;
        resultText.fontSize = 54;
        resultText.fontStyle = FontStyle.Bold;
        resultText.color = new Color(1f, 1f, 1f, 0f);
        resultText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (resultText.font == null)
        {
            resultText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        RectTransform textRect = resultText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        resultCanvas.gameObject.SetActive(false);
    }
}
