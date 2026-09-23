using UnityEngine;

public sealed class PhysicsReplayEventRecorder : MonoBehaviour
{
    public PhysicsSceneReplay owner;
    public string label;

    private void OnCollisionEnter(Collision value) { owner.Record(label, "collision-enter", value); }
    private void OnCollisionStay(Collision value) { owner.Record(label, "collision-stay", value); }
    private void OnCollisionExit(Collision value) { owner.Record(label, "collision-exit", value); }
    private void OnTriggerEnter(Collider value) { owner.Record(label, "trigger-enter", value); }
    private void OnTriggerStay(Collider value) { owner.Record(label, "trigger-stay", value); }
    private void OnTriggerExit(Collider value) { owner.Record(label, "trigger-exit", value); }
}
