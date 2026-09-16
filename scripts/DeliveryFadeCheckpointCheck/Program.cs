using SuperchargedPatch.Authoring.Modules;

int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}

Check(DeliveryFadeReincarnationContract.IsExactStory11PlateFactory(
    new[]{34,0,0},new[]{2},2),"exact Story 1-1 plate factory admitted");
Check(!DeliveryFadeReincarnationContract.IsExactStory11PlateFactory(
    new[]{34,0},new[]{2},2),"short factory chain rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11PlateFactory(
    new[]{34,0,1},new[]{2},2),"different prefab rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11PlateFactory(
    new[]{34,0,0},new[]{3},2),"different historical entity rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11PlateFactory(
    new[]{34,0,0},new[]{2,47},2),"non-root logical path rejected");

Check(DeliveryFadeReincarnationContract.IsDisjointFutureDeletionSet(
    Array.Empty<int>(),new[]{1,2,3,4}),"empty future deletion set admitted");
Check(DeliveryFadeReincarnationContract.IsDisjointFutureDeletionSet(
    new[]{55,57},new[]{1,2,3,4}),"distinct future returned-plate owners admitted");
Check(!DeliveryFadeReincarnationContract.IsDisjointFutureDeletionSet(
    new[]{55,55},new[]{1,2,3,4}),"duplicate future deletion rejected");
Check(!DeliveryFadeReincarnationContract.IsDisjointFutureDeletionSet(
    new[]{0,55},new[]{1,2,3,4}),"nonpositive future deletion rejected");
Check(!DeliveryFadeReincarnationContract.IsDisjointFutureDeletionSet(
    new[]{2,55},new[]{1,2,3,4}),"target historical plate deletion rejected");
Check(!DeliveryFadeReincarnationContract.IsDisjointFutureDeletionSet(
    null,new[]{1,2,3,4}),"null future deletion set rejected");

var historical=new[]{"Transform","PhysicalAttachment","ClientPlate","ClientIngredientContentGUI"};
var recreated=new[]{"Transform","PhysicalAttachment","ClientPlate","ClientIngredientContentGUI",
    DeliveryFadeReincarnationContract.PathMarkerType};
Check(DeliveryFadeReincarnationContract.IsExactRecreatedComponentTopology(historical,recreated),
    "one framework path marker is the only admitted new component");
Check(DeliveryFadeReincarnationContract.IsExactRecreatedComponentTopology(
    historical.Concat(new[]{DeliveryFadeReincarnationContract.PathMarkerType}).ToArray(),recreated),
    "a recaptured marker-bearing target remains reusable");
Check(!DeliveryFadeReincarnationContract.IsExactRecreatedComponentTopology(
    historical.Concat(new[]{DeliveryFadeReincarnationContract.PathMarkerType,
        DeliveryFadeReincarnationContract.PathMarkerType}).ToArray(),recreated),
    "duplicate historical factory receipt marker rejected");
Check(!DeliveryFadeReincarnationContract.IsExactRecreatedComponentTopology(historical,historical),
    "missing factory receipt marker rejected");
Check(!DeliveryFadeReincarnationContract.IsExactRecreatedComponentTopology(historical,
    recreated.Concat(new[]{DeliveryFadeReincarnationContract.PathMarkerType}).ToArray()),
    "duplicate factory receipt marker rejected");
Check(!DeliveryFadeReincarnationContract.IsExactRecreatedComponentTopology(historical,
    new[]{"Transform","ClientPlate","PhysicalAttachment","ClientIngredientContentGUI",
        DeliveryFadeReincarnationContract.PathMarkerType}),"behavioral component reordering rejected");
Check(!DeliveryFadeReincarnationContract.IsExactRecreatedComponentTopology(historical,
    new[]{"Transform","PhysicalAttachment","ClientPlate","Unexpected",
        "ClientIngredientContentGUI",DeliveryFadeReincarnationContract.PathMarkerType}),
    "extra gameplay component rejected");

var rendererMap=DeliveryFadeReincarnationContract.ExactUniqueKeyMap(
    new[]{"food@0","plate@0"},new[]{"plate@0","food@0"});
Check(rendererMap!=null&&rendererMap.SequenceEqual(new[]{1,0}),
    "renderer enumeration order is irrelevant after exact hierarchy-key proof");
Check(DeliveryFadeReincarnationContract.ExactUniqueKeyMap(
    new[]{"food@0","plate@0"},new[]{"food@0","food@0"})==null,
    "duplicate recreated renderer key rejected");
Check(DeliveryFadeReincarnationContract.ExactUniqueKeyMap(
    new[]{"food@0","food@0"},new[]{"food@0","plate@0"})==null,
    "duplicate target renderer key rejected");
Check(DeliveryFadeReincarnationContract.ExactUniqueKeyMap(
    new[]{"food@0","plate@0"},new[]{"food@0","other@0"})==null,
    "missing and extra renderer key rejected");

Check(DeliveryFadeReincarnationContract.IsTerminalFadeProgress(1f),"exact terminal progress admitted");
Check(DeliveryFadeReincarnationContract.IsTerminalFadeProgress(1.00000036f),"observed float staircase overshoot admitted");
Check(!DeliveryFadeReincarnationContract.IsTerminalFadeProgress(.99999f),"live fade progress rejected");
Check(!DeliveryFadeReincarnationContract.IsTerminalFadeProgress(1.001f),"unbounded terminal overshoot rejected");
Check(!DeliveryFadeReincarnationContract.IsTerminalFadeProgress(float.NaN),"nonfinite terminal progress rejected");
Check(DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,2,false,false,true,false,.3f,true,true,0f,.5f,new[]{false,false},new[]{.733333349f,.733333349f}),
    "exact observed Story 1-1 entity2 PC2 fade admitted");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,1,false,false,false,true,0f,true,true,0f,.5f,new[]{false,false},new[]{1f,1f}),
    "delivery wait phase rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    1,2,false,false,true,false,.3f,true,true,0f,.5f,new[]{false,false},new[]{.733333349f,.733333349f}),
    "different active plate lineage rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,2,false,false,true,false,.30000004f,true,true,0f,.5f,new[]{false,false},new[]{.733333349f,.733333349f}),
    "different fade progress rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,2,false,false,true,false,.3f,true,true,.1f,.5f,new[]{false,false},new[]{.733333349f,.733333349f}),
    "positive PFX delay rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,2,false,false,true,false,.3f,true,true,0f,.5f,new[]{false,true},new[]{.733333349f,.733333349f}),
    "enabled fade collider rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,2,false,false,true,false,.3f,true,true,0f,.5f,new[]{false,false},new[]{.7f,.733333349f}),
    "different fade material alpha rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,2,true,false,true,false,.3f,true,true,0f,.5f,new[]{false,false},new[]{.733333349f,.733333349f}),
    "disposing iterator rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,2,false,true,true,false,.3f,true,true,0f,.5f,new[]{false,false},new[]{.733333349f,.733333349f}),
    "errored iterator rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,2,false,false,false,false,.3f,true,true,0f,.5f,new[]{false,false},new[]{.733333349f,.733333349f}),
    "non-null current yield rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,2,false,false,true,false,.3f,true,false,0f,.5f,new[]{false,false},new[]{.733333349f,.733333349f}),
    "attached PFX rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,2,false,false,true,false,.3f,true,true,0f,.4f,new[]{false,false},new[]{.733333349f,.733333349f}),
    "different fade duration rejected");
Check(!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
    2,2,false,false,true,false,.3f,true,true,0f,.5f,new[]{false},new[]{.733333349f}),
    "different collider and material cardinality rejected");
var relayInner=new CountingIterator();
var relay=new DeliveryFadeResumeRelay(relayInner);
Check(relay.MoveNext()&&relay.Primed&&relay.Current==null&&relayInner.MoveNextCalls==0,
    "resume relay primes with one null yield without advancing restored iterator");
Check(relay.MoveNext()&&(int)relay.Current==7&&relayInner.MoveNextCalls==1,
    "resume relay delegates exactly once after its scheduling yield");
Check(DeliveryFadeReincarnationContract.IsExactObserverEntry(1,true,120,120),
    "single exact delivery observer admitted before cancellation");
Check(!DeliveryFadeReincarnationContract.IsExactObserverEntry(2,true,120,120),
    "extra delivery observer rejected before cancellation");
Check(!DeliveryFadeReincarnationContract.IsExactObserverEntry(1,true,121,120),
    "different delivery observer incarnation rejected before cancellation");
Check(DeliveryFadeReincarnationContract.IsNonEmptyDistinctSubset(new[]{31,32},new[]{30,31,32}),
    "fresh fade material ids admitted as an owned subset");
Check(!DeliveryFadeReincarnationContract.IsNonEmptyDistinctSubset(new[]{31,33},new[]{30,31,32}),
    "unowned fresh fade material id rejected");
Check(!DeliveryFadeReincarnationContract.IsNonEmptyDistinctSubset(new[]{31,31},new[]{30,31,32}),
    "duplicate fresh fade material identity rejected");
Check(DeliveryFadeReincarnationContract.IsRebindableDeliveryPhase(
    0,true,false,0f,false,false,false,false,false),"virgin iterator history phase rebindable");
Check(DeliveryFadeReincarnationContract.IsRebindableDeliveryPhase(
    1,false,true,0f,true,false,false,false,false),"wait iterator history phase rebindable");
Check(DeliveryFadeReincarnationContract.IsRebindableDeliveryPhase(
    2,true,false,.3f,true,true,true,true,true),"active PC2 iterator history phase rebindable");
Check(!DeliveryFadeReincarnationContract.IsRebindableDeliveryPhase(
    2,true,false,.3f,true,true,true,true,false),"active iterator without retained PFX identity rejected");
Check(!DeliveryFadeReincarnationContract.IsRebindableDeliveryPhase(
    -1,true,false,1f,true,true,false,false,true),"terminal iterator history phase rejected from active rebind");
Check(DeliveryFadeReincarnationContract.IsBoundedPresentationScale(
    1.00000012f,1f,1.00000012f,1f,1f,1f),"observed one-ULP presentation scale residual admitted");
Check(!DeliveryFadeReincarnationContract.IsBoundedPresentationScale(
    1.00001f,1f,1f,1f,1f,1f),"larger presentation scale residual rejected");
Check(!DeliveryFadeReincarnationContract.IsBoundedPresentationScale(
    float.NaN,1f,1f,1f,1f,1f),"nonfinite presentation scale rejected");

var targetRendererKeys=new[]{
    "AttachPoint#0/CompositeSushi(Clone)#0/IngredientContainer#0/m_recipe_sushi_02#0@0",
    "Plate#1@0"};
var targetRendererPresentation=new[]{true,false};
var currentRendererKeys=new[]{"Plate#1@0"};
var currentRendererPresentation=new[]{false};
Check(DeliveryFadeReincarnationContract.IsExactVirginStory11SushiPresentation(
    targetRendererKeys,targetRendererPresentation,currentRendererKeys,currentRendererPresentation,
    "AttachPoint#0/CompositeSushi(Clone)#0",1,1,true,true,true,
    "Composite(C=[Ingredient(23600)],O=[])","Composite(C=[Ingredient(23600)],O=[])"),
    "exact virgin Story 1-1 sushi presentation lifecycle admitted");
Check(!DeliveryFadeReincarnationContract.IsExactVirginStory11SushiPresentation(
    targetRendererKeys,targetRendererPresentation,new[]{"Plate#1@0","other@0"},new[]{false,false},
    "AttachPoint#0/CompositeSushi(Clone)#0",1,1,true,true,true,
    "Composite(C=[Ingredient(23600)],O=[])","Composite(C=[Ingredient(23600)],O=[])"),
    "extra pre-Start renderer rejected");
Check(!DeliveryFadeReincarnationContract.IsExactVirginStory11SushiPresentation(
    targetRendererKeys,targetRendererPresentation,currentRendererKeys,currentRendererPresentation,
    "AttachPoint#0/CompositeSushi(Clone)#0",1,1,true,false,true,
    "Composite(C=[Ingredient(23600)],O=[])","Composite(C=[Ingredient(23600)],O=[])"),
    "partially initialized meal lifecycle rejected");
Check(!DeliveryFadeReincarnationContract.IsExactVirginStory11SushiPresentation(
    targetRendererKeys,targetRendererPresentation,currentRendererKeys,currentRendererPresentation,
    "AttachPoint#0/CompositeSushi(Clone)#0",1,1,true,true,true,
    "Composite(C=[Ingredient(23600)],O=[])","Ingredient(23600)"),
    "different assigned composition rejected");
Check(!DeliveryFadeReincarnationContract.IsExactVirginStory11SushiPresentation(
    targetRendererKeys,targetRendererPresentation,currentRendererKeys,currentRendererPresentation,
    "AttachPoint#0/CompositeSushi(Clone)#0",2,1,true,true,true,
    "Composite(C=[Ingredient(23600)],O=[])","Composite(C=[Ingredient(23600)],O=[])"),
    "duplicate sushi lifecycle component rejected");

var retainedNullLike=new NullLike();
Check(retainedNullLike==null,"fixture overloads equality to mimic a destroyed Unity wrapper");
Check(DeliveryFadeReincarnationContract.HasManagedReference(retainedNullLike),
    "CLR wrapper existence is independent of overloaded null equality");
Check(!DeliveryFadeReincarnationContract.HasManagedReference(null),"true CLR null remains absent");

Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{
    passed=true,checks,gameCalls=0,
    scope="Pure destroyed-delivery factory/component/key/terminal and exact Story 1-1 entity2 PC2 admission contract; Unity object, coroutine, PFX and rendering proofs compile in the hot module and require native receipts."
}));

sealed class NullLike
{
    public static bool operator ==(NullLike left,NullLike right)=>true;
    public static bool operator !=(NullLike left,NullLike right)=>false;
    public override bool Equals(object value)=>ReferenceEquals(this,value);
    public override int GetHashCode()=>base.GetHashCode();
}

sealed class CountingIterator : System.Collections.IEnumerator
{
    public int MoveNextCalls { get; private set; }
    public object Current { get; private set; }
    public bool MoveNext()
    {
        MoveNextCalls++;
        if(MoveNextCalls!=1){Current=null;return false;}
        Current=7;return true;
    }
    public void Reset()=>throw new NotSupportedException();
}
