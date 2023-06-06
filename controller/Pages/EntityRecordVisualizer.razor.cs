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

        private BaseMatMenu[] Menu = new BaseMatMenu[2];
        private bool[] EntityMenuOpen = { false, false };
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

        private int SvgWidth {
            get {
                if (Level?.geometry == null) {
                    return Width;
                }
                return (int)((Level.geometry.size.X + 2) / 1.2 * SCALE);
            }
        }

        private int SvgHeight {
            get {
                if (Level?.geometry == null) {
                    return Width;
                }
                return (int)((Level.geometry.size.Y + 2) / 1.2 * SCALE);
            }
        }

        private float RenderScale {
            get {
                if (Level?.geometry == null) {
                    return 1;
                }
                return Width / (Math.Max(Level.geometry.size.Y + 2, Level.geometry.size.X + 2) * SCALE / 1.2f);
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
            await InvokeAsync(async () => {
                if (EntityMenuOpen[0]) {
                    EntityMenuOpen[0] = false;
                    EntityMenuOpen[1] = true;
                    await Menu[1].OpenAsync(EntityMenuAnchor);
                } else {
                    EntityMenuOpen[0] = true;
                    EntityMenuOpen[1] = false;
                    await Menu[0].OpenAsync(EntityMenuAnchor);
                }
                StateHasChanged();
            });
        }

        private async Task HandleTemplateClick(ActionTemplate template) {
            if (EditorState.SelectedChef != null && CanEdit) {
                var frame = EditorState.ResimulationFrame();
                EditorState.ApplyActionTemplate(template);
                await OnActionAdded.InvokeAsync(frame);
            }
            EntityMenuOpen[0] = EntityMenuOpen[1] = false;
        }

        [JSInvokable]
        public void NotifyWidthChange(float width) {
            Width = (int)width;
            StateHasChanged();
        }
    }
}