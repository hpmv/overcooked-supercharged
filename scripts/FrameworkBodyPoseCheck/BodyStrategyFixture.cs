using SuperchargedPatch;

public sealed class BodyStrategyFixture:IBodyRestoreStrategy
{
    public string Name{get;set;}="fixture";
    public int ApiVersion{get;set;}=1;
    public Action<NativeBodyPoseCheckpoint.Snapshot[]> Action{get;set;}
    public int Calls;
    public void Restore(NativeBodyPoseCheckpoint.Snapshot[] saved){Calls++;Action?.Invoke(saved);}
}
