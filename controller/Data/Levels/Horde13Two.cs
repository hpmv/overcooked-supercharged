using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Hpmv
{
    class Horde13TwoLevel : GameSetup
    {
        public (double x, double y)[] levelShapeTop = {
                (1, -1),
                (1, -5),
                (9.9, -5),
                (9.9, -4),
                (11, -4),
                (11, -1),
            };
        public (double x, double y)[] levelShapeBottom = {
                (1, -7),
                (1, -11),
                (11, -11),
                (11, -8),
                (9.9, -8),
                (9.9, -7)
            };

        public Horde13TwoLevel()
        {
            geometry = new GameMapGeometry(new Vector2(0f, 0f), new Vector2(1.2f * 12, 1.2f * 10));
            mapByChef[60] = new GameMap(new Vector2[][]{
                    levelShapeTop.Select(v => new Vector2((float) v.x, (float) v.y)).ToArray()
            }, geometry);
            mapByChef[62] = new GameMap(new Vector2[][]{
                    levelShapeBottom.Select(v => new Vector2((float) v.x, (float) v.y)).ToArray()
            }, geometry);

            entityRecords.CapturedInitialPositions[1] = new Vector3(7.2f, 0.702f, 0f);
            entityRecords.CapturedInitialPositions[2] = new Vector3(4.8f, 0.702f, 0f);
            entityRecords.CapturedInitialPositions[3] = new Vector3(2.4f, 0.702f, 0f);
            entityRecords.CapturedInitialPositions[4] = new Vector3(1.1999998f, 0.58f, -12f);
            entityRecords.CapturedInitialPositions[5] = new Vector3(2.4f, 0.58f, -12.03f);
            entityRecords.CapturedInitialPositions[6] = new Vector3(3.5999994f, 0.58f, -12f);
            entityRecords.CapturedInitialPositions[7] = new Vector3(4.8f, 0.58f, -12f);
            entityRecords.CapturedInitialPositions[8] = new Vector3(12f, 0f, -1.2f);
            entityRecords.CapturedInitialPositions[9] = new Vector3(12f, 0f, -2.4f);
            entityRecords.CapturedInitialPositions[10] = new Vector3(12f, 0f, -3.6f);
            entityRecords.CapturedInitialPositions[11] = new Vector3(15.6f, 0f, -8.400001f);
            entityRecords.CapturedInitialPositions[12] = new Vector3(15.6f, 0f, -10.8f);
            entityRecords.CapturedInitialPositions[13] = new Vector3(2.3999999f, 0f, 0f);
            entityRecords.CapturedInitialPositions[14] = new Vector3(4.8f, 0f, 0f);
            entityRecords.CapturedInitialPositions[15] = new Vector3(7.2f, 0f, 0f);
            entityRecords.CapturedInitialPositions[16] = new Vector3(0f, 0f, 0f);
            entityRecords.CapturedInitialPositions[17] = new Vector3(9.6f, 0f, -6f);
            entityRecords.CapturedInitialPositions[18] = new Vector3(7.2f, 0f, -6f);
            entityRecords.CapturedInitialPositions[19] = new Vector3(6f, 0f, -6f);
            entityRecords.CapturedInitialPositions[20] = new Vector3(8.4f, 0f, -6f);
            entityRecords.CapturedInitialPositions[21] = new Vector3(3.6f, 0f, -6f);
            entityRecords.CapturedInitialPositions[22] = new Vector3(2.3999999f, 0f, -6f);
            entityRecords.CapturedInitialPositions[23] = new Vector3(1.1999998f, 0f, -6f);
            entityRecords.CapturedInitialPositions[24] = new Vector3(0f, 0f, -4.8f);
            entityRecords.CapturedInitialPositions[25] = new Vector3(6f, 0f, -12f);
            entityRecords.CapturedInitialPositions[26] = new Vector3(0f, 0f, -10.8f);
            entityRecords.CapturedInitialPositions[27] = new Vector3(3.6f, 0f, 0f);
            entityRecords.CapturedInitialPositions[28] = new Vector3(0f, 0f, -6f);
            entityRecords.CapturedInitialPositions[29] = new Vector3(6f, 0f, 0f);
            entityRecords.CapturedInitialPositions[30] = new Vector3(0f, 0f, -12f);
            entityRecords.CapturedInitialPositions[31] = new Vector3(0f, 0f, -7.2000003f);
            entityRecords.CapturedInitialPositions[32] = new Vector3(4.8f, 0f, -12f);
            entityRecords.CapturedInitialPositions[33] = new Vector3(3.5999994f, 0f, -12f);
            entityRecords.CapturedInitialPositions[34] = new Vector3(1.1999998f, 0f, -12f);
            entityRecords.CapturedInitialPositions[35] = new Vector3(1.1999998f, 0f, 0f);
            entityRecords.CapturedInitialPositions[36] = new Vector3(2.3999999f, 0f, -12f);
            entityRecords.CapturedInitialPositions[37] = new Vector3(7.2f, 0f, -12f);
            entityRecords.CapturedInitialPositions[38] = new Vector3(8.4f, 0f, -12f);
            entityRecords.CapturedInitialPositions[39] = new Vector3(9.599999f, 0f, -12f);
            entityRecords.CapturedInitialPositions[40] = new Vector3(9.6f, 0f, 0f);
            entityRecords.CapturedInitialPositions[41] = new Vector3(8.4f, 0f, 0f);
            entityRecords.CapturedInitialPositions[42] = new Vector3(15.6f, 0f, -9.6f);
            entityRecords.CapturedInitialPositions[43] = new Vector3(0f, 0f, -9f);
            entityRecords.CapturedInitialPositions[44] = new Vector3(0f, 0f, -1.2f);
            entityRecords.CapturedInitialPositions[45] = new Vector3(-5.9604645E-08f, 0f, -3.607f);
            entityRecords.CapturedInitialPositions[46] = new Vector3(5.9604645E-08f, 0f, -2.405f);
            entityRecords.CapturedInitialPositions[47] = new Vector3(0f, 0f, 0f);
            entityRecords.CapturedInitialPositions[48] = new Vector3(3.6f, 0f, 0f);
            entityRecords.CapturedInitialPositions[49] = new Vector3(4.8f, 0f, -6f);
            entityRecords.CapturedInitialPositions[50] = new Vector3(0f, 0f, 0f);
            entityRecords.CapturedInitialPositions[51] = new Vector3(-6.54f, 1.7f, -6.52f);
            entityRecords.CapturedInitialPositions[52] = new Vector3(24.61f, 1.6999999f, -5.69f);
            entityRecords.CapturedInitialPositions[53] = new Vector3(9.54f, 1.6999999f, 6.1000004f);
            entityRecords.CapturedInitialPositions[54] = new Vector3(9.16f, 1.6999999f, -17.35f);
            entityRecords.CapturedInitialPositions[55] = new Vector3(1.181f, 0.63823044f, -0.069f);
            entityRecords.CapturedInitialPositions[56] = new Vector3(0f, 0f, 0f);
            entityRecords.CapturedInitialPositions[57] = new Vector3(0f, 0f, 0f);
            entityRecords.CapturedInitialPositions[58] = new Vector3(7f, -3.64f, 5f);
            entityRecords.CapturedInitialPositions[59] = new Vector3(3.2062917f, 1.5974045E-05f, -9.224195f);
            entityRecords.CapturedInitialPositions[60] = new Vector3(3.2062917f, 1.5974045E-05f, -3.224194f);
            entityRecords.CapturedInitialPositions[61] = new Vector3(8.006291f, 1.5974045E-05f, -3.224194f);
            entityRecords.CapturedInitialPositions[62] = new Vector3(8.006291f, 1.5974045E-05f, -9.224195f);
            entityRecords.CapturedInitialPositions[63] = new Vector3(3.5999994f, 0.58f, -12f);
            entityRecords.CapturedInitialPositions[64] = new Vector3(1.1999998f, 0.58f, -12f);
            entityRecords.CapturedInitialPositions[65] = new Vector3(2.4f, 0.58f, -12.03f);
            entityRecords.CapturedInitialPositions[66] = new Vector3(1.181f, 0.63823044f, -0.069f);
            entityRecords.CapturedInitialPositions[67] = new Vector3(2.4f, 0.702f, 0f);
            entityRecords.CapturedInitialPositions[68] = new Vector3(4.8f, 0.702f, 0f);
            entityRecords.CapturedInitialPositions[69] = new Vector3(4.8f, 0.58f, -12f);
            entityRecords.CapturedInitialPositions[70] = new Vector3(7.2f, 0.702f, 0f);


            var roating_tray_prefab = new PrefabRecord("Tray", "roating-tray") { MaxProgress = 12, CanContainIngredients = true, MaxIngredientCount = 4, CookingStage = 2 };
            var oven_prefab = new PrefabRecord("Oven", "oven") { IsHeatingStation = true };
            var beef_prefab = new PrefabRecord("Beef", "beef") { IsIngredient = true, IngredientId = 54456 };
            var chicken_prefab = new PrefabRecord("Chicken", "chicken") { IsIngredient = true, IngredientId = 32308 };
            var beef_crate_prefab = new PrefabRecord("Beef Crate", "beef-crate") { IsCrate = true };
            beef_crate_prefab.Spawns.Add(beef_prefab);
            var chicken_crate_prefab = new PrefabRecord("Chicken Crate", "chicken-crate") { IsCrate = true };
            chicken_crate_prefab.Spawns.Add(chicken_prefab);
            var chopped_potato_prefab = new PrefabRecord("Chopped Potato", "chopped-potato") { IsIngredient = true, IngredientId = 109880 };
            var chopped_carrot_prefab = new PrefabRecord("Chopped Carrot", "chopped-carrot") { IsIngredient = true, IngredientId = 17708 };
            var chopped_brocolli_prefab = new PrefabRecord("Chopped Brocolli", "chopped-brocolli") { IsIngredient = true, IngredientId = 112986 };
            var potato_prefab = new PrefabRecord("Potato", "potato") { MaxProgress = 1.4, IsChoppable = true };
            var carrot_prefab = new PrefabRecord("Carrot", "carrot") { MaxProgress = 1.4, IsChoppable = true };
            var brocolli_prefab = new PrefabRecord("Brocolli", "brocolli") { MaxProgress = 1.4, IsChoppable = true };
            potato_prefab.Spawns.Add(chopped_potato_prefab);
            carrot_prefab.Spawns.Add(chopped_carrot_prefab);
            brocolli_prefab.Spawns.Add(chopped_brocolli_prefab);
            var potato_crate_prefab = new PrefabRecord("Potato Crate", "potato-crate") { IsCrate = true };
            var carrot_crate_prefab = new PrefabRecord("Carrot Crate", "carrot-crate") { IsCrate = true };
            var brocolli_crate_prefab = new PrefabRecord("Brocolli Crate", "brocolli-crate") { IsCrate = true };
            potato_crate_prefab.Spawns.Add(potato_prefab);
            carrot_crate_prefab.Spawns.Add(carrot_prefab);
            brocolli_crate_prefab.Spawns.Add(brocolli_prefab);

            var board_prefab = new PrefabRecord("Board", "board") { CanUse = true, IsBoard = true };
            var plate_prefab = new PrefabRecord("Plate", "plate") { CanContainIngredients = true, CookingStage = 3 };
            var dirty_plate_prefab = new PrefabRecord("Dirty Plate", "dirty plate");
            var dirty_plate_spawner_prefab = new PrefabRecord("Dirty Plate Spawner", "dirty-plate-spawner");
            dirty_plate_spawner_prefab.Spawns.Add(new PrefabRecord("Dirty Plate Stack", "dirty-plate-stack"));
            dirty_plate_spawner_prefab.Spawns[0].Spawns.Add(dirty_plate_prefab);
            var clean_plate_spawner_prefab = new PrefabRecord("Clean Plate Spawner", "clean-plate-spawner");
            clean_plate_spawner_prefab.Spawns.Add(new PrefabRecord("Clean Plate Stack", "clean-plate-stack"));
            clean_plate_spawner_prefab.Spawns[0].Spawns.Add(plate_prefab);
            var washing_part_prefab = new PrefabRecord("Sink", "sink") { CanUse = true, MaxProgress = 3, IsWashingStation = true };
            var serve_prefab = new PrefabRecord("Serve", "serve") { OccupiedGridPoints = new Vector2[] { new Vector2(0, -0.6f), new Vector2(0, 0.6f) } };
            var button_prefab = new PrefabRecord("Button", "button") { IsButton = true };

            var chef_blue_prefab = new PrefabRecord("Blue", "chef-blue");
            var chef_red_prefab = new PrefabRecord("Red", "chef-red");
            var ignore_prefab = new PrefabRecord("Ignored", "ignored") { Ignore = true };

            var OVEN1 = entityRecords.RegisterKnownObject(13, oven_prefab);
            var OVEN2 = entityRecords.RegisterKnownObject(14, oven_prefab);
            var OVEN3 = entityRecords.RegisterKnownObject(15, oven_prefab);
            var TRAY1 = entityRecords.RegisterKnownObject(3, roating_tray_prefab);
            var TRAY2 = entityRecords.RegisterKnownObject(2, roating_tray_prefab);
            var TRAY3 = entityRecords.RegisterKnownObject(1, roating_tray_prefab);
            entityRecords.AttachInitialObjectTo(TRAY1, OVEN1);
            entityRecords.AttachInitialObjectTo(TRAY2, OVEN2);
            entityRecords.AttachInitialObjectTo(TRAY3, OVEN3);
            entityRecords.RegisterKnownObject(41, beef_crate_prefab);
            entityRecords.RegisterKnownObject(40, chicken_crate_prefab);
            entityRecords.RegisterKnownObject(8, potato_crate_prefab);
            entityRecords.RegisterKnownObject(9, carrot_crate_prefab);
            entityRecords.RegisterKnownObject(10, brocolli_crate_prefab);
            entityRecords.RegisterKnownObject(49, button_prefab);
            entityRecords.RegisterKnownObject(46, clean_plate_spawner_prefab);
            entityRecords.RegisterKnownObject(45, washing_part_prefab);
            entityRecords.RegisterKnownObject(42, dirty_plate_spawner_prefab);
            entityRecords.RegisterKnownObject(11, board_prefab);
            entityRecords.RegisterKnownObject(12, board_prefab);
            var PLATE1 = entityRecords.RegisterKnownObject(4, plate_prefab);
            var PLATE2 = entityRecords.RegisterKnownObject(5, plate_prefab);
            var PLATE3 = entityRecords.RegisterKnownObject(6, plate_prefab);
            var PLATE4 = entityRecords.RegisterKnownObject(7, plate_prefab);
            entityRecords.AttachInitialObjectTo(PLATE1, entityRecords.RegisterKnownObject(34));
            entityRecords.AttachInitialObjectTo(PLATE2, entityRecords.RegisterKnownObject(36));
            entityRecords.AttachInitialObjectTo(PLATE3, entityRecords.RegisterKnownObject(33));
            entityRecords.AttachInitialObjectTo(PLATE4, entityRecords.RegisterKnownObject(32));
            entityRecords.RegisterKnownObject(43, serve_prefab);

            entityRecords.RegisterKnownObject(59, ignore_prefab);
            entityRecords.RegisterKnownObject(61, ignore_prefab);
            entityRecords.RegisterKnownObject(54, ignore_prefab);
            entityRecords.RegisterKnownObject(52, ignore_prefab);


            RegisterChef(entityRecords.RegisterChef("blue", 60, chef_blue_prefab));
            RegisterChef(entityRecords.RegisterChef("red", 62, chef_red_prefab));

            entityRecords.RegisterOtherInitialObjects();
        }
    }
}