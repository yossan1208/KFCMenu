using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

internal static class TestMenu
{
    public static MenuData Load() =>
        MenuParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "menu.json")));

    // 指定した時間帯で注文できる購入選択肢だけを残したメニュー
    public static MenuData Load(ServiceTime time)
    {
        var menu = Load();
        menu.Options = MenuTime.AvailableAt(menu.Options, time);
        return menu;
    }

    public static List<Product> WithConditions(MenuData menu, params (string Id, int Required, int MaxSurplus)[] conditions)
    {
        foreach (var product in menu.Products)
        {
            product.Required = 0;
            product.MaxSurplus = 0;
        }
        foreach (var (id, required, maxSurplus) in conditions)
        {
            var product = menu.Products.Single(p => p.Id == id);
            product.Required = required;
            product.MaxSurplus = maxSurplus;
        }
        return menu.Products;
    }
}
