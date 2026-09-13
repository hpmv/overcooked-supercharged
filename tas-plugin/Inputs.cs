using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Oc2Tas
{
    [Serializable]
    public class ChefInput
    {
        public int player;
        public float x, y;
        public bool pickup, use, dash;
    }

    public static class Inputs
    {
        private static readonly ChefInput[] pads = { new ChefInput { player=0 }, new ChefInput { player=1 }, new ChefInput { player=2 }, new ChefInput { player=3 } };
        private static readonly Button[,] buttons = new Button[4, 4];
        private static readonly Axis[,] axes = new Axis[4, 2];
        public static bool Active = true;

        public static void Install(Harmony h)
        {
            for (int p=0;p<4;p++)
            {
                for (int b=0;b<4;b++) buttons[p,b]=new Button(p,b);
                for (int a=0;a<2;a++) axes[p,a]=new Axis(p,a);
            }
            h.Patch(AccessTools.Method(typeof(PlayerInputLookup), "GetButton"), postfix:new HarmonyMethod(typeof(Inputs), "GetButton"));
            h.Patch(AccessTools.Method(typeof(PlayerInputLookup), "GetValue"), postfix:new HarmonyMethod(typeof(Inputs), "GetValue"));
            h.Patch(AccessTools.Method(typeof(PlayerControls), "CanButtonBePressed"), transpiler:new HarmonyMethod(typeof(Inputs), "PermitBackgroundDevice"));
        }

        private static IEnumerable<CodeInstruction> PermitBackgroundDevice(IEnumerable<CodeInstruction> code)
        {
            MethodInfo focus=AccessTools.PropertyGetter(typeof(Application),"isFocused");
            MethodInfo replacement=AccessTools.Method(typeof(Inputs),"DeviceHasFocus");
            foreach(CodeInstruction instruction in code)
            {
                if(instruction.opcode==OpCodes.Call && object.Equals(instruction.operand,focus))instruction.operand=replacement;
                yield return instruction;
            }
        }
        private static bool DeviceHasFocus(){return Active||Application.isFocused;}

        public static void Apply(ChefInput[] states)
        {
            for (int i=0;i<4;i++) pads[i]=new ChefInput { player=i };
            if (states != null) foreach (ChefInput s in states)
            {
                if (s.player<0 || s.player>=4) throw new ArgumentException("player must be 0..3");
                if (float.IsNaN(s.x)||float.IsInfinity(s.x)||float.IsNaN(s.y)||float.IsInfinity(s.y)) throw new ArgumentException("axes must be finite");
                float mag=(float)Math.Sqrt(s.x*s.x+s.y*s.y);
                pads[s.player]=new ChefInput {player=s.player,x=mag>1?s.x/mag:s.x,y=mag>1?s.y/mag:s.y,pickup=s.pickup,use=s.use,dash=s.dash};
            }
            // Native LogicalButtonBase implements edge claiming and held duration.
            for(int p=0;p<4;p++) for(int b=0;b<4;b++) buttons[p,b].Poll();
        }
        public static void Release() { Apply(null); }
        public static ChefInput[] Current() { return pads; }

        private static void GetButton(PlayerInputLookup.LogicalButtonID __0, PlayerInputLookup.Player __1, ref ILogicalButton __result)
        {
            int p=(int)__1; if(!Active||p<0||p>3) return;
            int b=__0==PlayerInputLookup.LogicalButtonID.PickupAndDrop?0:__0==PlayerInputLookup.LogicalButtonID.WorkstationInteract?1:__0==PlayerInputLookup.LogicalButtonID.Dash?2:__0==PlayerInputLookup.LogicalButtonID.Curse?3:-1;
            if(b<0) return;
            buttons[p,b].Source=__result; __result=buttons[p,b];
        }
        private static void GetValue(PlayerInputLookup.LogicalValueID __0, PlayerInputLookup.Player __1, ref ILogicalValue __result)
        {
            int p=(int)__1; if(!Active||p<0||p>3) return;
            int a=__0==PlayerInputLookup.LogicalValueID.MovementX?0:__0==PlayerInputLookup.LogicalValueID.MovementY?1:-1;
            if(a<0)return;
            axes[p,a].Source=__result; __result=axes[p,a];
        }

        private sealed class Button : LogicalButtonBase
        {
            public ILogicalButton Source;
            private readonly int p,b;
            public Button(int player,int button) {p=player;b=button;}
            public override bool IsDown() {return b==0?pads[p].pickup:b==1?pads[p].use:b==2?pads[p].dash:false;}
            // Emulated devices remain connected when the diagnostic terminal has focus.
            protected override bool CanProcessInput() {return Active;}
            public void Poll() {Update(IsDown());}
            public override void GetLogicTreeData(out AcyclicGraph<ILogicalElement,LogicalLinkInfo> graph,out AcyclicGraph<ILogicalElement,LogicalLinkInfo>.Node head)
            {if(Source!=null){Source.GetLogicTreeData(out graph,out head);return;}graph=new AcyclicGraph<ILogicalElement,LogicalLinkInfo>(this);head=graph.GetNode(this);}
        }
        private sealed class Axis : ILogicalValue
        {
            public ILogicalValue Source;
            private readonly int p,a;
            public Axis(int player,int axis){p=player;a=axis;}
            public float GetValue(){return a==0?pads[p].x:pads[p].y;}
            public void GetLogicTreeData(out AcyclicGraph<ILogicalElement,LogicalLinkInfo> graph,out AcyclicGraph<ILogicalElement,LogicalLinkInfo>.Node head)
            {if(Source!=null){Source.GetLogicTreeData(out graph,out head);return;}graph=new AcyclicGraph<ILogicalElement,LogicalLinkInfo>(this);head=graph.GetNode(this);}
        }
    }
}
