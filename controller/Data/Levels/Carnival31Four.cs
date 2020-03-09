using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Hpmv {
    class Carnival31FourLevel : GameSetup {

        public (double x, double y)[] levelShape = {
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

        public Carnival31FourLevel() {

            map = new GameMap(new Vector2[][] {
                    levelShape.Select(v => new Vector2((float) v.x, (float) v.y)).ToArray()
                }, new Vector2(16.8f, -6f), new Vector2(1.2f * 12, 1.2f * 10));

            entityRecords.CapturedInitialPositions[1] = new Vector3(25.005999f, 0.6f, -9.592003f);
            entityRecords.CapturedInitialPositions[2] = new Vector3(20.401f, 0.269f, -6f);
            entityRecords.CapturedInitialPositions[3] = new Vector3(27.404999f, 0.6f, -9.592003f);
            entityRecords.CapturedInitialPositions[4] = new Vector3(29.806f, 0.6f, -9.592003f);
            entityRecords.CapturedInitialPositions[5] = new Vector3(22.8f, 0.269f, -6f);
            entityRecords.CapturedInitialPositions[6] = new Vector3(18.002998f, 0.269f, -6f);
            entityRecords.CapturedInitialPositions[7] = new Vector3(21.599998f, 0.5f, -6f);
            entityRecords.CapturedInitialPositions[8] = new Vector3(19.199999f, 0.5f, -6f);
            entityRecords.CapturedInitialPositions[9] = new Vector3(24f, 0.5f, -6f);
            entityRecords.CapturedInitialPositions[10] = new Vector3(27.601002f, 0.5f, -6f);
            entityRecords.CapturedInitialPositions[11] = new Vector3(20.401f, 0f, -6f);
            entityRecords.CapturedInitialPositions[12] = new Vector3(22.8f, 0f, -6f);
            entityRecords.CapturedInitialPositions[13] = new Vector3(18.002998f, 0f, -6f);
            entityRecords.CapturedInitialPositions[14] = new Vector3(31.201f, 0f, -14.399998f);
            entityRecords.CapturedInitialPositions[15] = new Vector3(31.201f, 0f, -12f);
            entityRecords.CapturedInitialPositions[16] = new Vector3(31.201f, 0f, -9.599998f);
            entityRecords.CapturedInitialPositions[17] = new Vector3(16.8f, 0f, -14.399998f);
            entityRecords.CapturedInitialPositions[18] = new Vector3(16.8f, 0f, -15.600002f);
            entityRecords.CapturedInitialPositions[19] = new Vector3(16.8f, 0f, -10.799999f);
            entityRecords.CapturedInitialPositions[20] = new Vector3(16.8f, 0f, -13.200001f);
            entityRecords.CapturedInitialPositions[21] = new Vector3(21.6f, 0f, -14.399998f);
            entityRecords.CapturedInitialPositions[22] = new Vector3(25.2f, 0f, -14.399998f);
            entityRecords.CapturedInitialPositions[23] = new Vector3(21.599998f, 0f, -12f);
            entityRecords.CapturedInitialPositions[24] = new Vector3(26.4f, 0f, -9.600002f);
            entityRecords.CapturedInitialPositions[25] = new Vector3(22.8f, 0f, -9.600002f);
            entityRecords.CapturedInitialPositions[26] = new Vector3(21.599998f, 0f, -9.599998f);
            entityRecords.CapturedInitialPositions[27] = new Vector3(27.601002f, 0f, -6f);
            entityRecords.CapturedInitialPositions[28] = new Vector3(24f, 0f, -6f);
            entityRecords.CapturedInitialPositions[29] = new Vector3(21.599998f, 0f, -6f);
            entityRecords.CapturedInitialPositions[30] = new Vector3(26.4f, 0f, -6f);
            entityRecords.CapturedInitialPositions[31] = new Vector3(26.401001f, 0f, -18f);
            entityRecords.CapturedInitialPositions[32] = new Vector3(22.8f, 0f, -18f);
            entityRecords.CapturedInitialPositions[33] = new Vector3(21.599998f, 0f, -18f);
            entityRecords.CapturedInitialPositions[34] = new Vector3(19.199999f, 0f, -18f);
            entityRecords.CapturedInitialPositions[35] = new Vector3(17.999998f, 0f, -18f);
            entityRecords.CapturedInitialPositions[36] = new Vector3(16.8f, 0f, -18f);
            entityRecords.CapturedInitialPositions[37] = new Vector3(19.199999f, 0f, -14.400002f);
            entityRecords.CapturedInitialPositions[38] = new Vector3(16.8f, 0f, -6f);
            entityRecords.CapturedInitialPositions[39] = new Vector3(16.8f, 0f, -12f);
            entityRecords.CapturedInitialPositions[40] = new Vector3(24f, 0f, -18f);
            entityRecords.CapturedInitialPositions[41] = new Vector3(30.001001f, 0f, -6f);
            entityRecords.CapturedInitialPositions[42] = new Vector3(31.201f, 0f, -18f);
            entityRecords.CapturedInitialPositions[43] = new Vector3(28.801f, 0f, -14.399998f);
            entityRecords.CapturedInitialPositions[44] = new Vector3(28.801f, 0f, -9.599998f);
            entityRecords.CapturedInitialPositions[45] = new Vector3(24f, 0f, -12f);
            entityRecords.CapturedInitialPositions[46] = new Vector3(26.4f, 0f, -12f);
            entityRecords.CapturedInitialPositions[47] = new Vector3(30.001001f, 0f, -14.399998f);
            entityRecords.CapturedInitialPositions[48] = new Vector3(30.001001f, 0f, -18f);
            entityRecords.CapturedInitialPositions[49] = new Vector3(31.201f, 0f, -16.8f);
            entityRecords.CapturedInitialPositions[50] = new Vector3(31.201f, 0f, -15.600002f);
            entityRecords.CapturedInitialPositions[51] = new Vector3(31.201f, 0f, -8.400002f);
            entityRecords.CapturedInitialPositions[52] = new Vector3(31.201f, 0f, -7.200001f);
            entityRecords.CapturedInitialPositions[53] = new Vector3(31.201f, 0f, -6f);
            entityRecords.CapturedInitialPositions[54] = new Vector3(25.199999f, 0f, -18f);
            entityRecords.CapturedInitialPositions[55] = new Vector3(20.4f, 0f, -18f);
            entityRecords.CapturedInitialPositions[56] = new Vector3(26.4f, 0f, -14.399998f);
            entityRecords.CapturedInitialPositions[57] = new Vector3(22.8f, 0f, -14.399998f);
            entityRecords.CapturedInitialPositions[58] = new Vector3(25.199999f, 0f, -12f);
            entityRecords.CapturedInitialPositions[59] = new Vector3(19.199999f, 0f, -12f);
            entityRecords.CapturedInitialPositions[60] = new Vector3(27.599998f, 0f, -12f);
            entityRecords.CapturedInitialPositions[61] = new Vector3(19.199999f, 0f, -6f);
            entityRecords.CapturedInitialPositions[62] = new Vector3(24f, 0f, -9.600002f);
            entityRecords.CapturedInitialPositions[63] = new Vector3(19.199999f, 0f, -9.600002f);
            entityRecords.CapturedInitialPositions[64] = new Vector3(25.199999f, 0f, -6f);
            entityRecords.CapturedInitialPositions[65] = new Vector3(20.4f, 0f, -9.600002f);
            entityRecords.CapturedInitialPositions[66] = new Vector3(24f, 0f, -14.399998f);
            entityRecords.CapturedInitialPositions[67] = new Vector3(30.001f, 0f, -9.600002f);
            entityRecords.CapturedInitialPositions[68] = new Vector3(25.200998f, 0f, -9.600002f);
            entityRecords.CapturedInitialPositions[69] = new Vector3(27.599998f, 0f, -9.600002f);
            entityRecords.CapturedInitialPositions[70] = new Vector3(27.601002f, 0f, -14.399998f);
            entityRecords.CapturedInitialPositions[71] = new Vector3(20.4f, 0f, -14.399998f);
            entityRecords.CapturedInitialPositions[72] = new Vector3(31.201f, 0f, -10.799999f);
            entityRecords.CapturedInitialPositions[73] = new Vector3(31.201f, 0f, -13.200001f);
            entityRecords.CapturedInitialPositions[74] = new Vector3(20.4f, 0f, -12f);
            entityRecords.CapturedInitialPositions[75] = new Vector3(22.8f, 0f, -12f);
            entityRecords.CapturedInitialPositions[76] = new Vector3(18f, 0f, -12f);
            entityRecords.CapturedInitialPositions[77] = new Vector3(28.201002f, 0f, -18f);
            entityRecords.CapturedInitialPositions[78] = new Vector3(16.8f, 0f, -16.8f);
            entityRecords.CapturedInitialPositions[79] = new Vector3(16.800999f, 0f, -9.600002f);
            entityRecords.CapturedInitialPositions[80] = new Vector3(28.801f, 0f, -12f);
            entityRecords.CapturedInitialPositions[81] = new Vector3(16.800001f, 0f, -8.406998f);
            entityRecords.CapturedInitialPositions[82] = new Vector3(16.799997f, 0f, -7.204998f);
            entityRecords.CapturedInitialPositions[83] = new Vector3(28.801144f, 0.9729098f, -6.313141f);
            entityRecords.CapturedInitialPositions[84] = new Vector3(28.801f, 0f, -6f);
            entityRecords.CapturedInitialPositions[85] = new Vector3(20.401f, 0.38596463f, -6f);
            entityRecords.CapturedInitialPositions[86] = new Vector3(22.8f, 0.38596463f, -6f);
            entityRecords.CapturedInitialPositions[87] = new Vector3(18.002998f, 0.38596463f, -6f);
            entityRecords.CapturedInitialPositions[88] = new Vector3(10.2f, 1.7f, -13.0700035f);
            entityRecords.CapturedInitialPositions[89] = new Vector3(39.8f, 1.6999999f, -14.0700035f);
            entityRecords.CapturedInitialPositions[90] = new Vector3(25.63f, 1.6999999f, -2.4400024f);
            entityRecords.CapturedInitialPositions[91] = new Vector3(24.4f, 1.6999999f, -21.91f);
            entityRecords.CapturedInitialPositions[92] = new Vector3(22.8f, 0f, -43.200005f);
            entityRecords.CapturedInitialPositions[93] = new Vector3(22.8f, 0f, -43.200005f);
            entityRecords.CapturedInitialPositions[94] = new Vector3(23.8f, -1f, -11.800005f);
            entityRecords.CapturedInitialPositions[95] = new Vector3(27.601002f, 0.5f, -6f);
            entityRecords.CapturedInitialPositions[96] = new Vector3(29.806f, 0.6f, -9.592003f);
            entityRecords.CapturedInitialPositions[97] = new Vector3(27.557293f, 3.2186508E-06f, -16.004192f);
            entityRecords.CapturedInitialPositions[98] = new Vector3(24f, 0.5f, -6f);
            entityRecords.CapturedInitialPositions[99] = new Vector3(21.676292f, 3.2186508E-06f, -16.054192f);
            entityRecords.CapturedInitialPositions[100] = new Vector3(21.599998f, 0.5f, -6f);
            entityRecords.CapturedInitialPositions[101] = new Vector3(21.786291f, 3.2186508E-06f, -7.954193f);
            entityRecords.CapturedInitialPositions[102] = new Vector3(19.199999f, 0.5f, -6f);
            entityRecords.CapturedInitialPositions[103] = new Vector3(27.597294f, 3.2186508E-06f, -7.864193f);
            entityRecords.CapturedInitialPositions[104] = new Vector3(20.401f, 0.269f, -6f);
            entityRecords.CapturedInitialPositions[105] = new Vector3(27.404999f, 0.6f, -9.592003f);
            entityRecords.CapturedInitialPositions[106] = new Vector3(25.005999f, 0.6f, -9.592003f);
            entityRecords.CapturedInitialPositions[107] = new Vector3(22.8f, 0.269f, -6f);
            entityRecords.CapturedInitialPositions[108] = new Vector3(18.002998f, 0.269f, -6f);


            var mixer_prefab = new PrefabRecord("Mixer", "mixer") { MaxProgress = 12, CanContainIngredients = true, MaxIngredientCount = 3, CookingStage = 2 };
            var mixer_station_prefab = new PrefabRecord("Mixer Station", "mixer-station") { IsMixerStation = true };
            var frier_prefab = new PrefabRecord("Frier", "frier") { MaxProgress = 10, CanContainIngredients = true, MaxIngredientCount = 3, CookingStage = 3 };
            var frier_station_prefab = new PrefabRecord("Frier Station", "frier-station") { IsHeatingStation = true };
            var board_prefab = new PrefabRecord("Board", "board") { CanUse = true, IsBoard = true };
            var honey_crate_prefab = new PrefabRecord("Honey Crate", "honey-crate") { IsCrate = true };
            honey_crate_prefab.Spawns.Add(new PrefabRecord("Honey", "honey") { MaxProgress = 1.4, IsChoppable = true });
            honey_crate_prefab.Spawns[0].Spawns.Add(new PrefabRecord("Chopped Honey", "chopped-honey") { IsIngredient = true, IngredientId = 19670 });
            var chocolate_crate_prefab = new PrefabRecord("Chocolate Crate", "chocolate-crate") { IsCrate = true };
            chocolate_crate_prefab.Spawns.Add(new PrefabRecord("Chocolate", "chocolate") { MaxProgress = 1.4, IsChoppable = true });
            chocolate_crate_prefab.Spawns[0].Spawns.Add(new PrefabRecord("Chopped Chocolate", "chopped-chocolate") { IsIngredient = true, IngredientId = 22804 });
            var berry_crate_prefab = new PrefabRecord("Berry Crate", "berry-crate") { IsCrate = true };
            berry_crate_prefab.Spawns.Add(new PrefabRecord("Berry", "berry") { MaxProgress = 1.4, IsChoppable = true });
            berry_crate_prefab.Spawns[0].Spawns.Add(new PrefabRecord("Chopped Berry", "chopped-berry") { IsIngredient = true, IngredientId = 129618 });
            var egg_crate_prefab = new PrefabRecord("Egg Crate", "egg-crate") { IsCrate = true };
            egg_crate_prefab.Spawns.Add(new PrefabRecord("Egg", "egg") { IsIngredient = true, IngredientId = 16620 });
            var flour_crate_prefab = new PrefabRecord("Flour Crate", "flour-crate") { IsCrate = true };
            flour_crate_prefab.Spawns.Add(new PrefabRecord("Flour", "flour") { IsIngredient = true, IngredientId = 18448 });
            var plate_prefab = new PrefabRecord("Plate", "plate") { CanContainIngredients = true, CookingStage = 4 };
            var dirty_plate_prefab = new PrefabRecord("Dirty Plate", "dirty plate");
            var dirty_plate_spawner_prefab = new PrefabRecord("Dirty Plate Spawner", "dirty-plate-spawner");
            dirty_plate_spawner_prefab.Spawns.Add(new PrefabRecord("Dirty Plate Stack", "dirty-plate-stack"));
            dirty_plate_spawner_prefab.Spawns[0].Spawns.Add(dirty_plate_prefab);
            var clean_plate_spawner_prefab = new PrefabRecord("Clean Plate Spawner", "clean-plate-spawner");
            clean_plate_spawner_prefab.Spawns.Add(new PrefabRecord("Clean Plate Stack", "clean-plate-stack"));
            clean_plate_spawner_prefab.Spawns[0].Spawns.Add(plate_prefab);
            var washing_part_prefab = new PrefabRecord("Sink", "sink") { CanUse = true, MaxProgress = 3, IsWashingStation = true };
            var serve_prefab = new PrefabRecord("Serve", "serve") { OccupiedGridPoints = new Vector2[] { new Vector2(-0.6f, 0f), new Vector2(0.6f, 0) } };
            var chef_red_prefab = new PrefabRecord("Chef Red", "chef-red");
            var chef_green_prefab = new PrefabRecord("Chef Green", "chef-green");
            var chef_yellow_prefab = new PrefabRecord("Chef Yellow", "chef-yellow");
            var chef_blue_prefab = new PrefabRecord("Chef Blue", "chef-blue");
            var ignore_prefab = new PrefabRecord("Ignored", "ignored") { Ignore = true };

            var MIXER_STATION1 = entityRecords.RegisterKnownObject(68, mixer_station_prefab);
            var MIXER_STATION2 = entityRecords.RegisterKnownObject(69, mixer_station_prefab);
            var MIXER_STATION3 = entityRecords.RegisterKnownObject(67, mixer_station_prefab);
            var MIXER1 = entityRecords.RegisterKnownObject(1, mixer_prefab);
            var MIXER2 = entityRecords.RegisterKnownObject(3, mixer_prefab);
            var MIXER3 = entityRecords.RegisterKnownObject(4, mixer_prefab);
            entityRecords.AttachInitialObjectTo(MIXER1, MIXER_STATION1);
            entityRecords.AttachInitialObjectTo(MIXER2, MIXER_STATION2);
            entityRecords.AttachInitialObjectTo(MIXER3, MIXER_STATION3);
            var FRIER1 = entityRecords.RegisterKnownObject(6, frier_prefab);
            var FRIER2 = entityRecords.RegisterKnownObject(2, frier_prefab);
            var FRIER3 = entityRecords.RegisterKnownObject(5, frier_prefab);
            var FRIER_STATION1 = entityRecords.RegisterKnownObject(13, frier_station_prefab);
            var FRIER_STATION2 = entityRecords.RegisterKnownObject(11, frier_station_prefab);
            var FRIER_STATION3 = entityRecords.RegisterKnownObject(12, frier_station_prefab);
            entityRecords.AttachInitialObjectTo(FRIER1, FRIER_STATION1);
            entityRecords.AttachInitialObjectTo(FRIER2, FRIER_STATION2);
            entityRecords.AttachInitialObjectTo(FRIER3, FRIER_STATION3);
            var BOARD1 = entityRecords.RegisterKnownObject(76, board_prefab);
            var BOARD2 = entityRecords.RegisterKnownObject(74, board_prefab);
            var BOARD3 = entityRecords.RegisterKnownObject(75, board_prefab);
            var HONEY_CRATE = entityRecords.RegisterKnownObject(66, honey_crate_prefab);

            var CHOCOLATE_CRATE = entityRecords.RegisterKnownObject(71, chocolate_crate_prefab);
            var BERRY_CRATE = entityRecords.RegisterKnownObject(70, berry_crate_prefab);
            var EGG_CRATE = entityRecords.RegisterKnownObject(72, egg_crate_prefab);
            var FLOUR_CRATE = entityRecords.RegisterKnownObject(73, flour_crate_prefab);
            var PLATE_1 = entityRecords.RegisterKnownObject(8, plate_prefab);
            var PLATE_2 = entityRecords.RegisterKnownObject(7, plate_prefab);
            var PLATE_3 = entityRecords.RegisterKnownObject(9, plate_prefab);
            var DIRTY_PLATE_SPAWNER = entityRecords.RegisterKnownObject(78, dirty_plate_spawner_prefab);
            var CLEAN_PLATE_SPAWNER = entityRecords.RegisterKnownObject(82, clean_plate_spawner_prefab);
            var WASHING_PART = entityRecords.RegisterKnownObject(81, washing_part_prefab);
            var OUTPUT = entityRecords.RegisterKnownObject(77, serve_prefab);

            var COUNTER01 = entityRecords.RegisterKnownObject(61);
            var COUNTER03 = entityRecords.RegisterKnownObject(29);
            var COUNTER05 = entityRecords.RegisterKnownObject(28);
            var COUNTER41 = entityRecords.RegisterKnownObject(59);
            var COUNTER43 = entityRecords.RegisterKnownObject(23);
            var COUNTER45 = entityRecords.RegisterKnownObject(45);
            var COUNTER46 = entityRecords.RegisterKnownObject(58);
            var COUNTER47 = entityRecords.RegisterKnownObject(46);
            var COUNTER48 = entityRecords.RegisterKnownObject(60);
            var COUNTER61 = entityRecords.RegisterKnownObject(37);
            var COUNTER63 = entityRecords.RegisterKnownObject(21);
            var COUNTER64 = entityRecords.RegisterKnownObject(57);
            var COUNTER66 = entityRecords.RegisterKnownObject(22);
            var COUNTER67 = entityRecords.RegisterKnownObject(56);
            var COUNTER69 = entityRecords.RegisterKnownObject(43);
            var COUNTER6A = entityRecords.RegisterKnownObject(47);
            var COUNTER21 = entityRecords.RegisterKnownObject(63);
            var COUNTER22 = entityRecords.RegisterKnownObject(65);
            var COUNTER23 = entityRecords.RegisterKnownObject(26);
            var COUNTER24 = entityRecords.RegisterKnownObject(25);
            var COUNTER25 = entityRecords.RegisterKnownObject(62);
            var COUNTER27 = entityRecords.RegisterKnownObject(24);
            var COUNTER29 = entityRecords.RegisterKnownObject(44);
            var COUNTER4B = entityRecords.RegisterKnownObject(15);
            var COUNTER90 = entityRecords.RegisterKnownObject(35);
            var COUNTER92 = entityRecords.RegisterKnownObject(55);
            var COUNTER97 = entityRecords.RegisterKnownObject(31);

            entityRecords.AttachInitialObjectTo(PLATE_1, COUNTER01);
            entityRecords.AttachInitialObjectTo(PLATE_2, COUNTER03);
            entityRecords.AttachInitialObjectTo(PLATE_3, COUNTER05);

            var CHEF1 = entityRecords.RegisterChef("red", 97, chef_red_prefab);
            var CHEF2 = entityRecords.RegisterChef("yellow", 99, chef_yellow_prefab);
            var CHEF3 = entityRecords.RegisterChef("green", 101, chef_green_prefab);
            var CHEF4 = entityRecords.RegisterChef("blue", 103, chef_blue_prefab);

            entityRecords.RegisterKnownObject(92, ignore_prefab);
            entityRecords.RegisterKnownObject(93, ignore_prefab);
            entityRecords.RegisterKnownObject(94, ignore_prefab);
            entityRecords.RegisterKnownObject(89, ignore_prefab);
            entityRecords.RegisterKnownObject(91, ignore_prefab);

            // Rigid bodies.

            entityRecords.RegisterKnownObject(95, ignore_prefab);
            entityRecords.RegisterKnownObject(96, ignore_prefab);
            entityRecords.RegisterKnownObject(98, ignore_prefab);
            entityRecords.RegisterKnownObject(100, ignore_prefab);
            entityRecords.RegisterKnownObject(102, ignore_prefab);
            entityRecords.RegisterKnownObject(104, ignore_prefab);
            entityRecords.RegisterKnownObject(105, ignore_prefab);
            entityRecords.RegisterKnownObject(106, ignore_prefab);
            entityRecords.RegisterKnownObject(107, ignore_prefab);
            entityRecords.RegisterKnownObject(108, ignore_prefab);

            entityRecords.RegisterOtherInitialObjects();

            RegisterChef(CHEF1);
            RegisterChef(CHEF2);
            RegisterChef(CHEF3);
            RegisterChef(CHEF4);

            // Below is temporary. Will remove.
            var builder = new GameActionGraphBuilder(sequences, map, null, new List<int>());
            var chef1 = builder.WithChef(CHEF1);
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

            if (true) return;
            {

                chef3 = chef3.GetFromCrate(EGG_CRATE).Goto(9, 4).ThrowTowards(MIXER1, biasDown1);

                chef2 = chef2.GetFromCrate(CHOCOLATE_CRATE).Aka(choco).PutOnto(BOARD2)
                    .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, biasDown2);

                x0 = chef3.WaitFor(chef2);

                chef3 = chef3.WaitFor(x0).GetFromCrate(EGG_CRATE).PutOnto(MIXER3);

                chef2 = chef2.WaitFor(x0).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER2, biasDown1);

                x0 = chef3.WaitFor(chef2);

                chef3 = chef3.WaitFor(x0).GetFromCrate(EGG_CRATE).Goto(9, 4).Aka(x1).PutOnto(MIXER2);

                chef2 = chef2.WaitFor(x0).GetFromCrate(FLOUR_CRATE).WaitFor(x1).ThrowTowards(MIXER3)
                    .GetFromCrate(FLOUR_CRATE).Aka(x1);

                chef3 = chef3.WaitFor(x1).GetFromCrate(EGG_CRATE).Goto(11, 5);

                chef1 = chef1.GetFromCrate(BERRY_CRATE).Aka(berry).PutOnto(BOARD1)
                    .GetFromCrate(HONEY_CRATE).Aka(honey).PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(honey).Aka(honey)
                    .Pickup(honey).Goto(9, 6).ThrowTowards(MIXER3);

                chef4 = chef4.DoWork(BOARD1).WaitForSpawn(berry).Aka(berry).Pickup(berry).ThrowTowards(MIXER1)
                    .DoWork(BOARD2).WaitForSpawn(choco).Aka(choco).Pickup(choco).Goto(7, 4).ThrowTowards(MIXER2, biasDown1)
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

                chef2 = chef2.WaitFor(x3).Goto(9, 6)
                    .WaitFor(x0).ThrowTowards(MIXER1, biasDown1).GetFromCrate(FLOUR_CRATE).Goto(9, 6)
                    .WaitFor(x2).ThrowTowards(MIXER3).GetFromCrate(FLOUR_CRATE).Goto(9, 6)
                    .WaitFor(x1).ThrowTowards(MIXER2);

                chef3 = chef3.WaitFor(x3).Goto(11, 6).WaitFor(x0).ThrowTowards(MIXER1, biasDown1).GetFromCrate(EGG_CRATE).Goto(11, 6)
                    .WaitFor(x2).ThrowTowards(MIXER3).GetFromCrate(EGG_CRATE).Goto(11, 6)
                    .WaitFor(x1).ThrowTowards(MIXER2);

                chef1 = chef1.WaitFor(x0).ThrowTowards(MIXER1)
                    .Pickup(choco).Goto(7, 6).WaitFor(x2).ThrowTowards(MIXER3, biasDown1)
                    .Pickup(berry).Goto(7, 6).WaitFor(x1).ThrowTowards(MIXER2, biasDown1);

                // friers full, second round mixers full, no ingredients cut, no grabs. ~4:11

                chef1 = chef1.GetFromCrate(BERRY_CRATE).Aka(berry).PutOnto(BOARD3).Aka(x3).DoWork(BOARD3).WaitForSpawn(berry).Aka(berry)
                    .Pickup(berry).PutOnto(COUNTER46)
                    .GetFromCrate(CHOCOLATE_CRATE).Aka(choco).PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(choco).Aka(choco)
                    .Pickup(choco).PutOnto(COUNTER45)
                    .GetFromCrate(HONEY_CRATE).Aka(honey).PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(honey).Aka(honey);

                chef4 = chef4.Pickup(PLATE_3).PrepareToPickup(FRIER1).WaitForCooked(FRIER1).PutOnto(FRIER1).PutOnto(COUNTER22).Aka(x0)
                    .Pickup(PLATE_2).PrepareToPickup(FRIER3).WaitForCooked(FRIER3).PutOnto(FRIER3).PutOnto(BOARD1).Aka(x1)
                    .Pickup(PLATE_1).PrepareToPickup(FRIER2).WaitForCooked(FRIER2).PutOnto(FRIER2).PutOnto(BOARD1).Aka(x2);

                chef3 = chef3.Goto(9, 4).PrepareToPickup(BOARD3).WaitFor(x3).DoWork(BOARD3).WaitFor(berry)
                    .PrepareToPickup(COUNTER22).WaitFor(x0).Pickup(PLATE_3).PutOnto(BOARD2).Aka(x0)
                    .Pickup(FRIER1).PrepareToPickup(MIXER1).WaitForMixed(MIXER1).PutOnto(MIXER1).Aka(x4).PutOnto(FRIER_STATION3)
                    .Goto(5, 2).WaitFor(x1).Pickup(FRIER3).PutOnto(MIXER3).Aka(x5).Goto(5, 1).PutOnto(FRIER_STATION2).Goto(7, 1);

                chef4 = chef4.Pickup(FRIER2).Goto(3, 2).PrepareToPickup(MIXER2).WaitForMixed(MIXER2).PutOnto(MIXER2).Aka(x6).PutOnto(FRIER_STATION1);

                if (true) return;

                chef2 = chef2.GetFromCrate(EGG_CRATE).PutOnto(COUNTER6A).GetFromCrate(EGG_CRATE).PutOnto(COUNTER69)
                    .GetFromCrate(EGG_CRATE).Goto(11, 6)
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

                chef1 = chef1.WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 1).Aka(plate1).Pickup(plate1).Aka(x7).PutOnto(BOARD1).Aka(x0)
                    .PrepareToPickup(DIRTY_PLATE_SPAWNER)
                    .WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 2).Aka(plate2).Pickup(plate2).PutOnto(BOARD1).Aka(x4);

                chef4 = chef4.PrepareToPickup(BOARD3).DoWork(BOARD3).WaitFor(x7)
                    .PrepareToPickup(BOARD1).WaitFor(x0).Pickup(plate1).PutOnto(WASHING_PART).Aka(x1)
                    .DoWork(WASHING_PART).WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Aka(plate1).Pickup(WASHING_PART).PutOnto(COUNTER21).Aka(x2);

                chef3 = chef3.WaitFor(x0).Goto(2, 2).WaitFor(x1).DoWork(WASHING_PART).WaitForCooked(FRIER1)
                    .Sleep(15).Pickup(FRIER1).Goto(3, 2).PrepareToPickup(COUNTER21).WaitFor(x2).PutOnto(plate1).Aka(x2)
                    .PrepareToPickup(MIXER1).WaitForMixed(MIXER1).PutOnto(MIXER1).Aka(x6).PutOnto(FRIER_STATION3);

                chef2 = chef2.GetFromCrate(BERRY_CRATE).Aka(berry).PutOnto(BOARD3).Aka(x3).DoWork(BOARD3).WaitForSpawn(berry).Aka(berry)
                    .Pickup(berry).PutOnto(COUNTER46)
                    .GetFromCrate(CHOCOLATE_CRATE).Aka(choco).PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(choco).Aka(choco)
                    .Pickup(choco).PutOnto(COUNTER45)
                    .GetFromCrate(HONEY_CRATE).Aka(honey).PutOnto(BOARD3).DoWork(BOARD3).WaitForSpawn(honey).Aka(honey);

                chef4 = chef4.PrepareToPickup(BOARD1).WaitFor(x4).Pickup(plate2).PutOnto(WASHING_PART)
                    .Pickup(plate1).PutOnto(COUNTER41).Aka(x6).DoWork(WASHING_PART).Aka(x4);

                chef3 = chef3.Goto(2, 2).WaitFor(x4).DoWork(WASHING_PART).WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Aka(plate2)
                    .Pickup(WASHING_PART).Pickup(FRIER3).PutOnto(COUNTER21).Aka(x5).Pickup(FRIER3).PrepareToPickup(MIXER3)
                    .WaitForMixed(MIXER3).Aka(x7).PutOnto(MIXER3).PutOnto(FRIER_STATION2);

                chef4 = chef4.WaitFor(x5).Pickup(COUNTER21).PutOnto(BOARD1).Aka(x5).DoWork(WASHING_PART)
                    .WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Aka(plate3).Pickup(WASHING_PART).Pickup(FRIER2).PutOnto(BOARD1).Aka(x4);

                chef3 = chef3.WaitFor(x4).Pickup(FRIER2).PrepareToPickup(MIXER2).WaitForMixed(MIXER2).Aka(x0).PutOnto(MIXER2)
                    .PutOnto(FRIER_STATION1).Goto(2, 2);

                chef1 = chef1.Pickup(COUNTER41).PutOnto(OUTPUT)
                    .PrepareToPickup(BOARD1).WaitFor(x5).Pickup(BOARD1).PutOnto(OUTPUT)
                    .PrepareToPickup(BOARD1).WaitFor(x4).Pickup(BOARD1).PutOnto(COUNTER92)
                    .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 1).Aka(plate1)
                    .Pickup(plate1).PutOnto(BOARD1).Aka(x1).Pickup(COUNTER92).PutOnto(OUTPUT)
                    .PrepareToPickup(DIRTY_PLATE_SPAWNER);

                chef2 = chef2.Pickup(berry).WaitFor(x6).ThrowTowards(MIXER1)
                    .GetFromCrate(EGG_CRATE).Goto(11, 6).ThrowTowards(MIXER1, biasDown2)
                    .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, biasDown2)
                    .GetFromCrate(EGG_CRATE).PutOnto(COUNTER6A).Pickup(honey).Goto(9, 6)
                    .WaitFor(x7).ThrowTowards(MIXER3).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER3)
                    .Pickup(COUNTER6A).ThrowTowards(MIXER3);

                chef2 = chef2.GetFromCrateAndChop(BERRY_CRATE, BOARD3, berry)
                    .Pickup(choco).WaitFor(x0).ThrowTowards(MIXER2).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER2, biasDown1)
                    .GetFromCrate(EGG_CRATE).ThrowTowards(MIXER2, biasDown1);

                // ~3:39, 6 plates served. Need to wash more dishes. Akas used: berry, x1.

                chef4 = chef4.WaitFor(x1).Pickup(BOARD1).PutOnto(WASHING_PART).Aka(x0).DoWork(WASHING_PART);
                chef3 = chef3.WaitFor(x0).DoWork(WASHING_PART).WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Aka(plate1).Pickup(WASHING_PART)
                    .Pickup(FRIER1).PutOnto(COUNTER21).Aka(x3).Pickup(FRIER1).PrepareToPickup(MIXER1).WaitForMixed(MIXER1).Aka(x0)
                    .PutOnto(MIXER1).PutOnto(FRIER_STATION3);

                chef1 = chef1.WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 1).Aka(plate2).Pickup(plate2).PutOnto(BOARD1).Aka(x1)
                    .GetFromCrate(CHOCOLATE_CRATE).Aka(choco).PutOnto(BOARD2);
                chef4 = chef4.WaitFor(plate1).PrepareToPickup(BOARD1).WaitFor(x1).Pickup(plate2).PutOnto(WASHING_PART).Aka(x5)
                    .WaitFor(x3).Pickup(COUNTER21).PutOnto(BOARD1).Aka(x1).DoWork(WASHING_PART)
                    .WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Aka(plate2).Pickup(WASHING_PART).Pickup(FRIER3).Aka(x3).PutOnto(COUNTER41).Aka(x4)
                    .PrepareToPickup(BOARD1);

                chef3 = chef3.WaitFor(x5).DoWork(WASHING_PART).WaitFor(plate2).Goto(4, 2)
                    .WaitFor(x3).Pickup(FRIER3).PrepareToPickup(MIXER3).WaitForMixed(MIXER3).Aka(x3).PutOnto(MIXER3)
                    .PutOnto(FRIER_STATION2).GotoNoDash(1, 1);
                chef1 = chef1.PrepareToPickup(BOARD1).WaitFor(x1).Pickup(BOARD1).PutOnto(OUTPUT)
                    .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 1).Aka(plate3).Pickup(plate3).PutOnto(BOARD1).Aka(x2)
                    .WaitFor(x4).Pickup(COUNTER41).PutOnto(COUNTER97);

                chef2 = chef2.GetFromCrate(EGG_CRATE).PutOnto(COUNTER6A).Pickup(berry).PutOnto(COUNTER46)
                    .GetFromCrateAndChop(HONEY_CRATE, BOARD3, honey).Pickup(honey).PutOnto(COUNTER45)
                    .Pickup(berry).WaitFor(x0).ThrowTowards(MIXER1).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, biasDown2)
                    .Pickup(COUNTER6A).ThrowTowards(MIXER1, biasDown2)
                    .GetFromCrateAndChop(BERRY_CRATE, BOARD3, berry).Pickup(berry).PutOnto(COUNTER46);

                chef4 = chef4.WaitFor(x2).Pickup(plate3).PutOnto(WASHING_PART).Aka(x2).DoWork(WASHING_PART).WaitForCleanPlate(CLEAN_PLATE_SPAWNER)
                    .Aka(plate3).Pickup(WASHING_PART).PutOnto(COUNTER21).Aka(x4);
                chef3 = chef3.WaitFor(x2).DoWork(WASHING_PART).WaitFor(plate3).Pickup(FRIER2).WaitFor(x4).PutOnto(plate3).Aka(x2)
                    .PrepareToPickup(MIXER2).WaitForMixed(MIXER2).Aka(x4).PutOnto(MIXER2).PutOnto(FRIER_STATION1).Goto(2, 2);

                chef4 = chef4.WaitFor(x2).Pickup(plate3).PutOnto(BOARD1).Aka(x2);
                chef1 = chef1.DoWork(BOARD2).WaitForSpawn(choco).Aka(choco)
                    .PrepareToPickup(BOARD1).WaitFor(x2).Pickup(BOARD1).PutOnto(COUNTER90).PrepareToPickup(DIRTY_PLATE_SPAWNER)
                    .WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 1).Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1).Aka(x2)
                    .Pickup(COUNTER90).PutOnto(OUTPUT);

                chef2 = chef2.Pickup(honey).Goto(9, 6).WaitFor(x3).ThrowTowards(MIXER3, biasDown2).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER3)
                    .GetFromCrate(EGG_CRATE).ThrowTowards(MIXER3)
                    .GetFromCrate(EGG_CRATE).PutOnto(COUNTER6A)
                    .Goto(5, 6).WaitFor(choco).Pickup(choco).Goto(9, 6).WaitFor(x4).ThrowTowards(MIXER2)
                    .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER2, biasDown1)
                    .Pickup(COUNTER6A).ThrowTowards(MIXER2, biasDown1)
                    .GetFromCrateAndChop(HONEY_CRATE, BOARD3, honey).Pickup(honey).PutOnto(COUNTER45)
                    .GetFromCrate(EGG_CRATE).Goto(11, 6);

                // ~03:23. In use akas: x2
                chef4 = chef4.WaitFor(x2).Pickup(BOARD1).PutOnto(WASHING_PART).DoWork(WASHING_PART).Aka(x2)
                    .WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Aka(plate1).Pickup(WASHING_PART).PutOnto(COUNTER21);
                chef3 = chef3.WaitFor(x2).DoWork(WASHING_PART).WaitFor(plate1)
                    .Pickup(FRIER1).PutOnto(plate1).Aka(x1).PrepareToPickup(MIXER1).WaitForMixed(MIXER1).Aka(x0).PutOnto(MIXER1)
                    .PutOnto(FRIER_STATION3);
                chef4 = chef4.WaitFor(x1).Pickup(plate1).PutOnto(BOARD1).Aka(x1);

                chef1 = chef1
                    .PrepareToPickup(BOARD1).WaitFor(x1).Pickup(plate1).PutOnto(OUTPUT)
                    .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 1).Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1).Aka(x1)
                    .GetFromCrate(CHOCOLATE_CRATE).Aka(choco).PutOnto(BOARD3).Aka(x7).PrepareToPickup(BOARD1);

                chef2 = chef2.WaitFor(x0).ThrowTowards(MIXER1, biasDown2).GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, biasDown2)
                    .Pickup(berry).ThrowTowards(MIXER1)
                    .GetFromCrateAndChop(BERRY_CRATE, BOARD3, berry).Pickup(berry).PutOnto(COUNTER46)
                    .WaitFor(x7).DoWork(BOARD3).WaitForSpawn(choco).Aka(choco).Pickup(choco).PutOnto(COUNTER47);

                chef4 = chef4.WaitFor(x1).Pickup(BOARD1).PutOnto(WASHING_PART).DoWork(WASHING_PART).Aka(x1)
                    .WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Aka(plate1).Pickup(WASHING_PART).Pickup(FRIER3).Aka(x0).PutOnto(BOARD1).Aka(x2);

                chef3 = chef3.WaitForCooked(FRIER3).Pickup(FRIER3).PutOnto(COUNTER21).Goto(2, 2).WaitFor(x1).DoWork(WASHING_PART)
                    .WaitFor(x0).Pickup(FRIER3).PutOnto(MIXER3).Aka(x0).PutOnto(FRIER_STATION2).Goto(2, 2);

                chef2 = chef2.Pickup(honey).Goto(9, 6).WaitFor(x0).ThrowTowards(MIXER3)
                    .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER3).GetFromCrate(EGG_CRATE).PutOnto(MIXER3)
                    .GetFromCrate(EGG_CRATE).PutOnto(COUNTER6A);

                chef1 = chef1.WaitFor(x2).Pickup(BOARD1).PutOnto(OUTPUT).Pickup(COUNTER97).PutOnto(OUTPUT)
                    .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 1).Pickup(DIRTY_PLATE_SPAWNER)
                    .PutOnto(BOARD1).Aka(x2);

                chef4 = chef4.WaitFor(x2).Pickup(BOARD1).PutOnto(WASHING_PART).DoWork(WASHING_PART).Aka(x2)
                    .WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Aka(plate1).Pickup(WASHING_PART).Goto(1, 3).PutOnto(COUNTER21);
                chef3 = chef3.WaitFor(x2).DoWork(WASHING_PART).WaitFor(plate1).Pickup(FRIER2).Goto(2, 2).PutOnto(COUNTER21).Aka(x2)
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
                    .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 2)
                    .Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1).Aka(x0);

                // ~03:04. In use akas: honey, x0, score 1512
                chef4 = chef4.WaitFor(x0).Pickup(BOARD1).PutOnto(WASHING_PART).DoWork(WASHING_PART).Aka(x0)
                    .WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Aka(plate1).Pickup(WASHING_PART).Pickup(COUNTER21).Aka(x1)
                    .PutOnto(BOARD1).Aka(x2)
                    .DoWork(WASHING_PART).Aka(x5);
                chef3 = chef3.Pickup(FRIER1).PutOnto(COUNTER21).Goto(2, 2).WaitFor(x0).DoWork(WASHING_PART)
                    .WaitFor(x1).Pickup(FRIER1).PutOnto(MIXER1).Aka(x3).PutOnto(FRIER_STATION3);

                chef1 = chef1.PrepareToPickup(BOARD1).WaitFor(x2).Pickup(BOARD1).PutOnto(OUTPUT)
                    .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 1).Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1)

                    .Aka(x7);
                chef4 = chef4.WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Aka(x6).Pickup(WASHING_PART).Pickup(FRIER3).Aka(x0)
                    .PutOnto(COUNTER41).Aka(x2)
                    .PrepareToPickup(BOARD1).WaitFor(x7).Pickup(BOARD1).PutOnto(WASHING_PART)
                    .DoWork(WASHING_PART).Aka(x7);

                chef3 = chef3
                    .Goto(2, 2).WaitFor(x5).DoWork(WASHING_PART).WaitFor(x6).Goto(4, 2)
                    .WaitFor(x0).Pickup(FRIER3).PutOnto(MIXER3).Aka(x4).PutOnto(FRIER_STATION2)
                    .Pickup(FRIER2).Goto(2, 2).PutOnto(COUNTER21).Aka(x6);

                chef2 = chef2.Pickup(berry).Goto(9, 6).WaitFor(x3).ThrowTowards(MIXER1)
                    .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER1, biasDown2)
                    .Pickup(COUNTER6A).ThrowTowards(MIXER1, biasDown2);

                chef2 = chef2.GetFromCrateAndChop(BERRY_CRATE, BOARD3, berry).Pickup(berry).PutOnto(COUNTER46)
                    .Pickup(honey).Goto(9, 6).WaitFor(x4).ThrowTowards(MIXER3, biasDown1)
                    .GetFromCrate(FLOUR_CRATE).ThrowTowards(MIXER3)
                    .GetFromCrate(EGG_CRATE).PutOnto(MIXER3);

                chef1 = chef1.PrepareToPickup(COUNTER41).WaitFor(x2).Pickup(COUNTER41)
                    .PutOnto(COUNTER97)
                    .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 1).Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1)
                    .Aka(x2).PrepareToPickup(COUNTER41);

                chef4 = chef4.WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Pickup(WASHING_PART).WaitFor(x6).PutOnto(COUNTER21).Aka(x1).PutOnto(COUNTER41).Aka(x0)
                    .WaitFor(x2).PutOnto(WASHING_PART);

                chef1 = chef1.WaitFor(x0).Pickup(COUNTER41).PutOnto(OUTPUT)
                    .PrepareToPickup(DIRTY_PLATE_SPAWNER).WaitForDirtyPlate(DIRTY_PLATE_SPAWNER, 1).Pickup(DIRTY_PLATE_SPAWNER).PutOnto(BOARD1).Aka(x0);

                chef3 = chef3.WaitFor(x1).Pickup(FRIER2).PrepareToPickup(MIXER2).WaitForMixed(MIXER2).PutOnto(MIXER2)
                    .PutOnto(FRIER_STATION1);

                chef4 = chef4.WaitFor(x0).Pickup(BOARD1).PutOnto(WASHING_PART).DoWork(WASHING_PART).Aka(x0).WaitForCleanPlate(CLEAN_PLATE_SPAWNER).Aka(plate1);

                chef3 = chef3.Goto(2, 2).WaitFor(x0).DoWork(WASHING_PART).WaitFor(plate1);

            }
        }
    }
}