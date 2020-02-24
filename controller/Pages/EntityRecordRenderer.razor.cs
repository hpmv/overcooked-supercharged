using Hpmv;
using Microsoft.AspNetCore.Components;

namespace controller.Pages {
    partial class EntityRecordRenderer {
        [Parameter]
        public GameEntityRecord Record { get; set; }

        [Parameter]
        public int Frame { get; set; }
    }
}