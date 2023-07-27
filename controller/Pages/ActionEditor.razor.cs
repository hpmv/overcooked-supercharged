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
        private double? targetProgressEditing;

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

        private double TargetProgressEditing {
            get {
                if (targetProgressEditing != null) {
                    return targetProgressEditing.Value;
                }
                if (Node.Action is WaitForWashingProgressAction wpa) {
                    return wpa.WashingProgress;
                }
                if (Node.Action is WaitForChoppingProgressAction cpa) {
                    return cpa.ChoppingProgress;
                }
                if (Node.Action is WaitForCookingProgressAction cpa2) {
                    return cpa2.CookingProgress;
                }
                if (Node.Action is WaitForMixingProgressAction mpa) {
                    return mpa.MixingProgress;
                }
                return 0;
            }
            set => targetProgressEditing = value;
        }

        private bool TargetProgressEdited => targetProgressEditing != null;

        private void AddDependency() {
            Selector.BeginSelect((dep) => {
                Node.Deps.Add(dep.Id);
                DependenciesChanged.InvokeAsync(false);
            });
        }

        private void HandleDelete(int dep) {
            Node.Deps.Remove(dep);
            StateHasChanged();
            DependenciesChanged.InvokeAsync(false);
        }
    }
}