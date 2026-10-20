using System.Numerics;

namespace Caero;

internal sealed partial class SeaPredator
{
    private readonly List<Vector3> navigation = [];
    private int navigationIndex;
    private float navigationTimer;
    private Vector3 navigationGoal;
    internal bool TakingDetour => navigationIndex < navigation.Count;

    private void ClearNavigation()
    { navigation.Clear(); navigationIndex = 0; navigationTimer = 0; }

    private static float PlanarDistance(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    private Vector3 RegionTarget(World world, Vector3 player)
    {
        int cx = (int)MathF.Round((player.X - 96) / World.CellSize), cz = (int)MathF.Round((player.Z - 96) / World.CellSize);
        var sites = from x in Enumerable.Range(cx - 1, 3) from z in Enumerable.Range(cz - 1, 3)
            let p = new Vector3((x + .5f) * World.CellSize, CruiseHeight, (z + .5f) * World.CellSize)
            orderby PlanarDistance(p, player) select p;
        return sites.FirstOrDefault(p => Clear(world, p, Vector3.UnitZ), new(Position.X, CruiseHeight, Position.Z));
    }

    private Vector3 PursuitTarget(World world, Vector3 goal, float dt)
    {
        navigationTimer -= dt;
        if (PlanarDistance(goal, navigationGoal) > 8)
        { ClearNavigation(); navigationGoal = goal; }
        while (TakingDetour && PlanarDistance(Position, navigation[navigationIndex]) < 6) navigationIndex++;
        if (navigationTimer <= 0)
        {
            navigationTimer = 1;
            // The mouth can reach the swimmer before the fish's centre does.
            if (CanTravel(world, Position, goal, 9))
            { navigation.Clear(); navigationIndex = 0; }
            else if (!TakingDetour)
            {
                navigation.Clear(); navigationIndex = 0;
                navigation.AddRange(FindDetour(world, goal));
            }
        }
        if (!TakingDetour) return goal;
        Vector3 next = navigation[navigationIndex];
        return new(next.X, goal.Y, next.Z);
    }

    private static bool CanTravel(World world, Vector3 from, Vector3 to, float stopShort = 0)
    {
        Vector3 delta = new(to.X - from.X, 0, to.Z - from.Z);
        float length = delta.Length();
        if (length <= stopShort + .01f) return true;
        Vector3 forward = delta / length;
        float travel = length - stopShort;
        int steps = Math.Max(1, (int)MathF.Ceiling(travel / 4));
        for (int i = 0; i <= steps; i++)
            if (!Clear(world, from + forward * (travel * i / steps), forward)) return false;
        return true;
    }

    private List<Vector3> FindDetour(World world, Vector3 goal)
    {
        // Island corners form open-water junctions. Axis-aligned edges run
        // through the gaps between bridge piers, rather than through islands.
        int Index(float coordinate) => (int)MathF.Floor((coordinate - World.CellSize / 2f) / World.CellSize);
        int minX = Math.Min(Index(Position.X), Index(goal.X)) - 1, maxX = Math.Max(Index(Position.X), Index(goal.X)) + 1;
        int minZ = Math.Min(Index(Position.Z), Index(goal.Z)) - 1, maxZ = Math.Max(Index(Position.Z), Index(goal.Z)) + 1;
        var sites = new List<Vector3>(); var ids = new Dictionary<(int, int), int>();
        for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
            {
                var point = new Vector3((x + .5f) * World.CellSize, Position.Y, (z + .5f) * World.CellSize);
                ids[(x, z)] = sites.Count; sites.Add(point);
            }
        var costs = Enumerable.Repeat(float.PositiveInfinity, sites.Count).ToArray();
        var previous = Enumerable.Repeat(-1, sites.Count).ToArray();
        var queue = new PriorityQueue<int, float>();
        for (int i = 0; i < sites.Count; i++)
            if (CanTravel(world, Position, sites[i]))
            { costs[i] = PlanarDistance(Position, sites[i]); queue.Enqueue(i, costs[i]); }
        float bestCost = float.PositiveInfinity; int best = -1;
        while (queue.TryDequeue(out int current, out float cost))
        {
            if (cost != costs[current] || cost >= bestCost) continue;
            Vector3 point = sites[current];
            float total = cost + PlanarDistance(point, goal);
            if (total < bestCost && CanTravel(world, point, goal, 9))
            { best = current; bestCost = total; }
            int x = Index(point.X), z = Index(point.Z);
            foreach (var key in new[] { (x - 1, z), (x + 1, z), (x, z - 1), (x, z + 1) })
            {
                if (!ids.TryGetValue(key, out int next) || cost + World.CellSize >= costs[next]) continue;
                if (!CanTravel(world, point, sites[next])) continue;
                costs[next] = cost + World.CellSize; previous[next] = current;
                queue.Enqueue(next, costs[next]);
            }
        }
        var route = new List<Vector3>();
        for (int i = best; i >= 0; i = previous[i]) route.Add(sites[i]);
        route.Reverse();
        return route;
    }
}
