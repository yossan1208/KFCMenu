namespace KfcMenu.Core.Models;

public class MenuOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Price { get; set; }
    public Dictionary<string, int> Includes { get; set; } = new();
    public List<ChoiceSlot> Choices { get; set; } = new();

    // 注文できる時間帯。"lunch" / "regular"、null ならいつでも
    public string? Availability { get; set; }
}
