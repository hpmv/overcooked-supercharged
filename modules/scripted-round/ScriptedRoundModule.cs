using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.AlteredComponents;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Extensions;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    public sealed class ScriptedRoundModule : IAuthoringModule
    {
        private static ScriptedRoundModule active;
        private Harmony harmony;
        private bool disposed;
        private readonly Dictionary<WarpableRoundData,ScriptedRoundBinding> bindings=new Dictionary<WarpableRoundData,ScriptedRoundBinding>();
        private string lastError;
        public string Name {get{return "native-story11-scripted-round-v1";}}
        public int ApiVersion {get{return 1;}}
        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("ScriptedRoundModule");
            if(args!=null&&args.Count!=0)throw new ArgumentException("Scripted-round operations take no arguments.");
            if(operation=="activate")Activate();
            else if(operation=="validate-current")ValidateCurrentNativeSource();
            else if(operation=="deactivate")Deactivate();
            else if(operation!="status")throw new ArgumentException("Use activate, validate-current, deactivate or status.");
            var rows=new List<object>();foreach(var binding in bindings.Values)rows.Add(binding.Diagnostics());
            return new Dictionary<string,object>{{"name",Name},{"active",ReferenceEquals(active,this)},{"lastError",lastError},{"bindings",rows.ToArray()},
                {"requiresNativeLevelReload",true},{"existingUnwrappedRoundAdopted",false},{"scope","Only live base-game Story11 four-player config"}};
        }
        private void Activate()
        {
            if(ReferenceEquals(active,this))return;
            if(active!=null)throw new InvalidOperationException("Another scripted-round adapter is active.");
            harmony=new Harmony("supercharged.module.story11.scripted-round");active=this;
            try
            {
                harmony.Patch(typeof(ScriptedCampaignLevelConfig).GetMethod("GetRoundData",Type.EmptyTypes),postfix:new HarmonyMethod(typeof(ScriptedRoundModule).GetMethod("AfterGetRoundData")));
                harmony.Patch(typeof(WarpableRoundData).GetMethod("GetNextRecipe",new[]{typeof(RoundInstanceDataBase)}),prefix:new HarmonyMethod(typeof(ScriptedRoundModule).GetMethod("BeforeNextRecipe")));
            }
            catch {harmony.UnpatchSelf();active=null;throw;}
        }
        private void ValidateCurrentNativeSource()
        {
            if(!ReferenceEquals(active,this))throw new InvalidOperationException("Activate before validating the current native source.");
            var session=GameUtils.GetGameSession();var v=session==null||session.LevelSettings==null?null:session.LevelSettings.SceneDirectoryVarientEntry;
            var config=v==null?null:v.LevelConfig as ScriptedCampaignLevelConfig;
            if(session==null||session.DLC!=-1||v==null||v.PlayerCount!=4||v.SceneName!="s_sushi_1_1"||config==null||config.name!="Sushi_1_1S_4P")
                throw new InvalidOperationException("Current native scene/config is not Story11 four-player scripted.");
            int count=0;
            foreach(var flow in UnityEngine.Object.FindObjectsOfType<ServerKitchenFlowControllerBase>())
            {
                var monitor=flow.GetMonitorForTeam(TeamID.One);var orders=monitor==null?null:monitor.OrdersController;
                if(orders==null)continue;
                RoundData original=orders.GetRoundData();
                var wrapper=original as WarpableRoundData;
                if(wrapper!=null&&bindings.ContainsKey(wrapper)){bindings[wrapper].RequireMetadata();count++;continue;}
                var source=original as ScriptedRoundData;
                if(source==null||source.GetType()!=typeof(ScriptedRoundData)||config.m_rounds==null||config.m_rounds.Length!=1||!ReferenceEquals(config.m_rounds[0],source))
                    throw new InvalidOperationException("Current native controller generator is not the exact selected scripted asset.");
                bool found=false;foreach(var b in bindings.Values)if(ReferenceEquals(b.Source,source)){b.RequireMetadata();found=true;break;}
                if(!found){var b=new ScriptedRoundBinding(source);bindings.Add(b.Wrapper,b);}
                count++;
            }
            if(count!=1)throw new InvalidOperationException("Expected one authoritative native Story11 order controller.");
        }
        public static void AfterGetRoundData(ScriptedCampaignLevelConfig __instance,ref RoundData __result)
        {
            if(active==null)return;
            var session=GameUtils.GetGameSession();
            var variant=session==null||session.LevelSettings==null?null:session.LevelSettings.SceneDirectoryVarientEntry;
            if(session==null||session.DLC!=-1||variant==null||!String.Equals(variant.SceneName,"s_sushi_1_1",StringComparison.OrdinalIgnoreCase))return;
            if(variant.PlayerCount!=4||!ReferenceEquals(variant.LevelConfig,__instance)||__instance.name!="Sushi_1_1S_4P")
                throw new InvalidOperationException("Scripted round requires exact native Story11 four-player config identity.");
            if(__result==null||__result.GetType()!=typeof(ScriptedRoundData))throw new InvalidOperationException("Unsupported native Story11 round generator.");
            try
            {
                foreach(var binding in active.bindings.Values)if(ReferenceEquals(binding.Source,__result))
                {binding.RequireMetadata();__result=binding.Wrapper;return;}
                var created=new ScriptedRoundBinding((ScriptedRoundData)__result);
                active.bindings.Add(created.Wrapper,created);__result=created.Wrapper;
            }
            catch(Exception e){active.lastError=e.ToString();throw;}
        }
        public static bool BeforeNextRecipe(WarpableRoundData __instance,RoundInstanceDataBase data,ref RecipeList.Entry[] __result)
        {
            ScriptedRoundBinding binding;
            if(active==null||!active.bindings.TryGetValue(__instance,out binding))return true;
            try {RecipeList.Entry[] manual;if(!binding.TryManual(data,out manual))return true;__result=manual;return false;}
            catch(Exception e){active.lastError=e.ToString();throw;}
        }
        private void Deactivate()
        {
            // Both manual replay and core preview can revisit prefix entries long
            // after the live cursor has passed6. Keep the handler for its lifetime.
            foreach(var flow in UnityEngine.Object.FindObjectsOfType<ServerKitchenFlowControllerBase>())
            {
                var monitor=flow.GetMonitorForTeam(TeamID.One);var orders=monitor==null?null:monitor.OrdersController;
                var wrapper=orders==null?null:orders.GetRoundData() as WarpableRoundData;
                if(wrapper!=null&&bindings.ContainsKey(wrapper))throw new InvalidOperationException("Cannot remove scripted adapter while its native round is live; leave/reload to an unwrapped level first.");
            }
            if(harmony!=null)harmony.UnpatchSelf();
            if(ReferenceEquals(active,this))active=null;
            bindings.Clear();
        }
        public void Dispose(){if(disposed)return;Deactivate();disposed=true;}
    }
}
