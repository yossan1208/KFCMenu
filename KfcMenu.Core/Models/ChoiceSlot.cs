namespace KfcMenu.Core.Models;

public class ChoiceSlot
{
    public int Count { get; set; }
    public List<string> From { get; set; } = new();
    public bool AllowDuplicate { get; set; }
    public string Label { get; set; } = "";
}
