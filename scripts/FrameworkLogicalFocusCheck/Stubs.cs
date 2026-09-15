using UnityEngine;
// Minimal instruction/metadata surface; the installed CLR2 Harmony runtime is
// validated by the separate full-plugin compile, not loaded into .NET 10.
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)]public class HarmonyPatch:Attribute { public HarmonyPatch(){} public HarmonyPatch(Type t,string n){} }
 public class CodeInstruction {
  public System.Reflection.Emit.OpCode opcode;public object operand;
  public List<System.Reflection.Emit.Label> labels=new();public List<object> blocks=new();
  public CodeInstruction(System.Reflection.Emit.OpCode op,object value=null){opcode=op;operand=value;}
 }
 public static class AccessTools {
  public static System.Reflection.MethodInfo PropertyGetter(Type t,string n)=>t.GetProperty(n,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)!.GetGetMethod(true);
  public static System.Reflection.MethodInfo Method(Type t,string n)=>t.GetMethod(n,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
 }
}
public class AcyclicGraph<T,U> { public class Node {} }
public interface ILogicalElement {}
public class LogicalLinkInfo {}
public interface ILogicalButton : ILogicalElement { bool IsDown(); bool JustPressed(); void GetLogicTreeData(out AcyclicGraph<ILogicalElement,LogicalLinkInfo> g,out AcyclicGraph<ILogicalElement,LogicalLinkInfo>.Node n); }
public interface ILogicalValue { float GetValue(); void GetLogicTreeData(out AcyclicGraph<ILogicalElement,LogicalLinkInfo> g,out AcyclicGraph<ILogicalElement,LogicalLinkInfo>.Node n); }
// Installed Update/claim/held semantics, with only the focus predicate injectable
// so the actual production transpiler's emitted instructions can be executed.
public abstract class LogicalButtonBase : ILogicalButton {
 private bool m_pressClaimed=true,m_releaseClaimed=true,m_down;
 private float m_buttonDownTime,m_buttonDownLength;
 public static Func<LogicalButtonBase,bool> Focus = _=>Application.isFocused;
 protected virtual bool CanProcessInput()=>Focus(this);
 protected void Update(bool isDown) {
  m_pressClaimed=m_pressClaimed&&isDown;m_releaseClaimed=m_releaseClaimed&&!isDown;
  if(isDown&&!m_down)m_buttonDownTime=Time.time;
  else if(isDown&&!m_down)m_buttonDownLength=Time.time-m_buttonDownTime;
  m_down=isDown;if(!CanProcessInput()){ClaimPressEvent();ClaimReleaseEvent();}
 }
 public bool HasUnclaimedPressEvent(){bool value=IsDown();Update(value);return value&&!m_pressClaimed;}
 public bool HasUnclaimedReleaseEvent(){bool value=IsDown();Update(value);return !value&&!m_releaseClaimed;}
 public bool JustPressed(){bool value=HasUnclaimedPressEvent();if(value)ClaimPressEvent();return value;}
 public bool JustReleased(){bool value=HasUnclaimedReleaseEvent();if(value)ClaimReleaseEvent();return value;}
 public void ClaimPressEvent(){m_pressClaimed=true;} public void ClaimReleaseEvent(){m_releaseClaimed=true;}
 public float GetHeldTimeLength(){bool value=IsDown();Update(value);if(value)m_buttonDownLength=Time.time-m_buttonDownTime;return m_buttonDownLength;}
 public abstract bool IsDown();
 public virtual void GetLogicTreeData(out AcyclicGraph<ILogicalElement,LogicalLinkInfo> g,out AcyclicGraph<ILogicalElement,LogicalLinkInfo>.Node n){g=null;n=null;}
}
public class GateLogicalButton : LogicalButtonBase {
 private ILogicalButton m_childButton;private Func<bool> m_callback;
 public GateLogicalButton(ILogicalButton child,Func<bool> callback){m_childButton=child;m_callback=callback;}
 public override bool IsDown()=>m_callback()&&m_childButton.IsDown();
}
public class PlayerIDProvider { public bool Local=true;public bool IsLocallyControlled()=>Local; }
public class PlayerControls {
 public class ControlSchemeData { public ILogicalButton m_pickupButton,m_worksurfaceUseButton,m_dashButton,m_curseButton; public void ClearEvents(){} }
 public GameObject gameObject;public T GetComponent<T>() where T:class=>gameObject.GetComponent<T>();public bool CanButtonBePressed()=>Application.isFocused;
 public void SetControlSchemeData(ControlSchemeData value){}
}
namespace UnityEngine {
 public static class Application { public static bool isFocused {get;set;} }
 public static class Time { public static float time; public static int frameCount; }
 public class GameObject {
  static int sequence; public int InstanceId=++sequence;public PlayerIDProvider Provider=new();
  public int GetInstanceID()=>InstanceId;public T GetComponent<T>() where T:class=>Provider as T;
 }
}
namespace Team17.Online.Multiplayer.Messaging {
 public static class EntitySerialisationRegistry { public static Dictionary<GameObject,uint> Ids=new();public static uint GetId(GameObject value)=>Ids.TryGetValue(value,out var id)?id:0; }
}
namespace Hpmv {
 public class InputData { public Dictionary<int,ChefInput> Input; public int NextFrame;public bool RequestResume,RequestPause;public Isset __isset=new();public class Isset{public bool nextFrame;} }
 public class ChefInput { public Vector Pad;public Button Pickup,Interact,Dash; }
 public class Vector { public double X,Y; }
 public class Button { public bool Down; }
}
