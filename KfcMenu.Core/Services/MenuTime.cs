using KfcMenu.Core.Models;

namespace KfcMenu.Core.Services;

public static class MenuTime
{
    // menu.json の availability に書く値
    public const string LunchValue = "lunch";
    public const string RegularValue = "regular";

    public static readonly TimeOnly LunchStart = new(10, 0);
    public static readonly TimeOnly LunchEnd = new(15, 0);

    // 10:00 以上 15:00 未満がランチ
    public static ServiceTime At(TimeOnly time) =>
        time >= LunchStart && time < LunchEnd ? ServiceTime.Lunch : ServiceTime.Regular;

    public static List<MenuOption> AvailableAt(IEnumerable<MenuOption> options, ServiceTime time)
    {
        var value = time == ServiceTime.Lunch ? LunchValue : RegularValue;
        return options.Where(o => o.Availability is null || o.Availability == value).ToList();
    }
}
