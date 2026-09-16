namespace Hpmv {
 public class InputData {public bool RequestPause,RequestResume,PreventInvalidState;public int NextFrame;public object Warp;public Flags __isset;public struct Flags{public bool nextFrame;}}
 public class OutputData {public int PhysicsFramesElapsed,FramesSinceLastNoPhysicsFrame,FrameNumber;public bool LastFramePaused,NextFramePaused;public List<string> ServerMessages=new();public List<string> EntityRegistry=new();}
 public static class Injector {public static InjectorServer Server;}
 public class Interceptor {public class Client {public Client(Thrift.Protocol.TProtocol p){}public void send_getNext(OutputData value)=>throw new Exception("Network must not execute in fixture");public InputData recv_getNext()=>throw new Exception("Network must not execute in fixture");}}
}
namespace Thrift.Transport {public class TTransport{}public class TStreamTransport:TTransport{public TStreamTransport(Stream a,Stream b){}}}
namespace Thrift.Protocol {public class TProtocol{}public class TBinaryProtocol:TProtocol{public TBinaryProtocol(Thrift.Transport.TTransport t){}}}
namespace UnityEngine {public static class Debug {public static void LogException(Exception e){} }}
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)]public class HarmonyPatch:Attribute{public HarmonyPatch(Type t,string name){}}
 [AttributeUsage(AttributeTargets.Method)]public class HarmonyPostfix:Attribute{}
}
namespace SuperchargedPatch.Extensions {}
namespace SuperchargedPatch {
 public static class Helpers {public static bool Paused=true;public static bool IsPaused()=>Paused;public static void Pause()=>Paused=true;public static void Resume()=>Paused=false;}
 public static class TASLogicalButton {public static readonly List<Hpmv.InputData> Applied=new();public static void ApplyInputFrame(Hpmv.InputData value)=>Applied.Add(value);}
 public static class WarpHandler {public static int Calls;public static void HandleWarpRequestIfAny(){Calls++;var value=Hpmv.Injector.Server.CurrentInput;}}
 public static class StateInvalidityManager {public static bool PreventInvalidState;}
 public static class ActiveStateCollector {public static readonly List<int> Notified=new();public static readonly List<int> CapturedPhysics=new();public static void NotifyFrame(int f)=>Notified.Add(f);public static void CollectDataForFrame(Hpmv.OutputData o)=>CapturedPhysics.Add(o.PhysicsFramesElapsed);}
}
namespace SuperchargedPatch.Bridge {public static class NativeSessionBridge {public static bool Blocked;public static Hpmv.InputData Neutral=new(){RequestPause=true};public static Hpmv.InputData FilterInput(Hpmv.InputData input)=>Blocked?Neutral:input;}}
public class MultiplayerController {public int Flushes;public void FlushAllPendingBatchedMessages()=>Flushes++;}
public static class TimeManager {public enum PauseLayer{Main}public static bool IsPaused(PauseLayer p)=>SuperchargedPatch.Helpers.IsPaused();}
public class ClientFlowControllerBase {}
public class ServerFlowControllerBase {}
public enum GameState {InLevel}
