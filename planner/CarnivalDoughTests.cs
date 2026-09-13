using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Recipe-admission regression using the actual rejected two-ingredient v5 bowl.</summary>
    public static int DoughSelfTest(JsonObject failedSnapshot)
    {
        int count = 0;
        void Check(bool value, string detail) { if (!value) throw new InvalidOperationException("Dough regression: " + detail); count++; }
        var native = KitchenModel.SnapshotState(failedSnapshot);
        var bowl = native["entities"]!.AsArray().OfType<JsonObject>().Single(e => I(e["id"]) == 6);
        var partial = CarnivalRecipes.ClassifyEntity(bowl).Food;
        var chocolate = CarnivalRecipes.GetRecipe(228996);
        var raspberry = CarnivalRecipes.GetRecipe(130976);
        Check(partial.IngredientIds.Order().SequenceEqual(new[] { CarnivalRecipes.Flour.Id, CarnivalRecipes.Egg.Id }.Order()),
            "recorded rejected bowl contains exactly native flour and egg");
        Check(MixedReady(partial), "native mixing completion is observable even with the assigned flavor missing");
        Check(!AssignedDoughReady(partial, chocolate) && !AssignedDoughReady(partial, raspberry),
            "neither assigned donut accepts the native mixed partial dough");
        FoodObservation ChangeMixed(FoodObservation node, Func<FoodObservation, FoodObservation> change)
            => node.Kind == FoodNodeKind.Mixed ? change(node) : node with { Children = node.Children.Select(c => ChangeMixed(c, change)).ToArray() };
        FoodObservation Ingredient(NativeRecipe recipe, int id)
            => recipe.ExpectedFood.DescendantsAndSelf().Single(n => n.Kind == FoodNodeKind.Ingredient && n.IngredientId == id);
        var complete = ChangeMixed(partial, n => n with { Children = [.. n.Children, Ingredient(chocolate, CarnivalRecipes.Chocolate.Id)] });
        Check(AssignedDoughReady(complete, chocolate), "complete assigned ingredient multiset plus native Mixed state permits transfer");
        Check(!AssignedDoughReady(complete, raspberry), "a different flavor's complete dough cannot consume this assignment");
        var duplicate = ChangeMixed(complete, n => n with { Children = [.. n.Children, Ingredient(chocolate, CarnivalRecipes.Flour.Id)] });
        Check(!AssignedDoughReady(duplicate, chocolate), "duplicate ingredients fail even when every required ingredient is present");
        var unmixed = ChangeMixed(complete, n => n with { Preparation = FoodPreparation.Mixing });
        Check(!AssignedDoughReady(unmixed, chocolate), "three ingredients alone do not replace native mixing readiness");
        var overmixed = ChangeMixed(complete, n => n with { Preparation = FoodPreparation.Overmixed });
        Check(!AssignedDoughReady(overmixed, chocolate), "overmixed dough stays ineligible");
        Check(!AssignedDoughReady(complete with { Preparation = FoodPreparation.Burnt }, chocolate), "ruined outer composition cannot be hidden by a Mixed descendant");
        Check(!AssignedDoughReady(complete, CarnivalRecipes.GetRecipe(296560)), "donut transfer requires a donut assignment");
        return count;
    }
}
