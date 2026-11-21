using SmartLogistics.Core;

namespace SmartLogistics.Web.Components.Planner;

/// <summary>Formatiranje vrijednosti za prikaz (kultura hr-HR postavljena je u Program.cs).</summary>
public static class Fmt
{
    public static string Km(double km) => $"{km:0.0} km";

    public static string Clock(double minutes)
    {
        int total = (int)Math.Round(minutes);
        return $"{total / 60}:{total % 60:00}";
    }

    public static string Action(StepAction a) => a switch
    {
        StepAction.Start => "Polazak iz baze",
        StepAction.Pickup => "Preuzimanje",
        StepAction.Delivery => "Isporuka",
        StepAction.Return => "Povratak u bazu",
        _ => a.ToString(),
    };

    private static readonly string[] Colors = ["#2563eb", "#ea580c", "#16a34a", "#9333ea", "#0891b2"];

    /// <summary>Boja kamiona prema njegovom redoslijedu u voznom parku.</summary>
    public static string TruckColor(int index) => Colors[index % Colors.Length];
}
