using System;
using System.Collections.Generic;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Diagnostic-only control used to test whether render-timed chef Animator
    // evaluation is the cause of rewind divergence. Activation disables only
    // Animator components below the four registered chefs; deactivation restores
    // each component's exact prior enabled flag. This is not a gameplay fix.
    public sealed class ChefAnimatorControlModule : IAuthoringModule
    {
        private sealed class SavedAnimator
        {
            internal Animator Animator;
            internal int InstanceId;
            internal bool Enabled;
            internal AnimatorCullingMode CullingMode;
            internal AnimatorUpdateMode UpdateMode;
            internal string Path;
        }

        private readonly List<SavedAnimator> saved=new List<SavedAnimator>();
        private string mode="inactive";
        private bool active,disposed;

        public string Name { get { return "chef-animator-control-v1-diagnostic-disable"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("ChefAnimatorControlModule");
            if(args==null)args=new Dictionary<string,object>();
            if(args.Count!=0)throw new ArgumentException("Operation takes no arguments.");
            if(operation=="activate")Activate(true);
            else if(operation=="activate-always-animate")Activate(false);
            else if(operation=="activate-animate-physics")ActivateAnimatePhysics();
            else if(operation=="deactivate")Deactivate();
            else if(operation!="status")throw new ArgumentException("Use activate, activate-always-animate, activate-animate-physics, deactivate or status.");
            return Receipt(operation);
        }

        private void Activate(bool disable)
        {
            RequirePaused();
            if(active)return;
            saved.Clear();
            ServerChefSynchroniser[] chefs=(ServerChefSynchroniser[])UnityEngine.Object.FindObjectsOfType(typeof(ServerChefSynchroniser));
            if(chefs.Length!=4)throw new InvalidOperationException("Diagnostic requires exactly four live registered chefs.");
            var identities=new HashSet<int>();
            foreach(ServerChefSynchroniser chef in chefs)
            {
                Animator[] animators=chef.GetComponentsInChildren<Animator>(true);
                if(animators.Length==0)throw new InvalidOperationException("Chef has no child Animator: "+PathOf(chef.transform));
                foreach(Animator animator in animators)
                {
                    if(animator==null || !identities.Add(animator.GetInstanceID()))continue;
                    saved.Add(new SavedAnimator { Animator=animator,InstanceId=animator.GetInstanceID(),Enabled=animator.enabled,
                        CullingMode=animator.cullingMode,UpdateMode=animator.updateMode,Path=PathOf(animator.transform) });
                }
            }
            if(saved.Count<4)throw new InvalidOperationException("Expected at least one distinct Animator per chef.");
            foreach(SavedAnimator item in saved)
            {
                if(disable)item.Animator.enabled=false;
                else item.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            }
            mode=disable?"disabled":"always-animate";active=true;
        }

        private void ActivateAnimatePhysics()
        {
            Activate(false);
            foreach(SavedAnimator item in saved)
            {
                item.Animator.cullingMode=item.CullingMode;
                item.Animator.updateMode=AnimatorUpdateMode.AnimatePhysics;
            }
            mode="animate-physics";
        }

        private void Deactivate()
        {
            RequirePaused();
            if(!active){saved.Clear();return;}
            foreach(SavedAnimator item in saved)
            {
                if(item.Animator==null)continue;
                if(item.Animator.GetInstanceID()!=item.InstanceId)throw new InvalidOperationException("Animator incarnation changed: "+item.Path);
                item.Animator.enabled=item.Enabled;
                item.Animator.cullingMode=item.CullingMode;
                item.Animator.updateMode=item.UpdateMode;
            }
            active=false;mode="inactive";
            saved.Clear();
        }

        private object Receipt(string operation)
        {
            var rows=new List<object>();
            if(active)
            {
                foreach(SavedAnimator item in saved)rows.Add(AnimatorRow(item.Animator,item.Path,item.Enabled));
            }
            else
            {
                foreach(ServerChefSynchroniser chef in UnityEngine.Object.FindObjectsOfType(typeof(ServerChefSynchroniser)))
                    foreach(Animator animator in chef.GetComponentsInChildren<Animator>(true))
                        rows.Add(AnimatorRow(animator,PathOf(animator.transform),animator.enabled));
            }
            return new Dictionary<string,object> {
                {"name",Name},{"apiVersion",1},{"operation",operation},{"active",active},{"mode",mode},
                {"animatorCount",rows.Count},{"animators",rows.ToArray()},
                {"scope","Diagnostic only: active mode either disables evaluation or forces AlwaysAnimate below the four chefs in both original and replay branches; exact prior enabled and culling values are restored on deactivate."}
            };
        }

        private static object AnimatorRow(Animator animator,string path,bool priorEnabled)
        {
            var layers=new List<object>();
            for(int layer=0;layer<animator.layerCount;layer++)
            {
                AnimatorStateInfo state=animator.GetCurrentAnimatorStateInfo(layer);
                layers.Add(new Dictionary<string,object> {
                    {"layer",layer},{"fullPathHash",state.fullPathHash},{"shortNameHash",state.shortNameHash},
                    {"normalizedTime",state.normalizedTime},{"length",state.length},{"speed",state.speed},
                    {"speedMultiplier",state.speedMultiplier},{"tagHash",state.tagHash},{"loop",state.loop},
                    {"inTransition",animator.IsInTransition(layer)}
                });
            }
            return new Dictionary<string,object> {
                {"instanceId",animator.GetInstanceID()},{"path",path},{"enabled",animator.enabled},
                {"priorEnabled",priorEnabled},{"speed",animator.speed},{"updateMode",animator.updateMode.ToString()},
                {"cullingMode",animator.cullingMode.ToString()},{"applyRootMotion",animator.applyRootMotion},
                {"layerCount",animator.layerCount},{"parameterCount",animator.parameterCount},
                {"controller",animator.runtimeAnimatorController==null?null:animator.runtimeAnimatorController.name},
                {"layers",layers.ToArray()}
            };
        }

        private static void RequirePaused()
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))throw new InvalidOperationException("Chef Animator control requires native pause.");
        }

        public void Dispose()
        {
            if(disposed)return;
            if(active)Deactivate();
            disposed=true;
        }

        private static string PathOf(Transform value)
        {
            string path=value.name;
            while(value.parent!=null){value=value.parent;path=value.name+"/"+path;}
            return path;
        }
    }
}
