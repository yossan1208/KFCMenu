using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

internal static class TestMenu
{
    public static MenuData Load() =>
        MenuParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "menu.json")));

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
