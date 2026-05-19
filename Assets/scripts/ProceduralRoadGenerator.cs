using System.Collections.Generic;
using UnityEngine;

public sealed class ProceduralRoadGenerator : MonoBehaviour
{
    private enum RoadPattern
    {
        ShortStraight,
        LongStraight,
        SmallTurn,
        StrongTurn,
        Hairpin
    }

    [System.Serializable]
    public struct Settings
    {
        public int Seed;
        public float TargetLength;
        public float RoadWidth;
        public float ControlPointSpacing;
        public int SamplesPerSegment;
        public float MaxSlopeAngle;

        public static Settings Default => new Settings
        {
            Seed = 4821,
            TargetLength = 2450f,
            RoadWidth = 30f,
            ControlPointSpacing = 28f,
            SamplesPerSegment = 7,
            MaxSlopeAngle = 4.5f
        };
    }

    public struct RoadSample
    {
        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Right;
        public float Distance;
    }

    [Header("Shape")]
    [SerializeField] private int seed = 4821;
    [SerializeField] private float targetLength = 2450f;
    [SerializeField] private float roadWidth = 30f;
    [SerializeField] private float controlPointSpacing = 28f;
    [SerializeField] private int samplesPerSegment = 7;
    [SerializeField] private float maxSlopeAngle = 4.5f;

    [Header("Visuals")]
    [SerializeField] private Material roadMaterial;
    [SerializeField] private Material shoulderMaterial;

    private readonly List<Vector3> controlPoints = new List<Vector3>();
    private readonly List<RoadSample> samples = new List<RoadSample>();
    private MeshCollider meshCollider;
    private float totalLength;
    private static PhysicsMaterial roadPhysicsMaterial;

    public IReadOnlyList<RoadSample> Samples => samples;
    public float TotalLength => totalLength;
    public float RoadWidth => roadWidth;

    public void Configure(int newSeed, Material newRoadMaterial, Material newShoulderMaterial)
    {
        seed = newSeed;
        roadMaterial = newRoadMaterial;
        shoulderMaterial = newShoulderMaterial;
    }

    public void Configure(Settings settings, Material newRoadMaterial, Material newShoulderMaterial)
    {
        seed = settings.Seed;
        targetLength = Mathf.Max(600f, settings.TargetLength);
        roadWidth = Mathf.Max(12f, settings.RoadWidth);
        controlPointSpacing = Mathf.Clamp(settings.ControlPointSpacing, 16f, 48f);
        samplesPerSegment = Mathf.Clamp(settings.SamplesPerSegment, 3, 12);
        maxSlopeAngle = Mathf.Clamp(settings.MaxSlopeAngle, 0f, 8f);
        roadMaterial = newRoadMaterial;
        shoulderMaterial = newShoulderMaterial;
    }

    public void Generate()
    {
        controlPoints.Clear();
        samples.Clear();

        BuildControlPoints();
        BuildSamples();
        BuildRoadMesh();
        BuildShoulders();
    }

    public bool IsRoadCollider(Collider collider)
    {
        return collider != null && collider == meshCollider;
    }

    public Pose GetPoseAtDistance(float distance, float laneOffset)
    {
        RoadSample sample = GetSampleAtDistance(distance);
        Vector3 position = sample.Position + sample.Right * laneOffset;
        Quaternion rotation = Quaternion.LookRotation(sample.Forward, Vector3.up);
        return new Pose(position, rotation);
    }

    public RoadSample GetSampleAtDistance(float distance)
    {
        if (samples.Count == 0)
        {
            return default;
        }

        float clamped = Mathf.Clamp(distance, 0f, totalLength);
        int low = 0;
        int high = samples.Count - 1;

        while (low < high)
        {
            int middle = (low + high) / 2;
            if (samples[middle].Distance < clamped)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return samples[Mathf.Clamp(low, 0, samples.Count - 1)];
    }

    public RoadSample GetNearestSample(Vector3 position, out float lateralOffset, out int nearestIndex)
    {
        nearestIndex = 0;
        lateralOffset = 0f;

        if (samples.Count == 0)
        {
            return default;
        }

        float bestSqrDistance = float.MaxValue;
        Vector2 query = new Vector2(position.x, position.z);

        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 samplePosition = samples[i].Position;
            Vector2 sample2D = new Vector2(samplePosition.x, samplePosition.z);
            float sqrDistance = (query - sample2D).sqrMagnitude;

            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                nearestIndex = i;
            }
        }

        RoadSample sample = samples[nearestIndex];
        lateralOffset = Vector3.Dot(position - sample.Position, sample.Right);
        return sample;
    }

    private void BuildControlPoints()
    {
        Random.InitState(seed);

        Vector3 position = Vector3.zero;
        float heading = 0f;
        float distance = 0f;
        float slope = 0f;
        int patternsSinceHairpin = 99;
        RoadPattern previousPattern = RoadPattern.LongStraight;

        controlPoints.Add(Vector3.back * controlPointSpacing);
        controlPoints.Add(position);

        AddPatternPoints(RoadPattern.LongStraight, 240f, 0f, ref position, ref heading, ref distance, ref slope);
        previousPattern = RoadPattern.LongStraight;

        while (distance < targetLength)
        {
            RoadPattern pattern = PickPattern(previousPattern, patternsSinceHairpin);
            float length = GetPatternLength(pattern);
            float angle = GetPatternAngle(pattern);

            AddPatternPoints(pattern, length, angle, ref position, ref heading, ref distance, ref slope);
            patternsSinceHairpin = pattern == RoadPattern.Hairpin ? 0 : patternsSinceHairpin + 1;
            previousPattern = pattern;
        }

        Vector3 forward = DirectionFromHeading(heading);
        controlPoints.Add(position + forward * controlPointSpacing);
        controlPoints.Add(position + forward * controlPointSpacing * 2f);
    }

    private RoadPattern PickPattern(RoadPattern previousPattern, int patternsSinceHairpin)
    {
        float roll = Random.value;

        if (patternsSinceHairpin >= 7 && previousPattern != RoadPattern.Hairpin && roll > 0.9f)
        {
            return RoadPattern.Hairpin;
        }

        if (previousPattern == RoadPattern.StrongTurn && roll < 0.35f)
        {
            return RoadPattern.LongStraight;
        }

        if (roll < 0.22f)
        {
            return RoadPattern.ShortStraight;
        }

        if (roll < 0.46f)
        {
            return RoadPattern.LongStraight;
        }

        if (roll < 0.74f)
        {
            return RoadPattern.SmallTurn;
        }

        return RoadPattern.StrongTurn;
    }

    private float GetPatternLength(RoadPattern pattern)
    {
        switch (pattern)
        {
            case RoadPattern.ShortStraight:
                return Random.Range(85f, 135f);
            case RoadPattern.LongStraight:
                return Random.Range(230f, 360f);
            case RoadPattern.SmallTurn:
                return Random.Range(120f, 180f);
            case RoadPattern.StrongTurn:
                return Random.Range(165f, 245f);
            case RoadPattern.Hairpin:
                return Random.Range(300f, 390f);
            default:
                return 140f;
        }
    }

    private float GetPatternAngle(RoadPattern pattern)
    {
        float side = Random.value < 0.5f ? -1f : 1f;

        switch (pattern)
        {
            case RoadPattern.SmallTurn:
                return side * Random.Range(30f, 45f);
            case RoadPattern.StrongTurn:
                return side * Random.Range(78f, 102f);
            case RoadPattern.Hairpin:
                return side * Random.Range(148f, 168f);
            default:
                return 0f;
        }
    }

    private void AddPatternPoints(
        RoadPattern pattern,
        float length,
        float angle,
        ref Vector3 position,
        ref float heading,
        ref float distance,
        ref float previousSlope)
    {
        int stepCount = Mathf.Max(2, Mathf.CeilToInt(length / controlPointSpacing));
        float stepLength = length / stepCount;
        float startHeading = heading;
        float targetSlope = Random.Range(-maxSlopeAngle, maxSlopeAngle);

        if (pattern == RoadPattern.Hairpin)
        {
            targetSlope *= 0.45f;
        }

        for (int i = 1; i <= stepCount; i++)
        {
            float t = i / (float)stepCount;
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            heading = startHeading + angle * smoothT;

            float slope = Mathf.Lerp(previousSlope, targetSlope, smoothT);
            Vector3 forward = DirectionFromHeading(heading);
            float heightDelta = Mathf.Tan(slope * Mathf.Deg2Rad) * stepLength;
            position += forward * stepLength;
            position.y += heightDelta;
            distance += stepLength;

            controlPoints.Add(position);
        }

        heading = startHeading + angle;
        previousSlope = targetSlope;
    }

    private Vector3 DirectionFromHeading(float heading)
    {
        float radians = heading * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)).normalized;
    }

    private void BuildSamples()
    {
        float accumulatedDistance = 0f;
        Vector3 previousPosition = EvaluateCatmullRom(1, 0f);

        int usableSegments = controlPoints.Count - 3;
        int sampleCount = usableSegments * samplesPerSegment + 1;

        for (int i = 0; i < sampleCount; i++)
        {
            float pathT = i / (float)(sampleCount - 1);
            float scaled = pathT * usableSegments;
            int segment = Mathf.Min(Mathf.FloorToInt(scaled), usableSegments - 1);
            float localT = scaled - segment;

            Vector3 position = EvaluateCatmullRom(segment + 1, localT);
            Vector3 forward = EvaluateCatmullRomTangent(segment + 1, localT).normalized;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            if (i > 0)
            {
                accumulatedDistance += Vector3.Distance(previousPosition, position);
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            samples.Add(new RoadSample
            {
                Position = position,
                Forward = forward,
                Right = right,
                Distance = accumulatedDistance
            });

            previousPosition = position;
        }

        totalLength = accumulatedDistance;
    }

    private void BuildRoadMesh()
    {
        MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        meshCollider = gameObject.GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }
        meshCollider.sharedMaterial = GetRoadPhysicsMaterial();

        int vertexCount = samples.Count * 2;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[(samples.Count - 1) * 6];

        float halfWidth = roadWidth * 0.5f;
        for (int i = 0; i < samples.Count; i++)
        {
            RoadSample sample = samples[i];
            vertices[i * 2] = transform.InverseTransformPoint(sample.Position - sample.Right * halfWidth);
            vertices[i * 2 + 1] = transform.InverseTransformPoint(sample.Position + sample.Right * halfWidth);

            float uvY = sample.Distance * 0.08f;
            uvs[i * 2] = new Vector2(0f, uvY);
            uvs[i * 2 + 1] = new Vector2(1f, uvY);
        }

        int triangleIndex = 0;
        for (int i = 0; i < samples.Count - 1; i++)
        {
            int leftA = i * 2;
            int rightA = i * 2 + 1;
            int leftB = (i + 1) * 2;
            int rightB = (i + 1) * 2 + 1;

            triangles[triangleIndex++] = leftA;
            triangles[triangleIndex++] = leftB;
            triangles[triangleIndex++] = rightA;
            triangles[triangleIndex++] = rightA;
            triangles[triangleIndex++] = leftB;
            triangles[triangleIndex++] = rightB;
        }

        Mesh mesh = new Mesh();
        mesh.name = "Procedural Road";
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        meshFilter.sharedMesh = mesh;
        meshCollider.sharedMesh = mesh;
        meshRenderer.sharedMaterial = roadMaterial;
    }

    private static PhysicsMaterial GetRoadPhysicsMaterial()
    {
        if (roadPhysicsMaterial != null)
        {
            return roadPhysicsMaterial;
        }

        roadPhysicsMaterial = new PhysicsMaterial("Arcade Road Frictionless")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        return roadPhysicsMaterial;
    }

    private void BuildShoulders()
    {
        if (shoulderMaterial == null)
        {
            return;
        }

        CreateShoulder("Left Edge", -1f);
        CreateShoulder("Right Edge", 1f);
    }

    private void CreateShoulder(string objectName, float side)
    {
        GameObject edge = new GameObject(objectName);
        edge.transform.SetParent(transform, false);

        MeshFilter meshFilter = edge.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = edge.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = shoulderMaterial;

        Vector3[] vertices = new Vector3[samples.Count * 2];
        Vector2[] uvs = new Vector2[samples.Count * 2];
        int[] triangles = new int[(samples.Count - 1) * 6];

        float inner = roadWidth * 0.5f;
        float outer = inner + 0.65f;

        for (int i = 0; i < samples.Count; i++)
        {
            RoadSample sample = samples[i];
            Vector3 innerPosition = sample.Position + sample.Right * inner * side + Vector3.up * 0.03f;
            Vector3 outerPosition = sample.Position + sample.Right * outer * side + Vector3.up * 0.08f;

            vertices[i * 2] = transform.InverseTransformPoint(innerPosition);
            vertices[i * 2 + 1] = transform.InverseTransformPoint(outerPosition);
            uvs[i * 2] = new Vector2(0f, sample.Distance * 0.12f);
            uvs[i * 2 + 1] = new Vector2(1f, sample.Distance * 0.12f);
        }

        int triangleIndex = 0;
        for (int i = 0; i < samples.Count - 1; i++)
        {
            int a = i * 2;
            int b = i * 2 + 1;
            int c = (i + 1) * 2;
            int d = (i + 1) * 2 + 1;

            if (side > 0f)
            {
                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = d;
            }
            else
            {
                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = d;
                triangles[triangleIndex++] = c;
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = objectName;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshFilter.sharedMesh = mesh;
    }

    private Vector3 EvaluateCatmullRom(int index, float t)
    {
        Vector3 p0 = controlPoints[index - 1];
        Vector3 p1 = controlPoints[index];
        Vector3 p2 = controlPoints[index + 1];
        Vector3 p3 = controlPoints[index + 2];

        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * ((2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private Vector3 EvaluateCatmullRomTangent(int index, float t)
    {
        Vector3 p0 = controlPoints[index - 1];
        Vector3 p1 = controlPoints[index];
        Vector3 p2 = controlPoints[index + 1];
        Vector3 p3 = controlPoints[index + 2];

        float t2 = t * t;

        return 0.5f * ((-p0 + p2) +
            2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t +
            3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t2);
    }
}
