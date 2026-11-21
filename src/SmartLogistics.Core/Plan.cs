namespace SmartLogistics.Core;

public enum StepAction { Start, Pickup, Delivery, Return }

/// <summary>Jedna radnja u ruti: preuzimanje ili isporuka određenog naloga.</summary>
public sealed record Stop(string OrderId, StepAction Action);

/// <summary>Redak u prikazu rute – stanje kamiona nakon izvršene radnje.</summary>
public sealed record Step(
    string LocationId,
    StepAction Action,
    string? OrderId,
    string? MachineId,
    double LegKm,
    double TotalKm,
    int LoadKg,
    int LoadItems,
    double ClockMin);

/// <summary>Ruta jednog kamiona i sva kršenja pravila koja je validator pronašao.</summary>
public sealed class Route
{
    public required Truck Truck { get; init; }
    public required IReadOnlyList<Stop> Stops { get; init; }
    public required IReadOnlyList<Step> Steps { get; init; }
    public required int DistanceDk { get; init; }
    public required double Minutes { get; init; }
    public required int OrdersDone { get; init; }
    public required IReadOnlyList<string> Violations { get; init; }

    public double Km => DistanceDk / 10.0;
    public bool Ok => Violations.Count == 0;
}

/// <summary>Nalog koji nije moguće izvršiti, s razlogom za upozorenje.</summary>
public sealed record Unassigned(string OrderId, string Reason)
{
    /// <summary>
    /// Upozorenje za neizvršeni nalog. Ako je stroj pretežak za sve kamione, to je razlog;
    /// inače nalog nije stao u radno vrijeme uz ostala ograničenja (timeReason).
    /// </summary>
    public static Unassigned For(Problem problem, Order order, string timeReason)
    {
        int w = problem.Weight(order);
        string reason = problem.Trucks.Any(t => w <= t.MaxKg)
            ? timeReason
            : $"Stroj {order.MachineId} ({w} kg) teži je od nosivosti svih kamiona (najviše {problem.Trucks.Max(t => t.MaxKg)} kg).";
        return new Unassigned(order.Id, reason);
    }
}

/// <summary>Rezultat optimizacije: rute svih kamiona + neizvršeni nalozi.</summary>
public sealed class Plan
{
    public required IReadOnlyList<Route> Routes { get; init; }
    public required IReadOnlyList<Unassigned> Unassigned { get; init; }
    public string SolverName { get; init; } = "";
    public bool IsOptimal { get; init; }
    public long ElapsedMs { get; init; }
    public int StatesExplored { get; init; }

    public int TotalDk => Routes.Sum(r => r.DistanceDk);
    public double TotalKm => TotalDk / 10.0;
    public int OrdersDone => Routes.Sum(r => r.OrdersDone);
}
