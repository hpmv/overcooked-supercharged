namespace OvercookedTAS.Controller;

public sealed record NavigationPath(bool Success,string? Error,string? Region,Point2[] Points,double Length,int ExpandedNodes,KitchenTransition[] RequiredTransitions);

public static class Navigation
{
    // A measured native contact flipped CanDepart between two runs solely
    // because capsule world-AABB radii differed by 9.54e-7 (frame1666).
    // This allowance applies only when departing an existing contact. New
    // obstacles and destinations retain the full navigation clearance.
    internal const double DepartureNumericAllowance=.00001;
    private static double DepartureRadius(KitchenModel model)=>Math.Max(0,model.ChefRadius-.002-DepartureNumericAllowance);
    public static bool IsWalkable(KitchenModel model,Point2 p,string region,IReadOnlyList<KitchenObstacle>? dynamic=null)
    {
        var floor=model.Regions.Single(r=>r.Id==region).FloorBounds;
        double clearance=model.ChefRadius+model.Clearance;
        return floor.Contains(p,clearance)&&!model.Obstacles.Concat(dynamic??[]).Any(o=>ObstacleDistanceSquared(p,o)<=clearance*clearance);
    }
    public static bool SegmentClear(KitchenModel model,Point2 a,Point2 b,string region,IReadOnlyList<KitchenObstacle>? dynamic=null)
    {
        if(!CanDepart(model,a,region,dynamic)||!IsWalkable(model,b,region,dynamic))return false;
        double clearance=model.ChefRadius+model.Clearance,physical=DepartureRadius(model);
        foreach(var obstacle in model.Obstacles.Concat(dynamic??[]))
        {
            if(obstacle.CircleRadius is { } radius)
            {
                var center=CircleCenter(obstacle);
                double circleDistance=ObstacleDistanceSquared(a,obstacle);
                double required=radius+(circleDistance>clearance*clearance?clearance:physical);
                // Native vertical chef capsules have circular horizontal
                // footprints. Their AABB corners are not physical obstacles.
                // Existing contact may depart only outward, with the same
                // physical-radius allowance used for native box contact.
                if(circleDistance<=clearance*clearance&&
                    (a.X-center.X)*(b.X-a.X)+(a.Z-center.Z)*(b.Z-a.Z)<-1e-9)return false;
                if(PointSegmentDistanceSquared(center,a,b)<=required*required)return false;
                continue;
            }
            var box=obstacle.Oriented?.LocalBounds??obstacle.Bounds;
            var from=obstacle.Oriented?.ToLocal(a)??a;var to=obstacle.Oriented?.ToLocal(b)??b;
            double distance=PointBoxDistanceSquared(from,box);
            if(distance>clearance*clearance)
            {
                if(SweptCircleIntersects(from,to,box,clearance))return false;
            }
            else
            {
                // An observed chef can legally rest on a station at native
                // radius, inside our extra navigation margin. Permit only
                // outward departure from that existing contact; never reduce
                // clearance around an obstacle newly approached by the path.
                double nx=from.X-Math.Clamp(from.X,box.MinX,box.MaxX),nz=from.Z-Math.Clamp(from.Z,box.MinZ,box.MaxZ);
                if(nx*(to.X-from.X)+nz*(to.Z-from.Z)<-1e-9||SweptCircleIntersects(from,to,box,physical))return false;
            }
        }
        return true;
    }
    private static bool CanDepart(KitchenModel model,Point2 p,string region,IReadOnlyList<KitchenObstacle>? dynamic)
    {
        double floorInset=Math.Max(0,model.ChefRadius-.002),physical=DepartureRadius(model);
        return model.Regions.Single(r=>r.Id==region).FloorBounds.Contains(p,floorInset)&&model.Obstacles.Concat(dynamic??[]).All(o=>ObstacleDistanceSquared(p,o)>physical*physical);
    }
    public static NavigationPath FindPath(KitchenModel model,Point2 start,Point2 target,double spacing=.15,IReadOnlyList<KitchenObstacle>? dynamic=null)
    {
        string? region=model.RegionAt(start),destination=model.RegionAt(target);
        NavigationPath Fail(string reason)=>new(false,reason,region,[],0,0,model.Transitions.Where(t=>t.FromRegion==region).ToArray());
        if(region is null||destination is null)return Fail("Start or target is outside the five measured platforms; the gap is not walkable.");
        if(region!=destination)return Fail("Separate platforms require a verified portal or cannon action; walking cannot cross the void or counter boundary.");
        if(!CanDepart(model,start,region,dynamic))return Fail("Start overlaps a physical obstacle or unmeasured floor edge beyond numeric contact allowance.");
        if(!IsWalkable(model,target,region,dynamic))return Fail("Target overlaps a station or platform edge; select an interaction approach point.");
        if(spacing<.05||spacing>.5)throw new ArgumentOutOfRangeException(nameof(spacing));
        if(SegmentClear(model,start,target,region,dynamic))return new(true,null,region,[start,target],start.Distance(target),0,[]);
        var bounds=model.Regions.Single(r=>r.Id==region).FloorBounds;
        int nx=(int)Math.Ceiling((bounds.MaxX-bounds.MinX)/spacing)+1,nz=(int)Math.Ceiling((bounds.MaxZ-bounds.MinZ)/spacing)+1;
        Point2 Point(int index)=>new(bounds.MinX+(index%nx)*spacing,bounds.MinZ+(index/nx)*spacing);
        var valid=new bool[nx*nz];
        for(int i=0;i<valid.Length;i++)valid[i]=IsWalkable(model,Point(i),region,dynamic);
        int Closest(Point2 p)=>Enumerable.Range(0,valid.Length).Where(i=>valid[i]&&Point(i).Distance(p)<=spacing*2.5&&SegmentClear(model,p,Point(i),region,dynamic)).OrderBy(i=>Point(i).Distance(p)).DefaultIfEmpty(-1).First();
        int source=Closest(start),goal=Closest(target);
        if(source<0||goal<0)return Fail("No grid node connects safely to the requested position.");
        var cost=Enumerable.Repeat(double.PositiveInfinity,valid.Length).ToArray();
        var previous=Enumerable.Repeat(-1,valid.Length).ToArray();
        var open=new PriorityQueue<int,(double Estimate,int Index)>();
        var closed=new bool[valid.Length];
        cost[source]=0;open.Enqueue(source,(Point(source).Distance(target),source));int expanded=0;
        while(open.TryDequeue(out int current,out _))
        {
            if(closed[current])continue;closed[current]=true;expanded++;
            if(current==goal)
            {
                var raw=new List<Point2>{target};
                for(int step=goal;step!=-1;step=previous[step])raw.Add(Point(step));
                raw.Add(start);raw.Reverse();
                var smooth=new List<Point2>{raw[0]};int at=0;
                while(at<raw.Count-1)
                {
                    int next=raw.Count-1;while(next>at+1&&!SegmentClear(model,raw[at],raw[next],region,dynamic))next--;
                    smooth.Add(raw[next]);at=next;
                }
                var length=smooth.Zip(smooth.Skip(1),(a,b)=>a.Distance(b)).Sum();
                return new(true,null,region,smooth.ToArray(),length,expanded,[]);
            }
            int x=current%nx,z=current/nx;
            for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
            {
                if(dx==0&&dz==0)continue;int xx=x+dx,zz=z+dz;
                if(xx<0||xx>=nx||zz<0||zz>=nz)continue;int next=zz*nx+xx;
                if(!valid[next]||closed[next]||!SegmentClear(model,Point(current),Point(next),region,dynamic))continue;
                double proposed=cost[current]+spacing*Math.Sqrt(dx*dx+dz*dz);
                if(proposed>=cost[next])continue;
                cost[next]=proposed;previous[next]=current;open.Enqueue(next,(proposed+Point(next).Distance(Point(goal)),next));
            }
        }
        return Fail("No collision-free path exists in this platform at the current clearance.");
    }
    public static NavigationPath ToStation(KitchenModel model,Point2 start,KitchenStation station,IReadOnlyList<KitchenObstacle>? dynamic=null)
    {
        var paths=station.Approaches.Where(a=>a.Region==model.RegionAt(start)).Select(a=>FindPath(model,start,a.Position,.15,dynamic)).Where(p=>p.Success).OrderBy(p=>p.Length).ToArray();
        return paths.FirstOrDefault()??new(false,"No reachable approach to this station on the chef's platform.",model.RegionAt(start),[],0,0,model.Transitions.Where(t=>t.FromRegion==model.RegionAt(start)).ToArray());
    }
    private static bool Intersects(Point2 a,Point2 b,Rect2 box)
    {
        double near=0,far=1;
        bool Slab(double origin,double direction,double min,double max)
        {
            if(Math.Abs(direction)<1e-12)return origin>=min&&origin<=max;
            double first=(min-origin)/direction,last=(max-origin)/direction;
            if(first>last)(first,last)=(last,first);near=Math.Max(near,first);far=Math.Min(far,last);return near<=far;
        }
        return Slab(a.X,b.X-a.X,box.MinX,box.MaxX)&&Slab(a.Z,b.Z-a.Z,box.MinZ,box.MaxZ);
    }
    private static double PointBoxDistanceSquared(Point2 p,Rect2 box)
    {
        double dx=p.X-Math.Clamp(p.X,box.MinX,box.MaxX),dz=p.Z-Math.Clamp(p.Z,box.MinZ,box.MaxZ);
        return dx*dx+dz*dz;
    }
    private static Point2 CircleCenter(KitchenObstacle obstacle)=>new((obstacle.Bounds.MinX+obstacle.Bounds.MaxX)/2,
        (obstacle.Bounds.MinZ+obstacle.Bounds.MaxZ)/2);
    internal static double ObstacleDistanceSquared(Point2 point,KitchenObstacle obstacle)
    {
        if(obstacle.CircleRadius is { } radius)
        {
            double distance=Math.Max(0,point.Distance(CircleCenter(obstacle))-radius);
            return distance*distance;
        }
        return obstacle.Oriented is { } box?PointBoxDistanceSquared(box.ToLocal(point),box.LocalBounds):PointBoxDistanceSquared(point,obstacle.Bounds);
    }
    private static double PointSegmentDistanceSquared(Point2 p,Point2 a,Point2 b)
    {
        double dx=b.X-a.X,dz=b.Z-a.Z,length=dx*dx+dz*dz;
        double t=length<1e-20?0:Math.Clamp(((p.X-a.X)*dx+(p.Z-a.Z)*dz)/length,0,1);
        double ex=p.X-a.X-t*dx,ez=p.Z-a.Z-t*dz;return ex*ex+ez*ez;
    }
    private static bool SweptCircleIntersects(Point2 a,Point2 b,Rect2 box,double radius)
    {
        if(!Intersects(a,b,box.Expand(radius)))return false;
        if(Intersects(a,b,box))return true;
        double limit=radius*radius;
        if(PointBoxDistanceSquared(a,box)<=limit||PointBoxDistanceSquared(b,box)<=limit)return true;
        return new[]{new Point2(box.MinX,box.MinZ),new Point2(box.MinX,box.MaxZ),new Point2(box.MaxX,box.MinZ),new Point2(box.MaxX,box.MaxZ)}.Any(c=>PointSegmentDistanceSquared(c,a,b)<=limit);
    }
}
