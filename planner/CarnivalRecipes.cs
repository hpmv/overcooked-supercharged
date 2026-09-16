using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public enum FoodPreparation { Unknown, Empty, Raw, Chopped, Mixing, Mixed, Cooking, Cooked, Burnt, Overmixed, Assembled }
public enum FoodNodeKind { Unknown, Ingredient, Composite, Mixed, Cooked }
public sealed record NativeIngredient(int Id, string Name, int ChopImpacts = 0)
{
    public double ChopSeconds => ChopImpacts * CarnivalRecipes.ChopImpactSeconds;
}
public sealed record NativePrepStep(string Operation, string Output, string[] Inputs, double Seconds, string Equipment);
public sealed record NativeRecipe(int Id, string Name, int BaseScore, NativeIngredient[] RequiredInputs,
    NativePrepStep[] Preparation, FoodObservation ExpectedFood);
public sealed record FoodObservation(FoodNodeKind Kind, FoodPreparation Preparation, int IngredientId,
    string Name, int CookingStepId, double Progress, FoodObservation[] Children)
{
    public IEnumerable<FoodObservation> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var node in child.DescendantsAndSelf()) yield return node;
    }
    public bool IsRuined => DescendantsAndSelf().Any(n => n.Preparation is FoodPreparation.Burnt or FoodPreparation.Overmixed);
    public int[] IngredientIds => DescendantsAndSelf().Where(n => n.Kind == FoodNodeKind.Ingredient).Select(n => n.IngredientId).ToArray();
}
public sealed record FoodEntityObservation(int EntityId, bool IsPlate, FoodObservation Food, string[] EvidenceGaps);
public sealed record MissingIngredient(NativeIngredient Ingredient, int Count);
public sealed record RecipeAssessment(NativeRecipe Recipe, bool ObservableMatch, bool CompleteEvidence,
    bool ReadyToDeliver, bool IsRuined, MissingIngredient[] MissingIngredients, int[] UnexpectedIngredientIds,
    string[] PreparationIssues, string[] EvidenceGaps);
public sealed record ThroughputEstimate(int RequiredDeliveries, double PredictedFreshScore, double AverageBaseScore,
    double RequiredDeliveriesPerSecond, double SecondsPerDelivery, double InitialTipDeficit,
    double WashingChefSeconds, double ChoppingChefSeconds, double PotSeconds, double PanSeconds,
    double MixerSeconds, double FryerSeconds, double ProductionLowerBoundSeconds, string Assumptions);

/// <summary>
/// Planning and conservative validation of observable native food state. This never changes the game.
/// Constants were read from the installed s_day_3_4 scene and its native ingredient/recipe bundles.
/// A predicted score is not a demonstrated run; transport, scheduling, orders and timing still need execution.
/// </summary>
public static class CarnivalRecipes
{
    public const double RoundSeconds = 270, OrderLifetimeSeconds = 136, ChopImpactSeconds = .2;
    public const double MixSeconds = 12, BoilSeconds = 12, PanSeconds = 12, DeepFrySeconds = 10;
    public const double PlateReturnSeconds = 7, WashSeconds = 3;
    public const int InitialPlates = 4, MaximumMultiplier = 4, StartupTipDeficit = 72;
    public const int PotCookingStepId = 20068, PanCookingStepId = 20294, DeepFryerCookingStepId = 17160;
    public const int MustardSwitchIndex = 0, KetchupSwitchIndex = 1;

    public static readonly NativeIngredient Bun = new(262914, "HotdogBun", 7);
    public static readonly NativeIngredient Frankfurter = new(284626, "Frankfurter");
    public static readonly NativeIngredient Onion = new(461162, "DLC08_Onion", 7);
    public static readonly NativeIngredient Flour = new(18448, "Flour");
    public static readonly NativeIngredient Egg = new(16620, "Egg");
    public static readonly NativeIngredient Chocolate = new(22804, "Chocolate", 7);
    public static readonly NativeIngredient Raspberry = new(129618, "Raspberry", 7);
    public static readonly NativeIngredient Ketchup = new(158482, "Ketchup");
    public static readonly NativeIngredient Mustard = new(17094, "Mustard");

    public static IReadOnlyDictionary<int, NativeIngredient> Ingredients { get; } =
        new ReadOnlyDictionary<int, NativeIngredient>(new[] { Bun, Frankfurter, Onion, Flour, Egg, Chocolate, Raspberry, Ketchup, Mustard }.ToDictionary(i => i.Id));

    public static IReadOnlyList<NativeRecipe> All { get; } = Array.AsReadOnly(new[]
    {
        Donut(130976, "Donut_Raspberry", Raspberry), Donut(228996, "Donut_Chocolate", Chocolate),
        Hotdog(224216, "Hotdog_Ketchup", 60, Ketchup), Hotdog(158500, "Hotdog_Mustard", 60, Mustard),
        Hotdog(125780, "Hotdog_Ketchup_Mustard", 80, Ketchup, Mustard),
        Hotdog(472326, "Hotdog_Onions", 80, Onion), Hotdog(257844, "Hotdog_Onions_Ketchup", 100, Onion, Ketchup),
        Hotdog(47642, "Hotdog_Onions_Mustard", 100, Onion, Mustard), Hotdog(296560, "Hotdog_Plain", 40)
    });
    public static IReadOnlyDictionary<int, NativeRecipe> ById { get; } = new ReadOnlyDictionary<int, NativeRecipe>(All.ToDictionary(r => r.Id));
    public static NativeRecipe GetRecipe(int recipeId) => ById.TryGetValue(recipeId, out var recipe)
        ? recipe : throw new ArgumentOutOfRangeException(nameof(recipeId), recipeId, "Not a Carnival 3-4 recipe.");

    private static FoodObservation Leaf(NativeIngredient i) => new(FoodNodeKind.Ingredient,
        i.ChopImpacts > 0 ? FoodPreparation.Chopped : FoodPreparation.Raw, i.Id, i.Name, 0, -1, []);
    private static FoodObservation Composite(params FoodObservation[] children) => new(FoodNodeKind.Composite,
        children.Length == 0 ? FoodPreparation.Empty : FoodPreparation.Assembled, 0, "assembly", 0, -1, children);
    private static FoodObservation Cook(int step, params FoodObservation[] children) => new(FoodNodeKind.Cooked, FoodPreparation.Cooked, 0, "cooked", step, 1, children);
    private static FoodObservation Mix(params FoodObservation[] children) => new(FoodNodeKind.Mixed, FoodPreparation.Mixed, 0, "mixed", 0, 1, children);

    private static NativeRecipe Donut(int id, string name, NativeIngredient flavor) => new(id, name, 100,
        [Flour, Egg, flavor],
        [new("chop", flavor.Name, [flavor.Name], flavor.ChopSeconds, "chopping board"),
         new("mix", "dough", [Flour.Name, Egg.Name, flavor.Name], MixSeconds, "mixer bowl (capacity 3)"),
         new("deep-fry", name, ["dough"], DeepFrySeconds, "fryer basket (capacity 1)"),
         new("plate", name, [name, "clean plate"], 0, "plate")],
        Cook(DeepFryerCookingStepId, Mix(Leaf(Flour), Leaf(Egg), Leaf(flavor))));

    private static NativeRecipe Hotdog(int id, string name, int score, params NativeIngredient[] extras)
    {
        var steps = new List<NativePrepStep>
        {
            new("chop", Bun.Name, [Bun.Name], Bun.ChopSeconds, "chopping board"),
            new("boil", Frankfurter.Name, [Frankfurter.Name], BoilSeconds, "pot (capacity 1)")
        };
        if (extras.Contains(Onion))
        {
            steps.Add(new("chop", Onion.Name, [Onion.Name], Onion.ChopSeconds, "chopping board"));
            steps.Add(new("pan-fry", Onion.Name, [Onion.Name], PanSeconds, "frying pan (capacity 1)"));
        }
        foreach (var sauce in extras.Where(i => i == Ketchup || i == Mustard))
            steps.Add(new("condiment", sauce.Name, ["held compatible plate", sauce.Name], 0, "condiment dispenser"));
        steps.Add(new("plate", name, [Bun.Name, Frankfurter.Name, .. extras.Select(i => i.Name), "clean plate"], 0, "plate"));
        return new(id, name, score, [Bun, Frankfurter, .. extras], steps.ToArray(),
            Composite([Leaf(Bun), Cook(PotCookingStepId, Leaf(Frankfurter)),
                .. extras.Select(i => i == Onion ? Cook(PanCookingStepId, Leaf(i)) : Leaf(i))]));
    }

    /// <summary>Parse a FoodState tree. Native state labels take precedence: order templates have progress=0 even when Cooked/Mixed.</summary>
    public static FoodObservation ClassifyFood(JsonNode? food) => ReadFood(food, 0);

    private static FoodObservation ReadFood(JsonNode? food, int depth)
    {
        if (food is not JsonObject obj || depth > 32) return Unknown("missing or excessive food tree");
        string type = S(obj["type"]), state = S(obj["state"]);
        var children = (obj["children"] as JsonArray)?.Select(n => ReadFood(n, depth + 1)).ToArray() ?? [];
        int id = I(obj["id"]);
        if (type.Contains("IngredientAssembledNode", StringComparison.Ordinal))
        {
            if (!Ingredients.TryGetValue(id, out var ingredient)) return new(FoodNodeKind.Ingredient, FoodPreparation.Unknown, id, S(obj["name"]), 0, -1, children);
            // These leaves exist only after native WorkableItem replacement; raw buns/flavors/onions have no ingredient node.
            return Leaf(ingredient);
        }
        double progress = N(obj["progress"], -1);
        if (type.Contains("CookedComposite", StringComparison.Ordinal))
            return new(FoodNodeKind.Cooked, CookState(state, progress), 0, "cooked", Math.Max(0, I(obj["cookingStepId"] ?? obj["cookingTypeId"])), progress, children);
        if (type.Contains("MixedComposite", StringComparison.Ordinal))
            return new(FoodNodeKind.Mixed, MixState(state, progress), 0, "mixed", 0, progress, children);
        if (type == "CompositeAssembledNode") return Composite(children);
        return Unknown(string.IsNullOrEmpty(type) ? "untyped food" : type) with { Children = children };
    }

    /// <summary>
    /// Container contents telemetry consists of ingredients, so reconstruct its mixing/cooking wrapper using live handler progress.
    /// This also recognizes raw WorkableItem entities, whose contents are empty before chopping.
    /// </summary>
    public static FoodEntityObservation ClassifyEntity(JsonNode? entity)
    {
        if (entity is not JsonObject obj) return new(0, false, Unknown("missing entity"), ["Entity is absent."]);
        var components = (obj["components"] as JsonArray)?.Select(S).ToHashSet(StringComparer.Ordinal) ?? [];
        bool plate = components.Contains("Plate") || components.Contains("ServerPlate");
        var children = (obj["contents"] as JsonArray)?.Select(ClassifyFood).ToArray() ?? [];
        var gaps = new List<string>();
        FoodObservation root;
        if(obj["composition"] is JsonObject composition)
        {
            root=Normalize(ClassifyFood(composition));
        }
        else if (children.Length == 0 && (components.Contains("WorkableItem") || components.Contains("ServerWorkableItem")))
        {
            var ingredient = IngredientFromEntityName(S(obj["name"]));
            root = ingredient is null ? Unknown("unrecognized raw item") : Leaf(ingredient) with
            {
                Preparation = N(obj["workProgress"], -1) >= 1 ? FoodPreparation.Chopped : FoodPreparation.Raw,
                Progress = N(obj["workProgress"], -1)
            };
        }
        else if (children.Length == 0) root = Composite();
        else
        {
            root = Normalize(Composite(children));
            bool mixable = components.Contains("MixableContainer") || components.Contains("ServerMixableContainer");
            bool cookable = components.Contains("CookableContainer") || components.Contains("ServerCookableContainer");
            if (mixable)
            {
                double progress = Ratio(obj, "mixingProgress", "mixingTime");
                root = new(FoodNodeKind.Mixed, MixState("", progress), 0, "mixed", 0, progress, children);
            }
            if (cookable && (!mixable || N(obj["cookingProgress"], -1) > 0))
            {
                double progress = Ratio(obj, "cookingProgress", "cookingTime");
                int step = Math.Max(0, I(obj["cookingTypeId"] ?? obj["cookingStepId"]));
                // Names are not used as proof of a cooking method; require telemetry from CookingHandler.AccessCookingType.
                root = new(FoodNodeKind.Cooked, CookState("", progress), 0, "cooked", step, progress, mixable ? [root] : children);
            }
        }
        if (root.DescendantsAndSelf().Any(n => n.Kind == FoodNodeKind.Cooked && n.CookingStepId == 0))
            gaps.Add("Cooking step IDs are absent; boiling, pan frying and deep frying cannot be fully distinguished.");
        if (root.DescendantsAndSelf().Any(n => n.Kind == FoodNodeKind.Unknown || n.Preparation == FoodPreparation.Unknown))
            gaps.Add("Some food nodes or preparation states are unknown.");
        return new(I(obj["id"]), plate, root, gaps.ToArray());
    }

    public static FoodEntityObservation ClassifyHeld(JsonObject snapshot, int playerId)
    {
        JsonObject state = snapshot["state"] as JsonObject ?? snapshot;
        var chef = (state["chefs"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(c => I(c["playerId"]) == playerId);
        int held = I(chef?["heldEntityId"]);
        var entity = held == 0 ? null : (state["entities"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(e => I(e["id"]) == held);
        return ClassifyEntity(entity);
    }

    public static RecipeAssessment MatchRecipe(JsonNode? entity, int recipeId, bool requirePlate = true) =>
        Assess(ClassifyEntity(entity), GetRecipe(recipeId), requirePlate);

    public static RecipeAssessment MatchHeld(JsonObject snapshot, int playerId, int recipeId) =>
        Assess(ClassifyHeld(snapshot, playerId), GetRecipe(recipeId), true);

    /// <summary>Match a known native order's recipeId. Unknown IDs throw instead of guessing from ingredient names.</summary>
    public static RecipeAssessment MatchOrder(JsonNode? entity, JsonNode? order, bool requirePlate = true) =>
        MatchRecipe(entity, I(order?["recipeId"]), requirePlate);

    public static MissingIngredient[] MissingComponents(JsonNode? entity, int recipeId) => MatchRecipe(entity, recipeId, false).MissingIngredients;

    public static RecipeAssessment[] MatchingRecipes(JsonNode? entity, bool requirePlate = true) =>
        All.Select(r => MatchRecipe(entity, r.Id, requirePlate)).Where(r => r.ObservableMatch).ToArray();

    private static RecipeAssessment Assess(FoodEntityObservation observed, NativeRecipe recipe, bool requirePlate)
    {
        var actual = observed.Food.IngredientIds.GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
        var required = recipe.RequiredInputs.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.Count());
        var missing = required.Where(p => p.Value > actual.GetValueOrDefault(p.Key))
            .Select(p => new MissingIngredient(Ingredients[p.Key], p.Value - actual.GetValueOrDefault(p.Key))).ToArray();
        var extra = actual.SelectMany(p => Enumerable.Repeat(p.Key, Math.Max(0, p.Value - required.GetValueOrDefault(p.Key)))).ToArray();
        var issues = new List<string>();
        if (observed.Food.IsRuined) issues.Add("Food is burnt or overmixed.");
        bool shape = TreeMatches(observed.Food, recipe.ExpectedFood, false);
        if (!shape && !observed.Food.IsRuined) issues.Add("Preparation or grouping does not match: " + Describe(recipe.ExpectedFood) + ".");
        if (requirePlate && !observed.IsPlate) issues.Add("A clean serving plate is required.");
        bool observable = shape && missing.Length == 0 && extra.Length == 0 && !observed.Food.IsRuined;
        bool completeEvidence = observable && TreeMatches(observed.Food, recipe.ExpectedFood, true) && observed.EvidenceGaps.Length == 0;
        return new(recipe, observable, completeEvidence, completeEvidence && (!requirePlate || observed.IsPlate),
            observed.Food.IsRuined, missing, extra, issues.ToArray(), observed.EvidenceGaps);
    }

    private static FoodObservation Normalize(FoodObservation n)
    {
        var children = n.Children.Select(Normalize).ToArray();
        if (n.Kind == FoodNodeKind.Composite)
        {
            children = children.SelectMany(c => c.Kind == FoodNodeKind.Composite ? c.Children : [c]).ToArray();
            if (children.Length == 1) return children[0];
        }
        return n with { Children = children };
    }

    private static bool TreeMatches(FoodObservation actual, FoodObservation expected, bool strict)
    {
        actual = Normalize(actual); expected = Normalize(expected);
        if (actual.Kind != expected.Kind || actual.Preparation != expected.Preparation) return false;
        if (expected.Kind == FoodNodeKind.Ingredient && actual.IngredientId != expected.IngredientId) return false;
        if (expected.Kind == FoodNodeKind.Cooked && actual.CookingStepId != expected.CookingStepId && (strict || actual.CookingStepId != 0)) return false;
        if (actual.Children.Length != expected.Children.Length) return false;
        // Preserve multiplicity and group boundaries; equal ingredient sets alone do not establish a native recipe match.
        var remaining = actual.Children.ToList();
        foreach (var child in expected.Children)
        {
            int index = remaining.FindIndex(n => TreeMatches(n, child, strict));
            if (index < 0) return false;
            remaining.RemoveAt(index);
        }
        return true;
    }

    public static string Describe(FoodObservation food)
    {
        if (food.Kind == FoodNodeKind.Ingredient) return food.Preparation.ToString().ToLowerInvariant() + " " + food.Name;
        string name = food.Kind == FoodNodeKind.Cooked ? food.CookingStepId switch
        { PotCookingStepId => "boiled", PanCookingStepId => "pan-fried", DeepFryerCookingStepId => "deep-fried", _ => "cooked" }
            : food.Kind == FoodNodeKind.Mixed ? "mixed" : "assembled";
        return name + "(" + string.Join(", ", food.Children.Select(Describe)) + ")";
    }

    public static int TipForRemainingFraction(double fraction)
    {
        if (!double.IsFinite(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction));
        return fraction > .66 ? 8 : fraction > .33 ? 5 : fraction > 0 ? 3 : 0;
    }

    public static int[] FreshTips(int count, int currentMultiplier = 0)
    {
        if (count < 0 || currentMultiplier is < 0 or > MaximumMultiplier) throw new ArgumentOutOfRangeException(nameof(count));
        int multiplier = currentMultiplier;
        return Enumerable.Range(0, count).Select(_ => { int tip = 8 * Math.Max(1, multiplier); multiplier = Math.Min(4, multiplier + 1); return tip; }).ToArray();
    }

    public static int PredictFreshOrderedScore(IEnumerable<int> recipeIds, int currentScore = 0, int currentMultiplier = 0)
    {
        var recipes = recipeIds.Select(GetRecipe).ToArray();
        return checked(currentScore + recipes.Sum(r => r.BaseScore) + FreshTips(recipes.Length, currentMultiplier).Sum());
    }

    /// <summary>Balanced menu planning estimate. Assumes FIFO fresh deliveries; does not predict the random order stream or certify feasibility.</summary>
    public static ThroughputEstimate EstimateTarget(int targetScore = 5000, double averageBaseScore = 80,
        double remainingSeconds = RoundSeconds, int currentScore = 0, int currentMultiplier = 0, int cleanPlates = InitialPlates)
    {
        if (!double.IsFinite(averageBaseScore) || averageBaseScore <= 0 || !double.IsFinite(remainingSeconds) || remainingSeconds <= 0 ||
            currentMultiplier is < 0 or > MaximumMultiplier || cleanPlates < 0) throw new ArgumentOutOfRangeException(nameof(averageBaseScore));
        int meals = 0, multiplier = currentMultiplier; double predicted = currentScore, deficit = 0;
        while (predicted < targetScore && multiplier < MaximumMultiplier)
        {
            int tip = 8 * Math.Max(1, multiplier); deficit += 32 - tip;
            predicted += averageBaseScore + tip; meals++;
            multiplier = Math.Min(4, multiplier + 1);
        }
        if (predicted < targetScore)
        {
            int additional = checked((int)Math.Ceiling((targetScore - predicted) / (averageBaseScore + 32)));
            meals = checked(meals + additional);
            predicted += additional * (averageBaseScore + 32);
        }
        double wash = Math.Max(0, meals - cleanPlates) * WashSeconds;
        double chop = meals * (4d / 3) * 7 * ChopImpactSeconds;
        double pot = meals * (7d / 9) * BoilSeconds, pan = meals * (3d / 9) * PanSeconds;
        double mix = meals * (2d / 9) * MixSeconds, fry = meals * (2d / 9) * DeepFrySeconds;
        double lowerBound = new[] { pot / 2, pan / 2, mix / 2, fry / 2, (wash + chop) / 4 }.Max();
        return new(meals, predicted, averageBaseScore, meals / remainingSeconds, meals == 0 ? 0 : remainingSeconds / meals,
            deficit, wash, chop, pot, pan, mix, fry, lowerBound,
            "Planning only: balanced nine-recipe ingredient demand, two vessels of each kind, four chefs, every delivery fresh and in order, no failures. " +
            "Lower bound omits all movement, ingredient loading, vessel transfer, plating, sauces, cannon/portal travel, plate-return latency and frame overhead. " +
            "The native order stream and a completed run must establish the achieved score.");
    }

    private static NativeIngredient? IngredientFromEntityName(string name)
    {
        string clean = name.Replace("(Clone)", "", StringComparison.Ordinal).Trim();
        return Ingredients.Values.FirstOrDefault(i => clean.Equals(i.Name, StringComparison.OrdinalIgnoreCase));
    }
    private static FoodObservation Unknown(string name) => new(FoodNodeKind.Unknown, FoodPreparation.Unknown, 0, name, 0, -1, []);
    private static FoodPreparation CookState(string state, double progress) => state.ToLowerInvariant() switch
    {
        "burnt" or "burned" or "ruined" => FoodPreparation.Burnt,
        "cooked" => FoodPreparation.Cooked,
        "raw" => progress > 0 ? FoodPreparation.Cooking : FoodPreparation.Raw,
        _ => progress > 2 ? FoodPreparation.Burnt : progress >= 1 ? FoodPreparation.Cooked : progress > 0 ? FoodPreparation.Cooking : progress == 0 ? FoodPreparation.Raw : FoodPreparation.Unknown
    };
    private static FoodPreparation MixState(string state, double progress) => state.ToLowerInvariant() switch
    {
        "overmixed" or "ruined" => FoodPreparation.Overmixed,
        "mixed" => FoodPreparation.Mixed,
        "unmixed" => progress > 0 ? FoodPreparation.Mixing : FoodPreparation.Raw,
        _ => progress > 2 ? FoodPreparation.Overmixed : progress >= 1 ? FoodPreparation.Mixed : progress > 0 ? FoodPreparation.Mixing : progress == 0 ? FoodPreparation.Raw : FoodPreparation.Unknown
    };
    private static double Ratio(JsonObject obj, string progress, string duration)
    {
        double p = N(obj[progress], -1), d = N(obj[duration], -1);
        return p >= 0 && d > 0 ? p / d : -1;
    }
    private static string S(JsonNode? value) => value?.ToString() ?? "";
    private static int I(JsonNode? value) => int.TryParse(S(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
    private static double N(JsonNode? value, double fallback) => double.TryParse(S(value), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : fallback;

    /// <summary>Independent assertions for the controller's selftest command; returns failures and performs no I/O.</summary>
    public static string[] SelfTest()
    {
        var failures = new List<string>();
        void Check(bool pass, string name) { if (!pass) failures.Add(name); }
        JsonObject Plate(string contents) => JsonNode.Parse("{\"id\":900,\"components\":[\"Plate\"],\"contents\":" + contents + "}")!.AsObject();
        const string bun = "{\"type\":\"IngredientAssembledNode\",\"id\":262914}";
        const string boiled = "{\"type\":\"CookedCompositeAssembledNode\",\"state\":\"Cooked\",\"progress\":0,\"cookingStepId\":20068,\"children\":[{\"type\":\"IngredientAssembledNode\",\"id\":284626}]}";
        var plain = Plate("[" + boiled + "," + bun + "]");
        Check(MatchRecipe(plain, 296560).ReadyToDeliver, "Chopped bun + boiled sausage matches regardless of child ordering; template progress zero does not override Cooked.");
        var wrongMethod = Plate("[" + boiled.Replace("20068", "20294", StringComparison.Ordinal) + "," + bun + "]");
        Check(!MatchRecipe(wrongMethod, 296560).ObservableMatch, "Fried sausage must not match boiled sausage.");
        var oldTelemetry = Plate("[" + boiled.Replace("\"cookingStepId\":20068,", "", StringComparison.Ordinal) + "," + bun + "]");
        Check(MatchRecipe(oldTelemetry, 296560).ObservableMatch && !MatchRecipe(oldTelemetry, 296560).ReadyToDeliver, "Missing cooking step is an observable match, not complete evidence.");
        var burnt = Plate("[" + boiled.Replace("\"Cooked\"", "\"Burnt\"", StringComparison.Ordinal) + "," + bun + "]");
        Check(MatchRecipe(burnt, 296560).IsRuined && !MatchRecipe(burnt, 296560).ObservableMatch, "Burnt filling must be rejected.");
        Check(!MatchRecipe(Plate("[" + boiled + "," + bun + "," + bun + "]"), 296560).ObservableMatch, "Duplicate ingredients must not match.");
        Check(MissingComponents(plain, 125780).Select(x => x.Ingredient.Id).Order().SequenceEqual(new[] { Mustard.Id, Ketchup.Id }.Order()), "Both sauce requirements remain missing from plain hotdog.");
        var raw = JsonNode.Parse("{\"name\":\"HotdogBun(Clone)\",\"components\":[\"WorkableItem\"],\"workProgress\":0,\"contents\":[]}");
        Check(ClassifyEntity(raw).Food.Preparation == FoodPreparation.Raw, "Unchopped bun with no food leaf is raw.");
        var pot = JsonNode.Parse("{\"name\":\"DLC08_utensil_pot_01\",\"components\":[\"CookableContainer\"],\"cookingProgress\":6,\"cookingTime\":12,\"cookingTypeId\":20068,\"contents\":[{\"type\":\"IngredientAssembledNode\",\"id\":284626}]}")!.AsObject();
        Check(ClassifyEntity(pot).Food.Preparation == FoodPreparation.Cooking, "Underlying sausage leaf does not hide a half-cooked pot state.");
        pot["cookingProgress"] = 24;
        Check(ClassifyEntity(pot).Food.Preparation == FoodPreparation.Cooked, "Native burn boundary is strictly greater than twice cooking time.");
        pot["cookingProgress"] = 24.01;
        Check(ClassifyEntity(pot).Food.IsRuined, "Cooking beyond twice cooking time burns the food.");
        const string donut = "[{\"type\":\"CookedCompositeAssembledNode\",\"state\":\"Cooked\",\"cookingStepId\":17160,\"children\":[{\"type\":\"MixedCompositeAssembledNode\",\"state\":\"Mixed\",\"children\":[{\"type\":\"IngredientAssembledNode\",\"id\":22804},{\"type\":\"IngredientAssembledNode\",\"id\":16620},{\"type\":\"IngredientAssembledNode\",\"id\":18448}]}]}]";
        Check(MatchRecipe(Plate(donut), 228996).ReadyToDeliver, "Deep-fried mixed chocolate dough matches its native nested recipe.");
        Check(!MatchRecipe(Plate(donut.Replace("\"Mixed\"", "\"Unmixed\"", StringComparison.Ordinal)), 228996).ObservableMatch, "Unmixed ingredients cannot pass as a cooked donut.");
        Check(!MatchRecipe(Plate(donut.Replace("MixedCompositeAssembledNode", "CompositeAssembledNode", StringComparison.Ordinal)), 228996).ObservableMatch, "Matching ingredient IDs with no mixing group must not pass.");
        Check(FreshTips(5).SequenceEqual(new[] { 8, 8, 16, 24, 32 }), "Fresh tip ramp uses the multiplier before increment.");
        Check(TipForRemainingFraction(.66) == 5 && TipForRemainingFraction(.33) == 3 && TipForRemainingFraction(0) == 0, "Native tip boundaries are strict.");
        var estimate = EstimateTarget();
        Check(estimate.RequiredDeliveries == 46 && estimate.PredictedFreshScore == 5080 && estimate.InitialTipDeficit == StartupTipDeficit, "5000 balanced planning estimate is 46 deliveries, 5080 predicted points, 72 startup deficit.");
        Check(All.Count == 9 && All.Average(r => r.BaseScore) == 80, "Nine native recipes average 80 base points.");
        return failures.ToArray();
    }
}
