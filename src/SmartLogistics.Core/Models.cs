namespace SmartLogistics.Core;

/// <summary>Lokacija sa simuliranim koordinatama u kilometrima.</summary>
public sealed record Location(string Id, string Name, double X, double Y);

/// <summary>Kamion: nosivost u kg i najveći broj strojeva koje istodobno može voziti.</summary>
public sealed record Truck(string Id, string Name, int MaxKg, int MaxItems);

public sealed record Machine(string Id, int WeightKg);

/// <summary>Transportni nalog: stroj se preuzima na jednoj lokaciji i isporučuje na drugoj.</summary>
public sealed record Order(string Id, string MachineId, string Pickup, string Delivery);

/// <summary>Parametri radnog dana. MaxShiftMin uključuje vožnju i utovar/istovar.</summary>
public sealed record Settings(double SpeedKmh = 60, double HandlingMin = 20, double MaxShiftMin = 9 * 60);

/// <summary>Cijeli ulaz za optimizaciju.</summary>
public sealed class Problem
{
    public required string Base { get; init; }
    public required IReadOnlyDictionary<string, Location> Locations { get; init; }
    public required IReadOnlyList<Truck> Trucks { get; init; }
    public required IReadOnlyDictionary<string, Machine> Machines { get; init; }
    public required IReadOnlyList<Order> Orders { get; init; }
    public Settings Settings { get; init; } = new();

    public int Weight(Order order) => Machines[order.MachineId].WeightKg;

    public Order? FindOrder(string orderId) => Orders.FirstOrDefault(o => o.Id == orderId);

    /// <summary>Provjera ispravnosti ulaza (reference, jedinstvene oznake) – ne poslovnih pravila.</summary>
    public List<string> InputErrors()
    {
        var errors = new List<string>();
        if (!Locations.ContainsKey(Base))
            errors.Add($"Baza {Base} nije definirana među lokacijama.");
        if (Orders.Select(o => o.Id).Distinct().Count() != Orders.Count)
            errors.Add("Oznake naloga nisu jedinstvene.");
        if (Trucks.Select(t => t.Id).Distinct().Count() != Trucks.Count)
            errors.Add("Oznake kamiona nisu jedinstvene.");
        if (Trucks.Count == 0)
            errors.Add("Nema nijednog kamiona.");
        foreach (var t in Trucks)
            if (t.MaxKg <= 0 || t.MaxItems <= 0)
                errors.Add($"{t.Name}: nosivost i broj strojeva moraju biti veći od 0.");
        foreach (var m in Machines.Values)
            if (m.WeightKg <= 0)
                errors.Add($"{m.Id}: težina mora biti veća od 0.");
        foreach (var o in Orders)
        {
            if (!Machines.ContainsKey(o.MachineId))
                errors.Add($"{o.Id}: nepoznat stroj {o.MachineId}.");
            foreach (var loc in new[] { o.Pickup, o.Delivery })
                if (!Locations.ContainsKey(loc))
                    errors.Add($"{o.Id}: nepoznata lokacija {loc}.");
        }
        return errors;
    }
}
