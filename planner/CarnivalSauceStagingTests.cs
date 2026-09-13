using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Read-only replay of the new provenance observer over the already recorded serial source37 route.</summary>
    public static int SauceStagingRecordedGuardSelfTest(JsonObject admission1346, IEnumerable<JsonObject> observations)
    {
        var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Recorded sauce guard attempted game I/O."), null)
        { response = admission1346.DeepClone().AsObject(), options = new(NearSauceStaging: true, ServiceSideHead: true),
            recipes = [CarnivalRecipes.GetRecipe(158500), CarnivalRecipes.GetRecipe(125780)] };
        p.Refresh(); p.mealPlates[0] = 11; p.mealFoods[1] = 151;
        // Force the already recorded source fallback by protecting alternatives.
        // Native states remain unchanged; only diagnostic planner bookkeeping is reconstructed.
        p.reserved.UnionWith([32, 45, 165, 61, 153, 33, 40]);
        p.Start(3, "captured-transfer-mixed-dough", [p.A("place", 18)], [6, 5, 18]);
        if (!p.TryAssemble(0, exactIndex: 1) || p.sauceStaging.Values.Single().Counter != 37)
            throw new InvalidOperationException("Recorded sauce guard did not select its actual source fallback.");
        var selection = p.sauceStaging.Values.Single(); int count = 0, last = 1345;
        foreach (var observation in observations)
        {
            int frame = I(KitchenModel.SnapshotState(observation)["gameplayFrame"]);
            if (frame == last) continue;
            if (frame != last + 1 || frame > 2155) throw new InvalidOperationException("Recorded sauce guard input has a missing/out-of-range native frame.");
            p.response = observation; p.Refresh(); p.ObserveSauceStaging(); last = frame; count++;
        }
        if (count != 810 || last != 2155 || !selection.AssemblyObserved)
            throw new InvalidOperationException("Recorded sauce guard did not cover the full native assembly interval.");
        p.ReleaseRemainingWorkResources(selection.Work!); p.workers[0] = null;
        p.CompleteSauceStaging(selection, 125780);
        if (p.sauceStaging.Count != 0) throw new InvalidOperationException("Recorded native output did not retire its exact staging guard.");
        return count;
    }

    /// <summary>Exact V11 admission geometry plus explicitly synthetic ownership/food lifecycle negatives. Never calls the game.</summary>
    public static int SauceStagingSelfTest(JsonObject admission1346)
    {
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("Near sauce staging regression: " + message); count++; }
        void Reject(Action action, string message)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, message); }
        CarnivalPlanner Make(bool enabled = true)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline sauce fixture attempted I/O."), null)
            { response = admission1346.DeepClone().AsObject(), options = new(NearSauceStaging: enabled, ServiceSideHead: true),
                recipes = [CarnivalRecipes.GetRecipe(158500), CarnivalRecipes.GetRecipe(125780)] };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline sauce action attempted I/O."), null);
            // Reconstructed from the closed trace just before meal2 admission:
            // first plate11 staged, base151 at37; two raw-sausage leases protect
            // counters32/61 and pass45; chef3 owns ongoing bowl6 transfer.
            p.mealPlates[0] = 11; p.mealFoods[1] = 151;
            foreach (int id in new[] { 32, 45, 165, 61, 153 }) p.reserved.Add(id);
            p.Start(3, "captured-transfer-mixed-dough", [p.A("place", 18)], [6, 5, 18]);
            return p;
        }
        SauceStagingSelection? Select(CarnivalPlanner p) => p.SelectNearSauceStaging(0, 1, 37, 151, 13, 49, 296560, [13, 151, 37, 49, 72, 79]);
        SauceStagingSelection Admit(CarnivalPlanner p)
        { if (!p.TryAssemble(0, exactIndex: 1)) throw new InvalidOperationException("Captured serial meal was not admitted."); return p.sauceStaging.Values.Single(); }
        void Assembled(CarnivalPlanner p, SauceStagingSelection s)
        {
            var native = p.Entity(s.Food)!["composition"]!.DeepClone();
            p.Entity(s.Plate)!["composition"] = native.DeepClone(); p.Entity(s.Plate)!["contents"] = new JsonArray(native);
            p.entities.Remove(s.Food); p.Entity(s.Source)!["attachedEntityId"] = 0;
            foreach (var e in p.entities.Values.Where(e => I(e["attachedEntityId"]) == s.Plate)) e["attachedEntityId"] = 0;
            p.chefs[0]["heldEntityId"] = s.Plate; p.ObserveSauceStaging();
        }
        void AddSauce(CarnivalPlanner p, SauceStagingSelection s, NativeIngredient ingredient)
        {
            var children = p.Entity(s.Plate)!["composition"]!["children"]!.AsArray();
            children.Add(new JsonObject { ["type"] = "IngredientAssembledNode", ["id"] = ingredient.Id, ["name"] = ingredient.Name, ["children"] = new JsonArray() });
            p.Entity(s.Plate)!["contents"] = new JsonArray(p.Entity(s.Plate)!["composition"]!.DeepClone());
        }

        var original = Make(); var candidate = Select(original)!;
        Check(original.Frame == 1346 && original.Attached(37) == 151 && original.Attached(33) == 0 && original.Attached(40) == 0,
            "actual GF1346 preserves original prepared source and both nearby empty counters");
        Check(candidate.Counter == 40, "captured four-leg loop chooses counter40 over33/source37; observed " + candidate.Counter + ": " + candidate.Evidence);
        Check(original.workers[0] is null && original.sauceStaging.Count == 0 && original.Free(40, 33, 37), "selection alone acquires no worker or resources");
        Check(Select(original)!.Evidence.ToJsonString() == candidate.Evidence.ToJsonString(), "identical native state gives deterministic candidate order, paths and tie ranking");
        Check(BestSauceStagingCounter([(40, 12.1234561), (37, 12.1234562)]) == 37 &&
            BestSauceStagingCounter([(37, 12.1234562), (40, 12.1234561)]) == 37,
            "micrometre-scale tied path scores use native ID independent of enumeration order");
        Check(BestSauceStagingCounter([(37, 12.123460), (40, 12.123456)]) == 40, "meaningfully shorter path retains priority over lower native ID");
        var denied = candidate.Evidence["candidates"]!.AsArray();
        Check(denied.Any(c => I(c!["counter"]) == 32 && !B(c["eligible"])) && denied.Any(c => I(c!["counter"]) == 42 && !B(c["eligible"])),
            "physically empty leased32 and protected42 cannot become temporary storage");
        var reserved40 = Make(); reserved40.reserved.Add(40); Check(Select(reserved40)?.Counter == 33, "reserved best counter falls back to next reachable empty counter");
        var bothReserved = Make(); bothReserved.reserved.UnionWith([33, 40]); Check(Select(bothReserved)?.Counter == 37, "original exact source remains safe fallback when empty alternatives are unavailable");
        var occupied = Make(); occupied.Entity(40)!["attachedEntityId"] = 161; Check(Select(occupied)?.Counter == 33, "occupied nearest counter is excluded without moving unrelated food");
        var unknown = Make(); unknown.Entity(40)!.Remove("observedOrdinal"); Check(Select(unknown)?.Counter == 33, "missing counter identity falls back to a fully observed station");
        var wrongSource = Make(); wrongSource.Entity(37)!["attachedEntityId"] = 161; Check(Select(wrongSource) is null, "changed source cannot be silently adopted");
        var heldSource = Make(); heldSource.chefs[2]["heldEntityId"] = 151; Check(Select(heldSource) is null, "a source held by another chef is not a native assembly station");
        var blocked = Make(); blocked.model.Obstacles[0] = new("fixture-full-static-obstruction", 99999, new(15, 26, -23, -9), "Synthetic static obstruction");
        Check(Select(blocked) is null && blocked.workers[0] is null, "no legal approach refuses admission without a worker or lease");
        var sourceLeased = Make(); sourceLeased.reserved.Add(37); Check(Select(sourceLeased) is null, "an externally owned source is not stolen even when another temp is free");

        var baseline = Make(false); Check(baseline.TryAssemble(0, exactIndex: 1) && baseline.sauceStaging.Count == 0, "default-off serial path retains no new observer metadata");
        Check(baseline.workers[0]!.Actions.Where(a => a["type"]?.ToString() == "place").First()["station"]!.ToString() == "37", "default-off temporary source remains unchanged");
        var p = Make(); var s = Admit(p); var work = p.workers[0]!;
        Check(s.Counter == 40 && work.OwnedResources.SetEquals(new[] { 13, 151, 37, 49, 72, 79, 40 }), "new temp is owned alongside original source, plate, food, output and both sauce stations");
        Check(work.Actions.Where(a => a["type"]?.ToString() == "switch-condiment").Select(a => I(a["index"])).SequenceEqual(new[] { 0, 1 }) &&
            work.Actions.Where(a => a["type"]?.ToString() == "apply").Select(a => I(a["expectedIngredientId"])).SequenceEqual(new[] { 17094, 158482 }),
            "native mustard-then-ketchup order and ingredient verification are unchanged");
        Check(work.Actions.Where(a => a["type"]?.ToString() == "place").First()["station"]!.ToString() == "40" &&
            work.Actions.Where(a => a["type"]?.ToString() == "take").Last()["station"]!.ToString() == "40" &&
            work.Actions.Single(a => a["type"]?.ToString() == "assemble")["station"]!.ToString() == "37", "only temporary parking/recovery moves; native source assembly stays37");
        p.ObserveSauceStaging(); Check(p.workers[0] == work && !p.Start(3, "steal-temp", [p.A("place", 40)], [40]), "same-frame native observer keeps original work and prevents temp theft");
        Assembled(p, s); Check(s.AssemblyObserved && p.Held(0) == 13, "exact native plate tree establishes source consumption without requiring destroyed raw entity");
        AddSauce(p, s, CarnivalRecipes.Mustard); p.Entity(40)!["attachedEntityId"] = 13; p.chefs[0]["heldEntityId"] = 0;
        p.ObserveSauceStaging(); Check(p.Attached(40) == 13 && work.OwnedResources.Contains(37), "parked first-sauce plate and emptied original source both remain protected");
        p.cannonInterruptions[0] = new CannonInterruption(0, work, 84, 78, p.Frame); p.workers[0] = new Work("synthetic-fire-child", [], [84, 78], null);
        p.ObserveSauceStaging(); Check(p.sauceStaging.ContainsKey(work), "original resource owner stays valid during bounded cannon interruption");
        p.workers[0] = work; p.cannonInterruptions.Clear();
        p.Entity(40)!["attachedEntityId"] = 0; p.chefs[0]["heldEntityId"] = 13; AddSauce(p, s, CarnivalRecipes.Ketchup); p.ObserveSauceStaging();
        Check(CarnivalRecipes.MatchRecipe(p.Entity(13), 125780).ReadyToDeliver, "same original native plate has exact completed dual-sauce recipe");
        p.Entity(49)!["attachedEntityId"] = 13; p.chefs[0]["heldEntityId"] = 0;
        var last = work.Actions.Last().DeepClone().AsObject(); last["player"] = 0; work.Actions.Clear(); work.Active = p.runner.CreateAction(last); work.Active.IsDone = true;
        p.ObserveSauceStaging(); p.CompleteWork();
        Check(p.sauceStaging.Count == 0 && p.workers[0] is null && p.mealPlates[1] == 13 && p.Free(40, 37, 13, 151, 49, 72, 79), "native completed output releases only original job resources and selection");
        Check(new[] { 32, 45, 165, 61, 153 }.All(p.reserved.Contains) && p.workers[3] is not null, "completion preserves unrelated raw buffers and other chef work");
        Check(p.Start(0, "successor-temp-owner", [p.A("place", 40)], [40]), "a later job can acquire the released temporary counter");
        p.ObserveSauceStaging(); Check(p.reserved.Contains(40), "retired observer cannot remove a successor's resource lease");

        foreach (string defect in new[] { "counter-ordinal", "counter-position", "source-ordinal", "plate-ordinal", "dispenser-ordinal", "button-ordinal", "missing-reservation", "missing-work-ownership", "foreign-temp-item", "unobserved-food-consumption", "reused-food-ordinal", "replaced-work" })
        {
            var bad = Make(); var lease = Admit(bad);
            switch (defect)
            {
                case "counter-ordinal": bad.Entity(40)!["observedOrdinal"] = 99999; break;
                case "counter-position": bad.Entity(40)!["position"]!["x"] = 22; break;
                case "source-ordinal": bad.Entity(37)!["observedOrdinal"] = 99999; break;
                case "plate-ordinal": bad.Entity(13)!["observedOrdinal"] = 99999; break;
                case "dispenser-ordinal": bad.Entity(72)!["observedOrdinal"] = 99999; break;
                case "button-ordinal": bad.Entity(79)!["observedOrdinal"] = 99999; break;
                case "missing-reservation": bad.reserved.Remove(40); break;
                case "missing-work-ownership": bad.workers[0]!.OwnedResources.Remove(40); break;
                case "foreign-temp-item": bad.Entity(40)!["attachedEntityId"] = 161; break;
                case "unobserved-food-consumption": bad.entities.Remove(151); bad.Entity(37)!["attachedEntityId"] = 0; break;
                case "reused-food-ordinal": bad.Entity(151)!["observedOrdinal"] = 99999; break;
                case "replaced-work": bad.workers[0] = new Work("replacement", [], [], null); break;
            }
            Reject(bad.ObserveSauceStaging, "before further inputs observer rejects " + defect);
        }
        return count;
    }
}
