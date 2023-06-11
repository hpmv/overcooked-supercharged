using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Hpmv {
    class Carnival34FourLevel : GameSetup {

        public (double x, double y)[] topleftShape = {
                (9.4, -11.8),
                (9.4, -13.4),
                (9.8, -13.4),
                (9.8, -14.5),
                (14.6, -14.5),
                (14.6, -11.8),
            };
        public (double x, double y)[] bottomLeftShape = {
            (9.4, -17.8),
            (9.4, -20.6),
            (14.6, -20.6),
            (14.6, -17.8),
        };

        public (double x, double y)[] topRightShape = {
                (26.2, -11.8),
                (26.2, -14.5),
                (30.8, -14.5),
                (30.8, -13.4),
                (31.4, -13.4),
                (31.4, -11.8),
        };

        public (double x, double y)[] bottomRightShape = {
                (26.2, -17.8),
                (26.2, -20.6),
                (31.4, -20.6),
                (31.4, -17.8),
        };

        public (double x, double y)[][] middleShape = {
            new[]{
                (16.6, -11.8),
                (16.6, -20.6),
                (24.2, -20.6),
                (24.2, -11.8),
            },
            new[]{
                (18.2, -14.6),
                (22.6, -14.6),
                (22.6, -17.8),
                (18.2, -17.8),
            }
        };

        public Carnival34FourLevel() {
            geometry = new GameMapGeometry(new Vector2(8.4f, -10.8f), new Vector2(1.2f * 20, 1.2f * 9));
            var middleMap = new GameMap(middleShape.Select(arr => arr.Select(p => new Vector2((float)p.x, (float)p.y)).ToArray()).ToArray(), geometry);
            var sideMap = new GameMap(new[] {topleftShape, topRightShape, bottomLeftShape, bottomRightShape}.Select(arr => arr.Select(p => new Vector2((float)p.x, (float)p.y)).ToArray()).ToArray(), geometry);
            mapByChef[104] = sideMap;
            mapByChef[105] = sideMap;
            mapByChef[103] = middleMap;
            mapByChef[106] = middleMap;

            // Entity 1 with types WorldObject, PhysicalAttach, SprayingUtensil, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown
            entityRecords.CapturedInitialPositions[1] = new Vector3(15.6f, 0.5f, -15.599998f);
            // Entity 2 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[2] = new Vector3(18.01f, 0.51f, -10.829998f);
            // Entity 3 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, MixingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[3] = new Vector3(22.8f, 0.563f, -10.759998f);
            // Entity 4 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[4] = new Vector3(18.010006f, 0.51f, -21.630001f);
            // Entity 5 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[5] = new Vector3(22.950008f, 0.448f, -21.61f);
            // Entity 6 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, MixingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[6] = new Vector3(24f, 0.563f, -10.759998f);
            // Entity 7 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[7] = new Vector3(16.81f, 0.51f, -10.829998f);
            // Entity 8 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[8] = new Vector3(24.150005f, 0.448f, -21.61f);
            // Entity 9 with types WorldObject, PhysicalAttach, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, CookingState, Unknown, RespawnBehaviour, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[9] = new Vector3(16.810005f, 0.51f, -21.630001f);
            // Entity 10 with types WorldObject, PhysicalAttach, Unknown, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown
            entityRecords.CapturedInitialPositions[10] = new Vector3(19.2f, 0.58f, -15.599998f);
            // Entity 11 with types WorldObject, PhysicalAttach, Unknown, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown
            entityRecords.CapturedInitialPositions[11] = new Vector3(21.599998f, 0.58f, -15.599998f);
            // Entity 12 with types WorldObject, PhysicalAttach, Unknown, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown
            entityRecords.CapturedInitialPositions[12] = new Vector3(19.2f, 0.58f, -16.8f);
            // Entity 13 with types WorldObject, PhysicalAttach, Unknown, Unknown, Unknown, IngredientContainer, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown
            entityRecords.CapturedInitialPositions[13] = new Vector3(21.599998f, 0.58f, -16.8f);
            // Entity 14 with types Flammable, AttachStation, Unknown, Unknown, MixingStation, Unknown
            entityRecords.CapturedInitialPositions[14] = new Vector3(22.8f, 0f, -10.799999f);
            // Entity 15 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[15] = new Vector3(22.8f, 0f, -21.599998f);
            // Entity 16 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[16] = new Vector3(18f, 0f, -21.599998f);
            // Entity 17 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[17] = new Vector3(18f, 0f, -10.799999f);
            // Entity 18 with types Flammable, AttachStation, Unknown, Unknown, MixingStation, Unknown
            entityRecords.CapturedInitialPositions[18] = new Vector3(24f, 0f, -10.799999f);
            // Entity 19 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[19] = new Vector3(16.8f, 0f, -10.799999f);
            // Entity 20 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[20] = new Vector3(24f, 0f, -21.599998f);
            // Entity 21 with types Flammable, AttachStation, Unknown, Unknown, CookingStation, Unknown
            entityRecords.CapturedInitialPositions[21] = new Vector3(16.8f, 0f, -21.599998f);
            // Entity 22 with types Flammable
            entityRecords.CapturedInitialPositions[22] = new Vector3(15.599999f, 0f, -10.799999f);
            // Entity 23 with types Flammable, AttachStation, Unknown, Workstation, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[23] = new Vector3(15.599999f, 0f, -14.399998f);
            // Entity 24 with types Flammable, AttachStation, Unknown, Workstation, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[24] = new Vector3(25.2f, 0f, -14.399998f);
            // Entity 25 with types Flammable
            entityRecords.CapturedInitialPositions[25] = new Vector3(25.2f, 0f, -10.799999f);
            // Entity 26 with types Flammable
            entityRecords.CapturedInitialPositions[26] = new Vector3(25.2f, 0f, -21.599998f);
            // Entity 27 with types Flammable
            entityRecords.CapturedInitialPositions[27] = new Vector3(15.599999f, 0f, -21.599998f);
            // Entity 28 with types Flammable
            entityRecords.CapturedInitialPositions[28] = new Vector3(8.4f, 0f, -10.799999f);
            // Entity 29 with types Flammable
            entityRecords.CapturedInitialPositions[29] = new Vector3(32.4f, 0f, -10.799999f);
            // Entity 30 with types Flammable
            entityRecords.CapturedInitialPositions[30] = new Vector3(32.4f, 0f, -21.599998f);
            // Entity 31 with types Flammable
            entityRecords.CapturedInitialPositions[31] = new Vector3(8.4f, 0f, -21.599998f);
            // Entity 32 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[32] = new Vector3(19.2f, 0f, -10.799999f);
            // Entity 33 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[33] = new Vector3(21.599998f, 0f, -10.799999f);
            // Entity 34 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[34] = new Vector3(14.4f, 0f, -21.599998f);
            // Entity 35 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[35] = new Vector3(10.799999f, 0f, -21.599998f);
            // Entity 36 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[36] = new Vector3(31.199999f, 0f, -21.599998f);
            // Entity 37 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[37] = new Vector3(21.599998f, 0f, -21.599998f);
            // Entity 38 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[38] = new Vector3(19.2f, 0f, -15.599998f);
            // Entity 39 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[39] = new Vector3(9.6f, 0f, -10.799999f);
            // Entity 40 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[40] = new Vector3(21.599998f, 0f, -15.599998f);
            // Entity 41 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[41] = new Vector3(19.2f, 0f, -16.8f);
            // Entity 42 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[42] = new Vector3(20.4f, 0f, -16.8f);
            // Entity 43 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[43] = new Vector3(21.599998f, 0f, -16.8f);
            // Entity 44 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[44] = new Vector3(15.599999f, 0f, -20.399998f);
            // Entity 45 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[45] = new Vector3(15.599999f, 0f, -13.199997f);
            // Entity 46 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[46] = new Vector3(15.599999f, 0f, -19.199997f);
            // Entity 47 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[47] = new Vector3(25.2f, 0f, -19.199997f);
            // Entity 48 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[48] = new Vector3(25.2f, 0f, -13.199997f);
            // Entity 49 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[49] = new Vector3(25.2f, 0f, -20.399998f);
            // Entity 50 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[50] = new Vector3(15.599999f, 0f, -18f);
            // Entity 51 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[51] = new Vector3(8.4f, 0f, -13.199997f);
            // Entity 52 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[52] = new Vector3(25.2f, 0f, -18f);
            // Entity 53 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[53] = new Vector3(8.4f, 0f, -20.399998f);
            // Entity 54 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[54] = new Vector3(8.4f, 0f, -19.199997f);
            // Entity 55 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[55] = new Vector3(27.6f, 0f, -21.599998f);
            // Entity 56 with types Flammable, AttachStation, Unknown, Workstation, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[56] = new Vector3(15.599999f, 0f, -12f);
            // Entity 57 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[57] = new Vector3(32.4f, 0f, -13.199997f);
            // Entity 58 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[58] = new Vector3(30f, 0f, -21.599998f);
            // Entity 59 with types Flammable, AttachStation, Unknown, Workstation, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[59] = new Vector3(25.2f, 0f, -12f);
            // Entity 60 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[60] = new Vector3(26.4f, 0f, -21.599998f);
            // Entity 61 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[61] = new Vector3(19.2f, 0f, -21.599998f);
            // Entity 62 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[62] = new Vector3(9.599999f, 0f, -21.599998f);
            // Entity 63 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[63] = new Vector3(15.599999f, 0f, -15.599998f);
            // Entity 64 with types Flammable, AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[64] = new Vector3(28.8f, 0f, -21.599998f);
            // Entity 65 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[65] = new Vector3(30f, 0f, -10.799999f);
            // Entity 66 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[66] = new Vector3(27.6f, 0f, -10.799999f);
            // Entity 67 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[67] = new Vector3(26.4f, 0f, -10.799999f);
            // Entity 68 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[68] = new Vector3(10.8f, 0f, -10.799999f);
            // Entity 69 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[69] = new Vector3(13.199999f, 0f, -10.799999f);
            // Entity 70 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[70] = new Vector3(14.4f, 0f, -10.799999f);
            // Entity 71 with types Flammable, AttachStation, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[71] = new Vector3(31.199999f, 0f, -10.799999f);
            // Entity 72 with types Flammable, Unknown, Unknown, PlacementItemSpawner, PickupItemSwitcher, Unknown
            entityRecords.CapturedInitialPositions[72] = new Vector3(20.4f, 0f, -10.799999f);
            // Entity 73 with types AttachStation, Unknown, PlateStation, Unknown
            entityRecords.CapturedInitialPositions[73] = new Vector3(32.40001f, 0f, -19.8f);
            // Entity 74 with types AttachStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[74] = new Vector3(20.4f, 0f, -21.599998f);
            // Entity 75 with types AttachStation, Unknown, WashingStation, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[75] = new Vector3(13.207007f, 0f, -21.600029f);
            // Entity 76 with types AttachStation, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[76] = new Vector3(12.005007f, 0f, -21.599976f);
            // Entity 77 with types AttachStation, Unknown, Unknown, Unknown, TriggerDisable, TriggerOnAnimator, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[77] = new Vector3(25.2f, 0f, -16.810001f);
            // Entity 78 with types AttachStation, Unknown, Unknown, Unknown, TriggerDisable, TriggerOnAnimator, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[78] = new Vector3(15.6f, 0f, -16.810001f);
            // Entity 79 with types AttachStation, Unknown, Unknown, Unknown, TriggerDisable, TriggerOnAnimator, Unknown, Unknown, Unknown, TriggerColourCycle
            entityRecords.CapturedInitialPositions[79] = new Vector3(20.4f, 0f, -15.599998f);
            // Entity 80 with types AttachStation, Unknown, RubbishBin, Unknown, Unknown
            entityRecords.CapturedInitialPositions[80] = new Vector3(25.2f, 0f, -15.599998f);
            // Entity 81 with types Unknown
            entityRecords.CapturedInitialPositions[81] = new Vector3(8.713142f, 0.9729098f, -14.399891f);
            // Entity 82 with types Unknown
            entityRecords.CapturedInitialPositions[82] = new Vector3(32.08686f, 0.9729098f, -14.400101f);
            // Entity 83 with types Unknown, SessionInteractable, Unknown, TriggerDisable, Unknown
            entityRecords.CapturedInitialPositions[83] = new Vector3(8.4f, 0f, -12f);
            // Entity 84 with types Unknown, PilotRotation, Unknown, Unknown, Cannon, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[84] = new Vector3(8.4f, 0f, -14.399998f);
            // Entity 85 with types Unknown, PilotRotation, Unknown, Unknown, Cannon, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[85] = new Vector3(32.4f, 0f, -14.399998f);
            // Entity 86 with types Unknown, SessionInteractable, Unknown, TriggerDisable, Unknown
            entityRecords.CapturedInitialPositions[86] = new Vector3(32.4f, 0f, -12f);
            // Entity 87 with types Unknown
            entityRecords.CapturedInitialPositions[87] = new Vector3(18.01f, 0.90999997f, -10.829998f);
            // Entity 88 with types Unknown
            entityRecords.CapturedInitialPositions[88] = new Vector3(22.950008f, 0.56496465f, -21.61f);
            // Entity 89 with types Unknown
            entityRecords.CapturedInitialPositions[89] = new Vector3(16.81f, 0.90999997f, -10.829998f);
            // Entity 90 with types Unknown
            entityRecords.CapturedInitialPositions[90] = new Vector3(24.150005f, 0.56496465f, -21.61f);
            // Entity 91 with types Teleportal, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[91] = new Vector3(28.8f, 2.5f, -10.799999f);
            // Entity 92 with types Teleportal, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[92] = new Vector3(31.455f, 0f, -18f);
            // Entity 93 with types Teleportal, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[93] = new Vector3(11.999999f, 2.5f, -10.799999f);
            // Entity 94 with types Teleportal, Unknown, Unknown, Unknown, Unknown, Unknown, Unknown
            entityRecords.CapturedInitialPositions[94] = new Vector3(9.283f, 0f, -18f);
            // Entity 95 with types RespawnCollider
            entityRecords.CapturedInitialPositions[95] = new Vector3(5.76f, 1.7f, -16.960003f);
            // Entity 96 with types RespawnCollider
            entityRecords.CapturedInitialPositions[96] = new Vector3(35.36f, 1.6999999f, -17.960003f);
            // Entity 97 with types RespawnCollider
            entityRecords.CapturedInitialPositions[97] = new Vector3(21.19f, 1.6999999f, -6.330002f);
            // Entity 98 with types RespawnCollider
            entityRecords.CapturedInitialPositions[98] = new Vector3(19.960001f, 1.6999999f, -25.800003f);
            // Entity 99 with types RespawnCollider
            entityRecords.CapturedInitialPositions[99] = new Vector3(1.5900002f, 1.7f, -16.560001f);
            // Entity 100 with types RespawnCollider
            entityRecords.CapturedInitialPositions[100] = new Vector3(39.24f, 1.6999999f, -16.210003f);
            // Entity 101 with types RespawnCollider
            entityRecords.CapturedInitialPositions[101] = new Vector3(20.830002f, 1.6999999f, -5.4000015f);
            // Entity 102 with types RespawnCollider
            entityRecords.CapturedInitialPositions[102] = new Vector3(19.600002f, 1.6999999f, -27.36f);
            // Entity 103 with types Chef, Unknown, ChefCarry, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, InputEvent, Unknown, AttachCatcher, Unknown
            entityRecords.CapturedInitialPositions[103] = new Vector3(18.146301f, 0.049987197f, -17.044193f);
            // Entity 104 with types Chef, Unknown, ChefCarry, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, InputEvent, Unknown, AttachCatcher, Unknown
            entityRecords.CapturedInitialPositions[104] = new Vector3(28.93629f, 0.049987197f, -13.244194f);
            // Entity 105 with types Chef, Unknown, ChefCarry, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, InputEvent, Unknown, AttachCatcher, Unknown
            entityRecords.CapturedInitialPositions[105] = new Vector3(12.166292f, 0.049987197f, -13.2641945f);
            // Entity 106 with types Chef, Unknown, ChefCarry, Unknown, Unknown, Unknown, Unknown, RespawnBehaviour, Unknown, InputEvent, Unknown, AttachCatcher, Unknown
            entityRecords.CapturedInitialPositions[106] = new Vector3(22.926292f, 0.049987197f, -15.704193f);
            // Entity 107 with types FlowController, TutorialPopup
            entityRecords.CapturedInitialPositions[107] = new Vector3(14.4f, 0f, -39.6f);
            // Entity 108 with types Unknown
            entityRecords.CapturedInitialPositions[108] = new Vector3(14.4f, 0f, -39.6f);
            // Entity 109 with types RespawnCollider
            entityRecords.CapturedInitialPositions[109] = new Vector3(20.2f, -1f, -16.599998f);
            // Entity 110 with types PhysicsObject
            entityRecords.CapturedInitialPositions[110] = new Vector3(21.599998f, 0.58f, -16.8f);
            // Entity 111 with types PhysicsObject
            entityRecords.CapturedInitialPositions[111] = new Vector3(24f, 0.563f, -10.759998f);
            // Entity 112 with types PhysicsObject
            entityRecords.CapturedInitialPositions[112] = new Vector3(16.810005f, 0.51f, -21.630001f);
            // Entity 113 with types PhysicsObject
            entityRecords.CapturedInitialPositions[113] = new Vector3(24.150005f, 0.448f, -21.61f);
            // Entity 114 with types PhysicsObject
            entityRecords.CapturedInitialPositions[114] = new Vector3(16.81f, 0.51f, -10.829998f);
            // Entity 115 with types PhysicsObject
            entityRecords.CapturedInitialPositions[115] = new Vector3(21.599998f, 0.58f, -15.599998f);
            // Entity 116 with types PhysicsObject
            entityRecords.CapturedInitialPositions[116] = new Vector3(22.950008f, 0.448f, -21.61f);
            // Entity 117 with types PhysicsObject
            entityRecords.CapturedInitialPositions[117] = new Vector3(18.01f, 0.51f, -10.829998f);
            // Entity 118 with types PhysicsObject
            entityRecords.CapturedInitialPositions[118] = new Vector3(19.2f, 0.58f, -15.599998f);
            // Entity 119 with types PhysicsObject
            entityRecords.CapturedInitialPositions[119] = new Vector3(15.6f, 0.5f, -15.599998f);
            // Entity 120 with types PhysicsObject
            entityRecords.CapturedInitialPositions[120] = new Vector3(18.010006f, 0.51f, -21.630001f);
            // Entity 121 with types PhysicsObject
            entityRecords.CapturedInitialPositions[121] = new Vector3(19.2f, 0.58f, -16.8f);
            // Entity 122 with types PhysicsObject
            entityRecords.CapturedInitialPositions[122] = new Vector3(22.8f, 0.563f, -10.759998f);

            var chef_red_prefab = new PrefabRecord("Chef Red", "chef-red") { IsChef = true };
            var chef_green_prefab = new PrefabRecord("Chef Green", "chef-green") { IsChef = true };
            var chef_yellow_prefab = new PrefabRecord("Chef Yellow", "chef-yellow") { IsChef = true };
            var chef_blue_prefab = new PrefabRecord("Chef Blue", "chef-blue") { IsChef = true };
            RegisterChef(
                entityRecords.RegisterChef(
                    "red", 104, new PrefabRecord("Chef Red", "chef-red") { IsChef = true }));
            RegisterChef(
                entityRecords.RegisterChef(
                    "green", 105, new PrefabRecord("Chef Green", "chef-green") { IsChef = true }));
            RegisterChef(
                entityRecords.RegisterChef(
                    "yellow", 106, new PrefabRecord("Chef Yellow", "chef-yellow") { IsChef = true }));
            RegisterChef(
                entityRecords.RegisterChef(
                    "blue", 103, new PrefabRecord("Chef Blue", "chef-blue") { IsChef = true }));

            var COUNTER = new PrefabRecord("Counter", "counter") { IsAttachStation = true };
            for (var i = 32; i <= 64; i++) {
                if (i != 56 && i != 59) {
                    entityRecords.RegisterKnownObject(i, COUNTER);
                }
            }

            var BUN_CRATE = new PrefabRecord("Bun Crate", "bread-crate") { IsAttachStation = true, IsCrate = true };
            var BUN = new PrefabRecord("Bun", "bread") { IsChoppable = true, CanBeAttached = true, IsThrowable = true, MaxProgress = 1.4 };
            var CHOPPED_BUN = new PrefabRecord("Chopped Bun", "chopped-bread") { IsIngredient = true, IngredientId = 262914, CanBeAttached = true, IsThrowable = true };
            BUN_CRATE.Spawns.Add(BUN);
            BUN.Spawns.Add(CHOPPED_BUN);
            entityRecords.RegisterKnownObject(68, BUN_CRATE);
            
            var ONION_CRATE = new PrefabRecord("Onion Crate", "onion-crate") { IsAttachStation = true, IsCrate = true };
            var ONION = new PrefabRecord("Onion", "onion") { IsChoppable = true, CanBeAttached = true, IsThrowable = true, MaxProgress = 1.4 };
            var CHOPPED_ONION = new PrefabRecord("Chopped Onion", "chopped-onion") { IsIngredient = true, IngredientId = 461162, CanBeAttached = true, IsThrowable = true };
            ONION_CRATE.Spawns.Add(ONION);
            ONION.Spawns.Add(CHOPPED_ONION);
            entityRecords.RegisterKnownObject(69, ONION_CRATE);
            
            var SAUSAGE_CRATE = new PrefabRecord("Sausage Crate", "sausage-crate") { IsAttachStation = true, IsCrate = true };
            var SAUSAGE = new PrefabRecord("Sausage", "sausage") { IsIngredient = true, IngredientId = 284626, CanBeAttached = true, IsThrowable = true };
            SAUSAGE_CRATE.Spawns.Add(SAUSAGE);
            entityRecords.RegisterKnownObject(70, SAUSAGE_CRATE);
            
            var EGG_CRATE = new PrefabRecord("Egg Crate", "egg-crate") { IsAttachStation = true, IsCrate = true };
            var EGG = new PrefabRecord("Egg", "egg") { IsIngredient = true, IngredientId = 16620, CanBeAttached = true, IsThrowable = true };
            EGG_CRATE.Spawns.Add(EGG);
            entityRecords.RegisterKnownObject(65, EGG_CRATE);

            var FLOUR_CRATE = new PrefabRecord("Flour Crate", "flour-crate") { IsAttachStation = true, IsCrate = true };
            var FLOUR = new PrefabRecord("Flour", "flour") { IsIngredient = true, IngredientId = 18448, CanBeAttached = true, IsThrowable = true };
            FLOUR_CRATE.Spawns.Add(FLOUR);
            entityRecords.RegisterKnownObject(71, FLOUR_CRATE);

            var BERRY_CRATE = new PrefabRecord("Berry Crate", "berry-crate") { IsAttachStation = true, IsCrate = true };
            var BERRY = new PrefabRecord("Berry", "berry") { IsChoppable = true, CanBeAttached = true, IsThrowable = true, MaxProgress = 1.4 };
            var CHOPPED_BERRY = new PrefabRecord("Chopped Berry", "chopped-berry") { IsIngredient = true, IngredientId = 129618, CanBeAttached = true, IsThrowable = true };
            BERRY_CRATE.Spawns.Add(BERRY);
            BERRY.Spawns.Add(CHOPPED_BERRY);
            entityRecords.RegisterKnownObject(67, BERRY_CRATE);

            var CHOCOLATE_CRATE = new PrefabRecord("Chocolate Crate", "chocolate-crate") { IsAttachStation = true, IsCrate = true };
            var CHOCOLATE = new PrefabRecord("Chocolate", "chocolate") { IsChoppable = true, CanBeAttached = true, IsThrowable = true, MaxProgress = 1.4 };
            var CHOPPED_CHOCOLATE = new PrefabRecord("Chopped Chocolate", "chopped-chocolate") { IsIngredient = true, IngredientId = 22804, CanBeAttached = true, IsThrowable = true };
            CHOCOLATE_CRATE.Spawns.Add(CHOCOLATE);
            CHOCOLATE.Spawns.Add(CHOPPED_CHOCOLATE);
            entityRecords.RegisterKnownObject(66, CHOCOLATE_CRATE);

            var CANNON = new PrefabRecord("Cannon", "cannon") { IsCannon = true };
            var cannon1 = entityRecords.RegisterKnownObject(84, CANNON);
            var cannon2 = entityRecords.RegisterKnownObject(85, CANNON);

            cannon1.data.AppendWith(0, d => {
                d.pilotRotationAngle = 90;
                return d;
            });
            cannon2.data.AppendWith(0, d => {
                d.pilotRotationAngle = 270;
                return d;
            });

            var BUTTON = new PrefabRecord("Button", "button") { CanUse = true };
            entityRecords.RegisterKnownObject(77, BUTTON);
            entityRecords.RegisterKnownObject(78, BUTTON);
            entityRecords.RegisterKnownObject(79, BUTTON);

            var BOARD = new PrefabRecord("Board", "board") { CanUse = true, IsBoard = true, IsAttachStation = true };
            entityRecords.RegisterKnownObject(56, BOARD);
            entityRecords.RegisterKnownObject(23, BOARD);
            entityRecords.RegisterKnownObject(59, BOARD);
            entityRecords.RegisterKnownObject(24, BOARD);

            var PLATE = new PrefabRecord("Plate", "plate") { CanContainIngredients = true, CanBeAttached = true };
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(10, PLATE),
                entityRecords.GetRecordFromPath(new []{38}));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(11, PLATE),
                entityRecords.GetRecordFromPath(new []{40}));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(12, PLATE),
                entityRecords.GetRecordFromPath(new []{41}));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(13, PLATE),
                entityRecords.GetRecordFromPath(new []{43}));


            var MIXER = new PrefabRecord("Mixer", "mixer") { MaxProgress = 12, CanContainIngredients = true, IsMixer = true, IsCookingHandler = true, CanBeAttached = true };
            var MIXER_STATION = new PrefabRecord("Mixer Station", "mixer-station") { IsAttachStation = true }; 
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(3, MIXER),
                entityRecords.RegisterKnownObject(14, MIXER_STATION));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(6, MIXER),
                entityRecords.RegisterKnownObject(18, MIXER_STATION));

            var FRIER = new PrefabRecord("Frier", "frier") { MaxProgress = 10, CanContainIngredients = true, IsCookingHandler = true, CanBeAttached = true };
            var FRIER_STATION = new PrefabRecord("Frier Station", "frier-station") { IsAttachStation = true };
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(5, FRIER),
                entityRecords.RegisterKnownObject(15, FRIER_STATION));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(8, FRIER),
                entityRecords.RegisterKnownObject(20, FRIER_STATION));

            var POT = new PrefabRecord("Pot", "pot") { MaxProgress = 12, CanContainIngredients = true, IsCookingHandler = true, CanBeAttached = true };
            var HEAT_STATION = new PrefabRecord("Heat Station", "heat-station") { IsAttachStation = true };
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(7, POT),
                entityRecords.RegisterKnownObject(19, HEAT_STATION));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(2, POT),
                entityRecords.RegisterKnownObject(17, HEAT_STATION));

            var PAN = new PrefabRecord("Pan", "pan") { MaxProgress = 12, CanContainIngredients = true, IsCookingHandler = true, CanBeAttached = true };
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(9, PAN),
                entityRecords.RegisterKnownObject(21, HEAT_STATION));
            entityRecords.AttachInitialObjectTo(
                entityRecords.RegisterKnownObject(4, PAN),
                entityRecords.RegisterKnownObject(16, HEAT_STATION));
            
            var DIRTY_PLATE = new PrefabRecord("Dirty Plate", "dirty plate"){ CanBeAttached = true };
            var DIRTY_PLATE_SPAWNER = new PrefabRecord("Dirty Plate Spawner", "dirty-plate-spawner");
            DIRTY_PLATE_SPAWNER.Spawns.Add(new PrefabRecord("Dirty Plate Stack", "dirty-plate-stack"){ CanBeAttached = true });
            DIRTY_PLATE_SPAWNER.Spawns[0].Spawns.Add(DIRTY_PLATE);
            var CLEAN_PLATE_SPAWNER = new PrefabRecord("Clean Plate Spawner", "clean-plate-spawner");
            CLEAN_PLATE_SPAWNER.Spawns.Add(new PrefabRecord("Clean Plate Stack", "clean-plate-stack"){ CanBeAttached = true });
            CLEAN_PLATE_SPAWNER.Spawns[0].Spawns.Add(PLATE);
            var WASHING_PART = new PrefabRecord("Sink", "sink") { CanUse = true, MaxProgress = 3, IsWashingStation = true };
            entityRecords.RegisterKnownObject(75, WASHING_PART);
            entityRecords.RegisterKnownObject(76, CLEAN_PLATE_SPAWNER);
            entityRecords.RegisterKnownObject(74, DIRTY_PLATE_SPAWNER);

            var SERVE = new PrefabRecord("Serve", "serve") { OccupiedGridPoints = new Vector2[] { new Vector2(0, -0.6f), new Vector2(0, 0.6f) } };
            entityRecords.RegisterKnownObject(73, SERVE);

            var TERMINAL = new PrefabRecord("Terminal", "terminal") { CanUse = true, IsTerminal = true };
            entityRecords.RegisterKnownObject(83, TERMINAL);
            entityRecords.RegisterKnownObject(86, TERMINAL);

            var FIRE_EXTINGUISHER = new PrefabRecord("Fire Extinguisher", "fire-extinguisher") { CanBeAttached = true };
            entityRecords.RegisterKnownObject(1, FIRE_EXTINGUISHER);

            var IGNORE = new PrefabRecord("", "") { Ignore = true };
            for (var i = 95; i <= 102; i++) {
                entityRecords.RegisterKnownObject(i, IGNORE);
            }
            for (var i = 107; i <= 122; i++) {
                entityRecords.RegisterKnownObject(i, IGNORE);
            }

            entityRecords.RegisterOtherInitialObjects();

            entityRecords.CalculateSpawningPathsForPrefabs();
        }
    }
}