namespace SmartLogistics.Core;

/// <summary>
/// Udaljenosti i vremena.
///
/// Svaka dionica je euklidska udaljenost (ravna crta, Pitagora) zaokružena na jednu
/// decimalu. Interno se udaljenosti drže kao cijeli broj desetinki kilometra ("dk"):
/// 8.5 km = 85 dk. Tako zbrajanje dionica nema grešaka s decimalnim brojevima.
/// </summary>
public static class Distance
{
    /// <summary>Udaljenost dionice u desetinkama km, zaokruženo na 0.1 km (half-up).</summary>
    public static int LegDk(Location a, Location b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return (int)Math.Floor(Math.Sqrt(dx * dx + dy * dy) * 10 + 0.5);
    }

    public static double LegKm(Location a, Location b) => LegDk(a, b) / 10.0;

    public static double DriveMinutes(int dk, Settings s) => dk / 10.0 / s.SpeedKmh * 60;

    /// <summary>Ukupno radno vrijeme rute: vožnja + utovari/istovari.</summary>
    public static double RouteMinutes(int dk, int actions, Settings s) => DriveMinutes(dk, s) + actions * s.HandlingMin;

    public static bool FitsShift(int dk, int actions, Settings s) => RouteMinutes(dk, actions, s) <= s.MaxShiftMin + 1e-9;
}
