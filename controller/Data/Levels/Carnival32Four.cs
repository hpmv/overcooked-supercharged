using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Hpmv {
    class Carnival32FourLevel : GameSetup {
        public (double x, double y)[][] levelShape = {
            new[]{
                (10.6, -10.6),
                (10.6, -18.2),
                (29, -18.2),
                (29, -10.6),
            },
            new[]{
                (14.6, -11),
                (25, -11),
                (25, -14.2),
                (14.6, -14.2),
            },
            new[]{
                (14.6, -14.6),
                (25, -14.6),
                (25, -17.8),
                (14.6, -17.8),
            }
        };

        public Carnival32FourLevel() {
            geometry = new GameMapGeometry(new Vector2(10.8f, -9.6f), new Vector2(1.2f * 15, 1.2f * 8));
            var levelMap = new GameMap(levelShape.Select(arr => arr.Select(p => new Vector2((float)p.x, (float)p.y)).ToArray()).ToArray(), geometry);
            mapByChef[56] = levelMap;
            mapByChef[57] = levelMap;
            mapByChef[58] = levelMap;
            mapByChef[59] = levelMap;

            // Entity 1 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[1] = new Vector3(21.62f, 0.448f, -15.6f);
            // Entity 2 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[2] = new Vector3(20.410002f, 0.51f, -13.229998f);
            // Entity 3 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[3] = new Vector3(20.42f, 0.448f, -15.6f);
            // Entity 4 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[4] = new Vector3(19.22f, 0.448f, -15.6f);
            // Entity 5 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[5] = new Vector3(19.21f, 0.51f, -13.229998f);
            // Entity 6 with types WorldObject, PhysicalAttach, SprayingUtensil, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown
            entityRecords.CapturedInitialPositions[6] = new Vector3(25.15f, 0.5f, -9.6f);
            // Entity 7 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[7] = new Vector3(18.01f, 0.51f, -13.229998f);
            // Entity 8 with types WorldObject, PhysicalAttach, Unknown, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, Unknown
            entityRecords.CapturedInitialPositions[8] = new Vector3(24f, 0.5f, -13.199999f);
            // Entity 9 with types WorldObject, PhysicalAttach, Unknown, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, Unknown
            entityRecords.CapturedInitialPositions[9] = new Vector3(24f, 0.5f, -15.6f);
            // Entity 10 with types WorldObject, PhysicalAttach, Unknown, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, Unknown
            entityRecords.CapturedInitialPositions[10] = new Vector3(15.6f, 0.5f, -15.6f);
            // Entity 11 with types WorldObject, PhysicalAttach, Unknown, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, Unknown
            entityRecords.CapturedInitialPositions[11] = new Vector3(15.6f, 0.5f, -13.199999f);
            // Entity 12 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[12] = new Vector3(18f, 0f, -13.199999f);
            // Entity 13 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[13] = new Vector3(19.2f, 0f, -15.6f);
            // Entity 14 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[14] = new Vector3(19.199999f, 0f, -13.199999f);
            // Entity 15 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[15] = new Vector3(20.400002f, 0f, -13.199999f);
            // Entity 16 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[16] = new Vector3(21.6f, 0f, -15.6f);
            // Entity 17 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[17] = new Vector3(20.4f, 0f, -15.6f);
            // Entity 18 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[18] = new Vector3(15.6f, 0f, -9.6f);
            // Entity 19 with types Flammable, AttachStation, Unknown, Workstation, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[19] = new Vector3(20.400002f, 0f, -9.6f);
            // Entity 20 with types Flammable, AttachStation, Unknown, Workstation, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[20] = new Vector3(19.2f, 0f, -9.6f);
            // Entity 21 with types Flammable, AttachStation, Unknown, Workstation, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[21] = new Vector3(20.400002f, 0f, -19.199999f);
            // Entity 22 with types Flammable, AttachStation, Unknown, Workstation, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[22] = new Vector3(19.2f, 0f, -19.199999f);
            // Entity 23 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[23] = new Vector3(25.2f, 0f, -9.6f);
            // Entity 24 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[24] = new Vector3(24f, 0f, -9.6f);
            // Entity 25 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[25] = new Vector3(15.6f, 0f, -19.199999f);
            // Entity 26 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[26] = new Vector3(14.400001f, 0f, -19.199999f);
            // Entity 27 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[27] = new Vector3(24f, 0f, -19.199999f);
            // Entity 28 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[28] = new Vector3(24f, 0f, -16.799997f);
            // Entity 29 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[29] = new Vector3(24f, 0f, -15.6f);
            // Entity 30 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[30] = new Vector3(24f, 0f, -13.199999f);
            // Entity 31 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[31] = new Vector3(24f, 0f, -12.000002f);
            // Entity 32 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[32] = new Vector3(15.6f, 0f, -12.000002f);
            // Entity 33 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[33] = new Vector3(15.6f, 0f, -13.199999f);
            // Entity 34 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[34] = new Vector3(15.6f, 0f, -15.6f);
            // Entity 35 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[35] = new Vector3(15.6f, 0f, -16.799997f);
            // Entity 36 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown, PlacementItemSpawner, PickupItemSwitcher, PickupItemSwitcher, Unknown
            entityRecords.CapturedInitialPositions[36] = new Vector3(10.8f, 0f, -9.6f);
            // Entity 37 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[37] = new Vector3(12f, 0f, -9.6f);
            // Entity 38 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[38] = new Vector3(13.2f, 0f, -9.6f);
            // Entity 39 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[39] = new Vector3(14.4f, 0f, -9.6f);
            // Entity 40 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[40] = new Vector3(25.2f, 0f, -19.199999f);
            // Entity 41 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[41] = new Vector3(26.400002f, 0f, -19.199999f);
            // Entity 42 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[42] = new Vector3(27.6f, 0f, -19.199999f);
            // Entity 43 with types AttachStation, Unknown, PlateStation, Unknown
            entityRecords.CapturedInitialPositions[43] = new Vector3(26.999998f, 0f, -9.6f);
            // Entity 44 with types AttachStation, Unknown, RubbishBin, Unknown, Unknown
            entityRecords.CapturedInitialPositions[44] = new Vector3(10.799999f, 0f, -19.200003f);
            // Entity 45 with types AttachStation, Unknown, WashingStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[45] = new Vector3(13.207001f, 0f, -19.199999f);
            // Entity 46 with types AttachStation, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[46] = new Vector3(12.005f, 0f, -19.199999f);
            // Entity 47 with types AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[47] = new Vector3(28.800001f, 0f, -9.6f);
            // Entity 48 with types AttachStation, Unknown, Unknown, Unknown, TriggerDisable, TriggerOnAnimator, Unknown, Unknown, Unknown, TriggerColourCycle
            entityRecords.CapturedInitialPositions[48] = new Vector3(28.8f, 0f, -19.199999f);
            // Entity 49 with types Unknown
            entityRecords.CapturedInitialPositions[49] = new Vector3(21.62f, 0.56496465f, -15.6f);
            // Entity 50 with types Unknown
            entityRecords.CapturedInitialPositions[50] = new Vector3(20.42f, 0.56496465f, -15.6f);
            // Entity 51 with types Unknown
            entityRecords.CapturedInitialPositions[51] = new Vector3(19.22f, 0.56496465f, -15.6f);
            // Entity 52 with types RespawnCollider
            entityRecords.CapturedInitialPositions[52] = new Vector3(5.76f, 1.7f, -14.560003f);
            // Entity 53 with types RespawnCollider
            entityRecords.CapturedInitialPositions[53] = new Vector3(35.36f, 1.6999999f, -15.560003f);
            // Entity 54 with types RespawnCollider
            entityRecords.CapturedInitialPositions[54] = new Vector3(21.19f, 1.6999999f, -3.9300022f);
            // Entity 55 with types RespawnCollider
            entityRecords.CapturedInitialPositions[55] = new Vector3(19.960001f, 1.6999999f, -23.400003f);
            // Entity 56 with types Chef, Unknown, ChefCarry, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, InputEvent, Unknown, AttachCatcher, Unknown
            entityRecords.CapturedInitialPositions[56] = new Vector3(13.326292f, 1.5974045E-05f, -12.5f);
            // Entity 57 with types Chef, Unknown, ChefCarry, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, InputEvent, Unknown, AttachCatcher, Unknown
            entityRecords.CapturedInitialPositions[57] = new Vector3(26.52629f, 1.5974045E-05f, -12.5f);
            // Entity 58 with types Chef, Unknown, ChefCarry, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, InputEvent, Unknown, AttachCatcher, Unknown
            entityRecords.CapturedInitialPositions[58] = new Vector3(13.326292f, 1.5974045E-05f, -16.099998f);
            // Entity 59 with types Chef, Unknown, ChefCarry, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, InputEvent, Unknown, AttachCatcher, Unknown
            entityRecords.CapturedInitialPositions[59] = new Vector3(26.52629f, 1.5974045E-05f, -16.099998f);
            // Entity 60 with types FlowController, TutorialPopup
            entityRecords.CapturedInitialPositions[60] = new Vector3(19.2f, 0f, -14.4f);
            // Entity 61 with types Unknown
            entityRecords.CapturedInitialPositions[61] = new Vector3(19.2f, 0f, -14.4f);
            // Entity 62 with types RespawnCollider
            entityRecords.CapturedInitialPositions[62] = new Vector3(20.2f, -1f, -14.2f);
            // Entity 63 with types PhysicsObject
            entityRecords.CapturedInitialPositions[63] = new Vector3(24f, 0.5f, -15.6f);
            // Entity 64 with types PhysicsObject
            entityRecords.CapturedInitialPositions[64] = new Vector3(19.22f, 0.448f, -15.6f);
            // Entity 65 with types PhysicsObject
            entityRecords.CapturedInitialPositions[65] = new Vector3(21.62f, 0.448f, -15.6f);
            // Entity 66 with types PhysicsObject
            entityRecords.CapturedInitialPositions[66] = new Vector3(24f, 0.5f, -13.199999f);
            // Entity 67 with types PhysicsObject
            entityRecords.CapturedInitialPositions[67] = new Vector3(15.6f, 0.5f, -15.6f);
            // Entity 68 with types PhysicsObject
            entityRecords.CapturedInitialPositions[68] = new Vector3(25.15f, 0.5f, -9.6f);
            // Entity 69 with types PhysicsObject
            entityRecords.CapturedInitialPositions[69] = new Vector3(19.21f, 0.51f, -13.229998f);
            // Entity 70 with types PhysicsObject
            entityRecords.CapturedInitialPositions[70] = new Vector3(20.410002f, 0.51f, -13.229998f);
            // Entity 71 with types PhysicsObject
            entityRecords.CapturedInitialPositions[71] = new Vector3(18.01f, 0.51f, -13.229998f);
            // Entity 72 with types PhysicsObject
            entityRecords.CapturedInitialPositions[72] = new Vector3(15.6f, 0.5f, -13.199999f);
            // Entity 73 with types PhysicsObject
            entityRecords.CapturedInitialPositions[73] = new Vector3(20.42f, 0.448f, -15.6f);

            var BLUE = new PrefabRecord("Chef Blue", "chef-blue") { IsChef = true };
            var RED = new PrefabRecord("Chef Red", "chef-red") { IsChef = true };
            var GREEN = new PrefabRecord("Chef Green", "chef-green") { IsChef = true };
            var YELLOW = new PrefabRecord("Chef Yellow", "chef-yellow") { IsChef = true };
            RegisterChef(entityRecords.RegisterChef("blue", 56, BLUE));
            RegisterChef(entityRecords.RegisterChef("red", 57, RED));
            RegisterChef(entityRecords.RegisterChef("green", 58, GREEN));
            RegisterChef(entityRecords.RegisterChef("yellow", 59, YELLOW));

            var CHICKEN_CRATE = new PrefabRecord("Chicken Crate", "chicken-crate") { IsCrate = true, IsAttachStation = true };
            var CHICKEN = new PrefabRecord("Chicken", "chicken") { IsChoppable = true, CanBeAttached = true, IsThrowable = true, MaxProgress = 1.4 };
            var CHOPPED_CHICKEN = new PrefabRecord("Chopped Chicken", "chopped-chicken") { CanBeAttached = true, IsThrowable = true, IsIngredient = true, IngredientId = 421028 };
            CHICKEN.Spawns.Add(CHOPPED_CHICKEN);
            CHICKEN_CRATE.Spawns.Add(CHICKEN);
            entityRecords.RegisterKnownObject(37, CHICKEN_CRATE);

            var BUN_CRATE = new PrefabRecord("Bun Crate", "bun-crate") { IsCrate = true, IsAttachStation = true };
            var BUN = new PrefabRecord("Bun", "bun") { CanBeAttached = true, IsThrowable = true, IsIngredient = true, IngredientId = 16088 };
            BUN_CRATE.Spawns.Add(BUN);
            entityRecords.RegisterKnownObject(38, BUN_CRATE);

            var MEAT_CRATE = new PrefabRecord("Meat Crate", "meat-crate") { IsCrate = true, IsAttachStation = true };
            var MEAT = new PrefabRecord("Meat", "meat") { IsChoppable = true, CanBeAttached = true, IsThrowable = true, MaxProgress = 1.4 };
            var CHOPPED_MEAT = new PrefabRecord("Chopped Meat", "chopped-meat") { CanBeAttached = true, IsThrowable = true, IsIngredient = true, IngredientId = 12958 };
            MEAT.Spawns.Add(CHOPPED_MEAT);
            MEAT_CRATE.Spawns.Add(MEAT);
            entityRecords.RegisterKnownObject(39, MEAT_CRATE);

            var POTATO_CRATE = new PrefabRecord("Potato Crate", "potato-crate") { IsCrate = true, IsAttachStation = true };
            var POTATO = new PrefabRecord("Potato", "potato") { IsChoppable = true, CanBeAttached = true, IsThrowable = true, MaxProgress = 1.4 };
            var CHOPPED_POTATO = new PrefabRecord("Chopped Potato", "chopped-potato") { CanBeAttached = true, IsThrowable = true, IsIngredient = true, IngredientId = 419988 };
            POTATO.Spawns.Add(CHOPPED_POTATO);
            POTATO_CRATE.Spawns.Add(POTATO);
            entityRecords.RegisterKnownObject(40, POTATO_CRATE);

            var ONION_CRATE = new PrefabRecord("Onion Crate", "onion-crate") { IsCrate = true, IsAttachStation = true };
            var ONION = new PrefabRecord("Onion", "onion") { IsChoppable = true, CanBeAttached = true, IsThrowable = true, MaxProgress = 1.4 };
            var CHOPPED_ONION = new PrefabRecord("Chopped Onion", "chopped-onion") { CanBeAttached = true, IsThrowable = true, IsIngredient = true, IngredientId = 442282 };
            ONION.Spawns.Add(CHOPPED_ONION);
            ONION_CRATE.Spawns.Add(ONION);
            entityRecords.RegisterKnownObject(41, ONION_CRATE);

            var CHEESE_CRATE = new PrefabRecord("Cheese Crate", "cheese-crate") { IsCrate = true, IsAttachStation = true };
            var CHEESE = new PrefabRecord("Cheese", "cheese") { IsChoppable = true, CanBeAttached = true, IsThrowable = true, MaxProgress = 1.4 };
            var CHOPPED_CHEESE = new PrefabRecord("Chopped Cheese", "chopped-cheese") { CanBeAttached = true, IsThrowable = true, IsIngredient = true, IngredientId = 509482 };
            CHEESE.Spawns.Add(CHOPPED_CHEESE);
            CHEESE_CRATE.Spawns.Add(CHEESE);
            entityRecords.RegisterKnownObject(42, CHEESE_CRATE);

            var BOARD = new PrefabRecord("Board", "board") { CanUse = true, IsBoard = true, IsAttachStation = true };
            entityRecords.RegisterKnownObject(19, BOARD);
            entityRecords.RegisterKnownObject(20, BOARD);
            entityRecords.RegisterKnownObject(21, BOARD);
            entityRecords.RegisterKnownObject(22, BOARD);

            var COUNTER = new PrefabRecord("Counter", "counter") { IsAttachStation = true };
            foreach (var i in new int[] {18, 32, 33, 34, 35, 23, 24, 25, 26, 27, 28, 29, 30, 31}) {
                entityRecords.RegisterKnownObject(i, COUNTER);
            }

            var PLATE = new PrefabRecord("Plate", "plate") { CanContainIngredients = true, CanBeAttached = true };
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(8, PLATE),
                entityRecords.GetRecordFromPath(new []{30}));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(9, PLATE),
                entityRecords.GetRecordFromPath(new []{29}));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(10, PLATE),
                entityRecords.GetRecordFromPath(new []{34}));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(11, PLATE),
                entityRecords.GetRecordFromPath(new []{33}));

            var DIRTY_PLATE_SPAWNER = new PrefabRecord("Dirty Plate Spawner", "dirty-plate-spawner") { IsAttachStation = true };
            var DIRTY_PLATE_STACK = new PrefabRecord("Dirty Plate Stack", "dirty-plate-stack") { CanBeAttached = true, IsStack = true };
            var DIRTY_PLATE = new PrefabRecord("Dirty Plate", "dirty plate");
            DIRTY_PLATE_STACK.Spawns.Add(DIRTY_PLATE);
            DIRTY_PLATE_SPAWNER.Spawns.Add(DIRTY_PLATE_STACK);
            var CLEAN_PLATE_SPAWNER = new PrefabRecord("Clean Plate Spawner", "clean-plate-spawner") { IsAttachStation = true };
            var CLEAN_PLATE_STACK = new PrefabRecord("Clean Plate Stack", "clean-plate-stack") { CanBeAttached = true, IsStack = true };
            CLEAN_PLATE_SPAWNER.Spawns.Add(CLEAN_PLATE_STACK);
            CLEAN_PLATE_STACK.Spawns.Add(PLATE);
            var WASHING_PART = new PrefabRecord("Sink", "sink") { CanUse = true, MaxProgress = 3, IsWashingStation = true, IsAttachStation = true };
            entityRecords.RegisterKnownObject(45, WASHING_PART);
            entityRecords.RegisterKnownObject(46, CLEAN_PLATE_SPAWNER);
            entityRecords.RegisterKnownObject(47, DIRTY_PLATE_SPAWNER);

            var SERVE = new PrefabRecord("Serve", "serve") { OccupiedGridPoints = new Vector2[] { new Vector2(0, -0.6f), new Vector2(0, 0.6f) } };
            entityRecords.RegisterKnownObject(43, SERVE);

            var FIRE_EXTINGUISHER = new PrefabRecord("Fire Extinguisher", "fire-extinguisher") { CanBeAttached = true };
            entityRecords.RegisterKnownObject(6, FIRE_EXTINGUISHER);

            var HEAT_STATION = new PrefabRecord("Heat Station", "heat-station") { IsAttachStation = true };
            var PAN = new PrefabRecord("Pan", "pan") { MaxProgress = 12, CanContainIngredients = true, IsCookingHandler = true, CanBeAttached = true };
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(7, PAN),
                entityRecords.RegisterKnownObject(12, HEAT_STATION));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(5, PAN),
                entityRecords.RegisterKnownObject(14, HEAT_STATION));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(2, PAN),
                entityRecords.RegisterKnownObject(15, HEAT_STATION));
            
            var FRIER = new PrefabRecord("Frier", "frier") { MaxProgress = 10, CanContainIngredients = true, IsCookingHandler = true, CanBeAttached = true };
            var FRIER_STATION = new PrefabRecord("Frier Station", "frier-station") { IsAttachStation = true };
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(4, FRIER),
                entityRecords.RegisterKnownObject(13, FRIER_STATION));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(3, FRIER),
                entityRecords.RegisterKnownObject(17, FRIER_STATION));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(1, FRIER),
                entityRecords.RegisterKnownObject(16, FRIER_STATION));

            var RED_DRINK = new PrefabRecord("Red Drink", "red-drink") { CanBeAttached = true, IsThrowable = true, IsIngredient = true, IngredientId = 430608 };
            var GREEN_DRINK = new PrefabRecord("Green Drink", "green-drink") { CanBeAttached = true, IsThrowable = true, IsIngredient = true, IngredientId = 430622 };
            var YELLOW_DRINK = new PrefabRecord("Yellow Drink", "yellow-drink") { CanBeAttached = true, IsThrowable = true, IsIngredient = true, IngredientId = 430630 };
            var DRINK_DISPENSER = new PrefabRecord("Drink Dispenser", "drink-dispenser") { IsPickupItemSwitcher = true, IsAttachStation = true };
            DRINK_DISPENSER.Spawns.Add(RED_DRINK);
            DRINK_DISPENSER.Spawns.Add(GREEN_DRINK);
            DRINK_DISPENSER.Spawns.Add(YELLOW_DRINK);
            entityRecords.RegisterKnownObject(36, DRINK_DISPENSER);

            var DRINK_BUTTON = new PrefabRecord("Drink Button", "drink-button") { HasTriggerColorCycle = true, IsAttachStation = true, CanUse = true };
            entityRecords.RegisterKnownObject(48, DRINK_BUTTON);

            var FLOW_CONTROLLER = new PrefabRecord("Flow Controller", "flow-controller") { IsKitchenFlowController = true };
            entityRecords.RegisterKnownObject(60, FLOW_CONTROLLER);

            var IGNORE = new PrefabRecord("", "") { Ignore = true };
            for (var i = 49; i <= 55; i++) {
                entityRecords.RegisterKnownObject(i, IGNORE);
            }
            for (var i = 61; i <= 73; i++) {
                entityRecords.RegisterKnownObject(i, IGNORE);
            }

            entityRecords.RegisterOtherInitialObjects();

            entityRecords.CalculateSpawningPathsForPrefabs();
        }
    }
}