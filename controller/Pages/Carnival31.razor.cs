using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Extensions;
using Blazor.Extensions.Canvas.Canvas2D;
using ClipperLib;
using Hpmv;
using Newtonsoft.Json;
using Team17.Online.Multiplayer.Messaging;

namespace controller.Pages {
    partial class Carnival31 : IDisposable {

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private Carnival31Handler handler;

        protected override void OnInitialized() {
            handler = new Carnival31Handler(this);
            DoConnect();
        }

        public void Dispose() {
            cancellationTokenSource.Cancel();
        }

        private Connector connector;

        private async void DoConnect() {
            connector = new Connector(handler);
            await connector.Connect(cancellationTokenSource.Token);
        }

        private Canvas2DContext _context;
        private BECanvasComponent _canvasReference;

        private string toJson(Serialisable serialisable) {
            if (serialisable == null) return "(null)";
            string serialized;
            try {
                serialized = JsonConvert.SerializeObject(serialisable, new JsonSerializerSettings() {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        Formatting = Formatting.Indented
                });
            } catch (Exception e) {
                serialized = e.Message;
            }
            return serialized;
        }

        private async void Repaint() {
            this._context = await this._canvasReference.CreateCanvas2DAsync();
            await _context.BeginBatchAsync();
            await _context.SetStrokeStyleAsync("#666666");
            await _context.BeginPathAsync();
            for (int i = 0; i < handler.levelShape.Length; i++) {
                if (i == 0) {
                    var point1 = Render(handler.levelShape[i].ToXZVector3());
                    await _context.MoveToAsync(point1.X, point1.Y);
                }
                int j = (i + 1) % handler.levelShape.Length;
                var point = Render(handler.levelShape[j].ToXZVector3());
                await _context.LineToAsync(point.X, point.Y);
            }
            await _context.StrokeAsync();
            await _context.EndBatchAsync();

        }

        private Vector2 Render(Vector3 point) {
            return new Vector2(30 * (-17 + point.X), 30 * -(6 + point.Z));
        }

        class Carnival31Handler : Interceptor.IAsync {

            public(double x, double y) [] levelShape = {
                (17.8, -7),
                (17.8, -11),
                (29.56, -11),
                (29.8, -11.24),
                (29.8, -12.76),
                (29.56, -13),
                (17.8, -13),
                (17.8, -17),
                (30.2, -17),
                (30.2, -15.4),
                (18.44, -15.4),
                (18.2, -15.16),
                (18.2, -13.64),
                (18.44, -13.4),
                (30.2, -13.4),
                (30.2, -10.6),
                (18.44, -10.6),
                (18.2, -10.36),
                (18.2, -8.84),
                (18.44, -8.6),
                (30.2, -8.6),
                (30.2, -7),
                (29.8, -7),
                (29.8, -7.5),
                (27.8, -7.5),
                (27.8, -7)
            };

            private const int SCALE = 1000;

            private List<List<IntPoint>> LevelPolygons;

            public Carnival31Handler(Carnival31 parent) {
                this.parent = parent;

                LevelPolygons = new List<List<IntPoint>> {
                    levelShape.Select(p => new IntPoint((int) (p.x * SCALE), (int) (p.y * SCALE))).ToList()
                };

                FakeEntityRegistry.entityToTypes.Clear(); // sigh.
            }

            Carnival31 parent;

            public GameEntities entities = new GameEntities();

            public async Task<InputData> getNextAsync(OutputData output, CancellationToken cancellationToken = default) {
                var time = DateTime.Now;
                var input = new InputData {
                    Input = new Dictionary<int, OneInputData>()
                };
                if (output.CharPos != null) {
                    // foreach (var entry in output.CharPos) {
                    //     var path = PathFinding.ShortestPathAroundObstacles(LevelPolygons, new IntPoint((int) (SCALE * entry.Value.Pos.X), (int) (SCALE * entry.Value.Pos.Z)),
                    //         new IntPoint(25000, -10800));
                    //     if (path.Count >= 2) {
                    //         var from = path[0];
                    //         var to = path[1];
                    //         Vector2 v = new Vector2(to.X - from.X, to.Y - from.Y);
                    //         v /= v.Length();
                    //         input.Input[entry.Key] = new OneInputData() {
                    //             Pad = new PadDirection {
                    //             X = v.X,
                    //             Y = -v.Y,
                    //             }
                    //         };
                    //     }
                    // }
                }
                if (output.EntityRegistry != null) {
                    foreach (var entity in output.EntityRegistry) {
                        FakeEntityRegistry.entityToTypes[entity.EntityId] = entity.SyncEntityTypes.Select(t => (EntityType) t).ToList();
                        var data = entities.entities.ComputeIfAbsent(entity.EntityId, () => new GameEntity());
                        data.entityId = entity.EntityId;
                        data.name = entity.Name;
                        data.pos = new UnityEngine.Vector3((float) entity.Pos.X, (float) entity.Pos.Y, (float) entity.Pos.Z);
                        foreach (var syncEntity in entity.SyncEntityTypes) {
                            if ((EntityType) syncEntity != EntityType.Unknown) {
                                data.data[(EntityType) syncEntity] = null;
                            }
                        }
                    }
                }
                if (output.Items != null) {
                    foreach (var entry in output.Items) {
                        //Console.WriteLine($"Received object {entry.Key}");
                        entities.entities.ComputeIfAbsent(entry.Key, () => new GameEntity()).pos = new UnityEngine.Vector3((float) entry.Value.Pos.X, (float) entry.Value.Pos.Y, (float) entry.Value.Pos.Z);

                    }
                }
                if (output.ServerMessages != null) {
                    foreach (var msg in output.ServerMessages) {
                        var item = Deserializer.Deserialize(msg.Type, msg.Message);
                        // Console.WriteLine("Got message " + (MessageType) msg.Type);
                        if (item is EntitySynchronisationMessage sync) {
                            foreach (var(type, payload) in sync.m_Payloads) {
                                if (type != EntityType.Unknown && payload != null) {
                                    entities.entities.ComputeIfAbsent((int) sync.m_Header.m_uEntityID, () => new GameEntity()).data[type] = payload;
                                }
                            }
                        } else if (item is EntityEventMessage eem) {
                            if (eem.m_EntityType != EntityType.Unknown && eem.m_Payload != null) {
                                entities.entities.ComputeIfAbsent((int) eem.m_Header.m_uEntityID, () => new GameEntity()).data[eem.m_EntityType] = eem.m_Payload;
                            }
                        } else if (item is SpawnEntityMessage sem) {

                        } else if (item is DestroyEntityMessage dem) {
                            entities.entities.Remove((int) dem.m_Header.m_uEntityID);
                        } else if (item is DestroyEntitiesMessage dems) {
                            foreach (var i in dems.m_ids) {
                                entities.entities.Remove((int) i);
                            }
                        }
                    }
                }

                await parent.InvokeAsync(() => {
                    parent.Repaint();
                    parent.StateHasChanged();
                });
                //Console.WriteLine("Delay: " + (DateTime.Now - time));
                return input;
            }
        }
    }

}