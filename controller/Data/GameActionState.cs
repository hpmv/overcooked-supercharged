using System;

namespace Hpmv {
    public class GameActionState {
        public GameAction Action { get; set; }
        public int ChefId { get; set; } = -1;
        public int ActionId { get; set; } = -1;
        public string Description { get; set; } = "";
        public bool IsActive { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? Duration { get; set; }
    }
}