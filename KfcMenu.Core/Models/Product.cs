namespace KfcMenu.Core.Models;

public class Product
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Required { get; set; }
    public int MaxSurplus { get; set; }
}
