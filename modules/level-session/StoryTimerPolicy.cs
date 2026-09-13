using System;
using System.Collections.Generic;
using System.Reflection;
using GameModes;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperchargedPatch.Authoring.Modules
{
    // Explicit user-authorized variant: 1-1 starts its native150s countdown at
    // gameplay start. Only the native delivery suppressor prerequisite changes.
    public sealed class StoryTimerPolicy : IDisposable
    {
        private const string Owner = "supercharged.module.story11.immediate-timer";
        private static StoryTimerPolicy active;
        private Harmony harmony;
        private readonly Action onReceipt;
        private readonly Dictionary<KitchenLevelConfigBase,int> originals = new Dictionary<KitchenLevelConfigBase,int>();
        private readonly List<object> receipts = new List<object>();
        public StoryTimerPolicy(Action onReceipt) { this.onReceipt=onReceipt; }
        public void Install()
        {
            if(active!=null && !System.Object.ReferenceEquals(active,this)) throw new InvalidOperationException("Another Story11 timer policy is active.");
            var prefix=typeof(StoryTimerPolicy).GetMethod("BeforeBegin",BindingFlags.Static|BindingFlags.Public);
            var server=typeof(ServerCampaignMode).GetMethod("Begin",Type.EmptyTypes);
            var client=typeof(ClientCampaignMode).GetMethod("Begin",Type.EmptyTypes);
            if(prefix==null||server==null||client==null)throw new MissingMethodException("Native CampaignMode.Begin timer boundary.");
            harmony=new Harmony(Owner);active=this;
            try { harmony.Patch(server,new HarmonyMethod(prefix)); harmony.Patch(client,new HarmonyMethod(prefix)); }
            catch { harmony.UnpatchSelf();active=null;throw; }
        }
        public void ApplySelected(KitchenLevelConfigBase config,string phase)
        {
            var session=GameUtils.GetGameSession();
            var variant=session==null||session.LevelSettings==null?null:session.LevelSettings.SceneDirectoryVarientEntry;
            if(session==null||session.DLC!=-1||variant==null||variant.PlayerCount!=4||
                !String.Equals(variant.SceneName,LevelSelection.Scene,StringComparison.OrdinalIgnoreCase)||
                !System.Object.ReferenceEquals(config,variant.LevelConfig)||config==null||config.name!="Sushi_1_1S_4P"||config.GetTimeLimit()!=150f)
                throw new InvalidOperationException("Immediate timer policy requires the exact native Story11 four-player150s config.");
            int original;
            if(!originals.TryGetValue(config,out original))
            {
                original=config.m_recipesBeforeTimerStarts;
                if(original!=1)throw new InvalidOperationException("Unrecognized native Story11 first-delivery prerequisite.");
                originals.Add(config,original);
            }
            int before=config.m_recipesBeforeTimerStarts;
            if(before!=0 && before!=original)throw new InvalidOperationException("Story11 timer prerequisite changed outside this policy.");
            config.m_recipesBeforeTimerStarts=0;
            if(config.GetTimeLimit()!=150f||config.m_recipesBeforeTimerStarts!=0)throw new InvalidOperationException("Native Story11 timer config postcondition failed.");
            receipts.Add(new Dictionary<string,object>{{"phase",phase},{"unityFrame",Time.frameCount},{"config",config.name},{"configInstanceId",config.GetInstanceID()},
                {"original",original},{"before",before},{"after",0},{"nativeDurationSeconds",150f},{"immediateStory11Timer",true},{"assetFileWritten",false}});
            if(onReceipt!=null)onReceipt();
        }
        public static void BeforeBegin(object __instance)
        {
            if(active==null)return;
            var session=GameUtils.GetGameSession();
            var variant=session==null||session.LevelSettings==null?null:session.LevelSettings.SceneDirectoryVarientEntry;
            if(session==null||session.DLC!=-1||variant==null||!String.Equals(variant.SceneName,LevelSelection.Scene,StringComparison.OrdinalIgnoreCase))return;
            if(!String.Equals(SceneManager.GetActiveScene().name,LevelSelection.Scene,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Story11 Begin occurred in a different native scene.");
            var field=__instance.GetType().GetField("m_context",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(field==null)throw new MissingFieldException(__instance.GetType().FullName,"m_context");
            object context=field.GetValue(__instance);
            var configField=context==null?null:context.GetType().GetField("m_levelConfig",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(configField==null)throw new MissingFieldException("Native campaign context.m_levelConfig");
            active.ApplySelected(configField.GetValue(context) as KitchenLevelConfigBase,__instance.GetType().Name+".Begin-prefix");
        }
        public object Diagnostics() { return new Dictionary<string,object>{{"installed",System.Object.ReferenceEquals(active,this)},{"scope","base-main-1-1/four-local-players"},{"immediateStory11Timer",true},{"receipts",receipts.ToArray()}}; }
        public void Dispose()
        {
            if(harmony!=null)harmony.UnpatchSelf();
            if(System.Object.ReferenceEquals(active,this))active=null;
            foreach(var item in originals)if(item.Key!=null&&item.Key.m_recipesBeforeTimerStarts==0)item.Key.m_recipesBeforeTimerStarts=item.Value;
            originals.Clear();
        }
    }
}

