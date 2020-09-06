
using Hpmv.Save;
using Microsoft.AspNetCore.Components;
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace controller.Pages {
    partial class AnalysisResultsViewer {
        
        [Parameter]
        public Analysis Analysis {get; set;}

        [Parameter]
        public double StartFrame {get; set;} = 0;

        [Parameter]
        public double PixelsPerFrame {get; set;} = 0.02;

        private const double LEFT_MARGIN = 100;

        private DotNetObjectReference<AnalysisResultsViewer> thisRef;
        private ElementReference containerRef;

        protected override async Task OnAfterRenderAsync(bool firstRender) {
            if (firstRender) {
                thisRef = DotNetObjectReference.Create(this);
                await JSRuntime.InvokeVoidAsync("setUpAnalysisResultsVisualizerHandlers", thisRef, containerRef);
            }
        }

        [JSInvokable]
        public void OnMouseWheel(double delta, double x, double y) {
            var pivotFrame = (x - LEFT_MARGIN) / PixelsPerFrame + StartFrame;
            PixelsPerFrame *= System.Math.Exp(-delta / 500);
            StartFrame = pivotFrame - (x-LEFT_MARGIN) / PixelsPerFrame;
            StateHasChanged();
        }

        [JSInvokable]
        public void OnMouseDrag(double deltaX, double deltaY) {
            StartFrame -= deltaX / PixelsPerFrame;
            StateHasChanged();
        }
    }
}
