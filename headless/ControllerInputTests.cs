using Google.Protobuf;
using Hpmv;
using System.Numerics;

namespace Supercharged.Headless;

/// <summary>Production held-button encoding and persisted branch continuation.</summary>
public static class ControllerInputTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool condition,string name) { if(!condition)throw new InvalidOperationException("Controller input: "+name);checks++; }
        void Button(ButtonOutput value,bool down,bool pressed,bool released,string name) =>
            Check(value.isDown==down && value.justPressed==pressed && value.justReleased==released,name);
        var state=new ControllerState();
        var states=new Versioned<ControllerState>(state);
        var actuals=new Versioned<ActualControllerInput>(default);
        bool[] held={true,true,true,false,false,true,false,false};
        bool previous=false;
        for(int i=0;i<held.Length;i++)
        {
            var (next,actual)=state.ApplyInputAndAdvanceFrame(new DesiredControllerInput { axes=new Vector2(.25f,-.5f),dash=held[i] });
            Button(actual.dash,held[i],held[i]&&!previous,!held[i]&&previous,"dash transition "+i);
            Check(next.dashButtonDown==held[i] && actual.axes==new Vector2(.25f,-.5f),"state and axes "+i);
            Button(actual.primary,false,false,false,"primary unchanged "+i);
            Button(actual.secondary,false,false,false,"secondary unchanged "+i);
            states.ChangeTo(next,i+1);actuals.ChangeTo(actual,i+1);state=next;previous=held[i];
        }
        // The old wire format has no field11; protobuf must default to up.
        var oldBytes=new Hpmv.Save.ControllerState { Axes=new Hpmv.Save.Vector2() }.ToByteArray();
        var legacy=Hpmv.Save.ControllerState.Parser.ParseFrom(oldBytes).FromProto();
        Check(!legacy.dashButtonDown,"old protobuf defaults to released");
        var restored=Hpmv.Save.ControllerState.Parser.ParseFrom(states[2].ToProto().ToByteArray()).FromProto();
        Check(restored.dashButtonDown,"held dash persists through actual protobuf bytes");
        var (continued,hold)=restored.ApplyInputAndAdvanceFrame(new DesiredControllerInput { dash=true });
        Button(hold.dash,true,false,false,"restored held dash does not invent second edge");
        var (_,release)=continued.ApplyInputAndAdvanceFrame(default);
        Button(release.dash,false,false,true,"restored held dash releases exactly once");
        states.RemoveAllAfter(2);actuals.RemoveAllAfter(2);
        var (_,branched)=states[2].ApplyInputAndAdvanceFrame(default);
        actuals.ChangeTo(branched,3);
        Button(actuals[3].dash,false,false,true,"history branch uses checkpoint held state");
        Check(actuals[3].ToProto().Dash.JustReleased && !actuals[3].ToProto().Dash.IsDown,"history protobuf retains real release");
        var roundtrip=Hpmv.Save.ActualControllerInput.Parser.ParseFrom(actuals[1].ToProto().ToByteArray()).FromProto();
        Button(roundtrip.dash,true,true,false,"actual history roundtrip includes native held edge");
        // Dash remains independent of existing pickup/secondary policy.
        var (picked,pickup)=legacy.ApplyInputAndAdvanceFrame(new DesiredControllerInput { primaryDown=true,primaryDownIsForPickup=true,dash=true });
        Button(pickup.primary,true,true,false,"pickup rise unchanged");
        Button(pickup.dash,true,true,false,"dash can coexist with primary");
        Check(picked.pickupCooldown>TimeSpan.Zero,"existing pickup cooldown retained");
        var (up,released)=picked.ApplyInputAndAdvanceFrame(new DesiredControllerInput { primaryUp=true });
        Button(released.primary,false,false,true,"pickup release unchanged");
        Button(released.dash,false,false,true,"dash neutral release alongside pickup");
        var (_,neutral)=up.ApplyInputAndAdvanceFrame(default);
        Button(neutral.dash,false,false,false,"second neutral tail has no repeated release");
        return checks;
    }
}
