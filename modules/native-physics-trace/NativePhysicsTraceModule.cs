using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Read-only native call observer. The companion x86 DLL installs reversible
    // pass-through trampolines while the game is paused. It records raw entry
    // state but never changes arguments, return values, bodies, or game data.
    public sealed class NativePhysicsTraceModule : IAuthoringModule
    {
        private static NativePhysicsTraceModule current;
        [StructLayout(LayoutKind.Sequential, Pack=8)]
        private struct NativeStatus
        {
            public uint ApiVersion, StructSize, EventSize, InstalledMask;
            public uint InstalledHookCount, Capacity;
            public int LatestSequence, DroppedEstimate;
            public uint LastError;
        }

        [StructLayout(LayoutKind.Sequential, Pack=8)]
        private struct NativeEvent
        {
            public int Sequence;
            public uint Kind, ThreadId;
            public long Qpc;
            public UIntPtr Self, ReturnAddress;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=8)] public UIntPtr[] Stack;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=8)] public UIntPtr[] Payload;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=8)] public UIntPtr[] Frames;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=5)] public UIntPtr[] Extra;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NativeApiVersion();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Install(UIntPtr unityBase,uint mask);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Uninstall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Clear();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Mark(uint code,UIntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Status(out NativeStatus status);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Read(int after,IntPtr output,int capacity);

        [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Unicode)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32",SetLastError=true)] private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module,string name);

        private readonly FieldInfo cachedPtr=typeof(UnityEngine.Object).GetField("m_CachedPtr",BindingFlags.Instance|BindingFlags.NonPublic);
        private IntPtr library;
        private NativeApiVersion apiVersion;
        private Install install;
        private Uninstall uninstall;
        private Clear clear;
        private Mark mark;
        private Status status;
        private Read read;
        private Harmony harmony;
        private string nativePath,nativeSha256;
        private uint unityPlayerBase;
        private bool active,disposed;

        public string Name { get { return "native-physics-trace-v17-mass-diagonalization"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("NativePhysicsTraceModule");
            if(args==null)args=new Dictionary<string,object>();
            if(operation=="activate") Activate(args);
            else if(operation=="deactivate") Deactivate();
            else if(operation=="clear") { RequireNoArgs(args);RequireLoaded();clear(); }
            else if(operation=="mark") { RequireLoaded();mark(UInt(args,"code"),new UIntPtr(UInt(args,"value"))); }
            else if(operation=="resolve-transforms") return ResolveTransforms(args);
            else if(operation!="status" && operation!="read") throw new ArgumentException("Use activate, deactivate, clear, mark, status, read or resolve-transforms.");
            return Receipt(operation,args);
        }

        private void Activate(Dictionary<string,object> args)
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))throw new InvalidOperationException("Native trace activation requires native pause.");
            if(active)return;
            string path=Path.GetFullPath(String(args,"nativePath"));
            string expected=String(args,"sha256").ToUpperInvariant();
            if(!File.Exists(path))throw new FileNotFoundException("Native trace DLL missing.",path);
            string actual=Hash(path);
            if(actual!=expected)throw new InvalidOperationException("Native trace DLL hash mismatch: "+actual);
            if(IntPtr.Size!=4)throw new InvalidOperationException("Native trace requires the x86 player.");
            ProcessModule unity=null;
            foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
                if(string.Equals(module.ModuleName,"UnityPlayer.dll",StringComparison.OrdinalIgnoreCase)){unity=module;break;}
                if(unity==null)throw new InvalidOperationException("UnityPlayer.dll is not loaded.");
            unityPlayerBase=unchecked((uint)unity.BaseAddress.ToInt32());
            library=LoadLibrary(path);
            if(library==IntPtr.Zero)throw new InvalidOperationException("LoadLibrary failed: "+Marshal.GetLastWin32Error());
            try {
                apiVersion=Export<NativeApiVersion>("oc2_trace_api_version");
                install=Export<Install>("oc2_trace_install");
                uninstall=Export<Uninstall>("oc2_trace_uninstall");
                clear=Export<Clear>("oc2_trace_clear");
                mark=Export<Mark>("oc2_trace_mark");
                status=Export<Status>("oc2_trace_status");
                read=Export<Read>("oc2_trace_read");
                if(apiVersion()!=1)throw new InvalidOperationException("Native trace API version mismatch.");
                uint mask=args.ContainsKey("mask")?UInt(args,"mask"):15u;
                if(install(new UIntPtr(unityPlayerBase),mask)==0)
                {
                    NativeStatus failed;status(out failed);
                    throw new InvalidOperationException("Native trace install failed, Win32/error code "+failed.LastError+".");
                }
                nativePath=path;nativeSha256=actual;active=true;
                Type helpers=typeof(SuperchargedPatch.Bridge.NativeSessionBridge).Assembly.GetType("SuperchargedPatch.Helpers",true);
                MethodInfo resume=AccessTools.DeclaredMethod(helpers,"Resume",Type.EmptyTypes);
                MethodInfo after=GetType().GetMethod("AfterAuthoringResume",BindingFlags.Public|BindingFlags.Static);
                if(resume==null||after==null)throw new InvalidOperationException("Authoring Resume marker target is unavailable.");
                harmony=new Harmony("supercharged.authoring.native-physics-trace."+GetType().Assembly.GetName().Name);
                harmony.Patch(resume,postfix:new HarmonyMethod(after));
                current=this;
            } catch {
                current=null;
                if(harmony!=null){harmony.UnpatchSelf();harmony=null;}
                if(active&&uninstall!=null)uninstall();active=false;
                ReleaseLibrary();throw;
            }
        }

        // This marker is emitted only after Helpers.Resume actually removed the
        // authoring pause. It precisely separates paused render evaluations
        // from the first resumed player-loop evaluation without altering either.
        public static void AfterAuthoringResume()
        {
            NativePhysicsTraceModule module=current;
            if(module!=null&&module.active&&module.mark!=null&&!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                module.mark(0xA50u,UIntPtr.Zero);
        }

        private Dictionary<string,object> Receipt(string operation,Dictionary<string,object> args)
        {
            NativeStatus value=new NativeStatus();
            if(library!=IntPtr.Zero && status!=null && status(out value)==0)throw new InvalidOperationException("Native status failed.");
            var receipt=new Dictionary<string,object> {
                {"name",Name},{"apiVersion",1},{"operation",operation},{"active",active},
                {"nativePath",nativePath},{"nativeSha256",nativeSha256},
                {"unityPlayerBase","0x"+unityPlayerBase.ToString("X8")},
                {"nativeApiVersion",value.ApiVersion},{"nativeStructSize",value.StructSize},
                {"nativeEventSize",value.EventSize},{"installedMask",value.InstalledMask},
                {"installedHookCount",value.InstalledHookCount},{"capacity",value.Capacity},
                {"latestSequence",value.LatestSequence},{"droppedEstimate",value.DroppedEstimate},
                {"lastError",value.LastError},{"physicsTransformHandles",PhysicsTransformHandles()},
                {"chefs",ChefPointers()},
                {"chefAnimators",ChefAnimatorPointers()},
                {"chefColliders",ChefColliderPointers()},
                {"scope","Read-only pass-through trace of verified Unity 2017.4.8f1 x86 native entry points; no argument, return-value, body, or game-data changes."}
            };
            if(operation=="read")receipt.Add("events",ReadEvents(args,value));
            return receipt;
        }

        private object ResolveTransforms(Dictionary<string,object> args)
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))throw new InvalidOperationException("Transform pointer resolution requires native pause.");
            if(!args.ContainsKey("pointers") || args["pointers"]==null)throw new ArgumentException("Missing pointers");
            IEnumerable values=args["pointers"] as IEnumerable;
            if(values==null || args["pointers"] is string)throw new ArgumentException("pointers must be an array.");
            var requested=new HashSet<uint>();
            foreach(object value in values)
            {
                uint pointer=ParsePointer(value);
                if(pointer==0)throw new ArgumentException("Transform pointers must be nonzero.");
                requested.Add(pointer);
                if(requested.Count>256)throw new ArgumentOutOfRangeException("pointers");
            }
            if(requested.Count==0)throw new ArgumentException("pointers must not be empty.");
            var matches=new List<object>();
            foreach(UnityEngine.Object item in Resources.FindObjectsOfTypeAll(typeof(Transform)))
            {
                Transform transform=item as Transform;
                if(transform==null)continue;
                IntPtr transformPointer=(IntPtr)cachedPtr.GetValue(transform);
                uint address=unchecked((uint)transformPointer.ToInt32());
                if(!requested.Contains(address))continue;
                IntPtr hierarchyPointer=IntPtr.Zero;
                int queueIndex=0;
                uint maskLow=0,maskHigh=0;
                if(transformPointer!=IntPtr.Zero)
                {
                    hierarchyPointer=new IntPtr(Marshal.ReadInt32(transformPointer,0x20));
                    if(hierarchyPointer!=IntPtr.Zero)
                    {
                        queueIndex=Marshal.ReadInt32(hierarchyPointer,0x1C);
                        maskLow=unchecked((uint)Marshal.ReadInt32(hierarchyPointer,0x20));
                        maskHigh=unchecked((uint)Marshal.ReadInt32(hierarchyPointer,0x24));
                    }
                }
                matches.Add(new Dictionary<string,object> {
                    {"transformPointer",Pointer(transformPointer)},
                    {"transformInstanceId",transform.GetInstanceID()},
                    {"gameObjectInstanceId",transform.gameObject.GetInstanceID()},
                    {"path",PathOf(transform)},
                    {"activeSelf",transform.gameObject.activeSelf},
                    {"activeInHierarchy",transform.gameObject.activeInHierarchy},
                    {"hierarchyPointer",Pointer(hierarchyPointer)},
                    {"queueIndex",queueIndex},
                    {"hierarchyMaskLow","0x"+maskLow.ToString("X8")},
                    {"hierarchyMaskHigh","0x"+maskHigh.ToString("X8")},
                    {"localPosition",Vector(transform.localPosition)},
                    {"localRotation",Quaternion(transform.localRotation)},
                    {"worldPosition",Vector(transform.position)},
                    {"worldRotation",Quaternion(transform.rotation)}
                });
            }
            var missing=new List<object>();
            foreach(uint pointer in requested)
            {
                bool found=false;
                foreach(object row in matches)
                {
                    var fields=(Dictionary<string,object>)row;
                    if(string.Equals((string)fields["transformPointer"],"0x"+pointer.ToString("X8"),StringComparison.Ordinal)){found=true;break;}
                }
                if(!found)missing.Add("0x"+pointer.ToString("X8"));
            }
            Dictionary<string,object> receipt=Receipt("resolve-transforms",args);
            receipt.Add("requestedCount",requested.Count);
            receipt.Add("matchedCount",matches.Count);
            receipt.Add("matches",matches.ToArray());
            receipt.Add("missing",missing.ToArray());
            receipt["scope"]="Paused, read-only resolution of explicitly supplied native Transform pointers through Resources.FindObjectsOfTypeAll; no native or managed state is written.";
            return receipt;
        }

        private object[] ReadEvents(Dictionary<string,object> args,NativeStatus state)
        {
            RequireLoaded();
            int after=args.ContainsKey("afterSequence")?Int(args,"afterSequence"):0;
            int maximum=args.ContainsKey("max")?Int(args,"max"):4096;
            if(maximum<1||maximum>32768)throw new ArgumentOutOfRangeException("max");
            int size=Marshal.SizeOf(typeof(NativeEvent));
            if(state.EventSize!=(uint)size)throw new InvalidOperationException("Native event layout mismatch: native="+state.EventSize+" managed="+size);
            IntPtr buffer=Marshal.AllocHGlobal(size*maximum);
            try {
                int count=read(after,buffer,maximum);
                if(count<0||count>maximum)throw new InvalidOperationException("Native read returned invalid count.");
                object[] events=new object[count];
                for(int i=0;i<count;i++)
                {
                    NativeEvent item=(NativeEvent)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64()+i*size),typeof(NativeEvent));
                    events[i]=new Dictionary<string,object> {
                        {"sequence",item.Sequence},{"kind",Kind(item.Kind)},{"kindId",item.Kind},
                        {"threadId",item.ThreadId},{"qpc",item.Qpc},{"self",Hex(item.Self)},
                        {"returnAddress",Hex(item.ReturnAddress)},{"stack",Hexes(item.Stack)},
                        {"payload",Hexes(item.Payload)},{"payloadFloat",Floats(item.Payload)},
                        {"frames",Hexes(item.Frames)},
                        {"extra",Hexes(item.Extra)},{"extraFloat",Floats(item.Extra)},
                        {"rawWorkUnit",item.Kind==50?RawWorkUnit(item):new object[0]},
                        {"rawElement0",item.Kind==51?Hexes(item.Payload):new object[0]},
                        {"rawElement1",item.Kind==51?Hexes(item.Frames):new object[0]},
                        {"contactManagerAllocation",item.Kind==52?ContactManagerAllocation(item):new object[0]},
                        {"rawContactManagerPrefix",item.Kind==52?RawContactManagerPrefix(item):new object[0]},
                        {"animatorStateMachine",item.Kind>=60&&item.Kind<=62?AnimatorStateMachine(item):new object[0]},
                        {"animatorStateMixer",item.Kind==63?AnimatorStateMixer(item):new object[0]},
                        {"animatorOuterMixer",item.Kind==64?AnimatorOuterMixer(item):new object[0]},
                        {"animatorStateMachineMemoryRaw",item.Kind==65?AnimatorStateMachineMemoryRaw(item):new object[0]},
                        {"animatorStateMachineOutputRaw",item.Kind==66?AnimatorStateMachineOutputRaw(item):new object[0]},
                        {"animatorConditionFloat",item.Kind==67?AnimatorConditionFloat(item):new object[0]},
                        {"animatorTransitionLifecycle",item.Kind>=68&&item.Kind<=71?AnimatorTransitionLifecycle(item):new object[0]}
                    };
                }
                return events;
            } finally { Marshal.FreeHGlobal(buffer); }
        }

        private object[] ChefPointers()
        {
            var rows=new List<object>();
            if(cachedPtr==null)return rows.ToArray();
            foreach(ServerChefSynchroniser chef in UnityEngine.Object.FindObjectsOfType(typeof(ServerChefSynchroniser)))
            {
                Rigidbody body=chef.GetComponent<Rigidbody>();
                if(body==null)continue;
                IntPtr pointer=(IntPtr)cachedPtr.GetValue(body);
                rows.Add(new Dictionary<string,object>{{"chefInstanceId",chef.GetInstanceID()},
                    {"bodyInstanceId",body.GetInstanceID()},{"bodyPointer",Hex(new UIntPtr(unchecked((uint)pointer.ToInt32())))},
                    {"path",PathOf(body.transform)}});
            }
            return rows.ToArray();
        }

        private object[] ChefColliderPointers()
        {
            var rows=new List<object>();
            if(cachedPtr==null)return rows.ToArray();
            foreach(Collider collider in UnityEngine.Object.FindObjectsOfType(typeof(Collider)))
            {
                Rigidbody body=collider.attachedRigidbody;
                if(body==null || body.GetComponent<ServerChefSynchroniser>()==null)continue;
                IntPtr colliderPointer=(IntPtr)cachedPtr.GetValue(collider);
                IntPtr transformPointer=(IntPtr)cachedPtr.GetValue(collider.transform);
                IntPtr bodyPointer=(IntPtr)cachedPtr.GetValue(body);
                IntPtr hierarchyPointer=IntPtr.Zero;
                IntPtr shapePointer=IntPtr.Zero;
                string shapeReadError=null;
                try {
                    // Unity 2017.4.8f1 BoxCollider::PoseChanged reads the PxShape
                    // pointer from Collider + 0x24. This is a bounded read from a
                    // live Collider object; no native memory is written.
                    if(colliderPointer!=IntPtr.Zero)shapePointer=new IntPtr(Marshal.ReadInt32(colliderPointer,0x24));
                } catch(Exception error) { shapeReadError=error.GetType().FullName+": "+error.Message; }
                try {
                    // Every Transform in one native hierarchy points to its
                    // TransformHierarchy at +0x20 in this shipped player.
                    if(transformPointer!=IntPtr.Zero)hierarchyPointer=new IntPtr(Marshal.ReadInt32(transformPointer,0x20));
                } catch { hierarchyPointer=IntPtr.Zero; }
                Transform transform=collider.transform;
                rows.Add(new Dictionary<string,object> {
                    {"colliderInstanceId",collider.GetInstanceID()},
                    {"colliderType",collider.GetType().FullName},
                    {"colliderPointer",Pointer(colliderPointer)},
                    {"shapePointer",Pointer(shapePointer)},
                    {"shapeReadError",shapeReadError},
                    {"enabled",collider.enabled},{"isTrigger",collider.isTrigger},
                    {"gameObjectInstanceId",collider.gameObject.GetInstanceID()},
                    {"path",PathOf(transform)},
                    {"transformInstanceId",transform.GetInstanceID()},
                    {"transformPointer",Pointer(transformPointer)},
                    {"hierarchyPointer",Pointer(hierarchyPointer)},
                    {"localPosition",Vector(transform.localPosition)},
                    {"localRotation",Quaternion(transform.localRotation)},
                    {"worldPosition",Vector(transform.position)},
                    {"worldRotation",Quaternion(transform.rotation)},
                    {"bodyInstanceId",body.GetInstanceID()},
                    {"bodyPointer",Pointer(bodyPointer)},
                    {"bodyPath",PathOf(body.transform)}
                });
            }
            return rows.ToArray();
        }

        private object[] ChefAnimatorPointers()
        {
            var rows=new List<object>();
            if(cachedPtr==null)return rows.ToArray();
            foreach(Animator animator in UnityEngine.Object.FindObjectsOfType(typeof(Animator)))
            {
                if(animator==null || animator.transform==null || animator.transform.parent==null)continue;
                ServerChefSynchroniser chef=animator.transform.parent.GetComponent<ServerChefSynchroniser>();
                if(chef==null)continue;
                IntPtr pointer=(IntPtr)cachedPtr.GetValue(animator);
                rows.Add(new Dictionary<string,object> {
                    {"animatorInstanceId",animator.GetInstanceID()},
                    {"animatorPointer",Pointer(pointer)},
                    {"path",PathOf(animator.transform)},
                    {"enabled",animator.enabled},{"speed",animator.speed},
                    {"updateMode",animator.updateMode.ToString()},
                    {"cullingMode",animator.cullingMode.ToString()},
                    {"applyRootMotion",animator.applyRootMotion}
                });
            }
            return rows.ToArray();
        }

        private object PhysicsTransformHandles()
        {
            var rows=new List<object>();
            string[] names={"collider-scale","collider-transform","rigidbody-transform","rigidbody-animation"};
            uint[] rvas={0xFA2E34u,0xFA2E38u,0xFA2E3Cu,0xFA2E40u};
            for(int i=0;i<rvas.Length;i++)
            {
                uint handle=unchecked((uint)Marshal.ReadInt32(new IntPtr(unchecked((int)(unityPlayerBase+rvas[i])))));
                uint low=handle<32u?1u<<(int)handle:0u;
                uint high=handle>=32u&&handle<64u?1u<<(int)(handle-32u):0u;
                rows.Add(new Dictionary<string,object> {
                    {"name",names[i]},{"rva","0x"+rvas[i].ToString("X8")},{"handle",handle},
                    {"maskLow","0x"+low.ToString("X8")},{"maskHigh","0x"+high.ToString("X8")}
                });
            }
            return rows.ToArray();
        }

        private void Deactivate()
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))throw new InvalidOperationException("Native trace deactivation requires native pause.");
            if(!active){ReleaseLibrary();return;}
            current=null;
            if(harmony!=null){harmony.UnpatchSelf();harmony=null;}
            if(uninstall()==0)throw new InvalidOperationException("Native trace uninstall failed; DLL retained.");
            active=false;ReleaseLibrary();
        }

        private T Export<T>(string name) where T:class
        {
            IntPtr pointer=GetProcAddress(library,name);
            if(pointer==IntPtr.Zero)throw new MissingMethodException("Native export missing: "+name);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(pointer,typeof(T));
        }

        private void ReleaseLibrary()
        {
            apiVersion=null;install=null;uninstall=null;clear=null;mark=null;status=null;read=null;
            if(library!=IntPtr.Zero){FreeLibrary(library);library=IntPtr.Zero;}
        }

        public void Dispose(){if(disposed)return;if(active)Deactivate();else ReleaseLibrary();disposed=true;}

        private void RequireLoaded(){if(!active||library==IntPtr.Zero)throw new InvalidOperationException("Native trace is not active.");}
        private static void RequireNoArgs(Dictionary<string,object> args){if(args.Count!=0)throw new ArgumentException("Operation takes no arguments.");}
        private static string String(Dictionary<string,object> args,string key){if(!args.ContainsKey(key)||args[key]==null)throw new ArgumentException("Missing "+key);return Convert.ToString(args[key]);}
        private static uint UInt(Dictionary<string,object> args,string key){return Convert.ToUInt32(args[key]);}
        private static int Int(Dictionary<string,object> args,string key){return Convert.ToInt32(args[key]);}
        private static uint ParsePointer(object value)
        {
            string text=value as string;
            if(text!=null && text.StartsWith("0x",StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(text.Substring(2),16);
            return Convert.ToUInt32(value);
        }
        private static string Hash(string path){using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
        private static string Hex(UIntPtr value){return "0x"+value.ToUInt32().ToString("X8");}
        private static string Pointer(IntPtr value){return "0x"+unchecked((uint)value.ToInt32()).ToString("X8");}
        private static object[] Vector(Vector3 value){return new object[]{value.x,value.y,value.z};}
        private static object[] Quaternion(UnityEngine.Quaternion value){return new object[]{value.x,value.y,value.z,value.w};}
        private static object[] Hexes(UIntPtr[] values){if(values==null)return new object[0];var result=new object[values.Length];for(int i=0;i<values.Length;i++)result[i]=Hex(values[i]);return result;}
        private static object[] RawWorkUnit(NativeEvent value)
        {
            var result=new object[26];
            for(int i=0;i<5;i++)result[i]=Hex(value.Stack[3+i]);
            for(int i=0;i<8;i++)result[5+i]=Hex(value.Payload[i]);
            for(int i=0;i<8;i++)result[13+i]=Hex(value.Frames[i]);
            for(int i=0;i<5;i++)result[21+i]=Hex(value.Extra[i]);
            return result;
        }
        private static object ContactManagerAllocation(NativeEvent value)
        {
            return new Dictionary<string,object> {
                {"context",Hex(value.Stack[0])},{"descriptor",Hex(value.Stack[1])},
                {"materialManager",Hex(value.Stack[2])},{"postFreeCount",value.Stack[3].ToUInt32()},
                {"freeArray",Hex(value.Stack[4])},{"poppedFreeSlot",Hex(value.Stack[5])},
                {"managerIndex",value.Payload[0].ToUInt32()},
                {"rigidCore0",Hex(value.Payload[1])},{"rigidCore1",Hex(value.Payload[2])},
                {"shapeCore0",Hex(value.Payload[3])},{"shapeCore1",Hex(value.Payload[4])},
                {"managerWord60",Hex(value.Payload[5])},
                {"transformCache0",value.Payload[6].ToUInt32()},
                {"transformCache1",value.Payload[7].ToUInt32()}
            };
        }
        private static object[] RawContactManagerPrefix(NativeEvent value)
        {
            var result=new object[13];
            for(int i=0;i<8;i++)result[i]=Hex(value.Frames[i]);
            for(int i=0;i<5;i++)result[8+i]=Hex(value.Extra[i]);
            return result;
        }
        private static object AnimatorStateMachine(NativeEvent value)
        {
            var input=new object[9];
            for(int i=0;i<8;i++)input[i]=Hex(value.Payload[i]);
            input[8]=Hex(value.Extra[0]);
            return new Dictionary<string,object> {
                {"constant",Hex(value.Stack[0])},{"input",Hex(value.Stack[1])},
                {"output",Hex(value.Stack[2])},{"memory",Hex(value.Stack[3])},
                {"workspace",Hex(value.Stack[4])},{"updateGraphSequence",value.Stack[5].ToUInt32()},
                {"layerOrdinal",value.Stack[6].ToUInt32()},{"pairedEntrySequence",value.Stack[7].ToUInt32()},
                {"inputWords",input},{"constantPrefixHash",Hex(value.Frames[0])},
                {"inputHash",Hex(value.Frames[1])},{"outputHash",Hex(value.Frames[2])},
                {"memoryHash",Hex(value.Frames[3])},{"workspaceHash",Hex(value.Frames[4])},
                {"liveDescriptor",Hex(value.Frames[5])},{"memoryCurrentState",value.Frames[6].ToUInt32()},
                {"memoryTransitionDestination",value.Frames[7].ToUInt32()},
                {"memoryPreviousState",value.Extra[3].ToUInt32()},
                {"memoryInterruptedRaw",value.Extra[4].ToUInt32()&0xFFu},
                {"memoryDisabledRaw",(value.Extra[4].ToUInt32()>>8)&0xFFu}
            };
        }
        private static object AnimatorStateMachineMemoryRaw(NativeEvent value)
        {
            var words=new object[28];
            for(int i=0;i<7;i++)words[i]=Hex(value.Stack[1+i]);
            for(int i=0;i<8;i++)words[7+i]=Hex(value.Payload[i]);
            for(int i=0;i<8;i++)words[15+i]=Hex(value.Frames[i]);
            for(int i=0;i<5;i++)words[23+i]=Hex(value.Extra[i]);
            return new Dictionary<string,object> {
                {"parentEvaluateSequence",value.Stack[0].ToUInt32()},
                {"sourceMemory",Hex(value.ReturnAddress)},
                {"captureValid",value.ReturnAddress!=UIntPtr.Zero},
                {"rawWords",words}
            };
        }
        private static object AnimatorStateMachineOutputRaw(NativeEvent value)
        {
            var words=new object[5];
            for(int i=0;i<5;i++)words[i]=Hex(value.Stack[1+i]);
            uint tail=value.Stack[5].ToUInt32();
            return new Dictionary<string,object> {
                {"parentEvaluateSequence",value.Stack[0].ToUInt32()},
                {"sourceOutput",Hex(value.ReturnAddress)},
                {"captureValid",value.ReturnAddress!=UIntPtr.Zero},
                {"rawWords",words},
                {"byteAt0x10",tail&0xFFu},{"byteAt0x11",(tail>>8)&0xFFu},
                {"byteAt0x12",(tail>>16)&0xFFu},{"byteAt0x13",(tail>>24)&0xFFu}
            };
        }
        private static object AnimatorConditionFloat(NativeEvent value)
        {
            return new Dictionary<string,object> {
                {"parentEvaluateSequence",value.Stack[0].ToUInt32()},
                {"updateGraphSequence",value.Stack[1].ToUInt32()},
                {"entryOrdinal",value.Stack[2].ToUInt32()},
                {"condition",Hex(value.Stack[3])},{"descriptor",Hex(value.Stack[4])},
                {"valueArray",Hex(value.Stack[5])},{"typedIndex",value.Stack[6].ToUInt32()},
                {"resolvedValueAddress",Hex(value.Stack[7])},
                {"conditionMode",value.Payload[0].ToUInt32()},{"parameterHash",Hex(value.Payload[1])},
                {"thresholdBits",Hex(value.Payload[2])},{"threshold",Float(value.Payload[2])},
                {"descriptorHash",Hex(value.Payload[3])},{"descriptorType",value.Payload[4].ToUInt32()},
                {"descriptorTypedIndex",value.Payload[5].ToUInt32()},
                {"floatCount",value.Payload[6].ToUInt32()},{"floatArrayRelative",unchecked((int)value.Payload[7].ToUInt32())},
                {"actualValueBits",Hex(value.Frames[0])},{"actualValue",Float(value.Frames[0])},
                {"memoryValueBits",Hex(value.Frames[1])},{"memoryValue",Float(value.Frames[1])},
                {"stateMachineInput",Hex(value.Frames[2])},{"inputValueArray",Hex(value.Frames[3])},
                {"controllerInput",Hex(value.Frames[4])},
                {"controllerInputSpeedBits",Hex(value.Frames[5])},{"controllerInputSpeed",Float(value.Frames[5])},
                {"currentState",value.Extra[0].ToUInt32()},{"nextState",value.Extra[1].ToUInt32()},
                {"currentDurationBits",Hex(value.Extra[2])},{"currentDuration",Float(value.Extra[2])},
                {"stateMachineInputSpeedBits",Hex(value.Extra[3])},{"stateMachineInputSpeed",Float(value.Extra[3])}
            };
        }
        private static object AnimatorStateMixer(NativeEvent value)
        {
            uint retained=value.Payload[7].ToUInt32();
            var weights=new List<object>();
            if(retained!=0xFFFFFFFFu)
            {
                if(retained>13u)retained=13u;
                for(uint i=0;i<retained;i++)
                    weights.Add(Hex(i<8u?value.Frames[i]:value.Extra[i-8u]));
            }
            return new Dictionary<string,object> {
                {"parentEntrySequence",value.Stack[0].ToUInt32()},
                {"updateGraphSequence",value.Stack[1].ToUInt32()},
                {"layerOrdinal",value.Stack[2].ToUInt32()},{"liveIndex",value.Stack[3].ToUInt32()},
                {"trueBranch",value.Stack[4].ToUInt32()!=0},{"live",Hex(value.Stack[5])},
                {"mixer",Hex(value.Stack[6])},{"mixerInternal",Hex(value.Stack[7])},
                {"liveVtable",Hex(value.Payload[0])},{"mixerVtable",Hex(value.Payload[1])},
                {"inputCount",value.Payload[2].ToUInt32()},{"entries",Hex(value.Payload[3])},
                {"activeCount",value.Payload[4].ToUInt32()},{"entryRecordsHash",Hex(value.Payload[5])},
                {"weightHash",Hex(value.Payload[6])},{"retainedWeightBits",weights.ToArray()},
                {"mixerFlagRaw",value.ReturnAddress.ToUInt32()}
            };
        }
        private static object AnimatorOuterMixer(NativeEvent value)
        {
            uint retained=value.Extra[2].ToUInt32();
            var words=new List<object>();
            if(retained!=0xFFFFFFFFu)
            {
                if(retained>16u)retained=16u;
                for(uint i=0;i<retained;i++)
                    words.Add(Hex(i<6u?value.Payload[2+i]:i<14u?value.Frames[i-6u]:value.Extra[i-14u]));
            }
            uint flags=value.Extra[3].ToUInt32();
            return new Dictionary<string,object> {
                {"parentEntrySequence",value.Stack[0].ToUInt32()},
                {"updateGraphSequence",value.Stack[1].ToUInt32()},
                {"layerOrdinal",value.Stack[2].ToUInt32()},{"liveIndex",value.Stack[3].ToUInt32()},
                {"outer",Hex(value.Stack[4])},{"outerInternal",Hex(value.Stack[5])},
                {"entries",Hex(value.Stack[6])},{"inputCount",value.Stack[7].ToUInt32()},
                {"outerVtable",Hex(value.Payload[0])},{"entryRecordsHash",Hex(value.Payload[1])},
                {"retainedEntryWords",words.ToArray()},{"modeRaw",flags&0xFFu},
                {"secondaryArgumentRaw",(flags>>8)&0xFFu},{"slotFlagRaw",(flags>>16)&0xFFu}
            };
        }
        private static object AnimatorTransitionLifecycle(NativeEvent value)
        {
            uint status=value.Extra[4].ToUInt32();
            uint retained=(status>>8)&0xFFu;
            if(retained>9u)retained=9u;
            var words=new List<object>();
            for(uint i=0;i<retained;i++)
                words.Add(Hex(i<4u?value.Payload[4+i]:value.Frames[i-4u]));
            object[] branches=new object[3];
            UIntPtr[] entryHashes={value.Frames[5],value.Frames[7],value.Extra[1]};
            UIntPtr[] stateHashes={value.Frames[6],value.Extra[0],value.Extra[2]};
            for(int i=0;i<3;i++)branches[i]=new Dictionary<string,object> {
                {"slot",i},{"captureValid",(status&(1u<<(16+i)))!=0},
                {"entryRecordsHash",Hex(entryHashes[i])},{"playableStateHash",Hex(stateHashes[i])}
            };
            uint flags=value.Stack[7].ToUInt32();
            bool start=value.Kind==70||value.Kind==71;
            bool exit=value.Kind==69||value.Kind==71;
            return new Dictionary<string,object> {
                {"operation",start?"start-interrupted-transition":"end-transition"},
                {"phase",exit?"exit":"entry"},{"outer",Hex(value.Self)},
                {"contextualController",Hex(value.Stack[0])},
                {"updateGraphSequence",value.Stack[1].ToUInt32()},
                {"layerOrdinal",value.Stack[2].ToUInt32()},
                {"pairedEntrySequence",value.Stack[3].ToUInt32()},
                {"evaluatorEntrySequence",value.Stack[4].ToUInt32()},
                {"argument0Raw",Hex(value.Stack[5])},{"argument1Raw",Hex(value.Stack[6])},
                {"modeRaw",flags&0xFFu},{"secondaryArgumentRaw",(flags>>8)&0xFFu},
                {"slotFlagRaw",(flags>>16)&0xFFu},
                {"outerInternal",Hex(value.Payload[0])},{"outerEntries",Hex(value.Payload[1])},
                {"outerInputCount",value.Payload[2].ToUInt32()},
                {"outerEntryRecordsHash",Hex(value.Payload[3])},
                {"retainedOuterEntryWords",words.ToArray()},
                {"outerSnapshotValid",(status&1u)!=0},{"outerVtableMatches",(status&2u)!=0},
                {"outerVtable",Hex(value.Extra[3])},{"branchSnapshots",branches}
            };
        }
        private static float Float(UIntPtr value){return BitConverter.ToSingle(BitConverter.GetBytes(value.ToUInt32()),0);}
        private static object[] Floats(UIntPtr[] values){if(values==null)return new object[0];var result=new object[values.Length];for(int i=0;i<values.Length;i++)result[i]=Float(values[i]);return result;}
        private static string PathOf(Transform value){string path=value.name;while(value.parent!=null){value=value.parent;path=value.name+"/"+path;}return path;}
        private static string Kind(uint value)
        {
            switch(value){case 1:return "marker";case 10:return "rigidbody-awake-from-load";case 11:return "rigidbody-create";
                case 12:return "rigidbody-move-position";case 13:return "rigidbody-set-is-kinematic";
                case 20:return "scene-add-rigid-body";case 21:return "scene-remove-rigid-body";
                case 30:return "scb-body-set-body2world";case 31:return "np-rigid-dynamic-set-global-pose";
                case 32:return "np-rigid-dynamic-set-kinematic-target";case 33:return "sc-body-core-set-body2world";
                case 34:return "np-rigid-body-set-cmass-local-pose-internal";
                case 35:return "np-rigid-body-set-mass-space-inertia-tensor";
                case 36:return "rigidbody-update-mass-distribution";
                case 37:return "rigidbody-update-mass-distribution-post";
                case 38:return "rigidbody-mass-distribution-shape";
                case 39:return "box-collider-pose-changed";
                case 40:return "physics-manager-simulate";case 41:return "physics-manager-sync-transforms";
                case 42:return "rigidbody-apply-constraints-post-get-pose";
                case 43:return "rigidbody-get-position-post-get-pose";
                case 44:return "px-diagonalize-entry";
                case 45:return "physics-manager-sync-transforms-post";
                case 46:return "transform-queue-changes";
                case 47:return "transform-dispatch-queued";
                case 50:return "pxc-discrete-narrowphase-pcm";
                case 51:return "nphase-core-overlap-created";
                case 52:return "pxs-contact-manager-created";
                case 53:return "animator-update-avatars";
                case 54:return "animator-write-properties";
                case 55:return "director-prepare-stage";
                case 56:return "director-process-stage";
                case 57:return "animator-controller-prepare-frame";
                case 58:return "animator-controller-clear-first-evaluation-flag";
                case 59:return "animator-controller-update-graph";
                case 60:return "animator-evaluate-state-machine-entry";
                case 61:return "animator-evaluate-state-machine-early-exit";
                case 62:return "animator-evaluate-state-machine-exit";
                case 63:return "animator-state-mixer-snapshot";
                case 64:return "animator-outer-mixer-snapshot";
                case 65:return "animator-state-machine-memory-raw";
                case 66:return "animator-state-machine-output-raw";
                case 67:return "animator-condition-float";
                case 68:return "animator-end-transition-entry";
                case 69:return "animator-end-transition-exit";
                case 70:return "animator-start-interrupted-transition-entry";
                case 71:return "animator-start-interrupted-transition-exit";
                default:return "unknown";}
        }
    }
}
