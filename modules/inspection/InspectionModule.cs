using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Host invokes only on its ready, paused, input-fenced main-thread boundary.
    // No callbacks, native object cache, guessed rollback, or per-frame work.
    public sealed class InspectionModule : IAuthoringModule
    {
        private const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        private bool disposed;
        public string Name { get { return "registered-component-inspection-v1"; } }
        public int ApiVersion { get { return 1; } }
        public void Dispose() { disposed=true; }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("InspectionModule");
            if(args==null)throw new ArgumentNullException("args");
            if(operation!="batch")return Execute(Prepare(operation,args));
            object value;
            if(!args.TryGetValue("operations",out value) || !(value is object[]))throw new ArgumentException("batch.operations must be an array.");
            var operations=(object[])value;
            if(operations.Length<1||operations.Length>32)throw new ArgumentException("Use1..32 explicit batch operations.");
            var prepared=new List<Prepared>();
            foreach(var item in operations)
            {
                var row=item as Dictionary<string,object>;
                if(row==null)throw new ArgumentException("Batch operation must be an object.");
                string name=Text(row,"operation");
                object nested;
                if(name=="batch"||!row.TryGetValue("args",out nested)||!(nested is Dictionary<string,object>))throw new ArgumentException("No nested batch; explicit operation args required.");
                prepared.Add(Prepare(name,(Dictionary<string,object>)nested));
            }
            // Validate all selectors/types before the first mutation; execution
            // rechecks identities after any preceding user-requested method.
            var receipts=new List<object>();
            foreach(var item in prepared)
            {
                var receipt=Execute(item);receipts.Add(receipt);
                if(!(bool)receipt["ok"])break;
            }
            bool ok=receipts.Count==prepared.Count;
            foreach(Dictionary<string,object> r in receipts)ok=ok&&(bool)r["ok"];
            return Map("ok",ok,"operations",receipts.ToArray(),"requested",prepared.Count,"rollbackPerformed",false);
        }

        private sealed class Prepared
        {
            internal string Operation;
            internal Dictionary<string,object> Args;
            internal int EntityId,ObjectId,ComponentId;
            internal GameObject Object;
            internal Component Component;
            internal MemberInfo Member;
            internal MethodInfo Method;
            internal object Value;
        }

        private static Prepared Prepare(string operation,Dictionary<string,object> args)
        {
            if(operation!="inspect"&&operation!="set"&&operation!="invoke")throw new ArgumentException("Use inspect, set, invoke or batch.");
            bool mutation=operation!="inspect";
            int entity=Int(args,"entityId");
            var obj=ResolveEntity(entity);
            var p=new Prepared { Operation=operation,Args=args,EntityId=entity,Object=obj,ObjectId=obj.GetInstanceID() };
            RequireExpected(args,"expectedObjectId",p.ObjectId,mutation);
            object selection;
            if(!args.TryGetValue("component",out selection))
            {
                if(mutation||args.ContainsKey("members"))throw new ArgumentException("Explicit component required.");
                return p;
            }
            string component=Text(args,"component");
            int found=0;
            foreach(var c in obj.GetComponents<Component>())
            {
                if(c==null || c.GetType().FullName!=component&&c.GetType().Name!=component)continue;
                if(args.ContainsKey("expectedComponentId")&&c.GetInstanceID()!=Int(args,"expectedComponentId"))continue;
                p.Component=c;found++;
            }
            if(found!=1)throw new InvalidOperationException("Component selector must match exactly one live component.");
            p.ComponentId=p.Component.GetInstanceID();RequireExpected(args,"expectedComponentId",p.ComponentId,mutation);
            if(operation=="set")
            {
                p.Member=FindMember(p.Component.GetType(),args);
                Type type=MemberType(p.Member);object raw;
                if(!args.TryGetValue("value",out raw))throw new ArgumentException("Explicit set value required.");
                var field=p.Member as FieldInfo;var property=p.Member as PropertyInfo;
                if(field!=null&&(field.IsInitOnly||field.IsLiteral)||property!=null&&(property.GetSetMethod(true)==null||property.GetGetMethod(true)==null))
                    throw new ArgumentException("Selected member is not readable and writable.");
                p.Value=ConvertValue(raw,type);
            }
            else if(operation=="invoke")
            {
                string name=Text(args,"method"),declaring=OptionalText(args,"declaringType");
                foreach(Type type in Ancestors(p.Component.GetType()))
                {
                    if(declaring!=null&&declaring!=type.FullName)continue;
                    foreach(var method in type.GetMethods(Members))
                        if(method.Name==name&&!method.IsGenericMethod&&!method.IsSpecialName&&method.GetParameters().Length==0)
                        {
                            if(p.Method!=null)throw new ArgumentException("Ambiguous method; provide declaringType.");
                            p.Method=method;
                        }
                }
                if(p.Method==null)throw new ArgumentException("Exact native no-argument instance method missing.");
                if(!args.ContainsKey("members")||SelectedMembers(p).Count==0)throw new ArgumentException("Invoke requires selected before/after observation members.");
            }
            // Selected reads are resolved without calling their getters here.
            if(args.ContainsKey("members"))SelectedMembers(p);
            return p;
        }

        private static Dictionary<string,object> Execute(Prepared p)
        {
            var receipt=Map("operation",p.Operation,"entityId",p.EntityId,"objectInstanceId",p.ObjectId,
                "componentInstanceId",p.ComponentId,"ok",false,"mutationAttempted",false,"rollbackPerformed",false);
            try
            {
                ValidateLive(p);
                receipt["before"]=Observe(p);
                if(p.Operation=="set")
                {
                    receipt["member"]=DescribeMember(p.Member);receipt["requestedValue"]=Encode(p.Value);
                    receipt["beforeValue"]=Encode(Read(p.Component,p.Member));
                    receipt["mutationAttempted"]=true;
                    var field=p.Member as FieldInfo;
                    if(field!=null)field.SetValue(p.Component,p.Value);else((PropertyInfo)p.Member).SetValue(p.Component,p.Value,null);
                }
                else if(p.Operation=="invoke")
                {
                    receipt["method"]=p.Method.DeclaringType.FullName+"."+p.Method.Name;
                    receipt["mutationAttempted"]=true;receipt["returned"]=Encode(p.Method.Invoke(p.Component,null));
                }
                ValidateLive(p);receipt["after"]=Observe(p);
                if(p.Member!=null)receipt["afterValue"]=Encode(Read(p.Component,p.Member));
                receipt["ok"]=true;
            }
            catch(Exception error)
            {
                var invocation=error as TargetInvocationException;
                receipt["error"]=(invocation!=null&&invocation.InnerException!=null?invocation.InnerException:error).ToString();
                try { ValidateLive(p);receipt["after"]=Observe(p); }
                catch(Exception afterError) { receipt["afterError"]=afterError.Message; }
            }
            return receipt;
        }

        private static GameObject ResolveEntity(int id)
        {
            if(id<=0)throw new ArgumentException("Positive native entityId required.");
            GameObject result=null;int count=0;var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++)
            {
                var entry=entries._items[i];
                if((int)entry.m_Header.m_uEntityID==id && entry.m_GameObject!=null){result=entry.m_GameObject;count++;}
            }
            if(count!=1)throw new InvalidOperationException("EntityId does not resolve to exactly one live registered GameObject.");
            return result;
        }
        private static void ValidateLive(Prepared p)
        {
            var current=ResolveEntity(p.EntityId);
            if(!object.ReferenceEquals(current,p.Object)||current.GetInstanceID()!=p.ObjectId)throw new InvalidOperationException("Registered GameObject incarnation changed.");
            if(p.ComponentId==0)return;
            bool found=false;
            foreach(var c in current.GetComponents<Component>())if(c!=null&&object.ReferenceEquals(c,p.Component)&&c.GetInstanceID()==p.ComponentId)found=true;
            if(!found)throw new InvalidOperationException("Component incarnation changed.");
        }
        private static object Observe(Prepared p)
        {
            var components=new List<object>();
            foreach(var c in p.Object.GetComponents<Component>())if(c!=null)components.Add(Map("type",c.GetType().FullName,"instanceId",c.GetInstanceID()));
            var result=Map("entityId",p.EntityId,"objectInstanceId",p.ObjectId,"name",p.Object.name,"components",components.ToArray());
            if(p.Component==null)return result;
            var metadata=new List<object>();
            foreach(Type type in Ancestors(p.Component.GetType()))
            {
                foreach(var field in type.GetFields(Members))metadata.Add(DescribeMember(field));
                foreach(var property in type.GetProperties(Members))if(property.GetIndexParameters().Length==0)metadata.Add(DescribeMember(property));
                if(metadata.Count>2048)throw new InvalidOperationException("Component member catalog exceeds bound.");
            }
            result["memberCatalog"]=metadata.ToArray();var values=new List<object>();
            var selected=SelectedMembers(p);
            if(p.Member!=null&&!selected.Contains(p.Member))selected.Add(p.Member);
            foreach(var member in selected)
            {
                var row=DescribeMember(member);
                try { row["value"]=Encode(Read(p.Component,member)); }
                catch(Exception error) { row["readError"]=error.Message; }
                values.Add(row);
            }
            result["values"]=values.ToArray();return result;
        }
        private static List<MemberInfo> SelectedMembers(Prepared p)
        {
            var result=new List<MemberInfo>();object raw;
            if(!p.Args.TryGetValue("members",out raw))return result;
            var selectors=raw as object[];
            if(selectors==null||selectors.Length>64)throw new ArgumentException("members must be an array of up to64 explicit selectors.");
            foreach(var item in selectors)
            {
                var selector=item as Dictionary<string,object>;
                if(selector==null)throw new ArgumentException("Member selector requires kind/name and optional declaringType.");
                var member=FindMember(p.Component.GetType(),selector);
                var property=member as PropertyInfo;
                if(property!=null&&property.GetGetMethod(true)==null)throw new ArgumentException("Property has no getter.");
                result.Add(member);
            }
            return result;
        }
        private static MemberInfo FindMember(Type root,Dictionary<string,object> args)
        {
            string kind=Text(args,"kind"),name=Text(args,"name"),declaring=OptionalText(args,"declaringType");
            if(kind!="field"&&kind!="property")throw new ArgumentException("Member kind must be field or property.");
            MemberInfo result=null;
            foreach(Type type in Ancestors(root))
            {
                if(declaring!=null&&declaring!=type.FullName)continue;
                MemberInfo member=kind=="field"?(MemberInfo)type.GetField(name,Members):type.GetProperty(name,Members);
                if(member==null)continue;
                var property=member as PropertyInfo;
                if(property!=null&&property.GetIndexParameters().Length!=0)throw new ArgumentException("Indexed properties are unsupported.");
                if(result!=null)throw new ArgumentException("Ambiguous member; provide declaringType.");
                result=member;
            }
            if(result==null)throw new ArgumentException("Exact instance member missing.");
            return result;
        }
        private static IEnumerable<Type> Ancestors(Type type) { for(;type!=null;type=type.BaseType)yield return type; }
        private static Type MemberType(MemberInfo m) { return m is FieldInfo?((FieldInfo)m).FieldType:((PropertyInfo)m).PropertyType; }
        private static object Read(Component c,MemberInfo m) { return m is FieldInfo?((FieldInfo)m).GetValue(c):((PropertyInfo)m).GetValue(c,null); }
        private static Dictionary<string,object> DescribeMember(MemberInfo m)
        {
            var f=m as FieldInfo;var p=m as PropertyInfo;
            return Map("kind",f!=null?"field":"property","name",m.Name,"declaringType",m.DeclaringType.FullName,
                "type",MemberType(m).FullName,"readable",f!=null||p.GetGetMethod(true)!=null,
                "writable",f!=null?!f.IsInitOnly&&!f.IsLiteral:p.GetSetMethod(true)!=null);
        }

        private static object ConvertValue(object value,Type type)
        {
            if(type==typeof(string)){if(value==null||value is string)return value;throw new ArgumentException("String value required.");}
            if(type==typeof(bool)){if(value is bool)return value;throw new ArgumentException("Boolean value required.");}
            if(type==typeof(Vector3)||type==typeof(Quaternion))
            {
                var map=value as Dictionary<string,object>;
                if(map==null||map.Count!=(type==typeof(Vector3)?3:4))throw new ArgumentException("Explicit vector/quaternion component object required.");
                float x=(float)ConvertValue(Required(map,"x"),typeof(float)),y=(float)ConvertValue(Required(map,"y"),typeof(float)),z=(float)ConvertValue(Required(map,"z"),typeof(float));
                if(type==typeof(Vector3))return new Vector3(x,y,z);
                return new Quaternion(x,y,z,(float)ConvertValue(Required(map,"w"),typeof(float)));
            }
            if(type.IsEnum)
            {
                if(!(value is string)||!Enum.IsDefined(type,value))throw new ArgumentException("An exact named enum value is required.");
                return Enum.Parse(type,(string)value,false);
            }
            if(!(value is long)&&!(value is double)&&!(value is int))throw new ArgumentException("Numeric scalar required.");
            double number=Convert.ToDouble(value,CultureInfo.InvariantCulture);
            if(Double.IsNaN(number)||Double.IsInfinity(number))throw new ArgumentException("Finite numeric value required.");
            if(type==typeof(double))return number;
            if(type==typeof(float)){float f=(float)number;if(Single.IsInfinity(f))throw new ArgumentException("Single-precision overflow.");return f;}
            if(type!=typeof(int)&&type!=typeof(uint)&&type!=typeof(long)&&type!=typeof(ulong)&&type!=typeof(short)&&type!=typeof(ushort)&&type!=typeof(byte)&&type!=typeof(sbyte))
                throw new ArgumentException("Only scalar, enum, Vector3 and Quaternion writes are supported.");
            if(Math.Truncate(number)!=number)throw new ArgumentException("Integral value required.");
            return Convert.ChangeType(value,type,CultureInfo.InvariantCulture);
        }
        private static object Encode(object value)
        {
            if(value==null)return null;Type type=value.GetType();
            if(value is Vector3){var v=(Vector3)value;return Map("type",type.FullName,"x",v.x,"y",v.y,"z",v.z);}
            if(value is Quaternion){var q=(Quaternion)value;return Map("type",type.FullName,"x",q.x,"y",q.y,"z",q.z,"w",q.w);}
            if(type.IsEnum)return Map("type",type.FullName,"name",value.ToString(),"number",((Enum)value).ToString("D"));
            if(type.IsPrimitive||value is string||value is decimal)return Map("type",type.FullName,"value",value);
            var obj=value as UnityEngine.Object;
            if(obj!=null)return Map("type",type.FullName,"instanceId",obj.GetInstanceID(),"name",obj.name);
            return Map("type",type.FullName,"valueOmitted",true);
        }
        private static object Required(Dictionary<string,object> map,string key){object value;if(!map.TryGetValue(key,out value))throw new ArgumentException("Missing "+key);return value;}
        private static string Text(Dictionary<string,object> map,string key){var value=Required(map,key) as string;if(String.IsNullOrEmpty(value)||value.Length>256)throw new ArgumentException("Explicit bounded "+key+" string required.");return value;}
        private static string OptionalText(Dictionary<string,object> map,string key){return map.ContainsKey(key)?Text(map,key):null;}
        private static int Int(Dictionary<string,object> map,string key){return (int)ConvertValue(Required(map,key),typeof(int));}
        private static void RequireExpected(Dictionary<string,object> map,string key,int actual,bool required)
        {if(required&&!map.ContainsKey(key))throw new ArgumentException("Mutation requires "+key);if(map.ContainsKey(key)&&Int(map,key)!=actual)throw new InvalidOperationException("Live "+key+" differs.");}
        private static Dictionary<string,object> Map(params object[] pairs){var result=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)result.Add((string)pairs[i],pairs[i+1]);return result;}
    }
}
