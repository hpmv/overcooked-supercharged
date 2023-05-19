using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Hpmv
{
    class Carnival32Three : GameSetup
    {

        public (double x, double y)[] levelShape = {
                (1, 1),
                (2, 1),
                (2, 2),
                (1, 2),
            };

        public Carnival32Three()
        {
            geometry = new GameMapGeometry(new Vector2(0f, 0f), new Vector2(1.2f * 12, 1.2f * 10));
            var map = new GameMap(new Vector2[][] {
                    levelShape.Select(v => new Vector2((float) v.x, (float) v.y)).ToArray()
                }, geometry);
            mapByChef[54] = map;
            mapByChef[56] = map;
            entityRecords.CapturedInitialPositions[1] = new Vector3(21.62f, 0.448f, -15.6f);
            entityRecords.CapturedInitialPositions[2] = new Vector3(20.410002f, 0.51f, -13.229998f);
            entityRecords.CapturedInitialPositions[3] = new Vector3(20.42f, 0.448f, -15.6f);
            entityRecords.CapturedInitialPositions[4] = new Vector3(19.22f, 0.448f, -15.6f);
            entityRecords.CapturedInitialPositions[5] = new Vector3(19.21f, 0.51f, -13.229998f);
            entityRecords.CapturedInitialPositions[6] = new Vector3(25.15f, 0.5f, -9.6f);
            entityRecords.CapturedInitialPositions[7] = new Vector3(18.01f, 0.51f, -13.229998f);
            entityRecords.CapturedInitialPositions[8] = new Vector3(24f, 0.5f, -13.199999f);
            entityRecords.CapturedInitialPositions[9] = new Vector3(24f, 0.5f, -15.6f);
            entityRecords.CapturedInitialPositions[10] = new Vector3(15.6f, 0.5f, -15.6f);
            entityRecords.CapturedInitialPositions[11] = new Vector3(15.6f, 0.5f, -13.199999f);
            entityRecords.CapturedInitialPositions[12] = new Vector3(18f, 0f, -13.199999f);
            entityRecords.CapturedInitialPositions[13] = new Vector3(19.2f, 0f, -15.6f);
            entityRecords.CapturedInitialPositions[14] = new Vector3(19.199999f, 0f, -13.199999f);
            entityRecords.CapturedInitialPositions[15] = new Vector3(20.400002f, 0f, -13.199999f);
            entityRecords.CapturedInitialPositions[16] = new Vector3(21.6f, 0f, -15.6f);
            entityRecords.CapturedInitialPositions[17] = new Vector3(20.4f, 0f, -15.6f);
            entityRecords.CapturedInitialPositions[18] = new Vector3(15.6f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[19] = new Vector3(20.400002f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[20] = new Vector3(19.2f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[21] = new Vector3(20.400002f, 0f, -19.199999f);
            entityRecords.CapturedInitialPositions[22] = new Vector3(19.2f, 0f, -19.199999f);
            entityRecords.CapturedInitialPositions[23] = new Vector3(25.2f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[24] = new Vector3(24f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[25] = new Vector3(15.6f, 0f, -19.199999f);
            entityRecords.CapturedInitialPositions[26] = new Vector3(14.400001f, 0f, -19.199999f);
            entityRecords.CapturedInitialPositions[27] = new Vector3(24f, 0f, -19.199999f);
            entityRecords.CapturedInitialPositions[28] = new Vector3(24f, 0f, -16.799997f);
            entityRecords.CapturedInitialPositions[29] = new Vector3(24f, 0f, -15.6f);
            entityRecords.CapturedInitialPositions[30] = new Vector3(24f, 0f, -13.199999f);
            entityRecords.CapturedInitialPositions[31] = new Vector3(24f, 0f, -12.000002f);
            entityRecords.CapturedInitialPositions[32] = new Vector3(15.6f, 0f, -12.000002f);
            entityRecords.CapturedInitialPositions[33] = new Vector3(15.6f, 0f, -13.199999f);
            entityRecords.CapturedInitialPositions[34] = new Vector3(15.6f, 0f, -15.6f);
            entityRecords.CapturedInitialPositions[35] = new Vector3(15.6f, 0f, -16.799997f);
            entityRecords.CapturedInitialPositions[36] = new Vector3(10.8f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[37] = new Vector3(12f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[38] = new Vector3(13.2f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[39] = new Vector3(14.4f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[40] = new Vector3(25.2f, 0f, -19.199999f);
            entityRecords.CapturedInitialPositions[41] = new Vector3(26.400002f, 0f, -19.199999f);
            entityRecords.CapturedInitialPositions[42] = new Vector3(27.6f, 0f, -19.199999f);
            entityRecords.CapturedInitialPositions[43] = new Vector3(26.999998f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[44] = new Vector3(10.799999f, 0f, -19.200003f);
            entityRecords.CapturedInitialPositions[45] = new Vector3(13.207001f, 0f, -19.199999f);
            entityRecords.CapturedInitialPositions[46] = new Vector3(12.005f, 0f, -19.199999f);
            entityRecords.CapturedInitialPositions[47] = new Vector3(28.800001f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[48] = new Vector3(28.8f, 0f, -19.199999f);
            entityRecords.CapturedInitialPositions[49] = new Vector3(21.62f, 0.56496465f, -15.6f);
            entityRecords.CapturedInitialPositions[50] = new Vector3(20.42f, 0.56496465f, -15.6f);
            entityRecords.CapturedInitialPositions[51] = new Vector3(19.22f, 0.56496465f, -15.6f);
            entityRecords.CapturedInitialPositions[52] = new Vector3(5.76f, 1.7f, -14.560003f);
            entityRecords.CapturedInitialPositions[53] = new Vector3(35.36f, 1.6999999f, -15.560003f);
            entityRecords.CapturedInitialPositions[54] = new Vector3(21.19f, 1.6999999f, -3.9300022f);
            entityRecords.CapturedInitialPositions[55] = new Vector3(19.960001f, 1.6999999f, -23.400003f);
            entityRecords.CapturedInitialPositions[56] = new Vector3(13.326292f, 0f, -12.5f);
            entityRecords.CapturedInitialPositions[57] = new Vector3(26.52629f, 0f, -12.5f);
            entityRecords.CapturedInitialPositions[58] = new Vector3(13.326292f, 0f, -16.099998f);
            entityRecords.CapturedInitialPositions[59] = new Vector3(26.52629f, 0f, -16.099998f);
            entityRecords.CapturedInitialPositions[60] = new Vector3(19.2f, 0f, -14.4f);
            entityRecords.CapturedInitialPositions[61] = new Vector3(19.2f, 0f, -14.4f);
            entityRecords.CapturedInitialPositions[62] = new Vector3(20.2f, -1f, -14.2f);
            entityRecords.CapturedInitialPositions[63] = new Vector3(24f, 0.5f, -15.6f);
            entityRecords.CapturedInitialPositions[64] = new Vector3(19.22f, 0.448f, -15.6f);
            entityRecords.CapturedInitialPositions[65] = new Vector3(21.62f, 0.448f, -15.6f);
            entityRecords.CapturedInitialPositions[66] = new Vector3(24f, 0.5f, -13.199999f);
            entityRecords.CapturedInitialPositions[67] = new Vector3(15.6f, 0.5f, -15.6f);
            entityRecords.CapturedInitialPositions[68] = new Vector3(25.15f, 0.5f, -9.6f);
            entityRecords.CapturedInitialPositions[69] = new Vector3(19.21f, 0.51f, -13.229998f);
            entityRecords.CapturedInitialPositions[70] = new Vector3(20.410002f, 0.51f, -13.229998f);
            entityRecords.CapturedInitialPositions[71] = new Vector3(18.01f, 0.51f, -13.229998f);
            entityRecords.CapturedInitialPositions[72] = new Vector3(15.6f, 0.5f, -13.199999f);
            entityRecords.CapturedInitialPositions[73] = new Vector3(20.42f, 0.448f, -15.6f);


            var mixer_prefab = new PrefabRecord("Mixer", "mixer") { MaxProgress = 12, CanContainIngredients = true };
            var mixer_station_prefab = new PrefabRecord("Mixer Station", "mixer-station") {};
            var frier_prefab = new PrefabRecord("Frier", "frier") { MaxProgress = 10, CanContainIngredients = true };
            var frier_station_prefab = new PrefabRecord("Frier Station", "frier-station") {};
            var board_prefab = new PrefabRecord("Board", "board") { CanUse = true, IsBoard = true };
            var blueberry_crate_prefab = new PrefabRecord("Bluberry Crate", "blueberry-crate") { IsCrate = true };
            blueberry_crate_prefab.Spawns.Add(new PrefabRecord("Blueberry", "blueberry") { MaxProgress = 1.4, IsChoppable = true });
            blueberry_crate_prefab.Spawns[0].Spawns.Add(new PrefabRecord("Chopped Blueberry", "chopped-blueberry") { IsIngredient = true, IngredientId = 83234 });
            var chocolate_crate_prefab = new PrefabRecord("Chocolate Crate", "chocolate-crate") { IsCrate = true };
            chocolate_crate_prefab.Spawns.Add(new PrefabRecord("Chocolate", "chocolate") { MaxProgress = 1.4, IsChoppable = true });
            chocolate_crate_prefab.Spawns[0].Spawns.Add(new PrefabRecord("Chopped Chocolate", "chopped-chocolate") { IsIngredient = true, IngredientId = 22804 });
            var egg_crate_prefab = new PrefabRecord("Egg Crate", "egg-crate") { IsCrate = true };
            egg_crate_prefab.Spawns.Add(new PrefabRecord("Egg", "egg") { IsIngredient = true, IngredientId = 16620 });
            var flour_crate_prefab = new PrefabRecord("Flour Crate", "flour-crate") { IsCrate = true };
            flour_crate_prefab.Spawns.Add(new PrefabRecord("Flour", "flour") { IsIngredient = true, IngredientId = 18448 });
            var plate_prefab = new PrefabRecord("Plate", "plate") { CanContainIngredients = true };
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
            var ignore_prefab = new PrefabRecord("Ignored", "ignored") { Ignore = true };

            var MIXER_STATION1 = entityRecords.RegisterKnownObject(14, mixer_station_prefab);
            var MIXER_STATION2 = entityRecords.RegisterKnownObject(12, mixer_station_prefab);
            var MIXER_STATION3 = entityRecords.RegisterKnownObject(13, mixer_station_prefab);
            var MIXER1 = entityRecords.RegisterKnownObject(3, mixer_prefab);
            var MIXER2 = entityRecords.RegisterKnownObject(1, mixer_prefab);
            var MIXER3 = entityRecords.RegisterKnownObject(2, mixer_prefab);
            entityRecords.AttachInitialObjectTo(MIXER1, MIXER_STATION1);
            entityRecords.AttachInitialObjectTo(MIXER2, MIXER_STATION2);
            entityRecords.AttachInitialObjectTo(MIXER3, MIXER_STATION3);
            var FRIER1 = entityRecords.RegisterKnownObject(7, frier_prefab);
            var FRIER2 = entityRecords.RegisterKnownObject(5, frier_prefab);
            var FRIER3 = entityRecords.RegisterKnownObject(6, frier_prefab);
            var FRIER_STATION1 = entityRecords.RegisterKnownObject(17, frier_station_prefab);
            var FRIER_STATION2 = entityRecords.RegisterKnownObject(18, frier_station_prefab);
            var FRIER_STATION3 = entityRecords.RegisterKnownObject(19, frier_station_prefab);
            entityRecords.AttachInitialObjectTo(FRIER1, FRIER_STATION1);
            entityRecords.AttachInitialObjectTo(FRIER2, FRIER_STATION2);
            entityRecords.AttachInitialObjectTo(FRIER3, FRIER_STATION3);
            var BOARD1 = entityRecords.RegisterKnownObject(35, board_prefab);
            var BOARD2 = entityRecords.RegisterKnownObject(34, board_prefab);
            // var BOARD3 = entityRecords.RegisterKnownObject(75, board_prefab);
            // var HONEY_CRATE = entityRecords.RegisterKnownObject(66, honey_crate_prefab);

            var CHOCOLATE_CRATE = entityRecords.RegisterKnownObject(39, chocolate_crate_prefab);
            var BLUEBERRY_CRATE = entityRecords.RegisterKnownObject(36, blueberry_crate_prefab);
            var EGG_CRATE = entityRecords.RegisterKnownObject(37, egg_crate_prefab);
            var FLOUR_CRATE = entityRecords.RegisterKnownObject(38, flour_crate_prefab);
            var PLATE_1 = entityRecords.RegisterKnownObject(11, plate_prefab);
            var PLATE_2 = entityRecords.RegisterKnownObject(8, plate_prefab);
            var PLATE_3 = entityRecords.RegisterKnownObject(9, plate_prefab);
            var PLATE_4 = entityRecords.RegisterKnownObject(10, plate_prefab);
            var DIRTY_PLATE_SPAWNER = entityRecords.RegisterKnownObject(41, dirty_plate_spawner_prefab);
            var CLEAN_PLATE_SPAWNER = entityRecords.RegisterKnownObject(44, clean_plate_spawner_prefab);
            var WASHING_PART = entityRecords.RegisterKnownObject(43, washing_part_prefab);
            var OUTPUT = entityRecords.RegisterKnownObject(40, serve_prefab);

            // var COUNTER01 = entityRecords.RegisterKnownObject(61);
            // var COUNTER03 = entityRecords.RegisterKnownObject(29);
            // var COUNTER05 = entityRecords.RegisterKnownObject(28);
            // var COUNTER41 = entityRecords.RegisterKnownObject(59);
            // var COUNTER43 = entityRecords.RegisterKnownObject(23);
            // var COUNTER45 = entityRecords.RegisterKnownObject(45);
            // var COUNTER46 = entityRecords.RegisterKnownObject(58);
            // var COUNTER47 = entityRecords.RegisterKnownObject(46);
            // var COUNTER48 = entityRecords.RegisterKnownObject(60);
            // var COUNTER61 = entityRecords.RegisterKnownObject(37);
            // var COUNTER63 = entityRecords.RegisterKnownObject(21);
            // var COUNTER64 = entityRecords.RegisterKnownObject(57);
            // var COUNTER66 = entityRecords.RegisterKnownObject(22);
            // var COUNTER67 = entityRecords.RegisterKnownObject(56);
            // var COUNTER69 = entityRecords.RegisterKnownObject(43);
            // var COUNTER6A = entityRecords.RegisterKnownObject(47);
            // var COUNTER21 = entityRecords.RegisterKnownObject(63);
            // var COUNTER22 = entityRecords.RegisterKnownObject(65);
            // var COUNTER23 = entityRecords.RegisterKnownObject(26);
            // var COUNTER24 = entityRecords.RegisterKnownObject(25);
            // var COUNTER25 = entityRecords.RegisterKnownObject(62);
            // var COUNTER27 = entityRecords.RegisterKnownObject(24);
            // var COUNTER29 = entityRecords.RegisterKnownObject(44);
            // var COUNTER4B = entityRecords.RegisterKnownObject(15);
            // var COUNTER90 = entityRecords.RegisterKnownObject(35);
            // var COUNTER92 = entityRecords.RegisterKnownObject(55);
            // var COUNTER97 = entityRecords.RegisterKnownObject(31);
            var COUNTER62 = entityRecords.RegisterKnownObject(27);
            entityRecords.AttachInitialObjectTo(PLATE_1, COUNTER62);
            var COUNTER63 = entityRecords.RegisterKnownObject(26);
            entityRecords.AttachInitialObjectTo(PLATE_2, COUNTER63);
            var COUNTER66 = entityRecords.RegisterKnownObject(25);
            entityRecords.AttachInitialObjectTo(PLATE_3, COUNTER66);
            var COUNTER67 = entityRecords.RegisterKnownObject(24);
            entityRecords.AttachInitialObjectTo(PLATE_4, COUNTER67);

            // entityRecords.AttachInitialObjectTo(PLATE_1, COUNTER01);
            // entityRecords.AttachInitialObjectTo(PLATE_2, COUNTER03);
            // entityRecords.AttachInitialObjectTo(PLATE_3, COUNTER05);

            var CHEF1 = entityRecords.RegisterChef("red", 54, chef_red_prefab);
            var CHEF2 = entityRecords.RegisterChef("green", 56, chef_green_prefab);
            // var CHEF3 = entityRecords.RegisterChef("green", 101, chef_green_prefab);
            // var CHEF4 = entityRecords.RegisterChef("blue", 103, chef_blue_prefab);

            entityRecords.RegisterKnownObject(49, ignore_prefab);
            entityRecords.RegisterKnownObject(53, ignore_prefab);  // chef3
            entityRecords.RegisterKnownObject(55, ignore_prefab);  // chef4
            entityRecords.RegisterKnownObject(97, ignore_prefab);

            // // Rigid bodies.

            entityRecords.RegisterKnownObject(57, ignore_prefab);
            entityRecords.RegisterKnownObject(58, ignore_prefab);
            entityRecords.RegisterKnownObject(59, ignore_prefab);
            entityRecords.RegisterKnownObject(60, ignore_prefab);
            entityRecords.RegisterKnownObject(61, ignore_prefab);
            entityRecords.RegisterKnownObject(62, ignore_prefab);
            entityRecords.RegisterKnownObject(63, ignore_prefab);
            entityRecords.RegisterKnownObject(64, ignore_prefab);
            entityRecords.RegisterKnownObject(65, ignore_prefab);
            entityRecords.RegisterKnownObject(66, ignore_prefab);
            entityRecords.RegisterKnownObject(67, ignore_prefab);

            entityRecords.RegisterOtherInitialObjects();

            RegisterChef(CHEF1);
            RegisterChef(CHEF2);
            // RegisterChef(CHEF3);
            // RegisterChef(CHEF4);

        }
    }
}