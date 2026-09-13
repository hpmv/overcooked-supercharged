using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OvercookedTAS.Controller;

public readonly record struct Point2(double X, double Z)
{
    public double Distance(Point2 other) => Math.Sqrt((X-other.X)*(X-other.X)+(Z-other.Z)*(Z-other.Z));
}
public readonly record struct Rect2(double MinX, double MaxX, double MinZ, double MaxZ)
{
    public bool Contains(Point2 p, double inset=0) => p.X>=MinX+inset && p.X<=MaxX-inset && p.Z>=MinZ+inset && p.Z<=MaxZ-inset;
    public Rect2 Expand(double amount) => new(MinX-amount,MaxX+amount,MinZ-amount,MaxZ+amount);
}
public sealed record KitchenRegion(string Id, Rect2 FloorBounds, string Evidence);
public readonly record struct OrientedBox2(Point2 Center,double HalfX,double HalfZ,double CosYaw,double SinYaw)
{
    public Point2 ToLocal(Point2 point)
    {
        double x=point.X-Center.X,z=point.Z-Center.Z;
        return new(CosYaw*x-SinYaw*z,SinYaw*x+CosYaw*z);
    }
    public Rect2 LocalBounds=>new(-HalfX,HalfX,-HalfZ,HalfZ);
}
public sealed record KitchenObstacle(string Key, int EntityId, Rect2 Bounds, string Evidence, bool Dynamic=false,OrientedBox2? Oriented=null,double? CircleRadius=null);
public sealed record StationApproach(string Region, Point2 Position, Point2 FaceToward);
public sealed record KitchenStation(string Key, int EntityId, string Name, string Role, string? Ingredient, Point2 Position, string[] Regions, string[] Components, StationApproach[] Approaches);
public sealed record KitchenTransition(string Kind, string FromRegion, string? ToRegion, string SourceKey, string? DestinationKey, bool Verified, string Evidence,Point2? Departure=null,Point2? Arrival=null,double? Angle=null);

public sealed class KitchenModel
{
    public string Scene {get; init;}="";
    public double TilePitch {get; init;}
    public double ChefRadius {get; init;}
    public double Clearance {get; init;}=.03;
    public KitchenRegion[] Regions {get; init;}=[];
    public KitchenObstacle[] Obstacles {get; init;}=[];
    public KitchenStation[] Stations {get; private set;}=[];
    public KitchenTransition[] Transitions {get; private set;}=[];
    public string[] Warnings {get; init;}=[];
    public string? RegionAt(Point2 p) => Regions.FirstOrDefault(r=>r.FloorBounds.Contains(p))?.Id;

    public static JsonObject SnapshotState(JsonObject snapshot) => snapshot["state"] as JsonObject ?? snapshot;
    public static Point2 Position(JsonNode? value) => new(N(value?["x"]),N(value?["z"]));
    internal static double N(JsonNode? value)
    {
        if(value is null)return 0;
        if(value is JsonValue scalar&&scalar.TryGetValue<double>(out var number))return number;
        return double.Parse(value.ToString(),CultureInfo.InvariantCulture);
    }
    internal static string[] Components(JsonObject entity) => (entity["components"] as JsonArray)?.Select(c=>c?.ToString()??"").ToArray()??[];

    public static KitchenModel Build(JsonObject snapshot)
    {
        var state=SnapshotState(snapshot);
        if(state["scene"]?.ToString()!="s_Day_3_4")throw new ArgumentException("This measured topology resolver requires Carnival scene s_Day_3_4.");
        var entities=(state["entities"] as JsonArray)?.OfType<JsonObject>().Where(e=>e["active"]?.GetValue<bool>()!=false).ToArray()??throw new ArgumentException("Snapshot needs entity telemetry.");
        var corners=entities.Where(e=>e["name"]?.ToString().Contains("countertop_01_standard_circus_corner",StringComparison.Ordinal)==true).ToArray();
        var cornerX=corners.Select(e=>Math.Round(Position(e["position"]).X,2)).Distinct().Order().ToArray();
        var cornerZ=corners.Select(e=>Math.Round(Position(e["position"]).Z,2)).Distinct().Order().ToArray();
        if(cornerX.Length!=4||cornerZ.Length!=2)throw new InvalidDataException("Expected four measured corner columns and two rows; topology changed or snapshot incomplete.");
        var counters=entities.Where(e=>e["name"]?.ToString().Contains("countertop_01_",StringComparison.Ordinal)==true).ToArray();
        var allX=counters.Select(e=>Math.Round(Position(e["position"]).X,2)).Distinct().Order().ToArray();
        var tile=allX.Zip(allX.Skip(1),(a,b)=>Math.Round(b-a,2)).Where(d=>d>.2&&d<2).GroupBy(x=>x).OrderByDescending(g=>g.Count()).ThenBy(g=>g.Key).FirstOrDefault()?.Key??throw new InvalidDataException("Cannot measure countertop grid pitch.");
        var cannons=entities.Where(e=>Components(e).Contains("Cannon")).OrderBy(e=>Position(e["position"]).X).ToArray();
        var groundPortals=entities.Where(e=>Components(e).Contains("TeleportalPlayerSender")&&N(e["position"]?["y"])<1).OrderBy(e=>Position(e["position"]).X).ToArray();
        if(cannons.Length!=2||groundPortals.Length!=2)throw new InvalidDataException("Expected two cannons and two floor portal senders.");
        double cannonZ=Math.Round(cannons.Average(e=>Position(e["position"]).Z),2),portalZ=Math.Round(groundPortals.Average(e=>Position(e["position"]).Z),2);
        if(!(cornerZ[0]<portalZ&&portalZ<cannonZ&&cannonZ<cornerZ[1]))throw new InvalidDataException("Measured island boundaries are inconsistent.");
        var chefEntities=entities.Where(e=>Components(e).Contains("PlayerIDProvider")).ToArray();
        var inactiveChefs=(state["chefs"] as JsonArray)?.OfType<JsonObject>().Where(c=>c["controlsEnabled"]?.GetValue<bool>()==false||c["respawning"]?.GetValue<bool>()==true).Select(c=>c["entityId"]?.GetValue<int>()??0).ToHashSet()??[];
        var heldIds=(state["chefs"] as JsonArray)?.OfType<JsonObject>().Select(c=>c["heldEntityId"]?.GetValue<int>()??0).Where(id=>id!=0).ToHashSet()??[];
        var heldRoots=entities.Where(e=>heldIds.Contains(Id(e))).Select(e=>e["hierarchyPath"]?.ToString()).Where(p=>!string.IsNullOrEmpty(p)).Select(p=>p!).ToArray();
        var chefRoots=chefEntities.Select(e=>e["hierarchyPath"]?.ToString()).Where(p=>!string.IsNullOrEmpty(p)).Select(p=>p!).ToArray();
        bool CarriedPath(string? path)=>path is not null&&heldRoots.Concat(chefRoots).Any(root=>path==root||path.StartsWith(root+"/",StringComparison.Ordinal));
        var radius=chefEntities.Where(e=>!inactiveChefs.Contains(Id(e))).SelectMany(e=>((e["colliders"] as JsonArray)?.OfType<JsonObject>()??[]).Select(c=>(Chef:e,Collider:c)))
            .Where(pair=>pair.Collider["type"]?.ToString()=="CapsuleCollider"&&pair.Collider["active"]?.GetValue<bool>()!=false&&pair.Collider["enabled"]?.GetValue<bool>()!=false&&pair.Collider["layer"]?.GetValue<int>()!=15&&(pair.Collider["hierarchyPath"] is null||pair.Collider["hierarchyPath"]?.ToString()==pair.Chef["hierarchyPath"]?.ToString()))
            .Select(pair=>N(pair.Collider["size"]?["x"])*.5).DefaultIfEmpty(.4).Max();
        var regions=new[]{
            new KitchenRegion("upper-left",new(cornerX[0],cornerX[1],cannonZ-tile/2,cornerZ[1]),"Counter corner columns/top row and left cannon tile; open floor extends half the measured grid pitch beyond its last tile center."),
            new KitchenRegion("lower-left",new(cornerX[0],cornerX[1],cornerZ[0],portalZ+tile/2),"Counter corner columns/bottom row and portal tile; open floor extends half the measured grid pitch beyond its last tile center."),
            new KitchenRegion("center",new(cornerX[1],cornerX[2],cornerZ[0],cornerZ[1]),"Inner corner columns and complete top/bottom station rows."),
            new KitchenRegion("upper-right",new(cornerX[2],cornerX[3],cannonZ-tile/2,cornerZ[1]),"Counter corner columns/top row and right cannon tile; open floor extends half the measured grid pitch beyond its last tile center."),
            new KitchenRegion("lower-right",new(cornerX[2],cornerX[3],cornerZ[0],portalZ+tile/2),"Counter corner columns/bottom row and portal tile; open floor extends half the measured grid pitch beyond its last tile center.")
        };
        var obstacles=new List<KitchenObstacle>();
        var stationData=new List<(JsonObject Entity,string Role,string Key)>();
        var seenCollider=new HashSet<string>(StringComparer.Ordinal);
        var seenColliderPath=new HashSet<string>(StringComparer.Ordinal);
        var entityTransforms=entities.Where(e=>e["hierarchyPath"] is not null).GroupBy(e=>e["hierarchyPath"]!.ToString(),StringComparer.Ordinal)
            .Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.Single(),StringComparer.Ordinal);
        foreach(var entity in entities)
        {
            var role=Role(entity);
            if(role is null)continue;
            var p=Position(entity["position"]);
            var key=StableKey(entity,role);
            stationData.Add((entity,role,key));
            bool structural=role is "counter" or "chop" or "mix-station" or "cook-station" or "fryer-station" or "ingredient-source" or "sink" or "drying" or "dirty-return" or "delivery" or "cannon-control" or "cannon-switch" or "condiment-switch" or "condiment" or "bin" or "cannon" or "portal" or "portal-receiver";
            if(!structural)continue;
            if(heldIds.Contains(Id(entity))||CarriedPath(entity["hierarchyPath"]?.ToString()))continue;
            var colliders=(entity["colliders"] as JsonArray)?.OfType<JsonObject>().Where(c=>c["enabled"]?.GetValue<bool>()!=false&&c["active"]?.GetValue<bool>()!=false&&c["layer"]?.GetValue<int>()!=15&&!CarriedPath(c["hierarchyPath"]?.ToString())&&N(c["size"]?["x"])>0&&N(c["size"]?["z"])>0&&N(c["center"]?["y"])+N(c["size"]?["y"])/2>.08&&N(c["center"]?["y"])-N(c["size"]?["y"])/2<2.05).ToArray()??[];
            // Known station triggers are footprint proxies because the snapshot
            // does not include their non-entity physical child colliders.
            var usable=colliders.Where(c=>c["trigger"]?.GetValue<bool>()==false).ToArray();
            if(usable.Length==0&&role is "cannon" or "portal" or "portal-receiver")continue;
            if(usable.Length==0)usable=colliders.Where(c=>c["type"]?.ToString()=="BoxCollider").ToArray();
            if(usable.Length==0)
            {
                obstacles.Add(new(key,Id(entity),new(p.X-tile/2,p.X+tile/2,p.Z-tile/2,p.Z+tile/2),"Measured grid tile proxy; entity has no serialized footprint collider."));
                continue;
            }
            foreach(var collider in usable)
            {
                var center=Position(collider["center"]);var size=Position(collider["size"]);
                var box=new Rect2(center.X-size.X/2,center.X+size.X/2,center.Z-size.Z/2,center.Z+size.Z/2);
                var signature=FormattableString.Invariant($"{box.MinX:F3}/{box.MaxX:F3}/{box.MinZ:F3}/{box.MaxZ:F3}");
                string? colliderPath=collider["hierarchyPath"]?.ToString();
                var oriented=colliderPath is not null&&entityTransforms.TryGetValue(colliderPath,out var transform)?ReconstructOrientedBox(collider,transform):null;
                if(oriented is { } shape)signature+=FormattableString.Invariant($"/obb/{shape.HalfX:F5}/{shape.HalfZ:F5}/{shape.CosYaw:F5}/{shape.SinYaw:F5}");
                if(colliderPath is not null&&!seenColliderPath.Add(colliderPath+"/"+signature))continue;
                if(seenCollider.Add(signature))obstacles.Add(new(key,Id(entity),box,
                    oriented is not null?"Measured active box collider; upright entity-root orientation and AABB reconstructed into validated oriented footprint":
                    collider["trigger"]?.GetValue<bool>()==true?"Measured station trigger footprint proxy":"Measured active solid child collider AABB",Oriented:oriented));
            }
        }
        var model=new KitchenModel
        {
            Scene=state["scene"]!.ToString(),TilePitch=tile,ChefRadius=radius,Regions=regions,Obstacles=obstacles.ToArray(),
            Warnings=["Floor polygons infer platform edges from runtime station geometry and the inspected screenshot; hidden scene floor/wall colliders are absent from this snapshot.","Station trigger bounds conservatively proxy missing physical child colliders. Replan if live movement disagrees.","Transition edges without a native destination remain unverified and are never treated as walkable connections."]
        };
        model.Stations=stationData.Select(item=>
        {
            var p=Position(item.Entity["position"]);
            var approaches=Approaches(model,p,item.Role).ToArray();
            return new KitchenStation(item.Key,Id(item.Entity),item.Entity["name"]?.ToString()??"",item.Role,item.Entity["spawnPrefab"]?.ToString(),p,approaches.Select(a=>a.Region).Distinct().Order().ToArray(),Components(item.Entity),approaches);
        }).ToArray();
        model.Transitions=BuildTransitions(model,stationData).ToArray();
        return model;
    }

    // A root BoxCollider's axes are its observed entity Transform axes. Bounds
    // alone do not reveal child-local rotation, so those remain conservative
    // AABBs. Near 45 degrees the inverse is ill-conditioned and also falls back.
    internal static OrientedBox2? ReconstructOrientedBox(JsonObject collider,JsonObject entity)
    {
        if(collider["type"]?.ToString()!="BoxCollider"||collider["trigger"]?.GetValue<bool>()!=false
            ||collider["hierarchyPath"] is null||collider["hierarchyPath"]?.ToString()!=entity["hierarchyPath"]?.ToString()
            ||entity["rotation"] is not JsonObject rotation)return null;
        double qx=N(rotation["x"]),qy=N(rotation["y"]),qz=N(rotation["z"]),qw=N(rotation["w"]);
        double norm=qx*qx+qy*qy+qz*qz+qw*qw;
        if(!double.IsFinite(norm)||Math.Abs(norm-1)>.001||Math.Abs(qx)>1e-5||Math.Abs(qz)>1e-5)return null;
        qy/=Math.Sqrt(norm);qw/=Math.Sqrt(norm);
        double cosine=1-2*qy*qy,sine=2*qw*qy,c=Math.Abs(cosine),s=Math.Abs(sine),determinant=c*c-s*s;
        if(Math.Abs(determinant)<.15)return null;
        var size=Position(collider["size"]);var center=Position(collider["center"]);
        if(!double.IsFinite(size.X)||!double.IsFinite(size.Z)||!double.IsFinite(center.X)||!double.IsFinite(center.Z)||size.X<=0||size.Z<=0)return null;
        double halfX=(c*size.X-s*size.Z)/(2*determinant),halfZ=(c*size.Z-s*size.X)/(2*determinant);
        if(halfX<=.001||halfZ<=.001||!double.IsFinite(halfX)||!double.IsFinite(halfZ))return null;
        double reconstructedX=2*(c*halfX+s*halfZ),reconstructedZ=2*(s*halfX+c*halfZ);
        if(Math.Abs(reconstructedX-size.X)>1e-5||Math.Abs(reconstructedZ-size.Z)>1e-5)return null;
        // Expand by two hundredths of a millimetre rather than shrinking the
        // native shape when its float bounds were rounded for JSON telemetry.
        return new(center,halfX+.00002,halfZ+.00002,cosine,sine);
    }

    private static IEnumerable<StationApproach> Approaches(KitchenModel model,Point2 p,string role)
    {
        var covering=model.Obstacles.Where(o=>o.Bounds.Contains(p)).ToArray();
        var bounds=covering.Length>0?new Rect2(covering.Min(o=>o.Bounds.MinX),covering.Max(o=>o.Bounds.MaxX),covering.Min(o=>o.Bounds.MinZ),covering.Max(o=>o.Bounds.MaxZ)):new Rect2(p.X-.6,p.X+.6,p.Z-.6,p.Z+.6);
        double margin=model.ChefRadius+model.Clearance+.04;
        var candidates=new List<Point2>();
        foreach(var along in new[]{-.35,0,.35})
        {
            candidates.Add(new(bounds.MinX-margin,p.Z+along));candidates.Add(new(bounds.MaxX+margin,p.Z+along));
            candidates.Add(new(p.X+along,bounds.MinZ-margin));candidates.Add(new(p.X+along,bounds.MaxZ+margin));
        }
        if(role is "portal" or "portal-receiver" or "cannon")
            foreach(var distance in new[]{.46,.7,1.0,1.3})foreach(var direction in new[]{new Point2(1,0),new Point2(-1,0),new Point2(0,1),new Point2(0,-1)})candidates.Add(new(p.X+direction.X*distance,p.Z+direction.Z*distance));
        foreach(var candidate in candidates.Distinct())
        {
            var region=model.RegionAt(candidate);
            if(region is not null&&Navigation.IsWalkable(model,candidate,region)&&candidate.Distance(p)<2.1)yield return new(region,candidate,p);
        }
    }

    private static IEnumerable<KitchenTransition> BuildTransitions(KitchenModel model,List<(JsonObject Entity,string Role,string Key)> data)
    {
        foreach(var item in data.Where(d=>d.Role=="portal"))
        {
            var station=model.Stations.Single(s=>s.Key==item.Key);
            int destination=(item.Entity["portalDestinationId"]??item.Entity["teleportalDestinationId"])?.GetValue<int>()??0;
            var target=model.Stations.SingleOrDefault(s=>s.EntityId==destination);
            var from=station.Regions.SingleOrDefault()??model.RegionAt(station.Position)??"unknown";
            var destinationEntity=target is null?null:data.SingleOrDefault(d=>d.Key==target.Key).Entity;
            var arrival=destinationEntity?["portalTeleportPoint"] is { } exit?Position(exit):target?.Position;
            var to=target is null?null:arrival is { } point?model.RegionAt(point)??target.Regions.SingleOrDefault():target.Regions.SingleOrDefault()??model.RegionAt(target.Position);
            bool nativeSender=item.Entity["portalSenderCount"] is null||N(item.Entity["portalSenderCount"])>0;
            yield return new("portal",from,to,item.Key,target?.Key,target is not null&&to is not null&&nativeSender,target is not null?"Native portal destination entity and teleport transform mapped to measured region":"No native destination id in snapshot; source is a verified native player sender.",station.Position,arrival);
        }
        foreach(var item in data.Where(d=>d.Role=="cannon"))
        {
            var station=model.Stations.Single(s=>s.Key==item.Key);
            var from=station.Regions.FirstOrDefault()??model.RegionAt(station.Position)??"unknown";
            var landing=item.Entity["cannonTarget"] is { } target?Position(target):(Point2?)null;
            var to=landing is { } endpoint?model.RegionAt(endpoint):null;
            yield return new("cannon",from,to,item.Key,null,to is not null,landing is not null?"Current native cannon target transform and angle; geometry verifies the configured endpoint, while flight execution still requires native interaction probes.":"Native cannon observed; current landing endpoint is missing.",station.Position,landing,item.Entity["cannonAngle"] is null?null:N(item.Entity["cannonAngle"]));
        }
    }

    public KitchenStation Resolve(string keyOrRole)
    {
        var matches=Candidates(keyOrRole);
        return matches.Length==1?matches[0]:throw new ArgumentException(matches.Length==0?"No station matches "+keyOrRole:"Station selector is ambiguous: "+string.Join(", ",matches.Select(s=>s.Key)));
    }
    public KitchenStation[] Candidates(string selector)
    {
        var alias=selector.ToLowerInvariant() switch
        {
            "plates" or "clean-plates"=>"plate",
            "cookpot" or "cookpots" or "pots"=>"pot",
            "pans"=>"pan",
            "mixers" or "mixing-bowls" or "bowls"=>"bowl",
            "baskets"=>"basket",
            "chopboards" or "boards" or "chopping-boards"=>"chop",
            "hotdogcrate" or "buns" or "bun"=>"HotdogBun",
            "sausage" or "sausages"=>"Frankfurter",
            "onion" or "onions"=>"DLC08_Onion",
            "mixing-stations"=>"mix-station",
            "cookers"=>"cook-station",
            "fryers"=>"fryer-station",
            _=>selector
        };
        return Stations.Where(s=>s.Key==alias||s.Role.Equals(alias,StringComparison.OrdinalIgnoreCase)||s.Ingredient?.Equals(alias,StringComparison.OrdinalIgnoreCase)==true||s.EntityId.ToString(CultureInfo.InvariantCulture)==alias).ToArray();
    }
    private static int Id(JsonObject entity)=>entity["id"]!.GetValue<int>();
    private static string StableKey(JsonObject entity,string role)
    {
        var name=Regex.Replace(entity["name"]?.ToString()??"entity",@"\s*\(\d+\)$","").ToLowerInvariant();
        var p=Position(entity["position"]);
        return FormattableString.Invariant($"{role}:{name}@{p.X:F2},{p.Z:F2}");
    }
    private static string? Role(JsonObject e)
    {
        var c=Components(e);var name=e["name"]?.ToString()??"";
        if(name.EndsWith("_Rigidbody",StringComparison.Ordinal)||c.Contains("PlayerIDProvider"))return null;
        if(c.Contains("Cannon"))return "cannon";
        if(c.Contains("Teleportal"))return c.Contains("TeleportalPlayerSender")?"portal":"portal-receiver";
        if(name.Contains("MultiControlTerminal",StringComparison.Ordinal))return "cannon-control";
        if(c.Contains("SwitchStation"))return name.Contains("Condiment",StringComparison.OrdinalIgnoreCase)?"condiment-switch":"cannon-switch";
        if(e["spawnPrefab"] is not null)return "ingredient-source";
        if(c.Contains("MixingStation"))return "mix-station";
        if(c.Contains("CookingStation"))return name.Contains("FryingStation",StringComparison.Ordinal)?"fryer-station":"cook-station";
        if(c.Contains("Workstation")&&c.Contains("ChoppingStationCosmeticDecisions"))return "chop";
        if(c.Contains("WashingStation"))return "sink";
        if(c.Contains("PlateStation"))return "delivery";
        if(c.Contains("PlateReturnStation"))return name.Contains("Drying",StringComparison.Ordinal)?"drying":"dirty-return";
        if(name.Contains("condiment_dispenser",StringComparison.Ordinal))return "condiment";
        if(name.StartsWith("Bin",StringComparison.Ordinal))return "bin";
        if(name.Contains("countertop_01_",StringComparison.Ordinal))return "counter";
        if(c.Contains("Plate"))return "plate";
        if(c.Contains("CarryableItem"))return name.Contains("mixer",StringComparison.Ordinal)?"bowl":name.Contains("frying_pan",StringComparison.Ordinal)?"pan":name.Contains("FrierBasket",StringComparison.Ordinal)?"basket":name.Contains("pot_",StringComparison.Ordinal)?"pot":"utensil";
        return null;
    }
}
