using Notify.Shared;
using Xunit;

namespace Notify.Shared.Tests;

public class PriorityTests
{
    [Theory]
    [InlineData(Priority.Low, 5)]
    [InlineData(Priority.Normal, 5)]
    [InlineData(Priority.High, 10)]
    public void ToApnsPriority_MapsExpected(Priority p, int apns)
    {
        Assert.Equal(apns, p.ToApnsPriority());
    }
}
