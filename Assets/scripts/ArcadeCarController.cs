using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public sealed class ArcadeCarController : MonoBehaviour
{
    [Header("Mode")]
    [SerializeField] private bool playerControlled;

    [Header("Handling")]
    [SerializeField] private CarHandlingProfile handlingProfile;
    [SerializeField] private CarHandlingProfile.Settings handling = CarHandlingProfile.Settings.Balanced;

    [Header("Road")]
    [SerializeField] private ProceduralRoadGenerator road;
    [SerializeField] private MonoBehaviour roadProviderSource;
    [SerializeField] private float respawnFallDepth = 16f;

    private IRoadProvider roadProvider;
    private Rigidbody body;
    private BoxCollider boxCollider;
    private ParticleSystem[] nitroEffects;
    private Vector2 driveInput;
    private Vector3 velocityDirection;
    private Vector3 externalVelocity;
    private bool driftHeld;
    private bool jumpRequested;
    private bool nitroRequested;
    private bool driftReleaseRequested;
    private bool drifting;
    private bool grounded;
    private float nitroTimer;
    private float driftBoostTimer;
    private float driftCharge;
    private float nextNitroTime;
    private float nextJumpTime;
    private float currentSpeed;
    private float smoothedSteer;
    private float yaw;
    private int lastSafeSampleIndex;
    private static PhysicsMaterial frictionlessMaterial;

    public bool PlayerControlled
    {
        get => playerControlled;
        set => playerControlled = value;
    }

    public float NormalizedSpeed => Mathf.Clamp01(currentSpeed / Mathf.Max(1f, handling.MaxSpeed));
    public float CurrentSpeed => currentSpeed;
    public float MaxSpeed => handling.MaxSpeed;
    public bool IsGrounded => grounded;
    public float VisualSteer => smoothedSteer;
    public bool IsBraking => driveInput.y < -0.05f && currentSpeed > 0.5f;

    public void Initialize(IRoadProvider newRoadProvider, bool isPlayer)
    {
        roadProvider = newRoadProvider;
        if (newRoadProvider is ProceduralRoadGenerator proceduralRoad)
        {
            road = proceduralRoad;
        }

        playerControlled = isPlayer;
    }

    public void Initialize(ProceduralRoadGenerator newRoad, bool isPlayer)
    {
        road = newRoad;
        roadProvider = newRoad;
        playerControlled = isPlayer;
    }

    public void ApplyHandling(CarHandlingProfile.Settings settings)
    {
        handling = settings.Validated();
        handlingProfile = null;
        ApplyBodySettings();
    }

    public void ApplyHandlingProfile(CarHandlingProfile profile)
    {
        handlingProfile = profile;
        handling = profile != null ? profile.RuntimeSettings : CarHandlingProfile.Settings.Balanced;
        ApplyBodySettings();
    }

    public void SetAiInput(float throttle, float steer, bool useNitro, bool jump, bool drift)
    {
        if (playerControlled)
        {
            return;
        }

        driveInput = new Vector2(Mathf.Clamp(steer, -1f, 1f), Mathf.Clamp(throttle, -1f, 1f));
        driftReleaseRequested |= driftHeld && !drift;
        driftHeld = drift;
        nitroRequested |= useNitro;
        jumpRequested |= jump;
    }

    public void SetAiInput(float throttle, float steer, bool useNitro, bool jump)
    {
        SetAiInput(throttle, steer, useNitro, jump, false);
    }

    public void ApplyGuardrailSlowdown(float speedMultiplier, Vector3 collisionNormal)
    {
        speedMultiplier = Mathf.Clamp(speedMultiplier, 0.1f, 1f);
        currentSpeed *= speedMultiplier;

        Vector3 planarNormal = Flatten(collisionNormal);
        if (planarNormal.sqrMagnitude > 0.001f)
        {
            planarNormal.Normalize();
            externalVelocity = Vector3.ProjectOnPlane(externalVelocity, planarNormal) * 0.45f;

            Vector3 planarVelocity = Flatten(body.linearVelocity);
            Vector3 dampedVelocity = Vector3.ProjectOnPlane(planarVelocity, planarNormal) * speedMultiplier;
            body.linearVelocity = dampedVelocity + Vector3.up * body.linearVelocity.y;
        }
    }

    public bool CanUseNitro()
    {
        return Time.time >= nextNitroTime;
    }

    private void Awake()
    {
        if (handlingProfile != null)
        {
            handling = handlingProfile.RuntimeSettings;
        }
        else
        {
            handling = handling.Validated();
        }

        body = GetComponent<Rigidbody>();
        boxCollider = GetComponent<BoxCollider>();
        velocityDirection = Flatten(transform.forward).normalized;
        if (velocityDirection.sqrMagnitude < 0.001f)
        {
            velocityDirection = Vector3.forward;
        }

        yaw = transform.eulerAngles.y;
        ApplyBodySettings();
        nitroEffects = BuildNitroEffects();
    }

    private void Update()
    {
        if (!playerControlled)
        {
            return;
        }

        ReadPlayerInput();
    }

    private void FixedUpdate()
    {
        UpdateGrounding();
        CaptureCollisionVelocity();
        ApplyActions();
        ApplyMovement();
        ApplyRespawnSafety();

        jumpRequested = false;
        nitroRequested = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        RoadGuardrailCollision guardrail = collision.collider.GetComponentInParent<RoadGuardrailCollision>();
        if (guardrail != null)
        {
            guardrail.ApplyEnter(collision, this);
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        RoadGuardrailCollision guardrail = collision.collider.GetComponentInParent<RoadGuardrailCollision>();
        if (guardrail != null)
        {
            guardrail.ApplyStay(collision, this);
        }
    }

    private void ApplyBodySettings()
    {
        if (body != null)
        {
            body.mass = handling.Mass;
            body.linearDamping = handling.LinearDamping;
            body.angularDamping = handling.AngularDamping;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        if (boxCollider != null)
        {
            boxCollider.center = new Vector3(0f, 0.55f, 0f);
            boxCollider.size = new Vector3(2.15f, 1.1f, 4.25f);
            boxCollider.sharedMaterial = GetFrictionlessMaterial();
        }
    }

    private void ReadPlayerInput()
    {
        float throttle = 0f;
        float steer = 0f;
        bool driftPressed = false;
        bool driftReleased = false;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed)
            {
                throttle += 1f;
            }

            if (keyboard.sKey.isPressed)
            {
                throttle -= 1f;
            }

            if (keyboard.dKey.isPressed)
            {
                steer += 1f;
            }

            if (keyboard.aKey.isPressed)
            {
                steer -= 1f;
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                jumpRequested = true;
            }

            driftPressed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            driftReleased = keyboard.leftShiftKey.wasReleasedThisFrame || keyboard.rightShiftKey.wasReleasedThisFrame;
        }

        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            nitroRequested = true;
        }

        if (mouse != null)
        {
            driftPressed |= mouse.rightButton.isPressed;
            driftReleased |= mouse.rightButton.wasReleasedThisFrame;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        throttle = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        steer = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        jumpRequested |= Input.GetKeyDown(KeyCode.Space);
        nitroRequested |= Input.GetMouseButtonDown(0);
        driftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetMouseButton(1);
        driftReleased = Input.GetKeyUp(KeyCode.LeftShift) || Input.GetKeyUp(KeyCode.RightShift) || Input.GetMouseButtonUp(1);
#endif

        driftHeld = driftPressed;
        driftReleaseRequested |= driftReleased;
        driveInput = new Vector2(Mathf.Clamp(steer, -1f, 1f), Mathf.Clamp(throttle, -1f, 1f));
    }

    private void UpdateGrounding()
    {
        grounded = false;

        if (Physics.Raycast(transform.position + Vector3.up * 0.45f, Vector3.down, out RaycastHit hit, 1.25f))
        {
            grounded = road == null || road.IsRoadCollider(hit.collider);
        }

        if (grounded && roadProvider != null)
        {
            roadProvider.GetNearestSample(transform.position, out float lateralOffset, out int nearestIndex);
            if (Mathf.Abs(lateralOffset) <= roadProvider.RoadWidth * 0.55f)
            {
                lastSafeSampleIndex = nearestIndex;
            }
        }
    }

    private void CaptureCollisionVelocity()
    {
        Vector3 planarVelocity = Flatten(body.linearVelocity);
        Vector3 expectedVelocity = velocityDirection * currentSpeed + externalVelocity;
        Vector3 collisionDelta = planarVelocity - expectedVelocity;

        if (collisionDelta.sqrMagnitude > 0.04f)
        {
            externalVelocity += collisionDelta * handling.CollisionVelocityCarry;
            externalVelocity = Vector3.ClampMagnitude(externalVelocity, handling.MaxSpeed * 0.55f);
        }

        externalVelocity = Vector3.MoveTowards(externalVelocity, Vector3.zero, handling.CollisionVelocityDecay * Time.fixedDeltaTime);
    }

    private void ApplyMovement()
    {
        float throttle = driveInput.y;
        float speedLimit = nitroTimer > 0f ? handling.NitroMaxSpeed : handling.MaxSpeed;
        if (driftBoostTimer > 0f)
        {
            speedLimit = Mathf.Max(speedLimit, handling.NitroMaxSpeed);
        }

        float speed01 = Mathf.Clamp01(currentSpeed / Mathf.Max(1f, handling.MaxSpeed));
        bool wantsDrift = driftHeld && grounded && currentSpeed >= handling.DriftMinSpeed && Mathf.Abs(smoothedSteer) > 0.08f;

        float targetSpeed = currentSpeed;
        if (throttle > 0f)
        {
            targetSpeed = speedLimit;
        }
        else if (throttle < 0f)
        {
            targetSpeed = 0f;
        }
        else
        {
            targetSpeed = Mathf.MoveTowards(currentSpeed, 0f, handling.CoastingDeceleration * Time.fixedDeltaTime);
        }

        float accelerationFactor = handling.AccelerationBySpeed.Evaluate(speed01);
        float launchFactor = Mathf.Lerp(handling.LaunchAccelerationMultiplier, 1f, speed01);
        float speedChangeRate = throttle < 0f ? handling.BrakeAcceleration : handling.Acceleration * accelerationFactor * launchFactor;
        if (driftBoostTimer > 0f)
        {
            speedChangeRate = Mathf.Max(speedChangeRate, handling.DriftBoostAcceleration);
        }

        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speedChangeRate * Time.fixedDeltaTime);
        currentSpeed = Mathf.Clamp(currentSpeed, 0f, speedLimit);

        float steerTarget = driveInput.x;
        smoothedSteer = Mathf.MoveTowards(smoothedSteer, steerTarget, handling.TurnInputSharpness * Time.fixedDeltaTime);

        float steerControl = grounded ? 1f : handling.AirControl;
        Vector3 preTurnForward = Flatten(transform.forward).normalized;
        float preTurnSlip = Vector3.Angle(velocityDirection, preTurnForward);
        float shapedSteer = Mathf.Sign(smoothedSteer) * smoothedSteer * smoothedSteer;
        float steeringSlip = Mathf.Abs(shapedSteer) * speed01 * handling.SteeringDriftAngle;
        float drift01 = Mathf.InverseLerp(handling.DriftStartAngle, handling.DriftFullAngle, preTurnSlip + steeringSlip);
        drifting = wantsDrift;
        if (drifting)
        {
            drift01 = Mathf.Max(drift01, 0.85f);
            driftCharge = Mathf.Clamp01(driftCharge + handling.DriftChargeRate * Mathf.Abs(smoothedSteer) * Time.fixedDeltaTime);
            float driftLossPerSecond = Mathf.Clamp01(1f - handling.DriftSpeedRetention);
            currentSpeed = Mathf.Max(0f, currentSpeed - currentSpeed * driftLossPerSecond * Time.fixedDeltaTime);
        }

        float steeringBySpeed = handling.SteeringBySpeed.Evaluate(speed01);
        float highSpeedTurnFactor = Mathf.Lerp(1f, handling.HighSpeedTurnMultiplier, speed01);
        float driftTurnFactor = Mathf.Lerp(1f, handling.DriftTurnMultiplier, drift01);
        float brakeTurnFactor = throttle < -0.01f ? Mathf.Lerp(1f, handling.BrakeTurnMultiplier, speed01) : 1f;
        float manualDriftTurnFactor = drifting ? handling.DriftSteerMultiplier : 1f;
        float turnDegrees = shapedSteer * handling.TurnRate * steeringBySpeed * highSpeedTurnFactor * driftTurnFactor * brakeTurnFactor * manualDriftTurnFactor * steerControl * Time.fixedDeltaTime;
        if (drifting)
        {
            turnDegrees += Mathf.Sign(smoothedSteer) * handling.DriftYawKick * speed01 * Time.fixedDeltaTime;
        }

        yaw += turnDegrees;

        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        body.MoveRotation(rotation);
        transform.rotation = rotation;
        body.angularVelocity = Vector3.zero;

        Vector3 forward = Flatten(transform.forward).normalized;
        if (currentSpeed < 0.2f)
        {
            velocityDirection = forward;
        }
        else
        {
            float slipAngle = Vector3.Angle(velocityDirection, forward);
            steeringSlip = Mathf.Abs(shapedSteer) * speed01 * handling.SteeringDriftAngle;
            float effectiveSlip = slipAngle + steeringSlip;
            drift01 = Mathf.InverseLerp(handling.DriftStartAngle, handling.DriftFullAngle, effectiveSlip);
            float grip = Mathf.Lerp(handling.Grip, handling.DriftGrip, drift01);
            grip *= handling.GripBySlip.Evaluate(Mathf.Clamp01(effectiveSlip / handling.DriftFullAngle));
            if (drifting)
            {
                grip = handling.DriftArcadeGrip;
            }

            float alignment = 1f - Mathf.Exp(-grip * Time.fixedDeltaTime);
            velocityDirection = Vector3.Slerp(velocityDirection, forward, alignment).normalized;
        }

        Vector3 localExternalVelocity = transform.InverseTransformDirection(externalVelocity);
        if (drifting)
        {
            float driftSide = -Mathf.Sign(smoothedSteer);
            float targetLateralSpeed = driftSide * currentSpeed * Mathf.Lerp(0.12f, 0.28f, Mathf.Abs(smoothedSteer));
            localExternalVelocity.x = Mathf.Lerp(localExternalVelocity.x, targetLateralSpeed, 4.5f * Time.fixedDeltaTime);
        }
        else
        {
            localExternalVelocity.x = Mathf.Lerp(localExternalVelocity.x, 0f, handling.LateralDamping * Time.fixedDeltaTime);
        }

        externalVelocity = transform.TransformDirection(localExternalVelocity);

        Vector3 finalPlanarVelocity = velocityDirection * currentSpeed + externalVelocity;
        Vector3 finalVelocity = finalPlanarVelocity + Vector3.up * body.linearVelocity.y;
        body.linearVelocity = finalVelocity;
        body.AddForce(Vector3.down * handling.ExtraGravity, ForceMode.Acceleration);
    }

    private void ApplyActions()
    {
        if (nitroTimer > 0f)
        {
            nitroTimer -= Time.fixedDeltaTime;
            currentSpeed = Mathf.MoveTowards(currentSpeed, handling.NitroMaxSpeed, handling.NitroAcceleration * Time.fixedDeltaTime);
        }

        if (driftBoostTimer > 0f)
        {
            driftBoostTimer -= Time.fixedDeltaTime;
            currentSpeed = Mathf.MoveTowards(currentSpeed, handling.NitroMaxSpeed, handling.DriftBoostAcceleration * Time.fixedDeltaTime);
        }

        if (driftReleaseRequested)
        {
            if (driftCharge >= handling.DriftMinChargeToBoost)
            {
                float boostStrength = Mathf.InverseLerp(handling.DriftMinChargeToBoost, 1f, driftCharge);
                Vector3 forward = Flatten(transform.forward).normalized;
                velocityDirection = Vector3.Slerp(velocityDirection, forward, handling.DriftExitAlignment).normalized;
                externalVelocity = Vector3.Lerp(externalVelocity, Vector3.zero, handling.DriftExitAlignment);
                currentSpeed = Mathf.Min(handling.NitroMaxSpeed, currentSpeed + handling.DriftBoostSpeed * boostStrength);
                driftBoostTimer = handling.DriftBoostDuration * Mathf.Lerp(0.35f, 1f, boostStrength);
                SetNitroEffects(true);
            }

            driftCharge = 0f;
            driftReleaseRequested = false;
        }

        if (nitroRequested && Time.time >= nextNitroTime)
        {
            nextNitroTime = Time.time + handling.NitroCooldown;
            nitroTimer = handling.NitroDuration;
            currentSpeed = Mathf.Min(handling.NitroMaxSpeed, currentSpeed + handling.NitroImpulse);
            SetNitroEffects(true);
        }

        if (jumpRequested && grounded && Time.time >= nextJumpTime)
        {
            nextJumpTime = Time.time + handling.JumpCooldown;
            Vector3 velocity = body.linearVelocity;
            if (velocity.y < 0f)
            {
                velocity.y = 0f;
            }

            body.linearVelocity = velocity;
            body.AddForce(Vector3.up * handling.JumpImpulse, ForceMode.VelocityChange);
        }

        if (nitroTimer <= 0f && driftBoostTimer <= 0f)
        {
            SetNitroEffects(false);
        }
    }

    private void ApplyRespawnSafety()
    {
        if (roadProvider == null || roadProvider.Samples.Count == 0)
        {
            return;
        }

        ProceduralRoadGenerator.RoadSample nearest = roadProvider.GetNearestSample(transform.position, out float lateralOffset, out _);
        bool tooLow = transform.position.y < nearest.Position.y - respawnFallDepth;
        bool farAway = Mathf.Abs(lateralOffset) > roadProvider.RoadWidth * 3.5f;

        if (!tooLow && !farAway)
        {
            return;
        }

        int safeIndex = Mathf.Clamp(lastSafeSampleIndex - 3, 0, roadProvider.Samples.Count - 1);
        ProceduralRoadGenerator.RoadSample safe = roadProvider.Samples[safeIndex];
        Quaternion safeRotation = Quaternion.LookRotation(safe.Forward, Vector3.up);
        yaw = safeRotation.eulerAngles.y;
        velocityDirection = safe.Forward.normalized;
        externalVelocity = Vector3.zero;
        driftCharge = 0f;
        driftBoostTimer = 0f;
        drifting = false;
        currentSpeed = 10f;
        transform.SetPositionAndRotation(safe.Position + Vector3.up * 0.25f, safeRotation);
        body.linearVelocity = velocityDirection * currentSpeed;
        body.angularVelocity = Vector3.zero;
    }

    private static Vector3 Flatten(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    private static PhysicsMaterial GetFrictionlessMaterial()
    {
        if (frictionlessMaterial != null)
        {
            return frictionlessMaterial;
        }

        frictionlessMaterial = new PhysicsMaterial("Arcade Car Frictionless")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        return frictionlessMaterial;
    }

    private ParticleSystem[] BuildNitroEffects()
    {
        ParticleSystem left = CreateNitroEffect("Nitro Left", new Vector3(-0.62f, 0.45f, -2.15f));
        ParticleSystem right = CreateNitroEffect("Nitro Right", new Vector3(0.62f, 0.45f, -2.15f));
        return new[] { left, right };
    }

    private ParticleSystem CreateNitroEffect(string objectName, Vector3 localPosition)
    {
        GameObject effect = new GameObject(objectName);
        effect.transform.SetParent(transform, false);
        effect.transform.localPosition = localPosition;
        effect.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        ParticleSystem particleSystem = effect.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particleSystem.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = 0.22f;
        main.startSpeed = 16f;
        main.startSize = 0.42f;
        main.startColor = new Color(0.2f, 0.75f, 1f, 0.9f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particleSystem.emission;
        emission.rateOverTime = 120f;

        ParticleSystem.ShapeModule shape = particleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 10f;
        shape.radius = 0.12f;

        ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        return particleSystem;
    }

    private void SetNitroEffects(bool active)
    {
        if (nitroEffects == null)
        {
            return;
        }

        for (int i = 0; i < nitroEffects.Length; i++)
        {
            ParticleSystem effect = nitroEffects[i];
            if (effect == null)
            {
                continue;
            }

            if (active && !effect.isPlaying)
            {
                effect.Play();
            }
            else if (!active && effect.isPlaying)
            {
                effect.Stop();
            }
        }
    }
}
