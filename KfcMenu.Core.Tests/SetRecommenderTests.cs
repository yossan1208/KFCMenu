using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

public class SetRecommenderTests
{
    private readonly MenuData menu = TestMenu.Load();

    private List<Recommendation> Recommend(params (string Id, int Required)[] conditions) =>
        RecommendAndCheck(conditions).Result;

    // おすすめを求め、どれも条件1〜5を満たすことを確かめる
    private (List<Recommendation> Result, ComboResult? Cheapest) RecommendAndCheck(params (string Id, int Required)[] conditions)
    {
        var products = TestMenu.WithConditions(menu, conditions.Select(c => (c.Id, c.Required, 0)).ToArray());
        var cheapest = CheapestComboFinder.FindCheapest(VariantExpander.Expand(menu.Options), products);
        var result = SetRecommender.Recommend(menu.Options, products, cheapest);
        foreach (var recommendation in result)
        {
            var problem = RecommendationChecker.Problem(recommendation, products, cheapest);
            Assert.True(problem is null, $"{string.Join(" + ", Describe(recommendation))}: {problem}");
        }
        return (result, cheapest);
    }

    private static string Options(Recommendation recommendation) =>
        RecommendationChecker.Signature(recommendation.Items.Select(i => (i.Option, i.Count)));

    private static int Singles(Recommendation recommendation) =>
        recommendation.Items.Where(i => RecommendationChecker.ItemCount(i.Option) == 1).Sum(i => i.Count);

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

    // 状態ごとに（単品の個数, 金額）順の上位だけを残していたころ「おすすめはありません」になっていた注文
    public static TheoryData<(string Id, int Required)[]> FormerlyEmptyOrders => new()
    {
        new[] { ("drink_m", 4), ("drink_s", 2), ("nugget", 1) },
        new[] { ("drink_s", 4), ("drink_m", 3) },
        new[] { ("chicken", 1), ("crispy", 4), ("potato_s", 3), ("drink_s", 3) },
        new[] { ("chicken", 12), ("crispy", 6), ("chocopie", 6), ("coleslaw", 6) },
        new[] { ("drink_s", 99) },
    };

    [Theory]
    [MemberData(nameof(FormerlyEmptyOrders))]
    public void OrdinaryOrdersGetValidRecommendations((string Id, int Required)[] order)
    {
        var (result, _) = RecommendAndCheck(order);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void DrinksAndNugget_FewestSinglesFirst()
    {
        // 総当たり（選べる枠の選び直しを考えた条件3）での上位: 単品1つの 3,060円 / 3,080円 / 3,100円。
        // キッズナゲット + M×3 + S×2（2,000円、最安より安い）も条件を満たすが単品が5つなので上位3つに入らない
        var (result, cheapest) = RecommendAndCheck(("drink_m", 4), ("drink_s", 2), ("nugget", 1));

        Assert.Equal(2180, cheapest!.TotalPrice);
        Assert.Equal([1, 1, 1], result.Select(Singles));
        Assert.Equal([3060, 3080, 3100], result.Select(r => r.TotalPrice));
        Assert.Equal("chickibisx4,drink_s_singlex1,kids_nuggetx1", Options(result[0]));
        Assert.Equal("chickibisx4,drink_m_singlex1,kids_nuggetx1", Options(result[1]));
    }

    [Fact]
    public void DrinksOnly_FewestSinglesFirst()
    {
        var (result, _) = RecommendAndCheck(("drink_s", 4), ("drink_m", 3));

        Assert.Equal([4, 4, 4], result.Select(Singles));
        Assert.Equal([2730, 2750, 2770], result.Select(r => r.TotalPrice));
        Assert.Equal("chickibisx3,drink_s_singlex4", Options(result[0]));
        Assert.Equal("chickibisx3,drink_m_singlex1,drink_s_singlex3", Options(result[1]));
    }

    [Fact]
    public void NoSingleThatAFreeDrinkSeatCouldCover()
    {
        // S + キッズナゲット（お好きなドリンク）はドリンクの席でSを選べば S が要らない。
        // 残るキッズナゲット（S）は最安そのもので、ほかは上限 885円 を超える
        Assert.Empty(Recommend(("potato_s", 1), ("drink_s", 1), ("nugget", 1)));
    }

    [Fact]
    public void NoSingleThatAFreeSideSeatCouldCover()
    {
        var result = Recommend(("chicken", 3), ("crispy", 2));

        // 4パック（クリスピー, お好きなサイド）+ クリスピー は出ない
        Assert.Equal(
            [["4パック[crispy,crispy]x1"], ["3セット[-]x1", "クリスピーx2"], ["チキビス[-]x1", "2セット[-]x1", "クリスピーx2"]],
            result.Select(Describe));
        Assert.Equal([1590, 1770, 2020], result.Select(r => r.TotalPrice));
    }

    [Fact]
    public void NoSingleThatAChickenSetsFreeSeatCouldCover()
    {
        // チキビス（お好きなドリンク）+ S、S + キッズチキン（お好きなドリンク）は出ない
        var recommendation = Assert.Single(Recommend(("chicken", 1), ("potato_s", 1), ("drink_s", 1)));

        Assert.Equal(["チキビス[drink_s]x1"], Describe(recommendation));
        Assert.Equal(550, recommendation.TotalPrice);
    }

    [Fact]
    public void FlexibleMinimality_OnASmallMenu()
    {
        // セット = a + 枠1つ（b か c）。単品b。a1・b1 なら セット[b] だけ。セット[お好き] + 単品b は無駄
        var products = new List<Product> { new() { Id = "a", Required = 1 }, new() { Id = "b", Required = 1 } };
        var set = new MenuOption
        {
            Id = "set", Name = "セット", Price = 100, Includes = new() { ["a"] = 1 },
            Choices = [new ChoiceSlot { Count = 1, From = ["b", "c"], AllowDuplicate = false, Label = "枠" }],
        };
        var singleB = new MenuOption { Id = "b1", Name = "単品b", Price = 10, Includes = new() { ["b"] = 1 } };

        var result = SetRecommender.Recommend([set, singleB], products, null);

        var recommendation = Assert.Single(result);
        Assert.Equal(["セット[b]x1"], Describe(recommendation));
    }

    [Fact]
    public void RandomSmallOrders_AlwaysValidAndNeverFalselyEmpty()
    {
        string[] pool = ["chicken", "burger", "twister", "potato_s", "drink_s", "drink_m", "biscuit", "nugget", "crispy", "coleslaw", "chocopie"];
        var random = new Random(20261024);
        for (var t = 0; t < 200; t++)
        {
            var order = pool.OrderBy(_ => random.Next()).Take(random.Next(1, 5))
                .Select(id => (id, random.Next(1, 5))).ToArray();

            var (result, cheapest) = RecommendAndCheck(order);

            // 「セット1つ + 残りを単品」で条件1〜5を満たす買い方が m 通りあれば、上位3つは少なくとも min(3, m) 件ある
            var available = RecommendationChecker.ValidOneSetPlusSingles(menu.Options, menu.Products, cheapest).Count;
            var label = string.Join(",", order.Select(o => $"{o.id}{o.Item2}"));
            Assert.True(result.Count >= Math.Min(3, available), $"{label}: {result.Count} 件、セット1つ + 単品 の有効な買い方 {available} 通り");
        }
    }

    [Fact]
    public void TooManyProductsForTheStateKey_Throws()
    {
        // 必要数99が20種類だと状態のキー（100^20）が UInt128 に収まらない。黙って衝突させずに例外にする
        var products = Enumerable.Range(0, 20).Select(i => new Product { Id = $"p{i}", Required = 99 }).ToList();
        var options = products
            .Select(p => new MenuOption { Id = $"{p.Id}_pair", Name = p.Id, Price = 100, Includes = new() { [p.Id] = 2 } })
            .ToList();

        Assert.Throws<OverflowException>(() => SetRecommender.Recommend(options, products, null));
    }
}
