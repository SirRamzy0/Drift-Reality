using UnityEngine;

public sealed class RoadGuardrailCollision : MonoBehaviour
{
    [SerializeField] private float enterSpeedMultiplier = 0.72f;
    [SerializeField] private float staySpeedMultiplier = 0.965f;

    public void Configure(float newEnterSpeedMultiplier, float newStaySpeedMultiplier)
    {
        enterSpeedMultiplier = Mathf.Clamp(newEnterSpeedMultiplier, 0.1f, 1f);
        staySpeedMultiplier = Mathf.Clamp(newStaySpeedMultiplier, 0.85f, 1f);
    }

    public void ApplyEnter(Collision collision, ArcadeCarController car)
    {
        ApplySlowdown(collision, car, enterSpeedMultiplier);
    }

    public void ApplyStay(Collision collision, ArcadeCarController car)
    {
        ApplySlowdown(collision, car, staySpeedMultiplier);
    }

    private void ApplySlowdown(Collision collision, ArcadeCarController car, float speedMultiplier)
    {
        if (car == null)
        {
            return;
        }

        Vector3 normal = Vector3.zero;
        for (int i = 0; i < collision.contactCount; i++)
        {
            normal += collision.GetContact(i).normal;
        }

        if (normal.sqrMagnitude < 0.001f)
        {
            normal = transform.right;
        }

        car.ApplyGuardrailSlowdown(speedMultiplier, normal.normalized);
    }
}
