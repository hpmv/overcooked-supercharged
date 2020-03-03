using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Hpmv;
using MatBlazor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace controller.Pages {
    partial class EntityRecordVisualizer {
        [Parameter]
        public GameSetup Level { get; set; }

        [Parameter]
        public EditorState EditorState { get; set; }

        [Parameter]
        public EventCallback<bool> OnActionAdded { get; set; }

        private float SCALE = 50;
        private DotNetObjectReference<EntityRecordVisualizer> thisRef;
        private ElementReference canvasRef;
        private ElementReference EntityMenuAnchor;
        private double EntityMenuX;
        private double EntityMenuY;

        private BaseMatMenu Menu;
        private List<GameEntityRecord> EntitiesForMenu = new List<GameEntityRecord>();

        private Vector2 Render(Vector2 point) {
            var pos = Level.map.CoordsToGridPos(point);
            pos += new Vector2(1, 1);
            return pos * SCALE;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender) {
            if (firstRender) {
                thisRef = DotNetObjectReference.Create(this);
                await JSRuntime.InvokeVoidAsync("setUpEntityRecordVisualizerHandlers", thisRef, canvasRef);
            }
        }

        [JSInvokable]
        public async Task HandleEntityClicks(string[] entityPaths, double x, double y) {
            EntitiesForMenu = entityPaths
                .Select(path => Level.entityRecords.GetRecordFromPath(EntityPath.ParseEntityPath(path)))
                .ToList();
            EntityMenuX = x;
            EntityMenuY = y;
            StateHasChanged();
            await InvokeAsync(async () =>
                await Menu.OpenAsync(EntityMenuAnchor)
            );
        }

        private async Task HandleTemplateClick(ActionTemplate template) {
            EditorState.ApplyActionTemplate(template);
            await OnActionAdded.InvokeAsync(false);
        }
    }
}