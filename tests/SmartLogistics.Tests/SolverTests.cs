using SmartLogistics.Core;
using static SmartLogistics.Tests.TestData;

namespace SmartLogistics.Tests;

/// <summary>Rješenje solvera mora poštovati sva poslovna ograničenja i biti optimalno.</summary>
public class SolverTests
{
    private static readonly Problem Problem = Default();
    private static readonly Plan Plan = ExactSolver.Solve(Problem);

    [Fact]
    public void Default_data_all_orders_served_with_optimal_distance()
    {
        Assert.Empty(Validator.ValidatePlan(Problem, Plan));
        Assert.Empty(Plan.Unassigned);
        Assert.Equal(10, Plan.OrdersDone);
        Assert.Equal(2001, Plan.TotalDk);   // 200.1 km – isto kao OR-Tools
    }

    [Fact]
    public void Capacity_and_item_limits_never_exceeded()
    {
        foreach (var route in Plan.Routes)
            foreach (var step in route.Steps)
            {
                Assert.InRange(step.LoadKg, 0, route.Truck.MaxKg);
                Assert.InRange(step.LoadItems, 0, route.Truck.MaxItems);
            }
    }

    [Fact]
    public void Pickup_before_delivery_on_same_truck()
    {
        var trucksPerOrder = new Dictionary<string, HashSet<string>>();
        foreach (var route in Plan.Routes)
        {
            var seen = new HashSet<Stop>();
            foreach (var stop in route.Stops)
            {
                if (!trucksPerOrder.TryGetValue(stop.OrderId, out var set))
                    trucksPerOrder[stop.OrderId] = set = [];
                set.Add(route.Truck.Id);
                if (stop.Action == StepAction.Delivery)
                    Assert.Contains(P(stop.OrderId), seen);
                seen.Add(stop);
            }
        }
        foreach (var order in Problem.Orders)
            Assert.Single(trucksPerOrder[order.Id]);
    }

    [Fact]
    public void Routes_start_and_end_at_base_within_shift()
    {
        foreach (var route in Plan.Routes)
        {
            Assert.Equal(Problem.Base, route.Steps[0].LocationId);
            Assert.Equal(Problem.Base, route.Steps[^1].LocationId);
            Assert.Equal(0, route.Steps[^1].LoadItems);
            Assert.True(route.Minutes <= Problem.Settings.MaxShiftMin);
        }
    }

    [Fact]
    public void Heavy_machines_go_to_truck_b()
    {
        var heavy = Problem.Orders.Where(o => Problem.Weight(o) > TruckA.MaxKg).Select(o => o.Id).ToHashSet();
        Assert.Equal(new HashSet<string> { "N06", "N07", "N08", "N09", "N10" }, heavy);
        var onB = Plan.Routes.Single(r => r.Truck.Id == "B").Stops.Select(s => s.OrderId).ToHashSet();
        Assert.Subset(onB, heavy);
    }

    [Fact]
    public void Too_heavy_order_is_reported_not_dropped_silently()
    {
        var p = Make(new() { ["L1"] = (5, 0), ["L2"] = (10, 0) },
            [("N1", 1000, "L1", "L2"), ("NX", 12000, "L1", "L2")]);
        var plan = ExactSolver.Solve(p);
        var u = Assert.Single(plan.Unassigned);
        Assert.Equal("NX", u.OrderId);
        Assert.Contains("teži je od nosivosti", u.Reason);
        Assert.Equal(1, plan.OrdersDone);
        Assert.Empty(Validator.ValidatePlan(p, plan));
    }

    [Fact]
    public void Shift_limit_leaves_order_unassigned_with_warning()
    {
        var p = Make(new() { ["L1"] = (5, 0), ["L2"] = (80, 0) },
            [("N1", 1000, "L1", "L0"), ("FAR", 1000, "L2", "L1")],
            settings: new Settings(MaxShiftMin: 120));
        var plan = ExactSolver.Solve(p);
        var u = Assert.Single(plan.Unassigned);
        Assert.Equal("FAR", u.OrderId);
        Assert.Contains("radno vrijeme", u.Reason);
        Assert.Empty(Validator.ValidatePlan(p, plan));
    }

    [Fact]
    public void Too_many_orders_raise_clear_error()
    {
        var p = Make(new() { ["L1"] = (5, 0) }, Enumerable.Range(0, 13).Select(i => ($"N{i}", 500, "L1", "L0")));
        var ex = Assert.Throws<InvalidOperationException>(() => ExactSolver.Solve(p));
        Assert.Contains("najviše 12", ex.Message);
    }

    /// <summary>Na nasumičnim malim instancama rezultat mora biti jednak potpunoj pretrazi.</summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Solver_matches_brute_force_on_random_instances(int seed)
    {
        var rng = new Random(seed);
        var locs = Enumerable.Range(1, 5).ToDictionary(i => $"L{i}", _ => ((double)rng.Next(-15, 16), (double)rng.Next(-15, 16)));
        var names = new[] { "L0" }.Concat(locs.Keys).ToArray();
        int[] weights = [800, 1300, 2400, 3100, 3900, 6100, 9400];
        var orders = Enumerable.Range(0, 3).Select(i =>
        {
            var pair = names.OrderBy(_ => rng.Next()).Take(2).ToArray();
            return ($"N{i}", weights[rng.Next(weights.Length)], pair[0], pair[1]);
        }).ToList();
        double[] shifts = [90, 150, 540];
        var p = Make(locs, orders, settings: new Settings(MaxShiftMin: shifts[rng.Next(shifts.Length)]));

        var plan = ExactSolver.Solve(p);
        Assert.Empty(Validator.ValidatePlan(p, plan));
        Assert.Equal(BruteForce(p), (plan.OrdersDone, plan.TotalDk));
    }

    public static TheoryData<int> Seeds() => new(Enumerable.Range(0, 25));
}
