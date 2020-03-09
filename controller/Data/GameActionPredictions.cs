namespace Hpmv {
    public class GameActionPredictions {
        public int? StartFrame { get; set; }
        public int? EndFrame { get; set; }

        public int LayoutTopFrame { get; set; } = 0;
        public double LayoutTopOffset { get; set; } = 0;
        public double LayoutTopMargin { get; set; } = 0;
        public double LayoutHeight { get; set; } = 24;

        public Save.GameActionPredictions ToProto() {
            return new Save.GameActionPredictions {
                StartFrame = StartFrame ?? -1,
                EndFrame = EndFrame ?? -1
            };
        }
    }

    public static class GameActionPredictionsFromProto {
        public static GameActionPredictions FromProto(this Save.GameActionPredictions proto) {
            return new GameActionPredictions {
                StartFrame = proto.StartFrame == -1 ? (int?)null : proto.StartFrame,
                EndFrame = proto.EndFrame == -1 ? (int?)null : proto.EndFrame
            };
        }
    }
}