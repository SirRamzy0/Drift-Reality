using System.Collections.Generic;
using UnityEngine;

public sealed class ManualTrackWaypoints : MonoBehaviour, IRoadProvider
{
    [Header("Waypoints")]
    [SerializeField] private Transform waypointsParent;
    [SerializeField] private bool loopTrack = true;

    [Header("Settings")]
    [SerializeField] private float roadWidth = 30f;

    private readonly List<ProceduralRoadGenerator.RoadSample> samples = new List<ProceduralRoadGenerator.RoadSample>();
    private float totalLength;

    public IReadOnlyList<ProceduralRoadGenerator.RoadSample> Samples => samples;
    public float TotalLength => totalLength;
    public float RoadWidth => roadWidth;

    private void Awake()
    {
        BuildSamplesFromWaypoints();
    }

    public void BuildSamplesFromWaypoints()
    {
        samples.Clear();

        Transform source = waypointsParent != null ? waypointsParent : transform;
        List<Vector3> waypoints = new List<Vector3>();

        for (int i = 0; i < source.childCount; i++)
        {
            waypoints.Add(source.GetChild(i).position);
        }

        if (waypoints.Count < 2)
        {
            samples.Add(new ProceduralRoadGenerator.RoadSample
            {
                Position = transform.position,
                Forward = transform.forward,
                Right = transform.right,
                Distance = 0f
            });

            totalLength = 0f;
            return;
        }

        float accumulatedDistance = 0f;

        for (int i = 0; i < waypoints.Count; i++)
        {
            int nextIndex = (i + 1) % waypoints.Count;
            if (!loopTrack && i == waypoints.Count - 1)
            {
                break;
            }

            Vector3 a = waypoints[i];
            Vector3 b = waypoints[nextIndex];
            Vector3 direction = (b - a);
            float segmentLength = direction.magnitude;

            if (segmentLength < 0.001f)
            {
                continue;
            }

            Vector3 forward = direction.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            int subSteps = Mathf.Max(1, Mathf.CeilToInt(segmentLength / 2f));
            float stepSize = segmentLength / subSteps;

            for (int s = 0; s < subSteps; s++)
            {
                Vector3 position = a + forward * (stepSize * s);
                samples.Add(new ProceduralRoadGenerator.RoadSample
                {
                    Position = position,
                    Forward = forward,
                    Right = right,
                    Distance = accumulatedDistance
                });

                accumulatedDistance += stepSize;
            }
        }

        totalLength = accumulatedDistance;
    }

    public Pose GetPoseAtDistance(float distance, float laneOffset)
    {
        ProceduralRoadGenerator.RoadSample sample = GetSampleAtDistance(distance);
        Vector3 position = sample.Position + sample.Right * laneOffset;
        Quaternion rotation = Quaternion.LookRotation(sample.Forward, Vector3.up);
        return new Pose(position, rotation);
    }

    public ProceduralRoadGenerator.RoadSample GetSampleAtDistance(float distance)
    {
        if (samples.Count == 0)
        {
            return new ProceduralRoadGenerator.RoadSample
            {
                Position = transform.position,
                Forward = transform.forward,
                Right = transform.right,
                Distance = 0f
            };
        }

        float clamped = totalLength > 0f ? Mathf.Clamp(distance, 0f, totalLength) : 0f;

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

    public ProceduralRoadGenerator.RoadSample GetNearestSample(Vector3 position, out float lateralOffset, out int nearestIndex)
    {
        nearestIndex = 0;
        lateralOffset = 0f;

        if (samples.Count == 0)
        {
            return new ProceduralRoadGenerator.RoadSample
            {
                Position = transform.position,
                Forward = transform.forward,
                Right = transform.right,
                Distance = 0f
            };
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

        ProceduralRoadGenerator.RoadSample sample = samples[nearestIndex];
        lateralOffset = Vector3.Dot(position - sample.Position, sample.Right);
        return sample;
    }

    public bool IsRoadCollider(Collider collider)
    {
        return false;
    }
}