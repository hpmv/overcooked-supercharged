using System.Collections.Generic;

namespace Hpmv {
    public class InputHistory {
        public readonly Dictionary<GameEntityRecord, Versioned<ActualControllerInput>> FrameInputs = new Dictionary<GameEntityRecord, Versioned<ActualControllerInput>>();

        public void CleanHistoryFromFrame(int frame) {
            foreach (var (chef, inputs) in FrameInputs) {
                inputs.RemoveAllFrom(frame);
            }
        }
    }
}