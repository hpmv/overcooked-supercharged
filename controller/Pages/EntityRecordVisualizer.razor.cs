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
        public EventCallback<int> OnActionAdded { get; set; }

        private float SCALE = 50;
        private DotNetObjectReference<EntityRecordVisualizer> thisRef;
        private ElementReference canvasRef;
        private ElementReference EntityMenuAnchor;
        private double EntityMenuX;
        private double EntityMenuY;

        private BaseMatMenu Menu;
        private bool EntityMenuOpen { get; set; }
        private List<GameEntityRecord> EntitiesForMenu = new List<GameEntityRecord>();

        private bool ShowControls { get; set; }

        private Vector2 Render(Vector2 point) {
            var pos = Level.geometry.CoordsToGridPos(point);
            pos += new Vector2(1, 1);
            return pos * SCALE;
        }

        private Vector2 InverseRender(Vector2 pos) {
            return Level.geometry.GridPosToCoords(pos / SCALE - new Vector2(1, 1));
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
            await InvokeAsync(async () => {
                EntityMenuOpen = true;
                await Menu.OpenAsync(EntityMenuAnchor);
            });
        }

        private async Task HandleTemplateClick(ActionTemplate template) {
            if (EditorState.SelectedChef != null) {
                var frame = EditorState.ResimulationFrame();
                EditorState.ApplyActionTemplate(template);
                await OnActionAdded.InvokeAsync(frame + 1);
            }
            EntityMenuOpen = false;
        }

        private bool IsAutoplaying {get; set;}

        private async Task StartAutoplay() {
            IsAutoplaying = true;
            DateTime nextFrame = DateTime.Now;
            while (IsAutoplaying) {
                nextFrame += TimeSpan.FromSeconds(1) / Config.FRAMERATE;
                DateTime now = DateTime.Now;
                await Task.Delay(now > nextFrame ? TimeSpan.Zero : (nextFrame - now));
                if (EditorState.SelectedFrame < Level.LastSimulatedFrame) {
                    EditorState.SelectedFrame++;
                    StateHasChanged();
                } else {
                    break;
                }
            }
            IsAutoplaying = false;
        }
    }
}