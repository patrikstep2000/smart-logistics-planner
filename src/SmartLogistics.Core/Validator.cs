namespace SmartLogistics.Core;

/// <summary>
/// Neovisni validator poslovnih ograničenja.
///
/// <see cref="SimulateRoute"/> "vozi" zadanu rutu korak po korak i bilježi svako kršenje
/// pravila. Ne zna ništa o solveru, pa se njime može provjeriti bilo koje rješenje:
/// naš solver, OR-Tools, ručno složena ruta ili brute force u testovima.
/// </summary>
public static class Validator
{
    public static Route SimulateRoute(Problem problem, Truck truck, IReadOnlyList<Stop> stops)
    {
        var s = problem.Settings;
        var baseLoc = problem.Locations[problem.Base];
        var loc = baseLoc;
        int dkTotal = 0, actions = 0, loadKg = 0;
        var onboard = new Dictionary<string, int>();   // nalog → težina stroja u kamionu
        var picked = new HashSet<string>();
        var delivered = new HashSet<string>();
        var violations = new List<string>();
        var steps = new List<Step> { new(baseLoc.Id, StepAction.Start, null, null, 0, 0, 0, 0, 0) };

        for (int i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            int no = i + 1;
            var order = problem.FindOrder(stop.OrderId);
            if (order is null)
            {
                violations.Add($"Korak {no}: nepoznat nalog {stop.OrderId}.");
                continue;
            }
            if (stop.Action is not (StepAction.Pickup or StepAction.Delivery))
            {
                violations.Add($"Korak {no}: nepoznata radnja {stop.Action}.");
                continue;
            }

            var target = problem.Locations[stop.Action == StepAction.Pickup ? order.Pickup : order.Delivery];
            int leg = Distance.LegDk(loc, target);
            dkTotal += leg;
            actions++;
            loc = target;
            int weight = problem.Weight(order);

            if (stop.Action == StepAction.Pickup)
            {
                if (!picked.Add(order.Id))
                {
                    violations.Add($"Korak {no}: {order.Id} je preuzet više puta.");
                }
                else
                {
                    onboard[order.Id] = weight;
                    loadKg += weight;
                    if (loadKg > truck.MaxKg)
                        violations.Add($"Korak {no}: prekoračena nosivost ({loadKg} kg > {truck.MaxKg} kg).");
                    if (onboard.Count > truck.MaxItems)
                        violations.Add($"Korak {no}: prekoračen broj strojeva ({onboard.Count} > {truck.MaxItems}).");
                }
            }
            else if (onboard.Remove(order.Id, out int w))
            {
                loadKg -= w;
                delivered.Add(order.Id);
            }
            else if (delivered.Contains(order.Id))
            {
                violations.Add($"Korak {no}: {order.Id} je isporučen više puta.");
            }
            else
            {
                violations.Add($"Korak {no}: {order.Id} isporučen prije preuzimanja.");
            }

            steps.Add(new Step(target.Id, stop.Action, order.Id, order.MachineId, leg / 10.0, dkTotal / 10.0,
                loadKg, onboard.Count, Distance.DriveMinutes(dkTotal, s) + actions * s.HandlingMin));
        }

        // Obavezan povratak u bazu.
        int back = Distance.LegDk(loc, baseLoc);
        dkTotal += back;
        double minutes = Distance.RouteMinutes(dkTotal, actions, s);
        steps.Add(new Step(baseLoc.Id, StepAction.Return, null, null, back / 10.0, dkTotal / 10.0,
            loadKg, onboard.Count, minutes));

        if (onboard.Count > 0)
            violations.Add($"Kamion se vratio u bazu sa strojevima: {string.Join(", ", onboard.Keys.Order())}.");
        if (minutes > s.MaxShiftMin + 1e-9)
            violations.Add($"Prekoračeno radno vrijeme: {minutes / 60:0.00} h > {s.MaxShiftMin / 60:0.00} h.");

        return new Route
        {
            Truck = truck,
            Stops = stops.ToList(),
            Steps = steps,
            DistanceDk = dkTotal,
            Minutes = minutes,
            OrdersDone = delivered.Count,
            Violations = violations,
        };
    }

    /// <summary>Sva kršenja ograničenja u planu; prazna lista znači ispravan plan.</summary>
    public static List<string> ValidatePlan(Problem problem, Plan plan)
    {
        var errors = new List<string>();
        foreach (var route in plan.Routes)
            errors.AddRange(route.Violations.Select(v => $"{route.Truck.Name}: {v}"));

        var trucksPerOrder = plan.Routes
            .SelectMany(r => r.Stops.Select(st => st.OrderId).Distinct())
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());
        var unassigned = plan.Unassigned.Select(u => u.OrderId).ToHashSet();

        foreach (var order in problem.Orders)
        {
            int n = trucksPerOrder.GetValueOrDefault(order.Id);
            if (n > 1)
                errors.Add($"{order.Id}: nalog se pojavljuje u {n} kamiona.");
            if (n > 0 && unassigned.Contains(order.Id))
                errors.Add($"{order.Id}: nalog je i izvršen i označen kao neizvršen.");
            if (n == 0 && !unassigned.Contains(order.Id))
                errors.Add($"{order.Id}: nalog nije izvršen, a nema upozorenja.");
        }
        return errors;
    }
}
