namespace KfcMenu.Core.Models;

public class Variant
{
    public required MenuOption Option { get; init; }
    public List<string> Picks { get; init; } = new();
    public Dictionary<string, int> Contents { get; init; } = new();
}
