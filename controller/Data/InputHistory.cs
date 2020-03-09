using System.Collections.Generic;

namespace Hpmv {
    public class InputHistory {
        public readonly Dictionary<GameEntityRecord, Versioned<ActualControllerInput>> FrameInputs = new Dictionary<GameEntityRecord, Versioned<ActualControllerInput>>();

        public void CleanHistoryFromFrame(int frame) {
            foreach (var (chef, inputs) in FrameInputs) {
                inputs.RemoveAllFrom(frame);
            }
        }

        public Save.InputHistory ToProto() {
            var result = new Save.InputHistory();
            foreach (var (chef, inputs) in FrameInputs) {
                result.FrameInputs[chef.path.ids[0]] = inputs.ToProto();
            }
            return result;
        }
    }

    public static class InputHistoryFromProto {
        public static InputHistory FromProto(this Save.InputHistory proto, LoadContext context) {
            var result = new InputHistory();
            foreach (var (chefId, inputs) in proto.FrameInputs) {
                result.FrameInputs[context.GetRootRecord(chefId)] = inputs.FromProto();
            }
            return result;
        }
    }
}