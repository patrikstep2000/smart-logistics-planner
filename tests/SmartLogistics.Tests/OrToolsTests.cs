using SmartLogistics.Core;

namespace SmartLogistics.Tests;

/// <summary>Neovisna kontrola egzaktnog solvera pomoću Google OR-Tools.</summary>
public class OrToolsTests
{
    [Fact]
    public void OrTools_plan_is_valid_and_never_beats_exact_solver()
    {
        var p = TestData.Default();
        var exact = ExactSolver.Solve(p);
        var ot = OrToolsSolver.Solve(p, TimeSpan.FromSeconds(3));
        Assert.Empty(Validator.ValidatePlan(p, ot));
        Assert.Equal(p.Orders.Count, ot.OrdersDone);
        // DP je egzaktan: OR-Tools smije naći isto ili lošije, nikad bolje.
        Assert.True(ot.TotalDk >= exact.TotalDk, $"OR-Tools {ot.TotalKm} km < DP {exact.TotalKm} km");
    }

    [Fact]
    public void OrTools_agrees_when_shift_is_tight()
    {
        var d = ProblemData.Load();
        d.Settings.MaxShiftHours = 5;
        var p = d.ToProblem();
        var exact = ExactSolver.Solve(p);
        var ot = OrToolsSolver.Solve(p, TimeSpan.FromSeconds(3));
        Assert.Empty(Validator.ValidatePlan(p, ot));
        Assert.True(ot.OrdersDone < exact.OrdersDone ||
                    (ot.OrdersDone == exact.OrdersDone && ot.TotalDk >= exact.TotalDk));
    }
}
