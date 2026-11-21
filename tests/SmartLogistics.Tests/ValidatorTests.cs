using SmartLogistics.Core;
using static SmartLogistics.Tests.TestData;

namespace SmartLogistics.Tests;

/// <summary>Validator mora prepoznati svako kršenje poslovnih ograničenja.</summary>
public class ValidatorTests
{
    [Fact]
    public void Valid_route_starts_and_ends_at_base()
    {
        var r = Validator.SimulateRoute(Default(), TruckA, [P("N01"), D("N01")]);
        Assert.True(r.Ok, string.Join("; ", r.Violations));
        Assert.Equal("L0", r.Steps[0].LocationId);
        Assert.Equal("L0", r.Steps[^1].LocationId);
        Assert.Equal(8.5 + 14.0 + 19.2, r.Km, 6);   // L0 → L1 → L3 → L0
        Assert.Equal(1, r.OrdersDone);
    }

    [Fact]
    public void Detects_weight_overload()
    {
        // Kamion A: M02 (1300 kg) + M04 (2400 kg) = 3700 kg > 3500 kg
        var r = Validator.SimulateRoute(Default(), TruckA, [P("N02"), P("N04"), D("N02"), D("N04")]);
        Assert.Contains(r.Violations, v => v.Contains("nosivost"));
    }

    [Fact]
    public void Detects_too_many_machines()
    {
        // Kamion B: 4 stroja (6300 kg – nosivost je OK) > 3 stroja
        var r = Validator.SimulateRoute(Default(), TruckB,
            [P("N01"), P("N02"), P("N03"), P("N04"), D("N01"), D("N02"), D("N03"), D("N04")]);
        Assert.Contains(r.Violations, v => v.Contains("broj strojeva"));
        Assert.DoesNotContain(r.Violations, v => v.Contains("nosivost"));
    }

    [Fact]
    public void Detects_delivery_before_pickup()
    {
        var r = Validator.SimulateRoute(Default(), TruckA, [D("N01"), P("N01")]);
        Assert.Contains(r.Violations, v => v.Contains("prije preuzimanja"));
    }

    [Fact]
    public void Detects_machine_left_on_truck()
    {
        var r = Validator.SimulateRoute(Default(), TruckA, [P("N01")]);
        Assert.Contains(r.Violations, v => v.Contains("vratio u bazu sa strojevima"));
    }

    [Fact]
    public void Detects_shift_overrun()
    {
        var p = Make(new() { ["L1"] = (30, 0), ["L2"] = (60, 0) }, [("N1", 500, "L1", "L2")],
            settings: new Settings(MaxShiftMin: 120));
        // 30 + 30 + 60 km = 120 min vožnje + 40 min radnji = 160 min > 120 min
        var r = Validator.SimulateRoute(p, TruckA, [P("N1"), D("N1")]);
        Assert.Contains(r.Violations, v => v.Contains("radno vrijeme"));
    }
}
