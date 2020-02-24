namespace Hpmv {
    public class GameActionPredictions {
        public bool IsEmpirical { get; set; }
        public int StartFrame { get; set; }
        public int EndFrame { get; set; }

        public int LayoutTopFrame { get; set; } = 0;
        public double LayoutTopOffset { get; set; } = 0;
        public double LayoutTopMargin { get; set; } = 0;
        public double LayoutHeight { get; set; } = 24;
    }
}