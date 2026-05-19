using UnityEngine;

public sealed class FinishLineTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        ArcadeCarController car = other.GetComponentInParent<ArcadeCarController>();
        if (car == null)
        {
            return;
        }

        string driver = car.PlayerControlled ? "Player" : car.name;
        Debug.Log(driver + " finished the race.");
        RaceBootstrap.Instance?.NotifyFinished(car);
    }
}
