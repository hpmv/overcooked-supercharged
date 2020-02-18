using System;

namespace Hpmv {
    public class GameActionState {
        public GameAction Action { get; set; }
        public int ChefId { get; set; } = -1;
        public int ActionId { get; set; } = -1;
        public string Error { get; set; } = "";
        public bool IsActive { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? Duration { get; set; }
    }

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