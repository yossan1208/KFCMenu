using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

public class SetRecommenderTests
{
    private readonly MenuData menu = TestMenu.Load();

    private List<Recommendation> Recommend(params (string Id, int Required)[] conditions)
    {
        var products = TestMenu.WithConditions(menu, conditions.Select(c => (c.Id, c.Required, 0)).ToArray());
        var cheapest = CheapestComboFinder.FindCheapest(VariantExpander.Expand(menu.Options), products);
        return SetRecommender.Recommend(menu.Options, products, cheapest);
    }

    // 例: "4パック[crispy,-]x2"（- はお好きなもの）
    private static List<string> Describe(Recommendation recommendation) =>
        recommendation.Items
            .Select(i => i.Picks.Count == 0
                ? $"{i.Option.Name}x{i.Count}"
                : $"{i.Option.Name}[{string.Join(",", i.Picks.Select(p => p ?? "-"))}]x{i.Count}")
            .ToList();

    [Fact]
    public void Chicken8_PacksFirstThenSets()
    {
        var result = Recommend(("chicken", 8));

        Assert.Equal(3, result.Count);

        Assert.Equal(["8パック[-,-,-,-]x1"], Describe(result[0]));
        Assert.Equal(2990, result[0].TotalPrice);
        Assert.Equal(3800, result[0].SingleItemTotal);
        Assert.Equal(810, result[0].Savings);
        Assert.Empty(result[0].Surplus);
        Assert.Equal(new Dictionary<string, int> { ["サイド"] = 4 }, result[0].FreeChoices);

        Assert.Equal(["4パック[-,-]x2"], Describe(result[1]));
        Assert.Equal(3180, result[1].TotalPrice);

        Assert.Equal(["2セット[-]x1", "3セット[-]x2"], Describe(result[2]));
        Assert.Equal(3270, result[2].TotalPrice);
        Assert.Equal(1050, result[2].Savings);
        Assert.Equal(new Dictionary<string, int> { ["potato_s"] = 3 }, result[2].Surplus);
        Assert.Equal(new Dictionary<string, int> { ["ドリンク"] = 3 }, result[2].FreeChoices);
    }

    [Fact]
    public void Chicken2_PriceCapKeepsOnlyCheapSets()
    {
        var result = Recommend(("chicken", 2));

        Assert.Equal(3, result.Count);
        Assert.Equal(["2セット[-]x1"], Describe(result[0]));
        Assert.Equal(890, result[0].TotalPrice);
        Assert.Equal(330, result[0].Savings);
        Assert.Equal(new Dictionary<string, int> { ["potato_s"] = 1 }, result[0].Surplus);
        Assert.Equal(["単x1", "チキビス[-]x1"], Describe(result[1]));
        Assert.Equal(880, result[1].TotalPrice);
        Assert.Equal(["単x1", "キッズチキン[-]x1"], Describe(result[2]));
        Assert.Equal(920, result[2].TotalPrice);
    }

    [Fact]
    public void SetCanBeCheaperThanTheCheapestExactMatch()
    {
        var result = Recommend(("chicken", 1), ("potato_s", 1), ("biscuit", 1));

        Assert.Equal(3, result.Count);
        Assert.Equal(["チキビス[-]x1"], Describe(result[0]));
        Assert.Equal(550, result[0].TotalPrice);
        Assert.Equal(630, result[0].Savings);
        Assert.Empty(result[0].Surplus);
        Assert.Equal(["ビスケットx1", "キッズチキン[-]x1"], Describe(result[1]));
        Assert.Equal(880, result[1].TotalPrice);
        Assert.Equal(["2セット[-]x1", "ビスケットx1"], Describe(result[2]));
        Assert.Equal(1180, result[2].TotalPrice);
        Assert.Equal(new Dictionary<string, int> { ["chicken"] = 1 }, result[2].Surplus);
    }

    [Fact]
    public void ExcludesTheCheapestCombination()
    {
        // 最安は 2セット（890円）なので、おすすめには出さない
        var result = Recommend(("chicken", 2), ("potato_s", 1), ("drink_s", 1));

        // 2つ目のドリンクは頼んでいないので「お好きなドリンク」になる
        Assert.Equal(3, result.Count);
        Assert.Equal(["チキビス[-]x1", "チキビス[drink_s]x1"], Describe(result[0]));
        Assert.Equal(1100, result[0].TotalPrice);
        Assert.Equal(new Dictionary<string, int> { ["ドリンク"] = 1 }, result[0].FreeChoices);
        Assert.Equal(1140, result[1].TotalPrice);
        Assert.Equal(1180, result[2].TotalPrice);
    }

    [Fact]
    public void RequestedDrinkFillsTheSetsDrinkSlot()
    {
        var result = Recommend(("burger", 2), ("drink_m", 2));

        Assert.Equal(3, result.Count);
        Assert.Equal(["セット[drink_m]x2"], Describe(result[0]));
        Assert.Equal(1380, result[0].TotalPrice);
        Assert.Equal(new Dictionary<string, int> { ["potato_s"] = 2 }, result[0].Surplus);
        Assert.Equal(["セット[drink_m]x1", "欲張りバーガー[drink_m]x1"], Describe(result[1]));
        Assert.Equal(1680, result[1].TotalPrice);
        Assert.Equal(["コンビx1", "セット[drink_m]x1", "チキビス[drink_m]x1"], Describe(result[2]));
        Assert.Equal(1790, result[2].TotalPrice);
    }

    [Fact]
    public void FewerSinglesComeFirstEvenWhenPricier()
    {
        var result = Recommend(("nugget", 3));

        Assert.Equal(
            [["キッズナゲット[-]x3"], ["ナゲットx1", "キッズナゲット[-]x2"], ["ナゲットx2", "キッズナゲット[-]x1"]],
            result.Select(Describe));
        Assert.Equal([1770, 1660, 1550], result.Select(r => r.TotalPrice));
    }

    [Fact]
    public void NothingRequired_ReturnsEmpty()
    {
        Assert.Empty(Recommend());
    }

    [Fact]
    public void NoSetUnderThePriceCap_ReturnsEmpty()
    {
        // 最安 580円 → 上限 870円。クリスピーが入るセットは 4パック（1,590円）以上しかない
        Assert.Empty(Recommend(("crispy", 2)));
    }

    [Fact]
    public void NoExactCheapest_RecommendsWithoutPriceCap()
    {
        // a を1個だけ買う方法はない（ペアしかない）ので最安は null。上限なしでペアを勧める
        var products = new List<Product> { new() { Id = "a", Required = 1 } };
        var pair = new MenuOption { Id = "pair", Name = "ペア", Price = 100, Includes = new() { ["a"] = 2 } };

        var cheapest = CheapestComboFinder.FindCheapest(VariantExpander.Expand([pair]), products);
        var result = SetRecommender.Recommend([pair], products, cheapest);

        Assert.Null(cheapest);
        var recommendation = Assert.Single(result);
        Assert.Equal(["ペアx1"], Describe(recommendation));
        Assert.Equal(new Dictionary<string, int> { ["a"] = 1 }, recommendation.Surplus);
    }

    [Fact]
    public void DropsPlansWithARemovableItem()
    {
        var products = new List<Product> { new() { Id = "a", Required = 3 } };
        var single = new MenuOption { Id = "single", Name = "単品", Price = 60, Includes = new() { ["a"] = 1 } };
        var pair = new MenuOption { Id = "pair", Name = "ペア", Price = 100, Includes = new() { ["a"] = 2 } };
        var cheapest = CheapestComboFinder.FindCheapest(VariantExpander.Expand([single, pair]), products);

        var result = SetRecommender.Recommend([single, pair], products, cheapest);

        // 最安は ペア + 単品（160円）。ペア×2（200円）は残り、ペア×2 + 単品 のような無駄のある案は出ない
        Assert.Equal(160, cheapest!.TotalPrice);
        var recommendation = Assert.Single(result);
        Assert.Equal(["ペアx2"], Describe(recommendation));
        Assert.Equal(200, recommendation.TotalPrice);
    }
}
