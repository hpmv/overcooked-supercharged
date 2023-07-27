using System;

namespace Hpmv {
    public class NodeSelector {
        public bool IsSelecting { get { return OnSelect != null; } }
        private Action<GameActionNode> OnSelect;
        private Action OnIsSelectingChanged;

        public NodeSelector(Action onIsSelectingChanged) {
            OnIsSelectingChanged = onIsSelectingChanged;
        }

        public void BeginSelect(Action<GameActionNode> onSelect) {
            OnSelect = onSelect;
            OnIsSelectingChanged.Invoke();
        }

        public void Select(GameActionNode node) {
            OnSelect?.Invoke(node);
            OnSelect = null;
            OnIsSelectingChanged.Invoke();
        }

        public void Cancel() {
            OnSelect = null;
            OnIsSelectingChanged.Invoke();
        }
    }
}
