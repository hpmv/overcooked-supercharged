using System.Threading.Tasks;

namespace Hpmv {
    public class NodeSelector {
        public bool IsSelecting { get { return completionSource != null; } }
        private TaskCompletionSource<GameActionNode> completionSource;

        public Task<GameActionNode> BeginSelect() {
            completionSource = new TaskCompletionSource<GameActionNode>();
            return completionSource.Task;
        }

        public void Select(GameActionNode node) {
            if (completionSource != null) {
                completionSource.SetResult(node);
                completionSource = null;
            }
        }

        public void Cancel() {
            completionSource.SetResult(null);
        }
    }
}
