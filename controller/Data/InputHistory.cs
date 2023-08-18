using System;
using System.Collections.Generic;
using System.Linq;

namespace Hpmv
{
    public class InputHistory
    {
        public readonly Dictionary<GameEntityRecord, Versioned<ActualControllerInput>> FrameInputs = new Dictionary<GameEntityRecord, Versioned<ActualControllerInput>>();

        public void CleanHistoryAfterFrame(int frame)
        {
            foreach (var (chef, inputs) in FrameInputs)
            {
                inputs.RemoveAllAfter(frame);
            }
        }

        public Save.InputHistory ToProto()
        {
            var result = new Save.InputHistory();
            foreach (var (chef, inputs) in FrameInputs)
            {
                result.FrameInputs[chef.path.ids[0]] = inputs.ToProto();
            }
            return result;
        }

        public bool CompareWith(InputHistory that, int frame)
        {
            var thisChefInputs = this.FrameInputs.ToDictionary(kv => kv.Key.path.ids[0], kv => kv.Value[frame]);
            var thatChefInputs = that.FrameInputs.ToDictionary(kv => kv.Key.path.ids[0], kv => kv.Value[frame]);

            foreach (var (chefId, thisInputs) in thisChefInputs)
            {
                if (!thatChefInputs.TryGetValue(chefId, out var thatInputs))
                {
                    Console.WriteLine($"Chef {chefId} does not exist in other records");
                    return false;
                }
                if (thisInputs != thatInputs)
                {
                    Console.WriteLine($"Input mismatch for chef {chefId}: {thisInputs} vs {thatInputs}");
                    return false;
                }
            }


            return true;
        }
    }

    public static class InputHistoryFromProto
    {
        public static InputHistory FromProto(this Save.InputHistory proto, LoadContext context)
        {
            var result = new InputHistory();
            foreach (var (chefId, inputs) in proto.FrameInputs)
            {
                result.FrameInputs[context.GetRootRecord(chefId)] = inputs.FromProto();
            }
            return result;
        }
    }
}