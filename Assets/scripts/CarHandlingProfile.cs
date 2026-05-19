using UnityEngine;

[CreateAssetMenu(menuName = "Drift Reality/Car Handling Profile", fileName = "CarHandlingProfile")]
public sealed class CarHandlingProfile : ScriptableObject
{
    [System.Serializable]
    public struct Settings
    {
        [Header("Identity")]
        public string DisplayName;

        [Header("Speed")]
        public float MaxSpeed;
        public float Acceleration;
        public float LaunchAccelerationMultiplier;
        public float BrakeAcceleration;
        public float CoastingDeceleration;
        public AnimationCurve AccelerationBySpeed;

        [Header("Steering")]
        public float TurnRate;
        public float TurnInputSharpness;
        public float HighSpeedTurnMultiplier;
        public float DriftTurnMultiplier;
        public float BrakeTurnMultiplier;
        public AnimationCurve SteeringBySpeed;

        [Header("Grip And Drift")]
        public float Grip;
        public float DriftGrip;
        public float DriftStartAngle;
        public float DriftFullAngle;
        public float SteeringDriftAngle;
        public float LateralDamping;
        public AnimationCurve GripBySlip;

        [Header("Manual Drift")]
        public float DriftMinSpeed;
        public float DriftSteerMultiplier;
        public float DriftYawKick;
        public float DriftSpeedRetention;
        public float DriftChargeRate;
        public float DriftMinChargeToBoost;
        public float DriftBoostSpeed;
        public float DriftBoostDuration;
        public float DriftBoostAcceleration;
        public float DriftExitAlignment;
        public float DriftArcadeGrip;

        [Header("Body")]
        public float Mass;
        public float LinearDamping;
        public float AngularDamping;
        public float ExtraGravity;
        public float AirControl;
        public float CollisionVelocityCarry;
        public float CollisionVelocityDecay;

        [Header("Nitro")]
        public float NitroImpulse;
        public float NitroMaxSpeed;
        public float NitroAcceleration;
        public float NitroCooldown;
        public float NitroDuration;

        [Header("Jump")]
        public float JumpImpulse;
        public float JumpCooldown;

        public static Settings Balanced => new Settings
        {
            DisplayName = "Balanced",
            MaxSpeed = 100f,
            Acceleration = 54f,
            LaunchAccelerationMultiplier = 0.72f,
            BrakeAcceleration = 145f,
            CoastingDeceleration = 18f,
            AccelerationBySpeed = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.45f, 0.9f),
                new Keyframe(1f, 0.42f)),
            TurnRate = 132f,
            TurnInputSharpness = 5.8f,
            HighSpeedTurnMultiplier = 0.5f,
            DriftTurnMultiplier = 1.28f,
            BrakeTurnMultiplier = 1.25f,
            SteeringBySpeed = new AnimationCurve(
                new Keyframe(0f, 0.5f),
                new Keyframe(0.28f, 0.92f),
                new Keyframe(1f, 0.52f)),
            Grip = 16.5f,
            DriftGrip = 9.5f,
            DriftStartAngle = 6f,
            DriftFullAngle = 22f,
            SteeringDriftAngle = 28f,
            LateralDamping = 15f,
            GripBySlip = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.55f, 0.82f),
                new Keyframe(1f, 0.58f)),
            DriftMinSpeed = 18f,
            DriftSteerMultiplier = 1.45f,
            DriftYawKick = 34f,
            DriftSpeedRetention = 0.94f,
            DriftChargeRate = 0.72f,
            DriftMinChargeToBoost = 0.22f,
            DriftBoostSpeed = 22f,
            DriftBoostDuration = 0.75f,
            DriftBoostAcceleration = 82f,
            DriftExitAlignment = 0.92f,
            DriftArcadeGrip = 11.5f,
            Mass = 900f,
            LinearDamping = 0.04f,
            AngularDamping = 5f,
            ExtraGravity = 28f,
            AirControl = 0.26f,
            CollisionVelocityCarry = 0.45f,
            CollisionVelocityDecay = 9f,
            NitroImpulse = 24f,
            NitroMaxSpeed = 140f,
            NitroAcceleration = 82f,
            NitroCooldown = 1.8f,
            NitroDuration = 0.6f,
            JumpImpulse = 7.5f,
            JumpCooldown = 1.2f
        };

        public Settings Validated()
        {
            Settings value = this;
            value.MaxSpeed = Mathf.Max(1f, value.MaxSpeed);
            value.Acceleration = Mathf.Max(0f, value.Acceleration);
            value.LaunchAccelerationMultiplier = Mathf.Clamp01(value.LaunchAccelerationMultiplier);
            value.BrakeAcceleration = Mathf.Max(0f, value.BrakeAcceleration);
            value.CoastingDeceleration = Mathf.Max(0f, value.CoastingDeceleration);
            value.TurnRate = Mathf.Max(0f, value.TurnRate);
            value.TurnInputSharpness = Mathf.Max(0.1f, value.TurnInputSharpness);
            value.HighSpeedTurnMultiplier = Mathf.Clamp(value.HighSpeedTurnMultiplier, 0.05f, 1f);
            value.DriftTurnMultiplier = Mathf.Clamp(value.DriftTurnMultiplier, 1f, 3f);
            value.BrakeTurnMultiplier = Mathf.Clamp(value.BrakeTurnMultiplier, 1f, 3f);
            value.Grip = Mathf.Max(0f, value.Grip);
            value.DriftGrip = Mathf.Max(0f, value.DriftGrip);
            value.DriftStartAngle = Mathf.Clamp(value.DriftStartAngle, 0f, 80f);
            value.DriftFullAngle = Mathf.Max(value.DriftStartAngle + 1f, value.DriftFullAngle);
            value.SteeringDriftAngle = Mathf.Max(0f, value.SteeringDriftAngle);
            value.LateralDamping = Mathf.Max(0f, value.LateralDamping);
            value.DriftMinSpeed = Mathf.Max(0f, value.DriftMinSpeed);
            value.DriftSteerMultiplier = Mathf.Clamp(value.DriftSteerMultiplier, 1f, 3f);
            value.DriftYawKick = Mathf.Max(0f, value.DriftYawKick);
            value.DriftSpeedRetention = Mathf.Clamp01(value.DriftSpeedRetention);
            value.DriftChargeRate = Mathf.Max(0f, value.DriftChargeRate);
            value.DriftMinChargeToBoost = Mathf.Clamp01(value.DriftMinChargeToBoost);
            value.DriftBoostSpeed = Mathf.Max(0f, value.DriftBoostSpeed);
            value.DriftBoostDuration = Mathf.Max(0f, value.DriftBoostDuration);
            value.DriftBoostAcceleration = Mathf.Max(0f, value.DriftBoostAcceleration);
            value.DriftExitAlignment = Mathf.Clamp01(value.DriftExitAlignment);
            value.DriftArcadeGrip = Mathf.Max(0f, value.DriftArcadeGrip);
            value.Mass = Mathf.Max(50f, value.Mass);
            value.LinearDamping = Mathf.Max(0f, value.LinearDamping);
            value.AngularDamping = Mathf.Max(0f, value.AngularDamping);
            value.ExtraGravity = Mathf.Max(0f, value.ExtraGravity);
            value.AirControl = Mathf.Clamp01(value.AirControl);
            value.CollisionVelocityCarry = Mathf.Clamp01(value.CollisionVelocityCarry);
            value.CollisionVelocityDecay = Mathf.Max(0f, value.CollisionVelocityDecay);
            value.NitroImpulse = Mathf.Max(0f, value.NitroImpulse);
            value.NitroMaxSpeed = Mathf.Max(value.MaxSpeed, value.NitroMaxSpeed);
            value.NitroAcceleration = Mathf.Max(0f, value.NitroAcceleration);
            value.NitroCooldown = Mathf.Max(0f, value.NitroCooldown);
            value.NitroDuration = Mathf.Max(0f, value.NitroDuration);
            value.JumpImpulse = Mathf.Max(0f, value.JumpImpulse);
            value.JumpCooldown = Mathf.Max(0f, value.JumpCooldown);

            if (value.AccelerationBySpeed == null || value.AccelerationBySpeed.length == 0)
            {
                value.AccelerationBySpeed = Balanced.AccelerationBySpeed;
            }

            if (value.SteeringBySpeed == null || value.SteeringBySpeed.length == 0)
            {
                value.SteeringBySpeed = Balanced.SteeringBySpeed;
            }

            if (value.GripBySlip == null || value.GripBySlip.length == 0)
            {
                value.GripBySlip = Balanced.GripBySlip;
            }

            return value;
        }

        public Settings CreateOpponentVariant(int index)
        {
            Settings value = Validated();
            float speed = 1f + Mathf.Sin(index * 1.37f) * 0.08f;
            float acceleration = 1f + Mathf.Cos(index * 0.91f) * 0.11f;
            float steering = 1f + Mathf.Sin(index * 2.11f) * 0.1f;
            float grip = 1f + Mathf.Cos(index * 1.73f) * 0.12f;
            float drift = 1f + Mathf.Sin(index * 0.63f) * 0.18f;

            value.DisplayName = "Generated Opponent " + index;
            value.MaxSpeed *= speed;
            value.NitroMaxSpeed *= speed;
            value.Acceleration *= acceleration;
            value.TurnRate *= steering;
            value.Grip *= grip;
            value.DriftGrip *= drift;
            value.Mass *= 1f + Mathf.Sin(index * 0.51f) * 0.07f;
            return value.Validated();
        }
    }

    [SerializeField] private Settings settings = Settings.Balanced;

    public Settings RuntimeSettings => settings.Validated();

    private void Reset()
    {
        settings = Settings.Balanced;
    }

    private void OnValidate()
    {
        settings = settings.Validated();
    }
}
