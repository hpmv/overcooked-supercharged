using System.Threading.Tasks;
using Hpmv;
using Microsoft.AspNetCore.Components;

namespace controller.Pages {
    public partial class ActionEditor {
        [Parameter]
        public GameActionNode Node { get; set; }

        [Parameter]
        public NodeSelector Selector { get; set; }

        [Parameter]
        public GameActionSequences Sequences { get; set; }

        private async Task AddDependency() {
            StateHasChanged();
            var dep = await Selector.BeginSelect();
            if (dep != null) {
                Node.Deps.Add(dep.Id);
            }
        }

        private void HandleDelete(int dep) {
            Node.Deps.Remove(dep);
            StateHasChanged();
        }
    }
}