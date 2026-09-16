using System;
using System.Collections.Generic;

namespace SuperchargedPatch.Authoring.Modules
{
    /// <summary>
    /// Pure managed validation used to distinguish saved Animator values from
    /// allocation identities which belong to the currently observed graph.
    /// This class deliberately has no Unity or native-checkpoint dependency.
    /// </summary>
    public static class AnimatorResumeSemanticState
    {
        public const int MixerRecordSize=92;
        public const int ControllerInputPrefixSize=12;
        public const int ControllerInputRecordSize=24;

        private const uint MissingInput=0xFFFFFFFFu;
        private static readonly int[] PointerOffsets={20,24,28,52,60,64,68,84};

        private sealed class MixerRecord
        {
            public int Index;
            public uint Kind;
            public uint Layer;
            public uint StateMachine;
            public uint TrueBranch;
            public uint Input;
            public uint Outer;
            public uint OuterInternal;
            public uint OuterEntries;
            public uint OuterInputCount;
            public uint OuterEntriesHash;
            public uint OuterMode;
            public uint OuterArgument;
            public uint OuterWeight;
            public uint OuterChild;
            public uint OuterWord8;
            public uint Mixer;
            public uint MixerInternal;
            public uint MixerEntries;
            public uint MixerInputCount;
            public uint MixerFlag;
            public uint MixerWeight;
            public uint MixerChild;
            public uint MixerWord8;
        }

        /// <summary>
        /// Validates two native 92-byte-record mixer observations as the same
        /// semantic graph, then returns a clone of savedBlob whose permitted
        /// branch-allocation fields refer to the observed graph. Saved weights
        /// are retained. Neither input array is mutated.
        /// </summary>
        public static bool TryRebaseMixerGraph(byte[] savedBlob,byte[] observedBlob,
            out byte[] rebasedBlob,out string error)
        {
            rebasedBlob=null;
            error=null;
            MixerRecord[] saved;
            MixerRecord[] observed;
            if(!TryParseMixerGraph(savedBlob,"saved",out saved,out error))return false;
            if(!TryParseMixerGraph(observedBlob,"observed",out observed,out error))return false;
            if(saved.Length!=observed.Length)
            {
                error="Mixer record count differs: saved="+saved.Length+", observed="+observed.Length+".";
                return false;
            }

            byte[] result=(byte[])savedBlob.Clone();
            for(int recordIndex=0;recordIndex<saved.Length;++recordIndex)
            {
                int recordBase=recordIndex*MixerRecordSize;
                for(int recordOffset=0;recordOffset<MixerRecordSize;++recordOffset)
                {
                    int offset=recordBase+recordOffset;
                    if(savedBlob[offset]==observedBlob[offset])continue;
                    if(IsWeightByte(saved[recordIndex],recordOffset))continue;
                    if(IsRebasedAllocationByte(saved[recordIndex],recordOffset))
                    {
                        result[offset]=observedBlob[offset];
                        continue;
                    }
                    error="Mixer semantic mismatch at record "+recordIndex+
                        ", offset "+recordOffset+" (saved="+savedBlob[offset]+
                        ", observed="+observedBlob[offset]+").";
                    return false;
                }
            }

            if(!HasSamePointerAliases(savedBlob,observedBlob,saved,out error))return false;
            rebasedBlob=result;
            return true;
        }

        /// <summary>
        /// Compares the ControllerInput prefix and its 24-byte layer records.
        /// The requested-state hash at layer offset +0 and normalized-time
        /// offset at +4 are consumed-command residue when both the saved and
        /// observed pending gates for that layer are clear.
        /// </summary>
        public static bool ControllerInputsEqual(byte[] savedBlob,byte[] observedBlob,
            bool[] savedPendingByLayer,bool[] observedPendingByLayer,out string error)
        {
            error=null;
            if(savedBlob==null||observedBlob==null)
            {
                error="ControllerInput blobs must be non-null.";
                return false;
            }
            if(savedPendingByLayer==null||observedPendingByLayer==null)
            {
                error="ControllerInput pending gates must be non-null.";
                return false;
            }
            if(savedPendingByLayer.Length!=observedPendingByLayer.Length)
            {
                error="ControllerInput pending-gate counts differ.";
                return false;
            }
            int expectedSize;
            try
            {
                expectedSize=checked(ControllerInputPrefixSize+
                    savedPendingByLayer.Length*ControllerInputRecordSize);
            }
            catch(OverflowException)
            {
                error="ControllerInput layer count is too large.";
                return false;
            }
            if(savedBlob.Length!=expectedSize||observedBlob.Length!=expectedSize)
            {
                error="ControllerInput shape differs from the supplied pending gates.";
                return false;
            }
            for(int offset=0;offset<ControllerInputPrefixSize;++offset)
            {
                if(savedBlob[offset]==observedBlob[offset])continue;
                error="ControllerInput prefix mismatch at offset "+offset+".";
                return false;
            }
            for(int layer=0;layer<savedPendingByLayer.Length;++layer)
            {
                int recordBase=ControllerInputPrefixSize+layer*ControllerInputRecordSize;
                bool mayIgnoreObservedWord=!savedPendingByLayer[layer]&&!observedPendingByLayer[layer];
                for(int recordOffset=0;recordOffset<ControllerInputRecordSize;++recordOffset)
                {
                    int offset=recordBase+recordOffset;
                    if(savedBlob[offset]==observedBlob[offset])continue;
                    if(mayIgnoreObservedWord&&recordOffset>=0&&recordOffset<8)continue;
                    error="ControllerInput mismatch at layer "+layer+
                        ", record offset "+recordOffset+".";
                    return false;
                }
            }
            return true;
        }

        private static bool TryParseMixerGraph(byte[] blob,string name,
            out MixerRecord[] records,out string error)
        {
            records=null;
            error=null;
            if(blob==null)
            {
                error="The "+name+" mixer blob is null.";
                return false;
            }
            if(blob.Length==0||blob.Length%MixerRecordSize!=0)
            {
                error="The "+name+" mixer blob is not a non-empty sequence of 92-byte records.";
                return false;
            }
            int count=blob.Length/MixerRecordSize;
            MixerRecord[] parsed=new MixerRecord[count];
            for(int i=0;i<count;++i)parsed[i]=ReadRecord(blob,i);

            int cursor=0;
            uint expectedLayer=0;
            List<MixerRecord> descriptors=new List<MixerRecord>();
            while(cursor<count)
            {
                MixerRecord descriptor=parsed[cursor];
                if(descriptor.Kind!=2||descriptor.Layer!=expectedLayer||
                   descriptor.TrueBranch!=MissingInput||descriptor.Input!=MissingInput)
                {
                    error=RecordError(name,descriptor,"expected synthetic layer descriptor "+expectedLayer);
                    return false;
                }
                descriptors.Add(descriptor);
                ++cursor;

                for(uint stateMachine=0;stateMachine<descriptor.StateMachine;++stateMachine)
                {
                    if(cursor>=count)
                    {
                        error="The "+name+" mixer blob ends before layer "+expectedLayer+
                            ", state machine "+stateMachine+".";
                        return false;
                    }
                    MixerRecord firstOuter=parsed[cursor];
                    uint outerCount=firstOuter.OuterInputCount;
                    if(outerCount<2)
                    {
                        error=RecordError(name,firstOuter,"outer input count is less than two");
                        return false;
                    }
                    MixerRecord[] outerByInput=new MixerRecord[outerCount];
                    for(uint input=0;input<outerCount;++input)
                    {
                        if(cursor>=count)
                        {
                            error="The "+name+" mixer blob ends inside the outer mixer records.";
                            return false;
                        }
                        MixerRecord outer=parsed[cursor++];
                        if(outer.Kind!=0||outer.Layer!=expectedLayer||
                           outer.StateMachine!=stateMachine||outer.TrueBranch!=2||
                           outer.Input!=input||outer.OuterInputCount!=outerCount)
                        {
                            error=RecordError(name,outer,"invalid outer-mixer role or count");
                            return false;
                        }
                        if(input!=0&&!SameOuterOwner(firstOuter,outer))
                        {
                            error=RecordError(name,outer,"outer-mixer owner fields disagree");
                            return false;
                        }
                        if(outer.Mixer!=0||outer.MixerInternal!=0||outer.MixerEntries!=0||
                           outer.MixerInputCount!=0||outer.MixerFlag!=0||outer.MixerWeight!=0||
                           outer.MixerChild!=0||outer.MixerWord8!=0)
                        {
                            error=RecordError(name,outer,"kind-0 record has inner-mixer fields");
                            return false;
                        }
                        if((input<2&&outer.OuterChild==0)||outer.Outer==0||
                           outer.OuterInternal==0||outer.OuterEntries==0)
                        {
                            error=RecordError(name,outer,"required outer allocation is null");
                            return false;
                        }
                        outerByInput[input]=outer;
                    }

                    for(uint branch=0;branch<2;++branch)
                    {
                        if(cursor>=count)
                        {
                            error="The "+name+" mixer blob ends before branch "+branch+".";
                            return false;
                        }
                        MixerRecord firstInner=parsed[cursor];
                        uint mixerCount=firstInner.MixerInputCount;
                        uint emitted=mixerCount==0?1u:mixerCount;
                        for(uint input=0;input<emitted;++input)
                        {
                            if(cursor>=count)
                            {
                                error="The "+name+" mixer blob ends inside branch "+branch+".";
                                return false;
                            }
                            MixerRecord inner=parsed[cursor++];
                            uint expectedInput=mixerCount==0?MissingInput:input;
                            uint expectedTrueBranch=branch==0?1u:0u;
                            if(inner.Kind!=1||inner.Layer!=expectedLayer||
                               inner.StateMachine!=stateMachine||inner.TrueBranch!=expectedTrueBranch||
                               inner.Input!=expectedInput||inner.MixerInputCount!=mixerCount)
                            {
                                error=RecordError(name,inner,"invalid branch-mixer role or count");
                                return false;
                            }
                            MixerRecord parent=outerByInput[branch];
                            if(!SameOuterEntry(parent,inner))
                            {
                                error=RecordError(name,inner,"branch does not describe its outer input");
                                return false;
                            }
                            if(inner.OuterChild==0||inner.Mixer==0||inner.MixerInternal==0||
                               inner.OuterChild!=inner.Mixer||inner.Mixer!=parent.OuterChild)
                            {
                                error=RecordError(name,inner,"branch allocation is not linked to its outer child");
                                return false;
                            }
                            if(mixerCount!=0&&inner.MixerEntries==0)
                            {
                                error=RecordError(name,inner,"non-empty branch has null entries");
                                return false;
                            }
                            if(input!=0&&!SameMixerOwner(firstInner,inner))
                            {
                                error=RecordError(name,inner,"branch-mixer owner fields disagree");
                                return false;
                            }
                            if(mixerCount==0&&(inner.MixerWeight!=0||inner.MixerChild!=0||inner.MixerWord8!=0))
                            {
                                error=RecordError(name,inner,"empty branch has an input payload");
                                return false;
                            }
                        }
                    }
                }
                ++expectedLayer;
            }
            for(int i=0;i<descriptors.Count;++i)
            {
                if(descriptors[i].OuterInputCount!=(uint)descriptors.Count)
                {
                    error=RecordError(name,descriptors[i],"descriptor layer count is inconsistent");
                    return false;
                }
            }
            records=parsed;
            return true;
        }

        private static bool SameOuterOwner(MixerRecord a,MixerRecord b)
        {
            return a.Outer==b.Outer&&a.OuterInternal==b.OuterInternal&&
                a.OuterEntries==b.OuterEntries&&a.OuterInputCount==b.OuterInputCount&&
                a.OuterEntriesHash==b.OuterEntriesHash&&a.OuterMode==b.OuterMode&&
                a.OuterArgument==b.OuterArgument;
        }

        private static bool SameOuterEntry(MixerRecord parent,MixerRecord inner)
        {
            return SameOuterOwner(parent,inner)&&parent.OuterWeight==inner.OuterWeight&&
                parent.OuterChild==inner.OuterChild&&parent.OuterWord8==inner.OuterWord8;
        }

        private static bool SameMixerOwner(MixerRecord a,MixerRecord b)
        {
            return a.Mixer==b.Mixer&&a.MixerInternal==b.MixerInternal&&
                a.MixerEntries==b.MixerEntries&&a.MixerInputCount==b.MixerInputCount&&
                a.MixerFlag==b.MixerFlag;
        }

        private static bool IsWeightByte(MixerRecord record,int offset)
        {
            if(record.Kind==0)return offset>=48&&offset<52;
            return record.Kind==1&&record.Input!=MissingInput&&offset>=80&&offset<84;
        }

        private static bool IsRebasedAllocationByte(MixerRecord record,int offset)
        {
            if(record.Kind==0&&record.Input<2)return offset>=52&&offset<56;
            if(record.Kind!=1)return false;
            return (offset>=52&&offset<56)||(offset>=60&&offset<72);
        }

        private static bool HasSamePointerAliases(byte[] savedBlob,byte[] observedBlob,
            MixerRecord[] records,out string error)
        {
            Dictionary<uint,uint> forward=new Dictionary<uint,uint>();
            Dictionary<uint,uint> reverse=new Dictionary<uint,uint>();
            for(int recordIndex=0;recordIndex<records.Length;++recordIndex)
            {
                int recordBase=recordIndex*MixerRecordSize;
                for(int i=0;i<PointerOffsets.Length;++i)
                {
                    int recordOffset=PointerOffsets[i];
                    uint saved=ReadUInt32(savedBlob,recordBase+recordOffset);
                    uint observed=ReadUInt32(observedBlob,recordBase+recordOffset);
                    if((saved==0)!=(observed==0))
                    {
                        error="Mixer pointer nullness differs at record "+recordIndex+
                            ", offset "+recordOffset+".";
                        return false;
                    }
                    if(saved==0)continue;
                    uint mapped;
                    if(forward.TryGetValue(saved,out mapped)&&mapped!=observed)
                    {
                        error="Mixer saved-pointer alias splits at record "+recordIndex+
                            ", offset "+recordOffset+".";
                        return false;
                    }
                    if(reverse.TryGetValue(observed,out mapped)&&mapped!=saved)
                    {
                        error="Mixer observed-pointer alias merges at record "+recordIndex+
                            ", offset "+recordOffset+".";
                        return false;
                    }
                    forward[saved]=observed;
                    reverse[observed]=saved;
                }
            }
            error=null;
            return true;
        }

        private static MixerRecord ReadRecord(byte[] bytes,int index)
        {
            int offset=index*MixerRecordSize;
            return new MixerRecord
            {
                Index=index,
                Kind=ReadUInt32(bytes,offset),Layer=ReadUInt32(bytes,offset+4),
                StateMachine=ReadUInt32(bytes,offset+8),TrueBranch=ReadUInt32(bytes,offset+12),
                Input=ReadUInt32(bytes,offset+16),Outer=ReadUInt32(bytes,offset+20),
                OuterInternal=ReadUInt32(bytes,offset+24),OuterEntries=ReadUInt32(bytes,offset+28),
                OuterInputCount=ReadUInt32(bytes,offset+32),OuterEntriesHash=ReadUInt32(bytes,offset+36),
                OuterMode=ReadUInt32(bytes,offset+40),OuterArgument=ReadUInt32(bytes,offset+44),
                OuterWeight=ReadUInt32(bytes,offset+48),OuterChild=ReadUInt32(bytes,offset+52),
                OuterWord8=ReadUInt32(bytes,offset+56),Mixer=ReadUInt32(bytes,offset+60),
                MixerInternal=ReadUInt32(bytes,offset+64),MixerEntries=ReadUInt32(bytes,offset+68),
                MixerInputCount=ReadUInt32(bytes,offset+72),MixerFlag=ReadUInt32(bytes,offset+76),
                MixerWeight=ReadUInt32(bytes,offset+80),MixerChild=ReadUInt32(bytes,offset+84),
                MixerWord8=ReadUInt32(bytes,offset+88)
            };
        }

        private static uint ReadUInt32(byte[] bytes,int offset)
        {
            return (uint)(bytes[offset]|bytes[offset+1]<<8|bytes[offset+2]<<16|bytes[offset+3]<<24);
        }

        private static string RecordError(string name,MixerRecord record,string detail)
        {
            return "The "+name+" mixer record "+record.Index+" is invalid: "+detail+".";
        }
    }
}
