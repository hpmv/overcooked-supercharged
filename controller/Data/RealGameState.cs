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
    /// After pause, can warp:
    ///    |     Frame N (Paused)     |               Frame X (Warping)                                                                |   Frame X   (Paused)                     |
    ///    | in0              out1    | in1 (warp to frame X); perform warping in LateUpdate(); out2 (not necessarily accurate state)  | in2 (state correct)        out3 (paused) |
    /// Finally can resume:
    ///    |     Frame N (Paused)     |               Frame N (AwaitingResume)                          |   Frame N+1 (Running)  |
    ///    | in0               out1   | in1 (resume); resume game in LateUpdate(); out2 (just resumed)  | in2               out3 |
    ///
    ///
    /// ===== Detailed explanation of frame handling =====
    ///
    /// Suppose there's a workflow that pauses the game, warps a frame, and then resumes the game.
    ///     At end of game frame X:
    ///       - plugin's LateUpdate() outputs data for logical frame N
    ///       - controller's getNextAsync() decides to pause, returning inputs for frame N + 1 along with a pause signal;
    ///         sets the controller's state to AwaitingPause
    ///     In game frame X + 1
    ///       - game's Update() functions of various components applies the supplied inputs for frame N + 1 and runs the game for one frame
    ///         bringing the logical frame to N + 1
    ///       - plugin's LateUpdate() outputs data for logical frame N + 1, and pauses the game
    ///       - controller's getNextAsync() advances logical frame to N + 1, applies the game updates for N + 1, sets the controller's
    ///         state to Paused.
    ///     In game frame X + 2
    ///       - game's Update() functions do not advance the game because it's paused, so we stay in logical frame N + 1
    ///       - plugin's LateUpdate() outputs some data
    ///       - controller's getNextAsync() does not advance logical frame and does not apply any game data, we stay in N + 1
    ///         suppose the user does something and triggers a warp to logical frame W
    ///     In game frame X + 3
    ///       - game's Update() functions still do nothing as the game is still paused
    ///       - plugin's LateUpdate() sees the warp input, and warps the game state to the desired game state
    ///         at logical frame W (which is the state captured after transitioning to logical frame W). The game's own tracked
    ///         logical frame resets to W
    ///       - controller's getNextAsync() gets the restoration results and resets controller state to logical frame W as well.
    ///         suppose the user then decides to resume; controller returns input to resume, and sets state to AwaitingResume
    ///     In game frame X + 4
    ///       - game's Update() functions still do nothing as the game is still paused
    ///       - plugin's LateUpdate() resumes the game, returning some data still for logical frame W
    ///       - controller ignores any data sent by the plugin, and does not advance the logical frame. It however does compute the
    ///         inputs for logical frame W + 1, and returns it to the plugin. It also sets the state to Running.
    ///     In game frame X + 5
    ///       - game's Update() functions apply the inputs for logical frame W + 1, which advances the logical frame to W + 2
    ///       - plugin's LateUpdate() outputs data for logical frame W + 2
    ///       - controller advances logical frame to W + 2 and applies game data for W + 2.
    ///
    /// For the beginning of the level:
    ///     At end of game frame X:
    ///       - plugin's LateUpdate() returns logical frame 0 as well as a message to start the level
    ///       - controller's getNextAsync() sees the level start message, and sets state to AwaitingResume. It then applies the
    ///         game data on frame 0 and computes input for frame 1, and finally sets state to Running.
    ///         (The intermediate AwaitingResume is a technical implementation detail; we don't set it to Running to begin with
    ///         since then we would've advanced the frame to 1 and computed input for frame 2).
    ///     In game frame X + 1:
    ///       - game's Update() functions apply the inputs for frame 1, which advances the logical frame to 1
    ///       - plugin's LateUpdate() outputs data for logical frame 1
    ///       - controller's getNextAsync() advances frame to 1 and computes inputs for frame 2.
    ///     Everything else proceeds normally like the Running state.
    public enum RealGameState {
        NotInLevel,
        AwaitingStart,
        Running,
        AwaitingPause,
        Paused,
        Warping,
        AwaitingResume,
    }

    public enum RealGameStateRequestKind {
        Pause,
        Resume,
        Warp,
    }

    public class RealGameStateRequest {
        public readonly RealGameStateRequestKind Kind;
        public readonly int FrameToWarpTo;
        public readonly TaskCompletionSource<bool> Completed = new TaskCompletionSource<bool>();

        private RealGameStateRequest(RealGameStateRequestKind kind, int frameToWarpTo) {
            Kind = kind;
            FrameToWarpTo = frameToWarpTo;
        }

        public static RealGameStateRequest Pause() {
            return new RealGameStateRequest(RealGameStateRequestKind.Pause, 0);
        }

        public static RealGameStateRequest Resume() {
            return new RealGameStateRequest(RealGameStateRequestKind.Resume, 0);
        }

        public static RealGameStateRequest Warp(int frameToWarpTo) {
            return new RealGameStateRequest(RealGameStateRequestKind.Warp, frameToWarpTo);
        }
    }
}