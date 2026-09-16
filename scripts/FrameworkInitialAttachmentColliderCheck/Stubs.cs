namespace UnityEngine
{
    public class Object
    {
        private static int next;
        private readonly int id=++next;
        public bool Destroyed;
        public int GetInstanceID()=>Destroyed?0:id;
        public static bool operator ==(Object a,Object b)
        {
            bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;
            return an||bn?an==bn:ReferenceEquals(a,b);
        }
        public static bool operator !=(Object a,Object b)=>!(a==b);
        public override bool Equals(object value)=>ReferenceEquals(this,value);
        public override int GetHashCode()=>id;
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform=>gameObject.transform;
    }
    public sealed class GameObject:Object
    {
        internal static readonly List<GameObject> All=new();
        public string name;
        public readonly Transform transform;
        public Rigidbody body;
        public readonly List<Collider> colliders=new();
        public bool activeSelf=true;
        public int layer;
        public bool activeInHierarchy=>!Destroyed&&activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name)
        {
            this.name=name;transform=new Transform{gameObject=this};All.Add(this);
        }
        public Rigidbody AddBody(){body=new Rigidbody{gameObject=this};return body;}
        public T AddCollider<T>()where T:Collider,new(){var value=new T{gameObject=this};colliders.Add(value);return value;}
        public T[] GetComponents<T>()where T:Component=>new Component[]{transform,body}.Concat(colliders)
            .Where(value=>value!=null&&!value.Destroyed).OfType<T>().ToArray();
        public T[] GetComponentsInChildren<T>(bool includeInactive)where T:Component=>All
            .Where(value=>!value.Destroyed&&(ReferenceEquals(value,this)||HasAncestor(value.transform,transform)))
            .SelectMany(value=>value.GetComponents<T>()).ToArray();
        private static bool HasAncestor(Transform value,Transform target)
        {for(var cursor=value.parent;cursor!=null;cursor=cursor.parent)if(ReferenceEquals(cursor,target))return true;return false;}
    }
    public sealed class Transform:Component
    {
        public Transform parent;
        public Vector3 localPosition,localScale=new(1,1,1);
        public Quaternion localRotation=new(0,0,0,1);
        public int GetSiblingIndex()
        {
            int index=0;
            foreach(var value in GameObject.All)
            {
                if(value.Destroyed||!ReferenceEquals(value.transform.parent,parent))continue;
                if(ReferenceEquals(value.transform,this))return index;
                index++;
            }
            return -1;
        }
    }
    public sealed class Rigidbody:Component {}
    public sealed class PhysicMaterial:Object {}
    public sealed class Mesh:Object {}
    public class Collider:Component
    {
        public bool enabled=true,isTrigger;
        public PhysicMaterial sharedMaterial;
        public Rigidbody attachedRigidbody
        {
            get {for(var cursor=transform;cursor!=null;cursor=cursor.parent)
                if(cursor.gameObject.body!=null&&!cursor.gameObject.body.Destroyed)return cursor.gameObject.body;return null;}
        }
    }
    public sealed class BoxCollider:Collider {public Vector3 center,size=new(1,1,1);}
    public sealed class SphereCollider:Collider {public Vector3 center;public float radius=.5f;}
    public sealed class CapsuleCollider:Collider {public Vector3 center;public float radius=.4f,height=2;public int direction=1;}
    public sealed class MeshCollider:Collider {public Mesh sharedMesh;public bool convex;}
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}
    }
}
namespace Hpmv
{
    public sealed class Point {public double X,Y,Z;}
    public sealed class Quaternion {public double X,Y,Z,W;}
    public sealed class EntityWarpSpec
    {
        public int EntityId;public Point Position,Velocity,AngularVelocity;public Quaternion Rotation;
        public Isset __isset;
        public struct Isset
        {
            public bool entityId,spawningPath,entityPathReference,position,rotation,velocity,angularVelocity,
                plateStation,chef,ingredientContainer,cannon,workstation,workableItem,throwableItem,terminal,
                pilotRotation,mixingHandler,cookingHandler,chefCarry,attachStation,pickupItemSwitcher,
                triggerColourCycle,plateReturnController,stack,washingStation,kitchenController,cookingStation,
                plateReturnStation;
        }
    }
}
