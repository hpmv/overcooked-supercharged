using System.Threading.Tasks;

namespace Hpmv {
    /// State used to determine inputs to give to the game. Each state listed below is used for the RPC call between that frame
    /// and next frame. As a reminder, the RPC call is made from the Unity side in LateUpdate() of our plugin with the data that
    /// we call "output" (from the perspective of the game, not the controller), and that RPC returns the "input" for the next
    /// frame.
    ///
    /// Initial state is NotInLevel. When the level is loaded, it transitions to AwaitingStart.
    /// When the ready-go countdown timer finishes, we transition to Running:
    ///    |     Frame 0 (AwaitingStart)         |  Frame 1 (Running) |
    ///    | in0 (nothing) out1 (level started)  | in1          out2  |
    /// To pause, transition via pausing command:
    ///    |     Frame N (Running)    |               Frame N+1 (AwaitingPause)                       |   Frame N+1 (Paused)   |
    ///    | in0               out1   | in1 (pause); pause game in LateUpdate(); out2 (just paused)   | in2      out3 (paused) |
    /// After pause, can restore state:
    ///    |     Frame N (Paused)     |               Frame X (RestoringState)                                                                |   Frame X   (Paused)                     |
    ///    | in0              out1    | in1 (restore state to frame X); restore state in LateUpdate(); out2 (not necessarily accurate state)  | in2 (state correct)        out3 (paused) |
    /// Finally can resume:
    ///    |     Frame N (Paused)     |               Frame N (AwaitingResume)                          |   Frame N+1 (Running)  |
    ///    | in0               out1   | in1 (resume); resume game in LateUpdate(); out2 (just resumed)  | in2               out3 |
    public enum RealGameState {
        NotInLevel,
        AwaitingStart,
        Running,
        AwaitingPause,
        Paused,
        RestoringState,
        AwaitingResume,
    }

    public enum RealGameStateRequestKind {
        Pause,
        Resume,
        RestoreState,
    }

    public class RealGameStateRequest {
        public readonly RealGameStateRequestKind Kind;
        public readonly int FrameToRestoreTo;
        public readonly TaskCompletionSource<bool> Completed = new TaskCompletionSource<bool>();

        private RealGameStateRequest(RealGameStateRequestKind kind, int frameToRestoreTo) {
            Kind = kind;
            FrameToRestoreTo = frameToRestoreTo;
        }

        public static RealGameStateRequest Pause() {
            return new RealGameStateRequest(RealGameStateRequestKind.Pause, 0);
        }

        public static RealGameStateRequest Resume() {
            return new RealGameStateRequest(RealGameStateRequestKind.Resume, 0);
        }

        public static RealGameStateRequest RestoreState(int frameToRestoreTo) {
            return new RealGameStateRequest(RealGameStateRequestKind.RestoreState, frameToRestoreTo);
        }
    }
}