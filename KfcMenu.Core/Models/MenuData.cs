namespace KfcMenu.Core.Models;

public class MenuData
{
    public List<Product> Products { get; set; } = new();
    public List<MenuOption> Options { get; set; } = new();
}
