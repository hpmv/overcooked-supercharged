// Controlled Unity/Harmony host only; native draw methods are copied separately.
using System.Reflection;
namespace UnityEngine {
 public class Object { public static T[] FindObjectsOfType<T>()=>NativeTestHost.Flows.OfType<T>().ToArray(); }
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);}
 public static class Random {
  public struct State {public uint Value;}
  public static State state{get;set;} public static int DrawCount;public static bool ThrowNext;
  public static void InitState(int seed){state=new(){Value=unchecked((uint)seed)};}
  public static float Range(float lo,float hi){DrawCount++;state=new(){Value=unchecked(state.Value*1664525u+1013904223u)};if(ThrowNext){ThrowNext=false;throw new InvalidOperationException("Injected native weighted RNG failure");}return lo+(state.Value>>8)/16777216f*(hi-lo);}
 }
}
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch:Attribute{public HarmonyPatch(Type t,string n){}}
 [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix:Attribute{}
 public sealed class HarmonyMethod{public HarmonyMethod(MethodInfo m){if(m==null)throw new Exception("Missing patch target");}}
 public sealed class Harmony{public Harmony(string s){}public void Patch(MethodInfo m,HarmonyMethod prefix=null,HarmonyMethod postfix=null){if(m==null)throw new Exception("Missing native method");}public void UnpatchSelf(){}}
}
public delegate T Generic<T,A,B>(A a,B b);
public abstract class RoundInstanceDataBase{}
public class CampaignLevelConfig{public virtual RoundData GetRoundData()=>null;}
public class ScriptedCampaignLevelConfig:CampaignLevelConfig{public string name="Sushi_1_1S_4P";public ScriptedRoundData[] m_rounds;public override RoundData GetRoundData()=>m_rounds[0];}
public sealed class RecipeList{public Entry[] m_recipes;public sealed class Entry{public OrderDefinitionNode m_order;public int m_scoreForMeal;public float m_weight;}}
public sealed class OrderDefinitionNode{public int m_uID;public string name;public AssembledDefinitionNode Convert()=>new(){RecipeId=m_uID};}
public sealed class AssembledDefinitionNode{public int RecipeId;}
namespace SuperchargedPatch{public sealed class RoundDataAuxMessage{public AssembledDefinitionNode[] recipes;public int currentIndex;}}
public static class NativeTestHost{public static readonly List<ServerKitchenFlowControllerBase> Flows=new();public static GameSession Session;}
public class GameSession{public int DLC=-1;public Settings LevelSettings=new();public sealed class Settings{public Variant SceneDirectoryVarientEntry;}public sealed class Variant{public string SceneName="s_sushi_1_1";public int PlayerCount=4;public object LevelConfig;}}
public static class GameUtils{public static GameSession GetGameSession()=>NativeTestHost.Session;}
public enum TeamID{One}
public sealed class ServerKitchenFlowControllerBase{public Monitor monitor=new();public Monitor GetMonitorForTeam(TeamID t)=>monitor;}
public sealed class Monitor{public Orders OrdersController=new();}
public sealed class Orders{public RoundData Source;}
namespace SuperchargedPatch.Extensions{public static class Ext{public static RoundData GetRoundData(this Orders o)=>o.Source;}}
