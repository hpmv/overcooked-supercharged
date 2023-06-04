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

        [Parameter]
        public EventCallback<bool> DependenciesChanged { get; set; }

        [Parameter]
        public bool CanEdit { get; set; }

        private int? numFramesEditing;
        private float? targetAngleEditing;

        private int NumFramesEditing {
            get => numFramesEditing ?? (Node.Action as WaitAction).NumFrames;
            set => numFramesEditing = value;
        }

        private bool NumFramesEdited => numFramesEditing != null;

        private float TargetAngleEditing {
            get => targetAngleEditing ?? (Node.Action as PilotRotationAction).TargetAngle;
            set => targetAngleEditing = value;
        }

        private bool TargetAngleEdited => targetAngleEditing != null;

        private async Task AddDependency() {
            StateHasChanged();
            var dep = await Selector.BeginSelect();
            if (dep != null) {
                Node.Deps.Add(dep.Id);
                await DependenciesChanged.InvokeAsync(false);
            }
        }

        private void HandleDelete(int dep) {
            Node.Deps.Remove(dep);
            StateHasChanged();
            DependenciesChanged.InvokeAsync(false);
        }
    }
}