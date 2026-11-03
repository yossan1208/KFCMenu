using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

public class MenuTimeTests
{
    [Theory]
    [InlineData(0, 0, ServiceTime.Regular)]
    [InlineData(9, 59, ServiceTime.Regular)]
    [InlineData(10, 0, ServiceTime.Lunch)]
    [InlineData(12, 30, ServiceTime.Lunch)]
    [InlineData(14, 59, ServiceTime.Lunch)]
    [InlineData(15, 0, ServiceTime.Regular)]
    [InlineData(23, 59, ServiceTime.Regular)]
    public void At_LunchIsFromTenToBeforeFifteen(int hour, int minute, ServiceTime expected)
    {
        Assert.Equal(expected, MenuTime.At(new TimeOnly(hour, minute)));
    }

    [Fact]
    public void At_OneSecondBeforeFifteenIsStillLunch()
    {
        Assert.Equal(ServiceTime.Lunch, MenuTime.At(new TimeOnly(14, 59, 59)));
    }

    [Fact]
    public void AvailableAt_KeepsAnytimeAndMatchingOptions()
    {
        var any = new MenuOption { Id = "any" };
        var lunch = new MenuOption { Id = "lunch", Availability = "lunch" };
        var regular = new MenuOption { Id = "regular", Availability = "regular" };

        Assert.Equal(["any", "lunch"], MenuTime.AvailableAt([any, lunch, regular], ServiceTime.Lunch).Select(o => o.Id));
        Assert.Equal(["any", "regular"], MenuTime.AvailableAt([any, lunch, regular], ServiceTime.Regular).Select(o => o.Id));
    }

    [Fact]
    public void AvailableAt_RealMenu()
    {
        var lunch = TestMenu.Load(ServiceTime.Lunch).Options;
        var regular = TestMenu.Load(ServiceTime.Regular).Options;

        Assert.Equal(30, lunch.Count);
        Assert.Equal(28, regular.Count);

        Assert.Contains(lunch, o => o.Name == "バーガーコンビ");
        Assert.Contains(lunch, o => o.Name == "チキン&ビスケットセット");
        Assert.DoesNotContain(regular, o => o.Name == "バーガーコンビ");
        Assert.DoesNotContain(regular, o => o.Name == "チキン&ビスケットセット");

        Assert.Equal(790, lunch.Single(o => o.Name == "バーガーセット").Price);
        Assert.Equal(890, regular.Single(o => o.Name == "バーガーセット").Price);
        Assert.Equal(590, regular.Single(o => o.Name == "キッズチキン").Price);
    }
}
