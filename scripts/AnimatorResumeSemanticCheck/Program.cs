using System;
using SuperchargedPatch.Authoring.Modules;

internal static class Program
{
    private const int RecordSize=AnimatorResumeSemanticState.MixerRecordSize;
    private static int passed;

    private static int Main()
    {
        try
        {
            Run("mixer role rebase succeeds and retains saved values",MixerRoleRebaseSucceeds);
            Run("mixer subtree mismatch is rejected",MixerSubtreeMismatchRejected);
            Run("mixer non-pointer mismatch is rejected",MixerNonPointerMismatchRejected);
            Run("mixer outer count mismatch is rejected",MixerOuterCountMismatchRejected);
            Run("mixer inner count mismatch is rejected",MixerInnerCountMismatchRejected);
            Run("mixer flag mismatch is rejected",MixerFlagMismatchRejected);
            Run("mixer alias merge is rejected",MixerAliasMergeRejected);
            Run("mixer whole-branch swap is rejected",MixerWholeBranchSwapRejected);
            Run("mixer non-branch child change is rejected",MixerNonBranchPointerRejected);
            Run("mixer broken branch link is rejected",MixerBrokenLinkRejected);
            Run("mixer descriptor identity change is rejected",MixerDescriptorChangeRejected);
            Run("mixer record role change is rejected",MixerRoleChangeRejected);
            Run("controller exact data compares equal",ControllerExactPasses);
            Run("controller +0 state hash may differ with both gates clear",ControllerStateHashPassesWhenClear);
            Run("controller +0 state hash is strict with saved gate set",ControllerStateHashFailsWithSavedGate);
            Run("controller +0 state hash is strict with observed gate set",ControllerStateHashFailsWithObservedGate);
            Run("controller +4 word may differ with both gates clear",ControllerObservedWordPassesWhenClear);
            Run("controller +4 word is strict with saved gate set",ControllerObservedWordFailsWithSavedGate);
            Run("controller +4 word is strict with observed gate set",ControllerObservedWordFailsWithObservedGate);
            Run("controller prefix remains strict",ControllerPrefixMismatchRejected);
            Run("controller other record bytes remain strict",ControllerOtherRecordMismatchRejected);
            Run("controller pending gate shape remains strict",ControllerGateShapeRejected);
            Console.WriteLine("AnimatorResumeSemanticCheck: PASS ("+passed+" tests)");
            return 0;
        }
        catch(Exception exception)
        {
            Console.Error.WriteLine("AnimatorResumeSemanticCheck: FAIL after "+passed+" tests");
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }

    private static void MixerRoleRebaseSucceeds()
    {
        byte[] saved=BuildSavedMixerGraph();
        byte[] observed=BuildObservedMixerGraph(saved);
        byte[] savedBefore=(byte[])saved.Clone();
        byte[] observedBefore=(byte[])observed.Clone();
        byte[] rebased;
        string error;
        True(AnimatorResumeSemanticState.TryRebaseMixerGraph(saved,observed,out rebased,out error),error);
        True(!Object.ReferenceEquals(saved,rebased),"Result must be a new array.");
        BytesEqual(savedBefore,saved,"Saved mixer blob was mutated.");
        BytesEqual(observedBefore,observed,"Observed mixer blob was mutated.");

        byte[] expected=(byte[])saved.Clone();
        CopyWord(observed,1,52,expected,1,52);
        CopyWord(observed,2,52,expected,2,52);
        for(int record=4;record<=5;++record)
        {
            CopyWord(observed,record,52,expected,record,52);
            CopyWord(observed,record,60,expected,record,60);
            CopyWord(observed,record,64,expected,record,64);
            CopyWord(observed,record,68,expected,record,68);
        }
        CopyWord(observed,6,52,expected,6,52);
        CopyWord(observed,6,60,expected,6,60);
        CopyWord(observed,6,64,expected,6,64);
        CopyWord(observed,6,68,expected,6,68);
        BytesEqual(expected,rebased,"Only role allocation fields should be rebased.");
        Equal(ReadWord(saved,3,48),ReadWord(rebased,3,48),"Saved outer weight was not retained.");
        Equal(ReadWord(saved,4,80),ReadWord(rebased,4,80),"Saved inner weight was not retained.");
        True(ReadWord(observed,3,48)!=ReadWord(rebased,3,48),"Observed outer weight leaked into result.");
        True(ReadWord(observed,4,80)!=ReadWord(rebased,4,80),"Observed inner weight leaked into result.");
    }

    private static void MixerSubtreeMismatchRejected()
    {
        byte[] saved=BuildSavedMixerGraph();
        byte[] observed=BuildObservedMixerGraph(saved);
        WriteWord(observed,4,84,0x4004u);
        RejectMixer(saved,observed,"subtree child");
    }

    private static void MixerNonPointerMismatchRejected()
    {
        byte[] saved=BuildSavedMixerGraph();
        byte[] observed=BuildObservedMixerGraph(saved);
        WriteWord(observed,3,56,0xAu);
        RejectMixer(saved,observed,"non-pointer");
    }

    private static void MixerOuterCountMismatchRejected()
    {
        byte[] saved=BuildSavedMixerGraph();
        byte[] observed=BuildObservedMixerGraph(saved);
        WriteWord(observed,2,32,4u);
        RejectMixer(saved,observed,"outer count");
    }

    private static void MixerInnerCountMismatchRejected()
    {
        byte[] saved=BuildSavedMixerGraph();
        byte[] observed=BuildObservedMixerGraph(saved);
        WriteWord(observed,4,72,3u);
        RejectMixer(saved,observed,"inner count");
    }

    private static void MixerFlagMismatchRejected()
    {
        byte[] saved=BuildSavedMixerGraph();
        byte[] observed=BuildObservedMixerGraph(saved);
        WriteWord(observed,4,76,9u);
        WriteWord(observed,5,76,9u);
        RejectMixer(saved,observed,"flag");
    }

    private static void MixerAliasMergeRejected()
    {
        byte[] saved=BuildSavedMixerGraph();
        byte[] observed=BuildObservedMixerGraph(saved);
        WriteWord(observed,2,52,0x5000u);
        WriteWord(observed,6,52,0x5000u);
        WriteWord(observed,6,60,0x5000u);
        RejectMixer(saved,observed,"alias merge");
    }

    private static void MixerWholeBranchSwapRejected()
    {
        byte[] saved=BuildTwoPopulatedBranchGraph();
        byte[] observed=(byte[])saved.Clone();

        // Move both existing branch objects, including their allocation-owned
        // child subtrees and flags, to the opposite outer slot. Connection and
        // mixer weights deliberately remain properties of the destination slot.
        WriteWord(observed,1,52,0x2100u);
        WriteWord(observed,2,52,0x2000u);
        CopyBranchObject(saved,6,observed,4);
        CopyBranchObject(saved,7,observed,5);
        CopyBranchObject(saved,4,observed,6);
        CopyBranchObject(saved,5,observed,7);
        RejectMixer(saved,observed,"whole branch swap");
    }

    private static void MixerNonBranchPointerRejected()
    {
        byte[] saved=BuildSavedMixerGraph();
        byte[] observed=BuildObservedMixerGraph(saved);
        WriteWord(observed,3,52,0x3004u);
        RejectMixer(saved,observed,"non-branch pointer");
    }

    private static void MixerBrokenLinkRejected()
    {
        byte[] saved=BuildSavedMixerGraph();
        byte[] observed=BuildObservedMixerGraph(saved);
        WriteWord(observed,4,60,0x5004u);
        RejectMixer(saved,observed,"broken branch link");
    }

    private static void MixerDescriptorChangeRejected()
    {
        byte[] saved=BuildSavedMixerGraph();
        byte[] observed=BuildObservedMixerGraph(saved);
        WriteWord(observed,0,52,0xA304u);
        RejectMixer(saved,observed,"descriptor");
    }

    private static void MixerRoleChangeRejected()
    {
        byte[] saved=BuildSavedMixerGraph();
        byte[] observed=BuildObservedMixerGraph(saved);
        WriteWord(observed,5,16,0u);
        RejectMixer(saved,observed,"role");
    }

    private static void ControllerExactPasses()
    {
        byte[] value=BuildControllerInput();
        string error;
        True(AnimatorResumeSemanticState.ControllerInputsEqual(value,(byte[])value.Clone(),
            new[]{true,false},new[]{false,true},out error),error);
    }

    private static void ControllerObservedWordPassesWhenClear()
    {
        byte[] saved=BuildControllerInput();
        byte[] observed=(byte[])saved.Clone();
        int offset=AnimatorResumeSemanticState.ControllerInputPrefixSize+4;
        observed[offset]^=0x55;
        observed[offset+1]^=0xAA;
        observed[offset+2]^=0x33;
        observed[offset+3]^=0xCC;
        string error;
        True(AnimatorResumeSemanticState.ControllerInputsEqual(saved,observed,
            new[]{false,false},new[]{false,false},out error),error);
    }

    private static void ControllerStateHashPassesWhenClear()
    {
        byte[] saved=BuildControllerInput();
        byte[] observed=(byte[])saved.Clone();
        int offset=AnimatorResumeSemanticState.ControllerInputPrefixSize;
        observed[offset]^=0x55;
        observed[offset+1]^=0xAA;
        observed[offset+2]^=0x33;
        observed[offset+3]^=0xCC;
        string error;
        True(AnimatorResumeSemanticState.ControllerInputsEqual(saved,observed,
            new[]{false,false},new[]{false,false},out error),error);
    }

    private static void ControllerStateHashFailsWithSavedGate()
    {
        ControllerStateHashRejects(new[]{true,false},new[]{false,false});
    }

    private static void ControllerStateHashFailsWithObservedGate()
    {
        ControllerStateHashRejects(new[]{false,false},new[]{true,false});
    }

    private static void ControllerObservedWordFailsWithSavedGate()
    {
        ControllerObservedWordRejects(new[]{true,false},new[]{false,false});
    }

    private static void ControllerObservedWordFailsWithObservedGate()
    {
        ControllerObservedWordRejects(new[]{false,false},new[]{true,false});
    }

    private static void ControllerPrefixMismatchRejected()
    {
        byte[] saved=BuildControllerInput();
        byte[] observed=(byte[])saved.Clone();
        observed[4]^=1;
        RejectController(saved,observed,new[]{false,false},new[]{false,false},"prefix");
    }

    private static void ControllerOtherRecordMismatchRejected()
    {
        byte[] saved=BuildControllerInput();
        byte[] observed=(byte[])saved.Clone();
        observed[AnimatorResumeSemanticState.ControllerInputPrefixSize+8]^=1;
        RejectController(saved,observed,new[]{false,false},new[]{false,false},"other record byte");
    }

    private static void ControllerGateShapeRejected()
    {
        byte[] saved=BuildControllerInput();
        RejectController(saved,(byte[])saved.Clone(),new[]{false},new[]{false},"gate shape");
    }

    private static void ControllerObservedWordRejects(bool[] savedGates,bool[] observedGates)
    {
        byte[] saved=BuildControllerInput();
        byte[] observed=(byte[])saved.Clone();
        observed[AnimatorResumeSemanticState.ControllerInputPrefixSize+4]^=1;
        RejectController(saved,observed,savedGates,observedGates,"pending gate");
    }

    private static void ControllerStateHashRejects(bool[] savedGates,bool[] observedGates)
    {
        byte[] saved=BuildControllerInput();
        byte[] observed=(byte[])saved.Clone();
        observed[AnimatorResumeSemanticState.ControllerInputPrefixSize]^=1;
        RejectController(saved,observed,savedGates,observedGates,"pending state-hash gate");
    }

    private static byte[] BuildSavedMixerGraph()
    {
        byte[] graph=new byte[7*RecordSize];
        SetRecord(graph,0,2,0,1,0xFFFFFFFFu,0xFFFFFFFFu,
            0xA000u,0xA100u,0xA200u,1,0,0,0,0,0xA300u,0,
            0xA400u,0,0,0,0,0,0,0);
        SetRecord(graph,1,0,0,0,2,0,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0x3F800000u,0x2000u,0,
            0,0,0,0,0,0,0,0);
        SetRecord(graph,2,0,0,0,2,1,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0,0x2100u,0,
            0,0,0,0,0,0,0,0);
        SetRecord(graph,3,0,0,0,2,2,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0x3E800000u,0x3000u,9,
            0,0,0,0,0,0,0,0);
        SetRecord(graph,4,1,0,0,1,0,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0x3F800000u,0x2000u,0,
            0x2000u,0x2200u,0x2300u,2,7,0x3F000000u,0x4000u,0x11u);
        SetRecord(graph,5,1,0,0,1,1,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0x3F800000u,0x2000u,0,
            0x2000u,0x2200u,0x2300u,2,7,0x3F000000u,0x4100u,0x12u);
        SetRecord(graph,6,1,0,0,0,0xFFFFFFFFu,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0,0x2100u,0,
            0x2100u,0x2400u,0x2500u,0,8,0,0,0);
        return graph;
    }

    private static byte[] BuildObservedMixerGraph(byte[] saved)
    {
        byte[] observed=(byte[])saved.Clone();
        WriteWord(observed,1,52,0x5000u);
        WriteWord(observed,2,52,0x5100u);
        for(int record=4;record<=5;++record)
        {
            WriteWord(observed,record,52,0x5000u);
            WriteWord(observed,record,60,0x5000u);
            WriteWord(observed,record,64,0x5200u);
            WriteWord(observed,record,68,0x5300u);
        }
        WriteWord(observed,6,52,0x5100u);
        WriteWord(observed,6,60,0x5100u);
        WriteWord(observed,6,64,0x5400u);
        WriteWord(observed,6,68,0x5500u);
        WriteWord(observed,3,48,0x3E000000u);
        WriteWord(observed,4,80,0x3E800000u);
        WriteWord(observed,5,80,0x3F400000u);
        return observed;
    }

    private static byte[] BuildTwoPopulatedBranchGraph()
    {
        byte[] graph=new byte[8*RecordSize];
        SetRecord(graph,0,2,0,1,0xFFFFFFFFu,0xFFFFFFFFu,
            0xA000u,0xA100u,0xA200u,1,0,0,0,0,0xA300u,0,
            0xA400u,0,0,0,0,0,0,0);
        SetRecord(graph,1,0,0,0,2,0,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0x3F800000u,0x2000u,0,
            0,0,0,0,0,0,0,0);
        SetRecord(graph,2,0,0,0,2,1,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0,0x2100u,0,
            0,0,0,0,0,0,0,0);
        SetRecord(graph,3,0,0,0,2,2,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0,0x3000u,0,
            0,0,0,0,0,0,0,0);
        SetRecord(graph,4,1,0,0,1,0,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0x3F800000u,0x2000u,0,
            0x2000u,0x2200u,0x2300u,2,0xE2u,0x3F800000u,0x4000u,0x11u);
        SetRecord(graph,5,1,0,0,1,1,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0x3F800000u,0x2000u,0,
            0x2000u,0x2200u,0x2300u,2,0xE2u,0,0x4100u,0x12u);
        SetRecord(graph,6,1,0,0,0,0,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0,0x2100u,0,
            0x2100u,0x2400u,0x2500u,2,0xE3u,0,0x4200u,0x21u);
        SetRecord(graph,7,1,0,0,0,1,
            0x1000u,0x1100u,0x1200u,3,0,2,0,0,0x2100u,0,
            0x2100u,0x2400u,0x2500u,2,0xE3u,0x3F800000u,0x4300u,0x22u);
        return graph;
    }

    private static void CopyBranchObject(byte[] source,int sourceRecord,byte[] destination,int destinationRecord)
    {
        // outerChild, mixer, mixerInternal, mixerEntries, mixerFlag,
        // mixerChild and mixerWord8 follow the branch object. Count is equal in
        // this fixture, while outer/mixer weights remain destination-slot data.
        int[] offsets={52,60,64,68,76,84,88};
        for(int i=0;i<offsets.Length;++i)
            CopyWord(source,sourceRecord,offsets[i],destination,destinationRecord,offsets[i]);
    }

    private static byte[] BuildControllerInput()
    {
        byte[] result=new byte[AnimatorResumeSemanticState.ControllerInputPrefixSize+
            2*AnimatorResumeSemanticState.ControllerInputRecordSize];
        for(int i=0;i<result.Length;++i)result[i]=(byte)(i*37+11);
        return result;
    }

    private static void SetRecord(byte[] bytes,int record,uint kind,uint layer,uint stateMachine,
        uint trueBranch,uint input,uint outer,uint outerInternal,uint outerEntries,uint outerCount,
        uint outerHash,uint outerMode,uint outerArgument,uint outerWeight,uint outerChild,uint outerWord8,
        uint mixer,uint mixerInternal,uint mixerEntries,uint mixerCount,uint mixerFlag,uint mixerWeight,
        uint mixerChild,uint mixerWord8)
    {
        uint[] words={kind,layer,stateMachine,trueBranch,input,outer,outerInternal,outerEntries,
            outerCount,outerHash,outerMode,outerArgument,outerWeight,outerChild,outerWord8,mixer,
            mixerInternal,mixerEntries,mixerCount,mixerFlag,mixerWeight,mixerChild,mixerWord8};
        for(int i=0;i<words.Length;++i)WriteWord(bytes,record,i*4,words[i]);
    }

    private static void CopyWord(byte[] source,int sourceRecord,int sourceOffset,
        byte[] destination,int destinationRecord,int destinationOffset)
    {
        for(int i=0;i<4;++i)
            destination[destinationRecord*RecordSize+destinationOffset+i]=
                source[sourceRecord*RecordSize+sourceOffset+i];
    }

    private static void WriteWord(byte[] bytes,int record,int offset,uint value)
    {
        int position=record*RecordSize+offset;
        bytes[position]=(byte)value;
        bytes[position+1]=(byte)(value>>8);
        bytes[position+2]=(byte)(value>>16);
        bytes[position+3]=(byte)(value>>24);
    }

    private static uint ReadWord(byte[] bytes,int record,int offset)
    {
        int position=record*RecordSize+offset;
        return (uint)(bytes[position]|bytes[position+1]<<8|bytes[position+2]<<16|bytes[position+3]<<24);
    }

    private static void RejectMixer(byte[] saved,byte[] observed,string context)
    {
        byte[] savedBefore=(byte[])saved.Clone();
        byte[] observedBefore=(byte[])observed.Clone();
        byte[] rebased;
        string error;
        True(!AnimatorResumeSemanticState.TryRebaseMixerGraph(saved,observed,out rebased,out error),
            "Expected mixer rejection for "+context+".");
        True(rebased==null,"Rejected mixer comparison returned a blob for "+context+".");
        True(!String.IsNullOrEmpty(error),"Rejected mixer comparison omitted an error for "+context+".");
        BytesEqual(savedBefore,saved,"Rejected comparison mutated saved mixer data for "+context+".");
        BytesEqual(observedBefore,observed,"Rejected comparison mutated observed mixer data for "+context+".");
    }

    private static void RejectController(byte[] saved,byte[] observed,bool[] savedGates,
        bool[] observedGates,string context)
    {
        string error;
        True(!AnimatorResumeSemanticState.ControllerInputsEqual(saved,observed,savedGates,observedGates,out error),
            "Expected ControllerInput rejection for "+context+".");
        True(!String.IsNullOrEmpty(error),"Rejected ControllerInput comparison omitted an error for "+context+".");
    }

    private static void Run(string name,Action action)
    {
        action();
        ++passed;
        Console.WriteLine("PASS: "+name);
    }

    private static void True(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
    }

    private static void Equal(uint expected,uint actual,string message)
    {
        if(expected!=actual)throw new InvalidOperationException(message+
            " Expected 0x"+expected.ToString("X8")+", actual 0x"+actual.ToString("X8")+".");
    }

    private static void BytesEqual(byte[] expected,byte[] actual,string message)
    {
        if(expected.Length!=actual.Length)throw new InvalidOperationException(message+" Length differs.");
        for(int i=0;i<expected.Length;++i)
            if(expected[i]!=actual[i])throw new InvalidOperationException(message+" First difference at "+i+".");
    }
}
