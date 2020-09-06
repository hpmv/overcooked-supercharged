
using Hpmv.Save;
using Microsoft.AspNetCore.Components;

namespace controller.Pages {
    partial class AnalysisRowVisualizer {
        
        [Parameter]
        public AnalysisRow Row {get; set;}

        [Parameter]
        public double StartFrame {get; set;} = 0;

        [Parameter]
        public double PixelsPerFrame {get; set;} = 0.1;

        private double OffsetToPixel(int frame) {
            return (frame - StartFrame) * PixelsPerFrame;
        }

        private double LengthToPixel(int length) {
            return length * PixelsPerFrame;
        }
    }
}
