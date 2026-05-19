using System;
using UnityEngine;

public sealed class RoadGuardrailBuilder : MonoBehaviour
{
    [Serializable]
    public struct Settings
    {
        public bool Enabled;
        public float MinTurnAngle;
        public int TurnLookAheadSamples;
        public int SampleStride;
        public float CoverageChance;
        public float BothSidesChance;
        public float GapChance;
        public float MinSectionLength;
        public float MaxSectionLength;
        public float EdgeOffset;
        public float SegmentLength;
        public float SegmentThickness;
        public float SegmentHeight;
        public float EnterSpeedMultiplier;
        public float StaySpeedMultiplier;

        public static Settings Default => new Settings
        {
            Enabled = true,
            MinTurnAngle = 9f,
            TurnLookAheadSamples = 8,
            SampleStride = 2,
            CoverageChance = 0.72f,
            BothSidesChance = 0.16f,
            GapChance = 0.18f,
            MinSectionLength = 18f,
            MaxSectionLength = 48f,
            EdgeOffset = 0.38f,
            SegmentLength = 7.5f,
            SegmentThickness = 0.58f,
            SegmentHeight = 1.25f,
            EnterSpeedMultiplier = 0.72f,
            StaySpeedMultiplier = 0.965f
        };
    }

    [SerializeField] private Settings settings = Settings.Default;
    [SerializeField] private Material guardrailMaterial;

    private static PhysicsMaterial guardrailPhysicsMaterial;

    public void Generate(ProceduralRoadGenerator road, Material material, int seed, Settings newSettings)
    {
        settings = newSettings;
        guardrailMaterial = material;

        if (!settings.Enabled || road == null || road.Samples.Count < 4)
        {
            return;
        }

        System.Random random = new System.Random(seed * 73856093 ^ 0x4D6F6E);
        int lookAhead = Mathf.Clamp(settings.TurnLookAheadSamples, 2, 24);
        int stride = Mathf.Clamp(settings.SampleStride, 1, 8);
        int index = lookAhead;

        while (index < road.Samples.Count - lookAhead)
        {
            float turnAngle = GetSignedTurnAngle(road, index, lookAhead);
            float turnStrength = Mathf.Abs(turnAngle);

            if (turnStrength < settings.MinTurnAngle || random.NextDouble() > settings.CoverageChance)
            {
                index += stride;
                continue;
            }

            int sectionSteps = GetSectionStepCount(road, index, stride, random);
            float outsideSide = -Mathf.Sign(turnAngle);
            bool bothSides = random.NextDouble() < settings.BothSidesChance && turnStrength > settings.MinTurnAngle * 1.65f;

            BuildSection(road, index, sectionSteps, stride, outsideSide, random);
            if (bothSides)
            {
                BuildSection(road, index, sectionSteps, stride, -outsideSide, random);
            }

            index += sectionSteps * stride + Mathf.Max(stride, Mathf.RoundToInt(RandomRange(random, 10f, 26f) / GetAverageSampleDistance(road)));
        }
    }

    private void BuildSection(ProceduralRoadGenerator road, int startIndex, int sectionSteps, int stride, float side, System.Random random)
    {
        for (int step = 0; step < sectionSteps; step++)
        {
            if (random.NextDouble() < settings.GapChance)
            {
                continue;
            }

            int sampleIndex = Mathf.Clamp(startIndex + step * stride, 0, road.Samples.Count - 1);
            ProceduralRoadGenerator.RoadSample sample = road.Samples[sampleIndex];
            CreateSegment(road, sample, side, random);
        }
    }

    private void CreateSegment(ProceduralRoadGenerator road, ProceduralRoadGenerator.RoadSample sample, float side, System.Random random)
    {
        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
        segment.name = side < 0f ? "Left Guardrail" : "Right Guardrail";
        segment.transform.SetParent(transform, false);

        float edge = road.RoadWidth * 0.5f + settings.EdgeOffset;
        float height = settings.SegmentHeight * RandomRange(random, 0.92f, 1.14f);
        float length = settings.SegmentLength * RandomRange(random, 0.78f, 1.26f);
        Vector3 position = sample.Position + sample.Right * side * edge + Vector3.up * (height * 0.5f + 0.06f);
        Quaternion rotation = Quaternion.LookRotation(sample.Forward, Vector3.up);

        segment.transform.SetPositionAndRotation(position, rotation);
        segment.transform.localScale = new Vector3(settings.SegmentThickness, height, length);

        MeshRenderer renderer = segment.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = guardrailMaterial;
        }

        Collider collider = segment.GetComponent<Collider>();
        if (collider != null)
        {
            collider.sharedMaterial = GetGuardrailPhysicsMaterial();
        }

        RoadGuardrailCollision collision = segment.AddComponent<RoadGuardrailCollision>();
        collision.Configure(settings.EnterSpeedMultiplier, settings.StaySpeedMultiplier);
        segment.isStatic = true;
    }

    private int GetSectionStepCount(ProceduralRoadGenerator road, int index, int stride, System.Random random)
    {
        float sectionLength = RandomRange(random, settings.MinSectionLength, settings.MaxSectionLength);
        float sampleDistance = Mathf.Max(0.5f, GetAverageSampleDistance(road));
        return Mathf.Max(2, Mathf.RoundToInt(sectionLength / (sampleDistance * stride)));
    }

    private static float GetSignedTurnAngle(ProceduralRoadGenerator road, int index, int lookAhead)
    {
        Vector3 before = road.Samples[Mathf.Max(0, index - lookAhead)].Forward;
        Vector3 after = road.Samples[Mathf.Min(road.Samples.Count - 1, index + lookAhead)].Forward;
        return Vector3.SignedAngle(before, after, Vector3.up);
    }

    private static float GetAverageSampleDistance(ProceduralRoadGenerator road)
    {
        if (road.Samples.Count < 2)
        {
            return 4f;
        }

        return road.TotalLength / Mathf.Max(1, road.Samples.Count - 1);
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        return Mathf.Lerp(min, max, (float)random.NextDouble());
    }

    private static PhysicsMaterial GetGuardrailPhysicsMaterial()
    {
        if (guardrailPhysicsMaterial != null)
        {
            return guardrailPhysicsMaterial;
        }

        guardrailPhysicsMaterial = new PhysicsMaterial("Arcade Guardrail")
        {
            dynamicFriction = 0.2f,
            staticFriction = 0.2f,
            bounciness = 0.02f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        return guardrailPhysicsMaterial;
    }
}
