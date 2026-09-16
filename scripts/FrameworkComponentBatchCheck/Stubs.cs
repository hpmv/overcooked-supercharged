namespace UnityEngine {
 public class Object {
  public int Id;public int GetInstanceID()=>Id;
  public static Dictionary<Type,Object[]> Queries=new();public static Dictionary<Type,int> Calls=new();
  public static T[] FindObjectsOfType<T>() where T:Object {Calls[typeof(T)]=Calls.GetValueOrDefault(typeof(T))+1;return Queries.GetValueOrDefault(typeof(T),Array.Empty<Object>()).Cast<T>().ToArray();}
  public static T FindObjectOfType<T>() where T:Object=>FindObjectsOfType<T>().FirstOrDefault();
 }
 public class MonoBehaviour:Object {}
}
public class ServerKitchenFlowControllerBase:UnityEngine.MonoBehaviour {}
public class ServerCannon:UnityEngine.MonoBehaviour {}
public class ClientPlayerControlsImpl_Default:UnityEngine.MonoBehaviour {}
public class ServerCookingStation:UnityEngine.MonoBehaviour {public int Value;}
public class ServerMixingStation:UnityEngine.MonoBehaviour {}
public class Unrelated:UnityEngine.MonoBehaviour {}
namespace SuperchargedPatch {public static class NativeSceneMetadata {public static int Refreshes;}}
