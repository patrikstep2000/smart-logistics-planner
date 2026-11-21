using System.Diagnostics;
using Google.OrTools.ConstraintSolver;
using Google.OrTools.Util;
using Google.Protobuf.WellKnownTypes;

namespace SmartLogistics.Core;

/// <summary>
/// Neovisna kontrola: isti problem riješen Google OR-Tools routing solverom.
///
/// OR-Tools ne garantira optimum (heuristika + lokalno pretraživanje), ali je potpuno
/// drugačija implementacija. Ako se DP i OR-Tools slože, to je jak dokaz da je DP
/// ispravan; ako OR-Tools ikad nađe KRAĆU ispravnu rutu, DP ima grešku.
///
/// Model: svako preuzimanje i svaka isporuka su zaseban čvor; čvor 0 je baza.
/// Dimenzije: kilometraža (cilj), težina i broj strojeva (kapacitet po vozilu),
/// radno vrijeme (vožnja + utovar/istovar, limit smjene). Nalog se smije izostaviti uz
/// kaznu veću od bilo koje rute, pa je cilj isti kao kod DP-a: najprije što više naloga.
/// </summary>
public static class OrToolsSolver
{
    public static Plan Solve(Problem problem, TimeSpan timeLimit)
    {
        var sw = Stopwatch.StartNew();
        var s = problem.Settings;
        var orders = problem.Orders;
        var trucks = problem.Trucks;

        // Čvorovi: 0 = baza, zatim (preuzimanje, isporuka) za svaki nalog.
        var nodeLoc = new List<string> { problem.Base };
        var nodeStop = new List<Stop?> { null };
        var weight = new List<long> { 0 };
        var count = new List<long> { 0 };
        foreach (var o in orders)
        {
            int w = problem.Weight(o);
            nodeLoc.AddRange([o.Pickup, o.Delivery]);
            nodeStop.AddRange([new Stop(o.Id, StepAction.Pickup), new Stop(o.Id, StepAction.Delivery)]);
            weight.AddRange([w, -w]);
            count.AddRange([1, -1]);
        }
        int nodes = nodeLoc.Count;
        var dist = new long[nodes][];
        var travel = new long[nodes][];   // desetinke minute: utovar/istovar u polaznom čvoru + vožnja
        long handling = (long)Math.Round(s.HandlingMin * 10);
        for (int i = 0; i < nodes; i++)
        {
            dist[i] = new long[nodes];
            travel[i] = new long[nodes];
            for (int j = 0; j < nodes; j++)
            {
                dist[i][j] = Distance.LegDk(problem.Locations[nodeLoc[i]], problem.Locations[nodeLoc[j]]);
                travel[i][j] = (i == 0 ? 0 : handling) + (long)Math.Round(dist[i][j] * 60 / s.SpeedKmh);
            }
        }

        var manager = new RoutingIndexManager(nodes, trucks.Count, 0);
        var routing = new RoutingModel(manager);

        // Matrice se predaju OR-Toolsu unaprijed, pa tijekom pretrage ne mora pozivati C# kod (puno brže).
        routing.SetArcCostEvaluatorOfAllVehicles(routing.RegisterTransitMatrix(dist));
        routing.AddDimensionWithVehicleCapacity(routing.RegisterUnaryTransitVector(weight.ToArray()), 0,
            trucks.Select(t => (long)t.MaxKg).ToArray(), true, "kg");
        routing.AddDimensionWithVehicleCapacity(routing.RegisterUnaryTransitVector(count.ToArray()), 0,
            trucks.Select(t => (long)t.MaxItems).ToArray(), true, "count");
        routing.AddDimension(routing.RegisterTransitMatrix(travel), 0, (long)(s.MaxShiftMin * 10), true, "time");
        var time = routing.GetMutableDimension("time");

        long maxLeg = 0;
        foreach (var row in dist) maxLeg = Math.Max(maxLeg, row.Max());
        long penalty = 10 * maxLeg * nodes + 1;   // veće od bilo koje rute

        var solver = routing.solver();
        for (int k = 0; k < orders.Count; k++)
        {
            long p = manager.NodeToIndex(1 + 2 * k), d = manager.NodeToIndex(2 + 2 * k);
            routing.AddPickupAndDelivery(p, d);
            solver.Add(solver.MakeEquality(routing.VehicleVar(p), routing.VehicleVar(d)));   // isti kamion
            solver.Add(solver.MakeLessOrEqual(time.CumulVar(p), time.CumulVar(d)));         // preuzimanje prije isporuke
            routing.AddDisjunction([p, d], 2 * penalty, 2);                                   // oba čvora ili nijedan
        }

        var parameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
        parameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.ParallelCheapestInsertion;
        parameters.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
        parameters.TimeLimit = Duration.FromTimeSpan(timeLimit);

        // Large Neighborhood Search: uz male izmjene OR-Tools povremeno "razruši" veći dio
        // rješenja (izbaci više naloga ili cijeli dio rute) i složi ga ponovno. Tako može u
        // jednom potezu izbaciti jedan nalog i ubaciti drugi, što malim izmjenama ne može.
        var lns = parameters.LocalSearchOperators;
        lns.UsePathLns = OptionalBoolean.BoolTrue;
        lns.UseFullPathLns = OptionalBoolean.BoolTrue;
        lns.UseInactiveLns = OptionalBoolean.BoolTrue;
        lns.UseGlobalCheapestInsertionPathLns = OptionalBoolean.BoolTrue;
        lns.UseLocalCheapestInsertionPathLns = OptionalBoolean.BoolTrue;
        lns.UseGlobalCheapestInsertionExpensiveChainLns = OptionalBoolean.BoolTrue;
        lns.UseLocalCheapestInsertionExpensiveChainLns = OptionalBoolean.BoolTrue;
        lns.UseGlobalCheapestInsertionCloseNodesLns = OptionalBoolean.BoolTrue;
        lns.UseLocalCheapestInsertionCloseNodesLns = OptionalBoolean.BoolTrue;
        lns.UseRelocatePathGlobalCheapestInsertionInsertUnperformed = OptionalBoolean.BoolTrue;

        var solution = routing.SolveWithParameters(parameters)
                       ?? throw new InvalidOperationException("OR-Tools nije pronašao rješenje.");

        var routes = new List<Route>();
        var served = new HashSet<string>();
        for (int v = 0; v < trucks.Count; v++)
        {
            var stops = new List<Stop>();
            long index = solution.Value(routing.NextVar(routing.Start(v)));
            while (!routing.IsEnd(index))
            {
                var stop = nodeStop[manager.IndexToNode(index)]!;
                stops.Add(stop);
                served.Add(stop.OrderId);
                index = solution.Value(routing.NextVar(index));
            }
            // OR-Tools rezultatu ne vjerujemo na riječ: ruta se ponovno provjerava našim validatorom.
            routes.Add(Validator.SimulateRoute(problem, trucks[v], stops));
        }

        return new Plan
        {
            Routes = routes,
            // OR-Tools ne pretražuje sve, pa "nije uspio uklopiti" (moguće je da bi egzaktni solver uspio).
            Unassigned = orders.Where(o => !served.Contains(o.Id))
                .Select(o => Unassigned.For(problem, o,
                    $"OR-Tools nije uspio uklopiti nalog u radno vrijeme vozača ({s.MaxShiftMin / 60:0.##} h) uz ostala ograničenja."))
                .ToList(),
            SolverName = "Google OR-Tools (GLS + LNS)",
            IsOptimal = false,
            ElapsedMs = sw.ElapsedMilliseconds,
        };
    }
}
