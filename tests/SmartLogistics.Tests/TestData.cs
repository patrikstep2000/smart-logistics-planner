using SmartLogistics.Core;

namespace SmartLogistics.Tests;

/// <summary>Pomoćne metode za testove: mali problemi i brute-force referenca.</summary>
public static class TestData
{
    public static readonly Truck TruckA = new("A", "Kamion A", 3500, 2);
    public static readonly Truck TruckB = new("B", "Kamion B", 10000, 3);

    public static Problem Default() => ProblemData.Load().ToProblem();

    /// <summary>Problem s bazom L0 u (0,0); svaki nalog ima vlastiti stroj zadane težine.</summary>
    public static Problem Make(
        Dictionary<string, (double X, double Y)> locations,
        IEnumerable<(string Id, int Kg, string Pickup, string Delivery)> orders,
        IReadOnlyList<Truck>? trucks = null,
        Settings? settings = null)
    {
        var locs = new Dictionary<string, Location> { ["L0"] = new("L0", "Baza", 0, 0) };
        foreach (var (id, (x, y)) in locations) locs[id] = new Location(id, id, x, y);
        var list = orders.ToList();
        return new Problem
        {
            Base = "L0",
            Locations = locs,
            Trucks = trucks ?? [TruckA, TruckB],
            Machines = list.ToDictionary(o => "M_" + o.Id, o => new Machine("M_" + o.Id, o.Kg)),
            Orders = list.Select(o => new Order(o.Id, "M_" + o.Id, o.Pickup, o.Delivery)).ToList(),
            Settings = settings ?? new Settings(),
        };
    }

    public static Stop P(string id) => new(id, StepAction.Pickup);
    public static Stop D(string id) => new(id, StepAction.Delivery);

    /// <summary>
    /// (broj izvršenih naloga, ukupni dk) isprobavanjem SVIH podjela i SVIH redoslijeda.
    /// Izvedivost provjerava isključivo validator, pa je ovo neovisno o kodu solvera.
    /// </summary>
    public static (int Served, int TotalDk) BruteForce(Problem problem)
    {
        var trucks = problem.Trucks;
        var orders = problem.Orders;
        (int Served, int TotalDk)? best = null;

        // Svaki nalog ide na jedan od kamiona ili ostaje neizvršen (vrijednost = trucks.Count).
        foreach (var assign in Assignments(orders.Count, trucks.Count + 1))
        {
            int total = 0;
            bool feasible = true;
            for (int t = 0; t < trucks.Count && feasible; t++)
            {
                var mine = orders.Where((_, i) => assign[i] == t).ToList();
                var stops = mine.Select(o => P(o.Id)).Concat(mine.Select(o => D(o.Id))).ToList();
                int? bestTruck = null;
                foreach (var perm in Permutations(stops))
                {
                    var r = Validator.SimulateRoute(problem, trucks[t], perm);
                    if (r.Ok && (bestTruck is null || r.DistanceDk < bestTruck)) bestTruck = r.DistanceDk;
                }
                if (bestTruck is null) feasible = false;
                else total += bestTruck.Value;
            }
            if (!feasible) continue;
            int served = assign.Count(a => a < trucks.Count);
            if (best is null || served > best.Value.Served || (served == best.Value.Served && total < best.Value.TotalDk))
                best = (served, total);
        }
        return best!.Value;
    }

    private static IEnumerable<int[]> Assignments(int n, int options)
    {
        var a = new int[n];
        while (true)
        {
            yield return (int[])a.Clone();
            int i = 0;
            while (i < n && ++a[i] == options) a[i++] = 0;
            if (i == n) yield break;
        }
    }

    private static IEnumerable<List<T>> Permutations<T>(List<T> items)
    {
        if (items.Count <= 1) { yield return [.. items]; yield break; }
        for (int i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, j) => j != i).ToList();
            foreach (var p in Permutations(rest)) { p.Insert(0, items[i]); yield return p; }
        }
    }
}
