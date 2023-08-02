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

        [Parameter]
        public bool CanEdit { get; set; }

        private float SCALE = 50;
        private int Width { get; set; } = 900;
        private DotNetObjectReference<EntityRecordVisualizer> thisRef;
        private ElementReference canvasRef;
        private ElementReference EntityMenuAnchor;
        private double EntityMenuX;
        private double EntityMenuY;

        private BaseMatMenu Menu;
        private List<GameEntityRecord> EntitiesForMenu = new List<GameEntityRecord>();

        private bool ShowControls { get; set; }

        private Vector2 Render(Vector2 point) {
            var pos = Level.geometry.CoordsToGridPos(point);
            pos += new Vector2(5, 1);
            return pos * SCALE;
        }

        private Vector2 InverseRender(Vector2 pos) {
            return Level.geometry.GridPosToCoords(pos / SCALE - new Vector2(5, 1));
        }

        private int SvgWidth {
            get {
                if (Level?.geometry == null) {
                    return Width;
                }
                return (int)((Level.geometry.size.X / 1.2 + 6) * SCALE);
            }
        }

        private int SvgHeight {
            get {
                if (Level?.geometry == null) {
                    return Width;
                }
                return (int)((Level.geometry.size.Y / 1.2 + 2) * SCALE);
            }
        }

        private float RenderScale {
            get {
                if (Level?.geometry == null) {
                    return 1;
                }
                return Width * 1.0f / SvgWidth;
            }
        }

        private string PolygonString(Vector2[][] points) {
            return string.Join(" ", points.Select(polygon => 
                "M " + string.Join(" L ", polygon.Select(p => {
                    var r = Render(p);
                    return $"{r.X},{r.Y}";
                })) + " Z"
            ));
        }

        private string PathString(List<Vector2> points) {
            return "M " + string.Join(" L ", points.Select(p => {
                var r = Render(p);
                return $"{r.X},{r.Y}";
            }));
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
            var renderScale = RenderScale;
            EntityMenuX = x / renderScale;
            EntityMenuY = y / renderScale;
            StateHasChanged();
            await InvokeAsync(() => {
                Menu.OpenAsync(EntityMenuAnchor);
                StateHasChanged();
            });
        }

        private void HandleTemplateClick(ActionTemplate template) {
            if (EditorState.SelectedActionIndex != null && CanEdit) {
                var frame = EditorState.ResimulationFrame(Level.LastEmpiricalFrame);
                EditorState.ApplyActionTemplate(template);
                OnActionAdded.InvokeAsync(frame);
            }
            Menu.CloseAsync();
        }

        [JSInvokable]
        public void NotifyWidthChange(float width) {
            Width = (int)width;
            StateHasChanged();
        }
    }
}