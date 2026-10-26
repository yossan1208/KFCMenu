namespace KfcMenu.Core.Models;

public class Recommendation
{
    public List<RecommendedItem> Items { get; init; } = new();
    public int TotalPrice { get; init; }

    // 同じ中身をすべて単品価格で買った場合の合計
    public int SingleItemTotal { get; init; }

    public int Savings => SingleItemTotal - TotalPrice;

    // 商品ID → 必要数を超えて付いてくる個数
    public Dictionary<string, int> Surplus { get; init; } = new();

    // 選べる枠のラベル → 「お好きなもの」の個数
    public Dictionary<string, int> FreeChoices { get; init; } = new();
}
