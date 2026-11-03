using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

public class SetRecommenderTests
{
    private readonly MenuData lunch = TestMenu.Load(ServiceTime.Lunch);
    private readonly MenuData regular = TestMenu.Load(ServiceTime.Regular);

    private List<Recommendation> Recommend(params (string Id, int Required)[] conditions) =>
        RecommendAndCheck(lunch, conditions).Result;

    private List<Recommendation> RecommendRegular(params (string Id, int Required)[] conditions) =>
        RecommendAndCheck(regular, conditions).Result;

    private (List<Recommendation> Result, ComboResult? Cheapest) RecommendAndCheck(params (string Id, int Required)[] conditions) =>
        RecommendAndCheck(lunch, conditions);

    // おすすめを求め、どれも条件1〜5を満たすことを確かめる
    private static (List<Recommendation> Result, ComboResult? Cheapest) RecommendAndCheck(MenuData menu, params (string Id, int Required)[] conditions)
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

    // 例: "4ピースパック[crispy,-]x2"（- はお好きなもの）
    private static List<string> Describe(Recommendation recommendation) =>
        recommendation.Items
            .Select(i => i.Picks.Count == 0
                ? $"{i.Option.Name}x{i.Count}"
                : $"{i.Option.Name}[{string.Join(",", i.Picks.Select(p => p ?? "-"))}]x{i.Count}")
            .ToList();

    // 期待値は、途中で候補を絞らない総当たりと照合したもの。単品の個数と金額が同じ候補が複数あるところは、どれが出てもよいので金額と単品の個数だけを確かめる

    [Fact]
    public void Chicken8_PacksFirst()
    {
        var result = Recommend(("chicken", 8));

        Assert.Equal(3, result.Count);

        Assert.Equal(["8ピースパック[-,-,-,-]x1"], Describe(result[0]));
        Assert.Equal(2990, result[0].TotalPrice);
        Assert.Equal(3800, result[0].SingleItemTotal);
        Assert.Equal(810, result[0].Savings);
        Assert.Empty(result[0].Surplus);
        Assert.Equal(new Dictionary<string, int> { ["サイド"] = 4 }, result[0].FreeChoices);

        Assert.Equal(["4ピースパック[-,-]x2"], Describe(result[1]));
        Assert.Equal(3180, result[1].TotalPrice);

        // 10ピースパックは単品10本と同額なので、お得額は0、チキンが2本余る
        Assert.Equal(["10ピースパックx1"], Describe(result[2]));
        Assert.Equal(3300, result[2].TotalPrice);
        Assert.Equal(0, result[2].Savings);
        Assert.Equal(new Dictionary<string, int> { ["chicken"] = 2 }, result[2].Surplus);
        Assert.Empty(result[2].FreeChoices);
    }

    [Fact]
    public void Chicken2_PriceCapKeepsOnlyCheapSets()
    {
        var result = Recommend(("chicken", 2));

        Assert.Equal(3, result.Count);
        Assert.Equal(["チキン2ピースセット[-]x1"], Describe(result[0]));
        Assert.Equal(940, result[0].TotalPrice);
        Assert.Equal(280, result[0].Savings);
        Assert.Equal(new Dictionary<string, int> { ["potato_s"] = 1 }, result[0].Surplus);
        Assert.Equal([1, 1], result.Skip(1).Select(Singles));
        Assert.Equal([920, 920], result.Skip(1).Select(r => r.TotalPrice));
        Assert.Equal(
            ["chicken_biscuit_set_lunchx1,chicken_singlex1", "chicken_singlex1,kids_chickenx1"],
            result.Skip(1).Select(Options).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SetCanBeCheaperThanTheCheapestExactMatch()
    {
        var result = Recommend(("chicken", 1), ("potato_s", 1), ("biscuit", 1));

        Assert.Equal(3, result.Count);
        Assert.Equal(["チキン&ビスケットセット[-]x1"], Describe(result[0]));
        Assert.Equal(590, result[0].TotalPrice);
        Assert.Equal(590, result[0].Savings);
        Assert.Empty(result[0].Surplus);
        Assert.Equal(["ビスケットx1", "キッズチキン[-]x1"], Describe(result[1]));
        Assert.Equal(880, result[1].TotalPrice);
        Assert.Equal(["チキン2ピースセット[-]x1", "ビスケットx1"], Describe(result[2]));
        Assert.Equal(1230, result[2].TotalPrice);
        Assert.Equal(new Dictionary<string, int> { ["chicken"] = 1 }, result[2].Surplus);
    }

    [Fact]
    public void ExcludesTheCheapestCombination()
    {
        // 最安は バーガーセット（790円）なので、おすすめには出さない
        var (result, cheapest) = RecommendAndCheck(("burger", 1), ("potato_s", 1), ("drink_s", 1));

        Assert.Equal(790, cheapest!.TotalPrice);
        Assert.DoesNotContain(result, r => Options(r) == "burger_set_lunchx1");
        Assert.Equal(3, result.Count);
        Assert.Equal(["欲張りバーガーセット[drink_s]x1"], Describe(result[0]));
        Assert.Equal([1090, 1180, 1180], result.Select(r => r.TotalPrice));
    }

    [Fact]
    public void RequestedDrinkFillsTheSetsDrinkSlot()
    {
        var result = Recommend(("burger", 2), ("drink_m", 2));

        Assert.Equal(3, result.Count);
        Assert.Equal(["バーガーセット[drink_m]x2"], Describe(result[0]));
        Assert.Equal(1580, result[0].TotalPrice);
        Assert.Equal(new Dictionary<string, int> { ["potato_s"] = 2 }, result[0].Surplus);
        Assert.Equal(["バーガーセット[drink_m]x1", "欲張りバーガーセット[drink_m]x1"], Describe(result[1]));
        Assert.Equal(1880, result[1].TotalPrice);
        Assert.Equal(1970, result[2].TotalPrice);
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
        // 最安 580円 → 上限 870円。クリスピーが入るセットは 4ピースパック（1,590円）以上しかない
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
        // 総当たりでの上位: 単品1つの 3,220円 が15通り。キッズナゲット + ドリンク M×3 + ドリンク S×2（2,000円、最安より安い）も
        // 条件を満たすが単品が5つなので上位3つに入らない
        var (result, cheapest) = RecommendAndCheck(("drink_m", 4), ("drink_s", 2), ("nugget", 1));

        Assert.Equal(2180, cheapest!.TotalPrice);
        Assert.Equal([1, 1, 1], result.Select(Singles));
        Assert.Equal([3220, 3220, 3220], result.Select(r => r.TotalPrice));
    }

    [Fact]
    public void DrinksOnly_FewestSinglesFirst()
    {
        var (result, _) = RecommendAndCheck(("drink_s", 4), ("drink_m", 3));

        Assert.Equal([4, 4, 4], result.Select(Singles));
        Assert.Equal([2850, 2850, 2850], result.Select(r => r.TotalPrice));
    }

    [Fact]
    public void NoSingleThatAFreeDrinkSeatCouldCover()
    {
        // ドリンク S + キッズナゲット（お好きなドリンク）はドリンクの席でSを選べば ドリンク S が要らない。
        // 残るキッズナゲット（S）は最安そのもので、ほかは上限 885円 を超える
        Assert.Empty(Recommend(("potato_s", 1), ("drink_s", 1), ("nugget", 1)));
    }

    [Fact]
    public void NoSingleThatAFreeSideSeatCouldCover()
    {
        var result = Recommend(("chicken", 3), ("crispy", 2));

        // 4ピースパック（クリスピー, お好きなサイド）+ クリスピー は出ない
        Assert.Equal(["4ピースパック[crispy,crispy]x1"], Describe(result[0]));
        Assert.Equal(["チキン3ピースセット[-]x1", "クリスピーx2"], Describe(result[1]));
        Assert.Equal([1590, 1820, 2110], result.Select(r => r.TotalPrice));
    }

    [Fact]
    public void NoSingleThatAChickenSetsFreeSeatCouldCover()
    {
        // チキン&ビスケットセット（お好きなドリンク）+ ドリンク S、ドリンク S + キッズチキン（お好きなドリンク）は出ない。
        // 最安は キッズチキン（S）590円で、同額のチキン&ビスケットセット（S）が残る
        var recommendation = Assert.Single(Recommend(("chicken", 1), ("potato_s", 1), ("drink_s", 1)));

        Assert.Equal(["チキン&ビスケットセット[drink_s]x1"], Describe(recommendation));
        Assert.Equal(590, recommendation.TotalPrice);
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
        foreach (var menu in new[] { lunch, regular })
        {
            for (var t = 0; t < 200; t++)
            {
                var order = pool.OrderBy(_ => random.Next()).Take(random.Next(1, 5))
                    .Select(id => (id, random.Next(1, 5))).ToArray();

                var (result, cheapest) = RecommendAndCheck(menu, order);

                // 「セット1つ + 残りを単品」で条件1〜5を満たす買い方が m 通りあれば、上位3つは少なくとも min(3, m) 件ある
                var available = RecommendationChecker.ValidOneSetPlusSingles(menu.Options, menu.Products, cheapest).Count;
                var label = string.Join(",", order.Select(o => $"{o.id}{o.Item2}"));
                Assert.True(result.Count >= Math.Min(3, available), $"{label}: {result.Count} 件、セット1つ + 単品 の有効な買い方 {available} 通り");
            }
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

    [Fact]
    public void Regular_UsesRegularSetPrices()
    {
        var result = RecommendRegular(("chicken", 2), ("potato_s", 1), ("drink_s", 1));

        Assert.Equal(
            ["chicken2_set_regularx1", "kids_chickenx2", "chicken3_set_regularx1"],
            result.Select(Options));
        Assert.Equal([1020, 1180, 1320], result.Select(r => r.TotalPrice));
    }

    [Fact]
    public void Regular_NoLunchOnlySets()
    {
        // ランチならチキン&ビスケットセット（590円）が1番目。通常時間は注文できない
        var result = RecommendRegular(("chicken", 1), ("potato_s", 1), ("biscuit", 1));

        Assert.DoesNotContain(result, r => r.Items.Any(i => i.Option.Availability == "lunch"));
        Assert.Equal(["ビスケットx1", "キッズチキン[-]x1"], Describe(result[0]));
        Assert.Equal([880, 1310, 1210], result.Select(r => r.TotalPrice));
    }

    [Fact]
    public void Regular_TwoPieceSetIsOverTheCap()
    {
        // 最安 660円 → 上限 990円。通常のチキン2ピースセット（1,020円）は上限を超える
        var recommendation = Assert.Single(RecommendRegular(("chicken", 2)));

        Assert.Equal(["単品チキンx1", "キッズチキン[-]x1"], Describe(recommendation));
        Assert.Equal(920, recommendation.TotalPrice);
    }
}
