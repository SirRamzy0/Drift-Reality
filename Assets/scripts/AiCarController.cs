using UnityEngine;

[RequireComponent(typeof(ArcadeCarController))]
public sealed class AiCarController : MonoBehaviour
{
    [Header("Path")]
    [SerializeField] private ProceduralRoadGenerator road;
    [SerializeField] private float baseLookAheadDistance = 44f;
    [SerializeField] private float speedLookAheadDistance = 72f;
    [SerializeField] private float turnScanDistance = 135f;
    [SerializeField] private float laneOffset;
    [SerializeField] private float laneChangeInterval = 4.5f;
    [SerializeField] private float laneChangeAmount = 4f;

    [Header("Driving")]
    [SerializeField] private float steeringAngleForFullInput = 42f;
    [SerializeField] private float comfortableTurnAngle = 18f;
    [SerializeField] private float hardTurnAngle = 82f;
    [SerializeField] private float minimumCornerSpeed = 28f;
    [SerializeField] private float brakeSpeedMargin = 8f;
    [SerializeField] private float driftTurnAngle = 38f;
    [SerializeField] private float driftMinimumSpeed01 = 0.38f;
    [SerializeField] private float throttleRecoverySpeed = 0.9f;

    [Header("Personality")]
    [SerializeField] private float aggression = 0.65f;
    [SerializeField] private float laneDiscipline = 0.68f;
    [SerializeField] private float nitroChance = 0.35f;
    [SerializeField] private float mistakeChance = 0.16f;
    [SerializeField] private float mistakeSeverity = 0.45f;

    private ArcadeCarController car;
    private float desiredLaneOffset;
    private float laneVelocity;
    private float nextLaneChangeTime;
    private float nextNitroCheck;
    private float nextMistakeCheck;
    private float driftUntilTime;
    private float mistakeUntilTime;
    private float mistakeSteerBias;
    private float mistakeThrottleBias;
    private float throttleMemory = 1f;

    public void Initialize(ProceduralRoadGenerator newRoad, float newLaneOffset)
    {
        road = newRoad;
        laneOffset = newLaneOffset;
        desiredLaneOffset = newLaneOffset;

        float personalitySeed = Mathf.Abs(newLaneOffset) + Mathf.Abs(newRoad != null ? newRoad.GetHashCode() : 17) * 0.013f;
        aggression = Mathf.Clamp01(0.48f + Mathf.Abs(Mathf.Sin(personalitySeed)) * 0.42f);
        laneDiscipline = Mathf.Clamp01(0.55f + Mathf.Abs(Mathf.Cos(personalitySeed * 0.7f)) * 0.32f);
        nitroChance = Mathf.Lerp(0.18f, 0.52f, aggression);
        mistakeChance = Mathf.Lerp(0.22f, 0.09f, aggression);
        mistakeSeverity = Mathf.Lerp(0.55f, 0.28f, laneDiscipline);
    }

    private void Awake()
    {
        car = GetComponent<ArcadeCarController>();
    }

    private void FixedUpdate()
    {
        if (road == null || road.Samples.Count == 0 || car == null)
        {
            return;
        }

        ProceduralRoadGenerator.RoadSample nearest = road.GetNearestSample(transform.position, out float currentLateralOffset, out _);
        UpdateMistakeState();
        UpdateLaneChoice(currentLateralOffset);

        float speed01 = car.NormalizedSpeed;
        float lookAhead = baseLookAheadDistance + speedLookAheadDistance * speed01;
        ProceduralRoadGenerator.RoadSample target = road.GetSampleAtDistance(nearest.Distance + lookAhead);
        Vector3 laneTarget = target.Position + target.Right * laneOffset;

        Vector3 toTarget = laneTarget - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f)
        {
            toTarget = transform.forward;
        }

        float signedAngle = Vector3.SignedAngle(transform.forward, toTarget.normalized, Vector3.up);
        float steer = Mathf.Clamp(signedAngle / steeringAngleForFullInput, -1f, 1f);

        float upcomingTurnAngle = EstimateUpcomingTurn(nearest.Distance, turnScanDistance);
        float targetSpeed = CalculateTargetSpeed(upcomingTurnAngle);
        float throttle = CalculateThrottle(targetSpeed, Mathf.Abs(signedAngle));
        bool drift = ShouldDrift(upcomingTurnAngle, signedAngle, targetSpeed);
        bool useNitro = ShouldUseNitro(upcomingTurnAngle, signedAngle, targetSpeed);

        if (Time.time <= mistakeUntilTime)
        {
            steer = Mathf.Clamp(steer + mistakeSteerBias, -1f, 1f);
            throttle = Mathf.Clamp(throttle + mistakeThrottleBias, -1f, 1f);
            drift = drift && mistakeThrottleBias > -0.35f;
        }

        car.SetAiInput(throttle, steer, useNitro, false, drift);
    }

    private void UpdateMistakeState()
    {
        if (Time.time < nextMistakeCheck)
        {
            return;
        }

        nextMistakeCheck = Time.time + Random.Range(1.2f, 3.6f);
        if (Random.value > mistakeChance)
        {
            return;
        }

        mistakeUntilTime = Time.time + Random.Range(0.35f, 1.05f);
        float severity = mistakeSeverity * Random.Range(0.55f, 1.25f);
        float kind = Random.value;

        if (kind < 0.34f)
        {
            mistakeSteerBias = Random.Range(-0.55f, 0.55f) * severity;
            mistakeThrottleBias = Random.Range(-0.15f, 0.2f) * severity;
        }
        else if (kind < 0.68f)
        {
            mistakeSteerBias = Random.Range(-0.22f, 0.22f) * severity;
            mistakeThrottleBias = Random.Range(-0.9f, -0.35f) * severity;
        }
        else
        {
            mistakeSteerBias = Random.Range(-0.4f, 0.4f) * severity;
            mistakeThrottleBias = Random.Range(0.25f, 0.75f) * severity;
        }
    }

    private void UpdateLaneChoice(float currentLateralOffset)
    {
        if (Time.time >= nextLaneChangeTime)
        {
            nextLaneChangeTime = Time.time + Random.Range(laneChangeInterval * 0.65f, laneChangeInterval * 1.4f);

            float roadHalfWidth = road.RoadWidth * 0.5f;
            float safeLaneLimit = roadHalfWidth - 3.2f;
            float randomLane = Random.Range(-safeLaneLimit, safeLaneLimit);
            float aggressionBias = Random.Range(-laneChangeAmount, laneChangeAmount) * aggression;
            desiredLaneOffset = Mathf.Lerp(laneOffset, randomLane + aggressionBias, 1f - laneDiscipline);
            desiredLaneOffset = Mathf.Clamp(desiredLaneOffset, -safeLaneLimit, safeLaneLimit);
        }

        float maxLaneSpeed = Mathf.Lerp(1.2f, 3.8f, aggression);
        laneOffset = Mathf.SmoothDamp(laneOffset, desiredLaneOffset, ref laneVelocity, maxLaneSpeed);

        float roadLimit = road.RoadWidth * 0.5f - 2.6f;
        if (Mathf.Abs(currentLateralOffset) > roadLimit)
        {
            desiredLaneOffset = Mathf.Sign(currentLateralOffset) * roadLimit * -0.35f;
            laneOffset = Mathf.MoveTowards(laneOffset, desiredLaneOffset, 9f * Time.fixedDeltaTime);
        }
    }

    private float EstimateUpcomingTurn(float currentDistance, float scanDistance)
    {
        ProceduralRoadGenerator.RoadSample start = road.GetSampleAtDistance(currentDistance + 12f);
        ProceduralRoadGenerator.RoadSample middle = road.GetSampleAtDistance(currentDistance + scanDistance * 0.55f);
        ProceduralRoadGenerator.RoadSample end = road.GetSampleAtDistance(currentDistance + scanDistance);

        float firstAngle = Mathf.Abs(Vector3.SignedAngle(start.Forward, middle.Forward, Vector3.up));
        float secondAngle = Mathf.Abs(Vector3.SignedAngle(middle.Forward, end.Forward, Vector3.up));
        float totalAngle = Mathf.Abs(Vector3.SignedAngle(start.Forward, end.Forward, Vector3.up));

        return Mathf.Max(totalAngle, firstAngle + secondAngle * 0.65f);
    }

    private float CalculateTargetSpeed(float upcomingTurnAngle)
    {
        float turn01 = Mathf.InverseLerp(comfortableTurnAngle, hardTurnAngle, upcomingTurnAngle);
        float cornerSpeed = Mathf.Lerp(car.MaxSpeed, minimumCornerSpeed, turn01);
        float aggressionBonus = Mathf.Lerp(-7f, 9f, aggression);
        return Mathf.Clamp(cornerSpeed + aggressionBonus, minimumCornerSpeed, car.MaxSpeed);
    }

    private float CalculateThrottle(float targetSpeed, float immediateAngle)
    {
        float speed = car.CurrentSpeed;
        float targetThrottle;

        if (speed > targetSpeed + brakeSpeedMargin)
        {
            targetThrottle = -1f;
        }
        else if (speed > targetSpeed)
        {
            targetThrottle = Mathf.Lerp(0.05f, -0.7f, Mathf.InverseLerp(targetSpeed, targetSpeed + brakeSpeedMargin, speed));
        }
        else
        {
            targetThrottle = immediateAngle > hardTurnAngle * 0.85f ? 0.35f : 1f;
        }

        throttleMemory = Mathf.MoveTowards(throttleMemory, targetThrottle, throttleRecoverySpeed * Time.fixedDeltaTime);
        return Mathf.Clamp(throttleMemory, -1f, 1f);
    }

    private bool ShouldDrift(float upcomingTurnAngle, float signedAngle, float targetSpeed)
    {
        bool sharpEnough = upcomingTurnAngle > driftTurnAngle || Mathf.Abs(signedAngle) > driftTurnAngle * 0.75f;
        bool fastEnough = car.NormalizedSpeed > driftMinimumSpeed01;
        bool notTooFast = car.CurrentSpeed < targetSpeed + brakeSpeedMargin * 1.5f;

        if (sharpEnough && fastEnough && notTooFast)
        {
            driftUntilTime = Mathf.Max(driftUntilTime, Time.time + Mathf.Lerp(0.35f, 0.9f, aggression));
        }

        return Time.time <= driftUntilTime;
    }

    private bool ShouldUseNitro(float upcomingTurnAngle, float signedAngle, float targetSpeed)
    {
        if (Time.time < nextNitroCheck)
        {
            return false;
        }

        nextNitroCheck = Time.time + Random.Range(0.7f, 1.5f);

        bool straightEnough = upcomingTurnAngle < comfortableTurnAngle && Mathf.Abs(signedAngle) < 8f;
        bool hasRoom = car.CurrentSpeed < targetSpeed + 4f;
        return straightEnough && hasRoom && car.CanUseNitro() && Random.value < nitroChance;
    }
}
