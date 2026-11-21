using System.Text.Json;

namespace SmartLogistics.Core;

/// <summary>
/// Ulazni podaci u obliku koji odgovara JSON datoteci (data/default.json).
/// Klase su promjenjive (get/set) kako bi ih web sučelje moglo izravno uređivati;
/// <see cref="ToProblem"/> ih pretvara u nepromjenjivi <see cref="Problem"/>.
/// </summary>
public sealed class ProblemData
{
    public string Base { get; set; } = "L0";
    public SettingsData Settings { get; set; } = new();
    public List<TruckData> Trucks { get; set; } = [];
    public List<MachineData> Machines { get; set; } = [];
    public List<LocationData> Locations { get; set; } = [];
    public List<OrderData> Orders { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "data", "default.json");

    public static ProblemData Load(string? path = null) =>
        JsonSerializer.Deserialize<ProblemData>(File.ReadAllText(path ?? DefaultPath), Json)
        ?? throw new InvalidDataException("Prazna JSON datoteka.");

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public ProblemData Clone() => JsonSerializer.Deserialize<ProblemData>(ToJson(), Json)!;

    public Problem ToProblem()
    {
        static string Req(string? value, string what) =>
            string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"Nedostaje {what}.") : value.Trim();

        return new Problem
        {
            Base = Base,
            Settings = new Settings(Settings.SpeedKmh, Settings.HandlingMin, Settings.MaxShiftHours * 60),
            Trucks = Trucks.Select(t => new Truck(Req(t.Id, "oznaka kamiona"),
                string.IsNullOrWhiteSpace(t.Name) ? t.Id! : t.Name, t.MaxKg, t.MaxItems)).ToList(),
            Machines = Machines.Select(m => new Machine(Req(m.Id, "oznaka stroja"), m.WeightKg))
                .GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.Last()),
            Locations = Locations.Select(l => new Location(Req(l.Id, "oznaka lokacije"),
                    string.IsNullOrWhiteSpace(l.Name) ? l.Id! : l.Name, l.X, l.Y))
                .GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.Last()),
            Orders = Orders.Select(o => new Order(Req(o.Id, "oznaka naloga"), Req(o.MachineId, "stroj naloga"),
                Req(o.Pickup, "lokacija preuzimanja"), Req(o.Delivery, "lokacija isporuke"))).ToList(),
        };
    }
}

public sealed class SettingsData
{
    public double SpeedKmh { get; set; } = 60;
    public double HandlingMin { get; set; } = 20;
    public double MaxShiftHours { get; set; } = 9;
}

public sealed class TruckData
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public int MaxKg { get; set; }
    public int MaxItems { get; set; }
}

public sealed class MachineData
{
    public string? Id { get; set; }
    public int WeightKg { get; set; }
}

public sealed class LocationData
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class OrderData
{
    public string? Id { get; set; }
    public string? MachineId { get; set; }
    public string? Pickup { get; set; }
    public string? Delivery { get; set; }
}
