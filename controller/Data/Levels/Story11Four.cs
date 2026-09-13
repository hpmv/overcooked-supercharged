using System.Numerics;

namespace Hpmv
{
    // Native IDs/anchors: story11-discovery-b/load.json, step6. Polygons are
    // source-derived counter footprints with 0.4m chef clearance, not a native
    // collider reconstruction. Actual target/progress gates remain mandatory.
    public sealed class Story11FourLevel : GameSetup
    {
        public bool InspectionOnly { get; }
        public Story11FourLevel(bool inspectionOnly = false)
        {
            InspectionOnly = inspectionOnly;
            if (inspectionOnly) { geometry = new GameMapGeometry(Vector2.Zero, Vector2.Zero); return; }
            geometry = new GameMapGeometry(new Vector2(7.2f, 1.2f), new Vector2(14.4f, 8.4f));
            var map = new GameMap(new[] {
                new[] { new Vector2(8.2f,.2f), new Vector2(20.6f,.2f), new Vector2(20.6f,-6.2f), new Vector2(8.2f,-6.2f) },
                new[] { new Vector2(9.8f,-2.6f), new Vector2(13f,-2.6f), new Vector2(13f,-4.6f), new Vector2(9.8f,-4.6f) },
                new[] { new Vector2(15.8f,-2.6f), new Vector2(19f,-2.6f), new Vector2(19f,-4.6f), new Vector2(15.8f,-4.6f) }
            }, geometry);
            entityRecords.CapturedInitialPositions[1] = new Vector3(12f, 0.55000001192092896f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[2] = new Vector3(10.800000190734863f, 0.55000001192092896f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[3] = new Vector3(16.799999237060547f, 0.55000001192092896f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[4] = new Vector3(18f, 0.55000001192092896f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[5] = new Vector3(21.600000381469727f, 0f, 0f);
            entityRecords.CapturedInitialPositions[6] = new Vector3(8.3999996185302734f, 0f, 1.2000000476837158f);
            entityRecords.CapturedInitialPositions[7] = new Vector3(10.800000190734863f, 0f, 1.2000000476837158f);
            entityRecords.CapturedInitialPositions[8] = new Vector3(19.200000762939453f, 0f, 1.2000000476837158f);
            entityRecords.CapturedInitialPositions[9] = new Vector3(18f, 0f, 1.2000000476837158f);
            entityRecords.CapturedInitialPositions[10] = new Vector3(9.6000003814697266f, 0f, 1.2000000476837158f);
            entityRecords.CapturedInitialPositions[11] = new Vector3(20.400001525878906f, 0f, 1.2000000476837158f);
            entityRecords.CapturedInitialPositions[12] = new Vector3(21.600000381469727f, 0f, -6f);
            entityRecords.CapturedInitialPositions[13] = new Vector3(18f, 0f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[14] = new Vector3(7.1999998092651367f, 0f, -6f);
            entityRecords.CapturedInitialPositions[15] = new Vector3(10.800000190734863f, 0f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[16] = new Vector3(7.1999998092651367f, 0f, -2.4000000953674316f);
            entityRecords.CapturedInitialPositions[17] = new Vector3(7.1999998092651367f, 0f, 0f);
            entityRecords.CapturedInitialPositions[18] = new Vector3(21.600000381469727f, 0f, 1.2000000476837158f);
            entityRecords.CapturedInitialPositions[19] = new Vector3(12f, 0f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[20] = new Vector3(7.1999998092651367f, 0f, 1.2000000476837158f);
            entityRecords.CapturedInitialPositions[21] = new Vector3(16.799999237060547f, 0f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[22] = new Vector3(21.600000381469727f, 0f, -7.2000002861022949f);
            entityRecords.CapturedInitialPositions[23] = new Vector3(7.1999998092651367f, 0f, -7.2000002861022949f);
            entityRecords.CapturedInitialPositions[24] = new Vector3(7.1999998092651367f, 0f, -4.8000001907348633f);
            entityRecords.CapturedInitialPositions[25] = new Vector3(12f, 0f, 1.2000000476837158f);
            entityRecords.CapturedInitialPositions[26] = new Vector3(16.799999237060547f, 0f, 1.2000000476837158f);
            entityRecords.CapturedInitialPositions[27] = new Vector3(9.5999994277954102f, 0f, -7.1999998092651367f);
            entityRecords.CapturedInitialPositions[28] = new Vector3(19.200000762939453f, 0f, -7.2000002861022949f);
            entityRecords.CapturedInitialPositions[29] = new Vector3(21.600000381469727f, 0f, -4.8000001907348633f);
            entityRecords.CapturedInitialPositions[30] = new Vector3(7.1999998092651367f, 0f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[31] = new Vector3(8.3999996185302734f, 0f, -7.2000002861022949f);
            entityRecords.CapturedInitialPositions[32] = new Vector3(20.400001525878906f, 0f, -7.2000002861022949f);
            entityRecords.CapturedInitialPositions[33] = new Vector3(21.600000381469727f, 0f, -1.7999999523162842f);
            entityRecords.CapturedInitialPositions[34] = new Vector3(21.600000381469727f, 0f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[35] = new Vector3(7.1999998092651367f, 0f, -1.2000000476837158f);
            entityRecords.CapturedInitialPositions[36] = new Vector3(4.8499999046325684f, 1.8200000524520874f, -4.2699999809265137f);
            entityRecords.CapturedInitialPositions[37] = new Vector3(24.020000457763672f, 1.8200000524520874f, -4.2399997711181641f);
            entityRecords.CapturedInitialPositions[38] = new Vector3(15.069999694824219f, 1.8200000524520874f, 3.4800000190734863f);
            entityRecords.CapturedInitialPositions[39] = new Vector3(14.260000228881836f, 1.8200000524520874f, -10.350000381469727f);
            entityRecords.CapturedInitialPositions[40] = new Vector3(0f, 0f, 0f);
            entityRecords.CapturedInitialPositions[41] = new Vector3(0f, 0f, 0f);
            entityRecords.CapturedInitialPositions[42] = new Vector3(7f, -1f, 5f);
            entityRecords.CapturedInitialPositions[43] = new Vector3(17.826292037963867f, 0.00018894672393798828f, -5.8341941833496094f);
            entityRecords.CapturedInitialPositions[44] = new Vector3(11.426291465759277f, 0.00018894672393798828f, -1.5841941833496094f);
            entityRecords.CapturedInitialPositions[45] = new Vector3(11.366291999816895f, 0.00018894672393798828f, -5.7341938018798828f);
            entityRecords.CapturedInitialPositions[46] = new Vector3(17.786293029785156f, 0.00018894672393798828f, -1.6741938591003418f);
            entityRecords.CapturedInitialPositions[47] = new Vector3(10.800000190734863f, 0.55000001192092896f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[48] = new Vector3(16.799999237060547f, 0.55000001192092896f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[49] = new Vector3(12f, 0.55000001192092896f, -3.6000001430511475f);
            entityRecords.CapturedInitialPositions[50] = new Vector3(18f, 0.55000001192092896f, -3.6000001430511475f);
            for (int id = 43; id <= 46; id++) {
                RegisterChef(entityRecords.RegisterChef("chef-" + id, id, new PrefabRecord("Chef " + id, "chef") { IsChef = true }));
                mapByChef[id] = map;
            }
            var counter = new PrefabRecord("Counter", "counter") { IsAttachStation = true };
            var corner = new PrefabRecord("Corner", "corner");
            for (int id = 5; id <= 26; id++) entityRecords.RegisterKnownObject(id, id is 18 or 20 or 22 or 23 ? corner : counter);
            var board = new PrefabRecord("Board", "board") { CanUse = true, IsBoard = true, IsAttachStation = true };
            foreach (int id in new[] { 27, 28, 31, 32 }) entityRecords.RegisterKnownObject(id, board);
            // Native crate names identify these asset prefabs. Eight native
            // WorkableItem stages finish at progress7; 0.2s per increment is the original framework's
            // estimator, not native completion proof. Recipe UIDs come from
            // matching SushiFish/Prawn ingredient assets (23600/21875).
            void Crate(int id, string name, int ingredient) {
                var prepared = new PrefabRecord("Chopped" + name, "chopped-" + name) { IsIngredient = true, IngredientId = ingredient, CanBeAttached = true, IsThrowable = true };
                var raw = new PrefabRecord(name, name) { IsChoppable = true, CanBeAttached = true, IsThrowable = true, MaxProgress = 1.4 };
                raw.Spawns.Add(prepared);
                var crate = new PrefabRecord(name + " Crate", name + "-crate") { IsAttachStation = true, IsCrate = true };
                crate.Spawns.Add(raw); entityRecords.RegisterKnownObject(id, crate);
            }
            Crate(29, "SushiPrawn", 21875); Crate(30, "SushiFish", 23600);
            var plate = new PrefabRecord("Plate", "plate") { CanContainIngredients = true, CanBeAttached = true };
            for (int id = 1; id <= 4; id++) entityRecords.RegisterKnownObject(id, plate);
            // Initial attachments are reconstructed from native messages, not inferred from coordinates.
            entityRecords.RegisterKnownObject(33, new PrefabRecord("Serve", "serve") { IsAttachStation = true,
                OccupiedGridPoints = new[] { new Vector2(0,-.6f), new Vector2(0,.6f) } });
            var stack = new PrefabRecord("CleanPlateStack", "clean-plate-stack") { CanBeAttached = true, IsStack = true };
            stack.Spawns.Add(plate);
            var plateReturn = new PrefabRecord("Clean Plate Return", "clean-plate-return") { IsAttachStation = true, IsPlateReturnStation = true };
            plateReturn.Spawns.Add(stack); entityRecords.RegisterKnownObject(34, plateReturn);
            entityRecords.RegisterKnownObject(35, new PrefabRecord("Bin", "bin") { IsAttachStation = true });
            entityRecords.RegisterKnownObject(40, new PrefabRecord("Flow Controller", "flow-controller") { IsKitchenFlowController = true });
            var ignored = new PrefabRecord("Noninteractive fixed object", "ignored") { Ignore = true };
            foreach (int id in new[] { 36,37,38,39,41,42,47,48,49,50 }) entityRecords.RegisterKnownObject(id, ignored);
            entityRecords.CalculateSpawningPathsForPrefabs();
        }
    }
}
