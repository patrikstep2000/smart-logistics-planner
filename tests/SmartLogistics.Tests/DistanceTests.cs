using SmartLogistics.Core;

namespace SmartLogistics.Tests;

public class DistanceTests
{
    [Fact]
    public void Euclidean_distance_is_rounded_to_one_decimal()
    {
        var loc = TestData.Default().Locations;
        Assert.Equal(8.5, Distance.LegKm(loc["L0"], loc["L1"]));    // √73  = 8.544
        Assert.Equal(14.0, Distance.LegKm(loc["L1"], loc["L3"]));   // √197 = 14.036
        Assert.Equal(12.5, Distance.LegKm(loc["L5"], loc["L7"]));   // √157 = 12.530
    }

    [Fact]
    public void Distance_is_symmetric_and_zero_on_same_location()
    {
        var loc = TestData.Default().Locations.Values.ToList();
        foreach (var a in loc)
        {
            Assert.Equal(0, Distance.LegDk(a, a));
            foreach (var b in loc) Assert.Equal(Distance.LegDk(a, b), Distance.LegDk(b, a));
        }
    }

    [Fact]
    public void Route_minutes_count_driving_and_handling()
    {
        // 60 km pri 60 km/h = 60 min, plus 4 radnje po 20 min
        Assert.Equal(60 + 80, Distance.RouteMinutes(600, 4, new Settings()));
    }
}
