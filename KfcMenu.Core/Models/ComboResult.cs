namespace KfcMenu.Core.Models;

public class ComboResult
{
    public List<(Variant Variant, int Count)> Items { get; init; } = new();
    public int TotalPrice { get; init; }
}
