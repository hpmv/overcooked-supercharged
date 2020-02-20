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
    partial class Carnival31_four : IDisposable {

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private Carnival31FourHandler handler;

        protected override void OnInitialized() {
            handler = new Carnival31FourHandler(this);
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

        class Carnival31FourHandler : Interceptor.IAsync {

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
            const int OUTPUT = 77;

            const int COUNTER41 = 59;
            const int COUNTER43 = 23;
            const int COUNTER45 = 45;
            const int COUNTER46 = 58;
            const int COUNTER47 = 46;
            const int COUNTER48 = 60;

            const int COUNTER61 = 37;
            const int COUNTER63 = 21;
            const int COUNTER64 = 57;
            const int COUNTER66 = 22;
            const int COUNTER67 = 56;
            const int COUNTER69 = 43;
            const int COUNTER6A = 47;

            const int COUNTER21 = 63;
            const int COUNTER22 = 65;
            const int COUNTER23 = 26;
            const int COUNTER24 = 25;
            const int COUNTER25 = 62;
            const int COUNTER27 = 24;
            const int COUNTER29 = 44;

            const int COUNTER01 = 61;
            const int COUNTER03 = 29;
            const int COUNTER05 = 28;

            const int COUNTER4B = 15;

            const int COUNTER90 = 35;
            const int COUNTER92 = 55;
            const int COUNTER97 = 31;

            const int CHEF1 = 97;
            const int CHEF2 = 99;
            const int CHEF3 = 101;
            const int CHEF4 = 103;

            public Carnival31FourHandler(Carnival31_four parent) {
                this.parent = parent;
                map = new GameMap(new Vector2[][] {
                    levelShape.Select(v => new Vector2((float) v.x, (float) v.y)).ToArray()
                }, new Vector2(18f, -7.2f));
                context.Entities = entities;
                context.Map = map;
                context.ChefHumanizers[CHEF1] = new ControllerState();
                context.ChefHumanizers[CHEF2] = new ControllerState();
                context.ChefHumanizers[CHEF3] = new ControllerState();
                context.ChefHumanizers[CHEF4] = new ControllerState();
                context.FramerateController = new FramerateController();

                //var walk1 = graph.AddAction(new GotoAction { Chef = 103, DesiredPos = new Vector2(22, -10.8f) });
                //var walk2 = graph.AddAction(new GotoAction { Chef = 97, DesiredPos = new Vector2(27, -13.2f) });
                //var walk3 = graph.AddAction(new GotoAction { Chef = 97, DesiredPos = new Vector2(26, -16.8f) }, walk1, walk2);
                //var walk4 = graph.AddAction(new GotoAction { Chef = 103, DesiredPos = new Vector2(18, -7.3f) }, walk1, walk2);

                sequences.AddChef(97);
                sequences.AddChef(99);
                sequences.AddChef(101);
                sequences.AddChef(103);
                var builder = new GameActionGraphBuilder(sequences, map, -1, new List<int>());
                var chef1 = builder.WithChef(CHEF1).SetFps(1000);
                var chef2 = builder.WithChef(CHEF2);
                var chef3 = builder.WithChef(CHEF3);
                var chef4 = builder.WithChef(CHEF4);

                var honey = builder.Placeholder();
                var berry = builder.Placeholder();
                var choco = builder.Placeholder();
                var honey2 = builder.Placeholder();
                var berry2 = builder.Placeholder();
                var choco2 = builder.Placeholder();
                var x0 = builder.Placeholder();
                var x1 = builder.Placeholder();
                var x2 = builder.Placeholder();
                var x3 = builder.Placeholder();
                var x4 = builder.Placeholder();
                var x5 = builder.Placeholder();
                var x6 = builder.Placeholder();
                var x7 = builder.Placeholder();
                var plate1 = builder.Placeholder();
                var plate2 = builder.Placeholder();
                var plate3 = builder.Placeholder();

                var biasDown2 = new Vector2(0, -0.8f);
                var biasDown1 = new Vector2(0, -0.4f);
                var washingPartPos = map.GridPos(0, 1) - new Vector2(-0.2f, 0);

                {

                    chef3 = chef3.GetFromCrate(EGG_CRATE).Goto(8, 3).ThrowTowards(MIXER1, biasDown1);

                    chef2 = chef2.GetFromCrate(CHOCOLATE_CRATE).Aka(choco).PutOnto(BOARD2)
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, biasDown2);

                    x0 = chef3.WaitFor(chef2);

                    chef3 = chef3.WaitFor(x0).GetFromCrate(EGG_CRATE).PutOnto(MIXER3);

                    chef2 = chef2.WaitFor(x0).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER2, biasDown1);

                    x0 = chef3.WaitFor(chef2);

                    chef3 = chef3.WaitFor(x0).GetFromCrate(EGG_CRATE).Goto(8, 3).Aka(x1).PutOnto(MIXER2);

                    chef2 = chef2.WaitFor(x0).GetFromCrate(FLOUR_CRATE).WaitFor(x1).ThrowTowards(MIXER3)
                        .GetFromCrate(FLOUR_CRATE).Aka(x1);

                    chef3 = chef3.WaitFor(x1).GetFromCrate(EGG_CRATE).Goto(10, 4);

                    chef1 = chef1.GetFromCrate(BERRY_CRATE).Aka(berry).PutOnto(BOARD1)
                        .GetFromCrate(HONEY_CRATE).Aka(honey).PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(honey).Aka(honey)
                        .Pickup(honey).Goto(8, 5).ThrowTowards(MIXER3);

                    chef4 = chef4.DoWork(BOARD1).WaitForSpawn(berry).Aka(berry).Pickup(berry).ThrowTowards(MIXER1)
                        .DoWork(BOARD2).WaitForSpawn(choco).Aka(choco).Pickup(choco).Goto(6, 3).ThrowTowards(MIXER2, biasDown1)
                        .Pickup(FRIER1).PutOnto(COUNTER25).Pickup(FRIER2).PutOnto(COUNTER27).Pickup(FRIER3).PutOnto(COUNTER29);

                    chef1 = chef1
                        .GetFromCrate(HONEY_CRATE).Aka(honey).PutOnto(BOARD3).DoWork(BOARD3)
                        .WaitForSpawn(honey).Aka(honey).Pickup(honey).PutOnto(COUNTER46)
                        .GetFromCrate(BERRY_CRATE).Aka(berry).PutOnto(BOARD3).DoWork(BOARD3)
                        .WaitForSpawn(berry).Aka(berry).Pickup(berry).PutOnto(COUNTER45)
                        .GetFromCrate(CHOCOLATE_CRATE).Aka(choco).Aka(x3).PutOnto(BOARD3).DoWork(BOARD3)
                        .WaitForSpawn(choco).Aka(choco).Pickup(honey);

                    chef4 = chef4
                        .Pickup(FRIER1).PrepareToPickup(MIXER1).WaitForMixed(MIXER1).PutOnto(MIXER1).Aka(x0).PutOnto(FRIER_STATION3)
                        .Pickup(FRIER3).PrepareToPickup(MIXER3).Aka(x2).PutOnto(MIXER3).PutOnto(FRIER_STATION2)
                        .Pickup(FRIER2).PrepareToPickup(MIXER2).Aka(x1).PutOnto(MIXER2).PutOnto(FRIER_STATION1);

                    chef2 = chef2.WaitFor(x3).Goto(8, 5)
                        .WaitFor(x0).ThrowTowards(MIXER1, biasDown1).GetFromCrate(FLOUR_CRATE).Goto(8, 5)
                        .WaitFor(x2).ThrowTowards(MIXER3).GetFromCrate(FLOUR_CRATE).Goto(8, 5)
                        .WaitFor(x1).ThrowTowards(MIXER2);

                    chef3 = chef3.WaitFor(x3).Goto(10, 5).WaitFor(x0).ThrowTowards(MIXER1, biasDown2).GetFromCrate(EGG_CRATE).Goto(10, 5)
                        .WaitFor(x2).ThrowTowards(MIXER3).GetFromCrate(EGG_CRATE).Goto(10, 5)
                        .WaitFor(x1).ThrowTowards(MIXER2);

                    chef1 = chef1.WaitFor(x0).ThrowTowards(MIXER1)
                        .Pickup(choco).Goto(6, 5).WaitFor(x2).ThrowTowards(MIXER3, biasDown1)
                        .Pickup(berry).Goto(6, 5).WaitFor(x1).ThrowTowards(MIXER2, biasDown1);

                    // friers full, second round mixers full, no ingredients cut, no grabs. ~4:11

                    chef1 = chef1.GetFromCrate(BERRY_CRATE).Aka(berry).PutOnto(BOARD3).Aka(x3).DoWork(BOARD3).WaitForSpawn(berry).Aka(berry)
                        .Pickup(berry).PutOnto(COUNTER46)
                        .GetFromCrate(CHOCOLATE_CRATE).Aka(choco).PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(choco).Aka(choco)
                        .Pickup(choco).PutOnto(COUNTER45)
                        .GetFromCrate(HONEY_CRATE).Aka(honey).PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(honey).Aka(honey);

                    chef4 = chef4.Pickup(PLATE_3).PrepareToPickup(FRIER1).WaitForCooked(FRIER1).PutOnto(FRIER1).PutOnto(COUNTER22).Aka(x0)
                        .Pickup(PLATE_2).PrepareToPickup(FRIER3).WaitForCooked(FRIER3).PutOnto(FRIER3).PutOnto(BOARD1).Aka(x1)
                        .Pickup(PLATE_1).PrepareToPickup(FRIER2).WaitForCooked(FRIER2).PutOnto(FRIER2).PutOnto(BOARD1).Aka(x2);

                    chef3 = chef3.Goto(8, 3).PrepareToPickup(BOARD3).WaitFor(x3).DoWork(BOARD3).WaitFor(berry)
                        .PrepareToPickup(COUNTER22).WaitFor(x0).Pickup(PLATE_3).PutOnto(BOARD2).Aka(x0)
                        .Pickup(FRIER1).PrepareToPickup(MIXER1).WaitForMixed(MIXER1).PutOnto(MIXER1).Aka(x4).PutOnto(FRIER_STATION3)
                        .Goto(4, 1).WaitFor(x1).Pickup(FRIER3).PutOnto(MIXER3).Aka(x5).Goto(4, 0).PutOnto(FRIER_STATION2).Goto(6, 0);

                    chef4 = chef4.Pickup(FRIER2).Goto(2, 1).PrepareToPickup(MIXER2).WaitForMixed(MIXER2).PutOnto(MIXER2).Aka(x6).PutOnto(FRIER_STATION1);

                    chef2 = chef2.GetFromCrate(EGG_CRATE).PutOnto(COUNTER6A).GetFromCrate(EGG_CRATE).PutOnto(COUNTER69)
                        .GetFromCrate(EGG_CRATE).Goto(10, 5)
                        .WaitFor(x4).ThrowTowards(MIXER1, biasDown2).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, biasDown2)
                        .Pickup(berry).ThrowTowards(MIXER1).Pickup(COUNTER6A)
                        .WaitFor(x5).ThrowTowards(MIXER3).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER3)
                        .Pickup(choco).ThrowTowards(MIXER3, biasDown1).Pickup(COUNTER69)
                        .WaitFor(x6).ThrowTowards(MIXER2).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER2)
                        .Pickup(honey).ThrowTowards(MIXER2, biasDown1);

                    chef1 = chef1.PrepareToPickup(BOARD2).WaitFor(x0).Pickup(BOARD2).PutOnto(COUNTER66)
                        .PrepareToPickup(BOARD1).WaitFor(x1).Pickup(BOARD1).PutOnto(OUTPUT)
                        .PrepareToPickup(BOARD1).WaitFor(x2).Pickup(BOARD1).PutOnto(OUTPUT)
                        .Pickup(COUNTER66).PutOnto(OUTPUT).PrepareToPickup(DIRTY_PLATE_SPAWNER);

                    chef1 = chef1.WaitForDirtyPlate().Aka(plate1).Pickup(plate1).Aka(x7).PutOnto(BOARD1).Aka(x0)
                        .PrepareToPickup(DIRTY_PLATE_SPAWNER)
                        .WaitForDirtyPlate().Aka(plate2).WaitForDirtyPlate().Pickup(plate2).PutOnto(BOARD1).Aka(x4);

                    chef4 = chef4.PrepareToPickup(BOARD3).DoWork(BOARD3).WaitFor(x7)
                        .PrepareToPickup(BOARD1).WaitFor(x0).Pickup(plate1).PutOnto(WASHING_PART).Aka(x1)
                        .DoWork(WASHING_PART).WaitForCleanPlate().Aka(plate1).Pickup(WASHING_PART).PutOnto(COUNTER21).Aka(x2);

                    chef3 = chef3.WaitFor(x0).Goto(1, 1).WaitFor(x1).DoWork(WASHING_PART).WaitForCooked(FRIER1)
                        .Sleep(15).Pickup(FRIER1).Goto(2, 1).PrepareToPickup(COUNTER21).WaitFor(x2).PutOnto(plate1).Aka(x2)
                        .PrepareToPickup(MIXER1).WaitForMixed(MIXER1).PutOnto(MIXER1).Aka(x6).PutOnto(FRIER_STATION3);

                    chef2 = chef2.GetFromCrate(BERRY_CRATE).Aka(berry).PutOnto(BOARD3).Aka(x3).DoWork(BOARD3).WaitForSpawn(berry).Aka(berry)
                        .Pickup(berry).PutOnto(COUNTER46)
                        .GetFromCrate(CHOCOLATE_CRATE).Aka(choco).PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(choco).Aka(choco)
                        .Pickup(choco).PutOnto(COUNTER45)
                        .GetFromCrate(HONEY_CRATE).Aka(honey).PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(honey).Aka(honey);

                    chef4 = chef4.PrepareToPickup(BOARD1).WaitFor(x4).Pickup(plate2).PutOnto(WASHING_PART)
                        .Pickup(plate1).PutOnto(COUNTER41).Aka(x6).DoWork(WASHING_PART).Aka(x4);

                    chef3 = chef3.Goto(1, 1).WaitFor(x4).DoWork(WASHING_PART).WaitForCleanPlate().Aka(plate2)
                        .Pickup(WASHING_PART).Pickup(FRIER3).PutOnto(COUNTER21).Aka(x5).Pickup(FRIER3).PrepareToPickup(MIXER3)
                        .WaitForMixed(MIXER3).Aka(x7).PutOnto(MIXER3).PutOnto(FRIER_STATION2);

                    chef4 = chef4.WaitFor(x5).Pickup(COUNTER21).PutOnto(BOARD1).Aka(x5).DoWork(WASHING_PART)
                        .WaitForCleanPlate().Aka(plate3).Pickup(WASHING_PART).Pickup(FRIER2).PutOnto(BOARD1).Aka(x4);

                    chef3 = chef3.WaitFor(x4).Pickup(FRIER2).PrepareToPickup(MIXER2).WaitForMixed(MIXER2).Aka(x0).PutOnto(MIXER2)
                        .PutOnto(FRIER_STATION1).Goto(1, 1);

                    chef1 = chef1.Pickup(COUNTER41).PutOnto(OUTPUT)
                        .PrepareToPickup(BOARD1).WaitFor(x5).Pickup(BOARD1).PutOnto(OUTPUT)
                        .PrepareToPickup(BOARD1).WaitFor(x4).Pickup(BOARD1).PutOnto(COUNTER92)
                        .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate().Aka(plate1)
                        .Pickup(plate1).PutOnto(BOARD1).Aka(x1).Pickup(COUNTER92).PutOnto(OUTPUT)
                        .PrepareToPickup(DIRTY_PLATE_SPAWNER);

                    chef2 = chef2.Pickup(berry).WaitFor(x6).ThrowTowards(MIXER1)
                        .GetFromCrate(EGG_CRATE).Goto(10, 5).ThrowTowards(MIXER1, biasDown2)
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, biasDown2)
                        .GetFromCrate(EGG_CRATE).PutOnto(COUNTER6A).Pickup(honey).Goto(8, 5)
                        .WaitFor(x7).ThrowTowards(MIXER3).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER3)
                        .Pickup(COUNTER6A).ThrowTowards(MIXER3);

                    chef2 = chef2.GetFromCrateAndChop(BERRY_CRATE, BOARD3, berry)
                        .Pickup(choco).WaitFor(x0).ThrowTowards(MIXER2).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER2, biasDown1)
                        .GetFromCrate(EGG_CRATE).ThrowTowards(MIXER2, biasDown1);

                    // ~3:39, 6 plates served. Need to wash more dishes. Akas used: berry, x1.

                    chef4 = chef4.WaitFor(x1).Pickup(BOARD1).PutOnto(WASHING_PART).Aka(x0).DoWork(WASHING_PART);
                    chef3 = chef3.WaitFor(x0).DoWork(WASHING_PART).WaitForCleanPlate().Aka(plate1).Pickup(WASHING_PART)
                        .Pickup(FRIER1).PutOnto(COUNTER21).Aka(x3).Pickup(FRIER1).PrepareToPickup(MIXER1).WaitForMixed(MIXER1).Aka(x0)
                        .PutOnto(MIXER1).PutOnto(FRIER_STATION3);

                    chef1 = chef1.WaitForDirtyPlate().Aka(plate2).Pickup(plate2).PutOnto(BOARD1).Aka(x1)
                        .GetFromCrate(CHOCOLATE_CRATE).Aka(choco).PutOnto(BOARD2);
                    chef4 = chef4.WaitFor(plate1).PrepareToPickup(BOARD1).WaitFor(x1).Pickup(plate2).PutOnto(WASHING_PART).Aka(x5)
                        .WaitFor(x3).Pickup(COUNTER21).PutOnto(BOARD1).Aka(x1).DoWork(WASHING_PART)
                        .WaitForCleanPlate().Aka(plate2).Pickup(WASHING_PART).Pickup(FRIER3).Aka(x3).PutOnto(COUNTER41).Aka(x4)
                        .PrepareToPickup(BOARD1);

                    chef3 = chef3.WaitFor(x5).DoWork(WASHING_PART).WaitFor(plate2).Goto(3, 1)
                        .WaitFor(x3).Pickup(FRIER3).PrepareToPickup(MIXER3).WaitForMixed(MIXER3).Aka(x3).PutOnto(MIXER3)
                        .PutOnto(FRIER_STATION2).GotoNoDash(1, 1);
                    chef1 = chef1.PrepareToPickup(BOARD1).WaitFor(x1).Pickup(BOARD1).PutOnto(OUTPUT)
                        .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate().Aka(plate3).Pickup(plate3).PutOnto(BOARD1).Aka(x2)
                        .WaitFor(x4).Pickup(COUNTER41).PutOnto(COUNTER97);

                    chef2 = chef2.GetFromCrate(EGG_CRATE).PutOnto(COUNTER6A).Pickup(berry).PutOnto(COUNTER46)
                        .GetFromCrateAndChop(HONEY_CRATE, BOARD3, honey).Pickup(honey).PutOnto(COUNTER45)
                        .Pickup(berry).WaitFor(x0).ThrowTowards(MIXER1).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, biasDown2)
                        .Pickup(COUNTER6A).ThrowTowards(MIXER1, biasDown2)
                        .GetFromCrateAndChop(BERRY_CRATE, BOARD3, berry).Pickup(berry).PutOnto(COUNTER46);

                    chef4 = chef4.WaitFor(x2).Pickup(plate3).PutOnto(WASHING_PART).Aka(x2).DoWork(WASHING_PART).WaitForCleanPlate()
                        .Aka(plate3).Pickup(WASHING_PART).PutOnto(COUNTER21).Aka(x4);
                    chef3 = chef3.WaitFor(x2).DoWork(WASHING_PART).WaitFor(plate3).Pickup(FRIER2).WaitFor(x4).PutOnto(plate3).Aka(x2)
                        .PrepareToPickup(MIXER2).WaitForMixed(MIXER2).Aka(x4).PutOnto(MIXER2).PutOnto(FRIER_STATION1).Goto(1, 1);

                    chef4 = chef4.WaitFor(x2).Pickup(plate3).PutOnto(BOARD1).Aka(x2);
                    chef1 = chef1.DoWork(BOARD2).WaitForSpawn(choco).Aka(choco)
                        .PrepareToPickup(BOARD1).WaitFor(x2).Pickup(BOARD1).PutOnto(COUNTER90).PrepareToPickup(DIRTY_PLATE_SPAWNER)
                        .WaitForDirtyPlate().Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1).Aka(x2)
                        .Pickup(COUNTER90).PutOnto(OUTPUT)
                        .SetFps(60);

                    chef2 = chef2.Pickup(honey).Goto(8, 5).WaitFor(x3).ThrowTowards(MIXER3, biasDown2).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER3)
                        .GetFromCrate(EGG_CRATE).ThrowTowards(MIXER3)
                        .GetFromCrate(EGG_CRATE).PutOnto(COUNTER6A)
                        .Goto(4, 5).WaitFor(choco).Pickup(choco).Goto(8, 5).WaitFor(x4).ThrowTowards(MIXER2)
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER2, biasDown1)
                        .Pickup(COUNTER6A).ThrowTowards(MIXER2, biasDown1)
                        .GetFromCrateAndChop(HONEY_CRATE, BOARD3, honey).Pickup(honey).PutOnto(COUNTER45)
                        .GetFromCrate(EGG_CRATE).Goto(10, 5);

                    // ~03:23. In use akas: x2
                    chef4 = chef4.WaitFor(x2).Pickup(BOARD1).PutOnto(WASHING_PART).DoWork(WASHING_PART).Aka(x2)
                        .WaitForCleanPlate().Aka(plate1).Pickup(WASHING_PART).PutOnto(COUNTER21);
                    chef3 = chef3.WaitFor(x2).DoWork(WASHING_PART).WaitFor(plate1)
                        .Pickup(FRIER1).PutOnto(plate1).Aka(x1).PrepareToPickup(MIXER1).WaitForMixed(MIXER1).Aka(x0).PutOnto(MIXER1)
                        .PutOnto(FRIER_STATION3);
                    chef4 = chef4.WaitFor(x1).Pickup(plate1).PutOnto(BOARD1).Aka(x1);

                    chef1 = chef1
                        .PrepareToPickup(BOARD1).WaitFor(x1).Pickup(plate1).PutOnto(OUTPUT)
                        .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate().Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1).Aka(x1)
                        .GetFromCrate(CHOCOLATE_CRATE).Aka(choco).PutOnto(BOARD3).Aka(x7).PrepareToPickup(BOARD1);

                    chef2 = chef2.WaitFor(x0).ThrowTowards(MIXER1, biasDown2).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, biasDown2)
                        .Pickup(berry).ThrowTowards(MIXER1)
                        .GetFromCrateAndChop(BERRY_CRATE, BOARD3, berry).Pickup(berry).PutOnto(COUNTER46)
                        .WaitFor(x7).DoWork(BOARD3).WaitForSpawn(choco).Aka(choco).Pickup(choco).PutOnto(COUNTER47);

                    chef4 = chef4.WaitFor(x1).Pickup(BOARD1).PutOnto(WASHING_PART).DoWork(WASHING_PART).Aka(x1)
                        .WaitForCleanPlate().Aka(plate1).Pickup(WASHING_PART).Pickup(FRIER3).Aka(x0).PutOnto(BOARD1).Aka(x2);

                    chef3 = chef3.WaitForCooked(FRIER3).Pickup(FRIER3).PutOnto(COUNTER21).Goto(1, 1).WaitFor(x1).DoWork(WASHING_PART)
                        .WaitFor(x0).Pickup(FRIER3).PutOnto(MIXER3).Aka(x0).PutOnto(FRIER_STATION2).Goto(1, 1);

                    chef2 = chef2.Pickup(honey).Goto(8, 5).WaitFor(x0).ThrowTowards(MIXER3)
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER3).GetFromCrate(EGG_CRATE).PutOnto(MIXER3)
                        .GetFromCrate(EGG_CRATE).PutOnto(COUNTER6A);

                    chef1 = chef1.WaitFor(x2).Pickup(BOARD1).PutOnto(OUTPUT).Pickup(COUNTER97).PutOnto(OUTPUT)
                        .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate().Pickup(DIRTY_PLATE_SPAWNER)
                        .PutOnto(BOARD1).Aka(x2);

                    chef4 = chef4.WaitFor(x2).Pickup(BOARD1).PutOnto(WASHING_PART).DoWork(WASHING_PART).Aka(x2)
                        .WaitForCleanPlate().Aka(plate1).Pickup(WASHING_PART).Goto(0, 2).PutOnto(COUNTER21);
                    chef3 = chef3.WaitFor(x2).DoWork(WASHING_PART).WaitFor(plate1).Pickup(FRIER2).Goto(1, 1).PutOnto(COUNTER21).Aka(x2)
                        .PutOnto(MIXER2).Aka(x0).PutOnto(FRIER_STATION1);

                    chef4 = chef4.WaitFor(x2).Pickup(plate1).PutOnto(BOARD1).Aka(x1);

                    chef2 = chef2
                        .GetFromCrateAndChop(HONEY_CRATE, BOARD3, honey).Pickup(honey).PutOnto(COUNTER45)
                        .Pickup(choco).WaitFor(x0).ThrowTowards(MIXER2)
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER2)
                        .Pickup(COUNTER6A).ThrowTowards(MIXER2)
                        .GetFromCrate(EGG_CRATE).PutOnto(COUNTER6A)
                        .GetFromCrateAndChop(CHOCOLATE_CRATE, BOARD3, choco).Pickup(choco).PutOnto(COUNTER47);

                    chef1 = chef1.WaitFor(x1).Pickup(BOARD1).PutOnto(OUTPUT)
                        .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate().WaitForDirtyPlate()
                        .Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1).Aka(x0);

                    // ~03:04. In use akas: honey, x0, score 1512
                    chef4 = chef4.WaitFor(x0).Pickup(BOARD1).PutOnto(WASHING_PART).DoWork(WASHING_PART).Aka(x0)
                        .WaitForCleanPlate().Aka(plate1).Pickup(WASHING_PART).Pickup(COUNTER21).Aka(x1)
                        .PutOnto(BOARD1).Aka(x2)
                        .DoWork(WASHING_PART).Aka(x5);
                    chef3 = chef3.Pickup(FRIER1).PutOnto(COUNTER21).Goto(1, 1).WaitFor(x0).DoWork(WASHING_PART)
                        .WaitFor(x1).Pickup(FRIER1).PutOnto(MIXER1).Aka(x3).PutOnto(FRIER_STATION3);

                    chef1 = chef1.PrepareToPickup(BOARD1).WaitFor(x2).Pickup(BOARD1).PutOnto(OUTPUT)
                        .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate().Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1)

                        .Aka(x7);
                    chef4 = chef4.WaitForCleanPlate().Aka(x6).Pickup(WASHING_PART).Pickup(FRIER3).Aka(x0)
                        .PutOnto(COUNTER41).Aka(x2)
                        .PrepareToPickup(BOARD1).WaitFor(x7).Pickup(BOARD1).PutOnto(WASHING_PART)
                        .DoWork(WASHING_PART).Aka(x7);

                    chef3 = chef3
                        .Goto(1, 1).WaitFor(x5).DoWork(WASHING_PART).WaitFor(x6).Goto(3, 1)
                        .WaitFor(x0).Pickup(FRIER3).PutOnto(MIXER3).Aka(x4).PutOnto(FRIER_STATION2)
                        .Pickup(FRIER2).Goto(1, 1).PutOnto(COUNTER21).Aka(x6);

                    chef2 = chef2.Pickup(berry).Goto(8, 5).WaitFor(x3).ThrowTowards(MIXER1)
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, biasDown2)
                        .Pickup(COUNTER6A).ThrowTowards(MIXER1, biasDown2);

                    chef2 = chef2.GetFromCrateAndChop(BERRY_CRATE, BOARD3, berry).Pickup(berry).PutOnto(COUNTER46)
                        .Pickup(honey).Goto(8, 5).WaitFor(x4).ThrowTowards(MIXER3, biasDown1)
                        .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER3)
                        .GetFromCrate(EGG_CRATE).PutOnto(MIXER3);

                    chef1 = chef1.PrepareToPickup(COUNTER41).WaitFor(x2).Pickup(COUNTER41)
                        .PutOnto(COUNTER97)
                        .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate().Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1)
                        .Aka(x2).PrepareToPickup(COUNTER41);

                    chef4 = chef4.WaitForCleanPlate().Pickup(WASHING_PART).WaitFor(x6).PutOnto(COUNTER21).Aka(x1).PutOnto(COUNTER41).Aka(x0)
                        .WaitFor(x2).PutOnto(WASHING_PART);

                    chef1 = chef1.WaitFor(x0).Pickup(COUNTER41).PutOnto(OUTPUT)
                        .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate().Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1).Aka(x0);

                    chef3 = chef3.WaitFor(x1).Pickup(FRIER2).PrepareToPickup(MIXER2).WaitForMixed(MIXER2).PutOnto(MIXER2)
                        .PutOnto(FRIER_STATION1);

                    chef4 = chef4.WaitFor(x0).Pickup(BOARD1).PutOnto(WASHING_PART).DoWork(WASHING_PART).Aka(x0).WaitForCleanPlate().Aka(plate1);

                    chef3 = chef3.Goto(1, 1).WaitFor(x0).DoWork(WASHING_PART).WaitFor(plate1);

                }

                executor.Graph = sequences.ToGraph();
                TimelineLayout.Sequences = sequences;

                FakeEntityRegistry.entityToTypes.Clear(); // sigh.
            }

            Carnival31_four parent;

            public GameEntities entities = new GameEntities();

            private GameActionContext context = new GameActionContext();

            public GameActionSequences sequences = new GameActionSequences();
            private GameActionExecutor executor = new GameActionExecutor();

            private IEnumerator<InputData> CurrentSession;
            bool started = false;
            public GameActionGraphState CurrentState;
            public TimelineLayout TimelineLayout = new TimelineLayout();

            private void RestartLevel() {
                CurrentState = executor.Graph.NewState();
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
                    //parent.Repaint();
                    TimelineLayout.DoLayout();
                    parent.StateHasChanged();
                });

                foreach (var(id, humanizer) in context.ChefHumanizers) {
                    humanizer.AdvanceFrame();
                }

                if (!started && entities.entities.ContainsKey(97) && entities.entities.ContainsKey(103) && CurrentState != null && CurrentSession == null) {
                    CurrentSession = executor.GenUpdate(CurrentState, context, sequences);
                    started = true;
                }

                if (CurrentSession != null) {
                    if (CurrentSession.MoveNext()) {
                        var inputs = CurrentSession.Current;
                        context.FramerateController.WaitTillNextFrame();
                        return inputs;
                    } else {
                        CurrentSession = null;
                    }
                }
                //Console.WriteLine("Delay: " + (DateTime.Now - time));
                var inputs2 = new InputData();
                if (restarted) inputs2.ResetOrderSeed = 12347;
                context.FramerateController.WaitTillNextFrame();
                return inputs2;
            }
        }
    }

}