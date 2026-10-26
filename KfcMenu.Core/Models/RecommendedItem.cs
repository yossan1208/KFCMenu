namespace KfcMenu.Core.Models;

public class RecommendedItem
{
    public required MenuOption Option { get; init; }

    // 選べる枠の順に並べた商品ID。null は「お好きなもの」（頼んだ商品として数えない席）
    public List<string?> Picks { get; init; } = new();

    public int Count { get; init; }
}
