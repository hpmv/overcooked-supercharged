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

        private double TimeToPx(TimeSpan time) {
            return time.TotalMilliseconds * 1.0 / 10;
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

            private GameMap map;

            public Carnival31Handler(Carnival31 parent) {
                this.parent = parent;
                map = new GameMap(new Vector2[][] {
                    levelShape.Select(v => new Vector2((float) v.x, (float) v.y)).ToArray()
                }, new Vector2(18f, -7.2f));
                context.Entities = entities;
                context.Map = map;
                context.ChefHumanizers[97] = new Humanizer();
                context.ChefHumanizers[103] = new Humanizer();

                //var walk1 = graph.AddAction(new GotoAction { Chef = 103, DesiredPos = new Vector2(22, -10.8f) });
                //var walk2 = graph.AddAction(new GotoAction { Chef = 97, DesiredPos = new Vector2(27, -13.2f) });
                //var walk3 = graph.AddAction(new GotoAction { Chef = 97, DesiredPos = new Vector2(26, -16.8f) }, walk1, walk2);
                //var walk4 = graph.AddAction(new GotoAction { Chef = 103, DesiredPos = new Vector2(18, -7.3f) }, walk1, walk2);

                const int MIXER_STATION1 = 68;
                const int MIXER_STATION2 = 69;
                const int MIXER_STATION3 = 67;
                const int MIXER1 = 1;
                const int MIXER2 = 3;
                const int MIXER3 = 4;
                const int FRIER1 = 6;
                const int FRIER2 = 2;
                const int FRIER3 = 5;
                const int FRIER_STATION1 = 13;
                const int FRIER_STATION2 = 11;
                const int FRIER_STATION3 = 12;
                const int BOARD1 = 76;
                const int BOARD2 = 74;
                const int BOARD3 = 75;
                const int HONEY_CRATE = 66;
                const int CHOCOLATE_CRATE = 71;
                const int BERRY_CRATE = 70;
                const int EGG_CRATE = 72;
                const int FLOUR_CRATE = 73;
                const int PLATE_1 = 8;
                const int PLATE_2 = 7;
                const int PLATE_3 = 9;
                const int DIRTY_PLATE_SPAWNER = 78;
                const int CLEAN_PLATE_SPAWNER = 82;
                const int WASHING_PART = 81;

                var builder = graph.Builder(map);
                var chef1 = builder.Chef1();
                var chef2 = builder.Chef2();

                GameActionGraphBuilder berry1, choco1, honey1;

                {
                    var berry = chef1 = chef1.GetFromCrate(BERRY_CRATE);
                    chef1 = chef1.ThrowTowards(BOARD3);
                    var choco = chef1 = chef1.GetFromCrate(CHOCOLATE_CRATE);
                    choco = chef1 = chef1.PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(choco);
                    chef1 = chef1.Pickup(choco).Goto(6, 5).ThrowTowards(MIXER_STATION2);

                    berry = chef1 = chef1.Pickup(berry).PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(berry);
                    chef1 = chef1.Pickup(berry).Goto(7, 5).ThrowTowards(MIXER_STATION3, new Vector2(0, -0.8f));

                    chef2 = chef2
                        .GetFromCrate(EGG_CRATE).PutOnto(MIXER_STATION3)
                        .GetFromCrate(EGG_CRATE).Goto(10, 5).ThrowTowards(MIXER_STATION2)
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER_STATION2, new Vector2(0, -0.4f))
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER_STATION1, new Vector2(0, -0.8f))
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER_STATION3)
                        .GetFromCrate(EGG_CRATE).PutOnto(MIXER_STATION1)
                        .DoWork(BOARD3);

                    var honey = chef1 = chef1.GetFromCrate(HONEY_CRATE);
                    var x0 = chef1 = chef1.PutOnto(BOARD3);
                    honey = chef1 = chef1.DoWork(BOARD3).WaitForSpawn(honey);
                    chef2 = chef2.WaitFor(x0).DoWork(BOARD3).WaitFor(honey).Pickup(BOARD3).PutOnto(MIXER_STATION1);

                    honey = chef1 = chef1.GetFromCrate(HONEY_CRATE);
                    x0 = chef1 = chef1.PutOnto(BOARD3);
                    berry = chef1 = chef1.GetFromCrate(BERRY_CRATE);
                    choco = chef1 = chef1.PutOnto(45).GetFromCrate(CHOCOLATE_CRATE);
                    honey = chef2 = chef2.WaitFor(x0).DoWork(BOARD3).WaitForSpawn(honey);
                    chef1 = chef1.PutOnto(23).WaitFor(honey).Pickup(berry);

                    chef2 = chef2.Pickup(honey).PutOnto(25);
                    honey1 = honey;
                    berry1 = berry;
                    choco1 = choco;

                    var sync = chef1.WaitFor(chef2);
                    chef1 = sync;
                    chef2 = sync.Chef2();
                }

                {
                    var x0 = chef1 = chef1.PutOnto(BOARD3);
                    var berry = chef1 = chef1.DoWork(BOARD3).WaitForSpawn(berry1);
                    chef2 = chef2.WaitFor(x0).DoWork(BOARD3).WaitFor(berry).Pickup(berry).PutOnto(58);
                    x0 = chef1 = chef1.Pickup(choco1).PutOnto(BOARD3);
                    var choco = chef1 = chef1.DoWork(BOARD3).WaitForSpawn(choco1);
                    chef2 = chef2.WaitFor(x0).DoWork(BOARD3).WaitFor(choco).Pickup(choco).PutOnto(45);
                    var honey = chef1 = chef1.GetFromCrate(HONEY_CRATE);
                    honey = chef1 = chef1.PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(honey);
                    chef1 = chef1.Pickup(honey).PutOnto(57).Pickup(choco).Goto(8, 5).ThrowTowards(MIXER2)
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER2).GetFromCrate(EGG_CRATE).Goto(10, 5).ThrowTowards(MIXER_STATION2, new Vector2(0, -0.4f))
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER3).GetFromCrate(EGG_CRATE).PutOnto(MIXER3)
                        .Pickup(berry).ThrowTowards(MIXER3).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, new Vector2(0, -0.8f))
                        .GetFromCrate(EGG_CRATE).Goto(10, 5).ThrowTowards(MIXER1, new Vector2(0, -0.8f));
                    berry = chef1 = chef1.GetFromCrate(BERRY_CRATE);
                    berry = chef1 = chef1.PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(berry);
                    chef1 = chef1.Pickup(berry).PutOnto(45);

                    choco = chef1 = chef1.GetFromCrate(CHOCOLATE_CRATE);
                    choco = chef1 = chef1.PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(choco);
                    chef1 = chef1.Pickup(choco).PutOnto(23);

                    honey = chef1 = chef1.GetFromCrate(HONEY_CRATE);
                    honey = chef1 = chef1.PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(honey);

                    chef2 = chef2.Pickup(FRIER1).Pickup(MIXER2).PutOnto(FRIER_STATION1)
                        .Pickup(FRIER2).Pickup(MIXER3).PutOnto(FRIER_STATION2)
                        .Pickup(FRIER3).Pickup(MIXER1).PutOnto(FRIER_STATION3)
                        .Pickup(honey1).PutOnto(MIXER1);

                    chef2 = chef2.DoWork(BOARD3).WaitFor(berry);

                    x0 = chef2 = chef2.Pickup(PLATE_1).WaitForCooked(FRIER1).Pickup(FRIER1).PutOnto(BOARD1);
                    chef1 = chef1.WaitFor(x0).Pickup(PLATE_1).PutOnto(HONEY_CRATE);
                    x0 = chef2 = chef2.Pickup(PLATE_2).WaitForCooked(FRIER2).Pickup(FRIER2).PutOnto(59);
                    chef1 = chef1.WaitFor(x0).Pickup(PLATE_2).PutOnto(55);
                    x0 = chef2 = chef2.Pickup(PLATE_3).WaitForCooked(FRIER3).Pickup(FRIER3).PutOnto(BOARD1);
                    chef1 = chef1.WaitFor(x0).Pickup(PLATE_3).PutOnto(77).Pickup(PLATE_1).PutOnto(77).Pickup(PLATE_2).PutOnto(77);

                    chef2 = chef2.Pickup(FRIER1).Pickup(MIXER3).PutOnto(FRIER_STATION1)
                        .Pickup(FRIER2).Pickup(MIXER2).PutOnto(FRIER_STATION2)
                        .Pickup(FRIER3).Pickup(MIXER1).PutOnto(FRIER_STATION3);

                    var berry2 = chef1 = chef1.GetFromCrate(BERRY_CRATE);
                    chef1 = chef1.PutOnto(BOARD2);

                    var dirty_plate = chef1 = chef1.PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate();
                    x0 = chef1 = chef1.Pickup(dirty_plate).PutOnto(BOARD1);
                    chef2 = chef2.WaitFor(x0).Pickup(dirty_plate).PutOnto(WASHING_PART).DoWork(WASHING_PART);
                    dirty_plate = chef1 = chef1.PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate().WaitForDirtyPlate();
                    x0 = chef1 = chef1.Pickup(dirty_plate).PutOnto(BOARD1);
                    chef2 = chef2.WaitFor(x0).Pickup(dirty_plate).PutOnto(WASHING_PART).DoWork(WASHING_PART);

                    var plate1 = chef2 = chef2.WaitForCleanPlate();
                    chef2 = chef2.Pickup(WASHING_PART).Pickup(FRIER1).PutOnto(BOARD1);
                    var plate2 = chef2 = chef2.DoWork(WASHING_PART).WaitForCleanPlate();
                    chef2 = chef2.Pickup(WASHING_PART).Pickup(FRIER2).PutOnto(59);
                    var plate3 = chef2 = chef2.DoWork(WASHING_PART).WaitForCleanPlate();
                    chef2 = chef2.Pickup(WASHING_PART).Pickup(FRIER3).PutOnto(BOARD1);

                    chef1 = chef1
                        .Pickup(berry).ThrowTowards(MIXER2, new Vector2(0, -0.8f))
                        .Pickup(honey).Goto(8, 5).ThrowTowards(MIXER3, new Vector2(0, -0.4f))
                        .GetFromCrate(EGG_CRATE).PutOnto(MIXER_STATION3)
                        .GetFromCrate(EGG_CRATE).Goto(10, 5).ThrowTowards(MIXER_STATION2, new Vector2(0, -0.4f))
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER_STATION2, new Vector2(0, -0.4f))
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER_STATION1, new Vector2(0, -0.8f))
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER_STATION3)
                        .GetFromCrate(EGG_CRATE).Goto(8, 5).ThrowTowards(MIXER_STATION1, new Vector2(0, -0.4f))
                        .Pickup(choco).ThrowTowards(MIXER1);

                    chef2 = chef2.Pickup(FRIER1).PrepareToPickup(MIXER3).WaitForMixed(MIXER3).Pickup(MIXER3).PutOnto(FRIER_STATION1)
                        .Pickup(FRIER2).PrepareToPickup(MIXER2).WaitForMixed(MIXER2).Pickup(MIXER2).PutOnto(FRIER_STATION2)
                        .Pickup(FRIER3).PrepareToPickup(MIXER1).WaitForMixed(MIXER1).Pickup(MIXER1).PutOnto(FRIER_STATION3);

                    chef1 = chef1.Pickup(plate1).PutOnto(HONEY_CRATE)
                        .PrepareToPickup(59).WaitFor(plate2).Pickup(plate2).PutOnto(55)
                        .PrepareToPickup(BOARD1).WaitFor(plate3).Pickup(plate3).PutOnto(77)
                        .Pickup(plate2).PutOnto(77).Pickup(plate1).PutOnto(77);
                }

                executor.Graph = graph;

                FakeEntityRegistry.entityToTypes.Clear(); // sigh.
            }

            Carnival31 parent;

            public GameEntities entities = new GameEntities();

            private GameActionContext context = new GameActionContext();

            private GameActionGraph graph = new GameActionGraph();
            private GameActionExecutor executor = new GameActionExecutor();

            private IEnumerator<InputData> CurrentSession;
            bool started = false;
            public GameActionGraphState CurrentState;

            private void RestartLevel() {
                CurrentState = graph.NewState();
                CurrentSession = null;
                started = false;
                entities.entities.Clear();
            }

            public async Task<InputData> getNextAsync(OutputData output, CancellationToken cancellationToken = default) {
                var time = DateTime.Now;
                var input = new InputData {
                    Input = new Dictionary<int, OneInputData>()
                };
                if (output.CharPos != null) {
                    foreach (var entry in output.CharPos) {
                        var state = entities.chefs.ComputeIfAbsent(entry.Key, () => new ChefState());
                        state.forward = entry.Value.ForwardDirection.ToNumericsVector().XZ();
                        state.highlightedForPickup = entry.Value.HighlightedForPickup;
                        state.highlightedForUse = entry.Value.HighlightedForUse;
                        state.highlightedForPlacement = entry.Value.HighlightedForPlacement;
                        state.dashTimer = entry.Value.DashTimer;
                    }
                }
                if (output.EntityRegistry != null) {
                    foreach (var entity in output.EntityRegistry) {
                        FakeEntityRegistry.entityToTypes[entity.EntityId] = entity.SyncEntityTypes.Select(t => (EntityType) t).ToList();
                        var data = entities.EntityOrCreate(entity.EntityId);
                        data.entityId = entity.EntityId;
                        data.name = entity.Name;
                        data.pos = new Vector3((float) entity.Pos.X, (float) entity.Pos.Y, (float) entity.Pos.Z);
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
                        entities.EntityOrCreate(entry.Key).pos = new Vector3((float) entry.Value.Pos.X, (float) entry.Value.Pos.Y, (float) entry.Value.Pos.Z);

                    }
                }
                bool restarted = false;
                if (output.ServerMessages != null) {
                    foreach (var msg in output.ServerMessages) {
                        var item = Deserializer.Deserialize(msg.Type, msg.Message);
                        // Console.WriteLine("Got message " + (MessageType) msg.Type);
                        if (item is EntitySynchronisationMessage sync) {
                            foreach (var(type, payload) in sync.m_Payloads) {
                                if (type != EntityType.Unknown && payload != null) {
                                    entities.EntityOrCreate((int) sync.m_Header.m_uEntityID).data[type] = payload;
                                }
                            }
                        } else if (item is EntityEventMessage eem) {
                            if (eem.m_EntityType != EntityType.Unknown && eem.m_Payload != null) {
                                entities.EntityOrCreate((int) eem.m_Header.m_uEntityID).data[eem.m_EntityType] = eem.m_Payload;
                            }
                        } else if (item is SpawnEntityMessage sem) {
                            entities.EntityOrCreate((int) sem.m_DesiredHeader.m_uEntityID).spawnSourceEntityId = (int) sem.m_SpawnerHeader.m_uEntityID;
                        } else if (item is SpawnPhysicalAttachmentMessage spem) {
                            var inner = spem.m_SpawnEntityData;
                            entities.EntityOrCreate((int) inner.m_DesiredHeader.m_uEntityID).spawnSourceEntityId = (int) inner.m_SpawnerHeader.m_uEntityID;
                        } else if (item is DestroyEntityMessage dem) {
                            entities.entities.Remove((int) dem.m_Header.m_uEntityID);
                        } else if (item is DestroyEntitiesMessage dems) {
                            foreach (var i in dems.m_ids) {
                                entities.entities.Remove((int) i);
                            }
                        } else if (item is LevelLoadByIndexMessage llbim) {
                            RestartLevel();
                            restarted = true;
                        }
                    }
                }

                await parent.InvokeAsync(() => {
                    parent.Repaint();
                    parent.StateHasChanged();
                });

                foreach (var(id, humanizer) in context.ChefHumanizers) {
                    humanizer.AdvanceFrame();
                }

                if (!started && entities.entities.ContainsKey(97) && entities.entities.ContainsKey(103) && CurrentState != null && CurrentSession == null) {
                    CurrentSession = executor.GenUpdate(CurrentState, context);
                    started = true;
                }

                if (CurrentSession != null) {
                    if (CurrentSession.MoveNext()) {
                        var inputs = CurrentSession.Current;
                        return inputs;
                    } else {
                        CurrentSession = null;
                    }
                }
                //Console.WriteLine("Delay: " + (DateTime.Now - time));
                var inputs2 = new InputData();
                if (restarted) inputs2.ResetOrderSeed = 12347;
                return inputs2;
            }
        }
    }

}