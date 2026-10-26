namespace KfcMenu.Core.Models;

public class MenuOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Price { get; set; }
    public Dictionary<string, int> Includes { get; set; } = new();
    public List<ChoiceSlot> Choices { get; set; } = new();
}
