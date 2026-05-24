using System.Collections.Generic;
using UnityEngine;

public interface IRoadProvider
{
    IReadOnlyList<ProceduralRoadGenerator.RoadSample> Samples { get; }
    float TotalLength { get; }
    float RoadWidth { get; }

    Pose GetPoseAtDistance(float distance, float laneOffset);
    ProceduralRoadGenerator.RoadSample GetSampleAtDistance(float distance);
    ProceduralRoadGenerator.RoadSample GetNearestSample(Vector3 position, out float lateralOffset, out int nearestIndex);
    bool IsRoadCollider(Collider collider);
}