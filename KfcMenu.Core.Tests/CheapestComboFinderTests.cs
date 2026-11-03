using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

public class CheapestComboFinderTests
{
    private readonly MenuData lunch = TestMenu.Load(ServiceTime.Lunch);
    private readonly MenuData regular = TestMenu.Load(ServiceTime.Regular);

    private ComboResult? Find(params (string Id, int Required, int MaxSurplus)[] conditions) =>
        Find(lunch, conditions);

    private static ComboResult? Find(MenuData menu, params (string Id, int Required, int MaxSurplus)[] conditions) =>
        CheapestComboFinder.FindCheapest(VariantExpander.Expand(menu.Options), TestMenu.WithConditions(menu, conditions));

    private static List<string> Describe(ComboResult result) =>
        result.Items
            .Select(i => $"{i.Variant.Option.Name}x{i.Count}[{string.Join(",", i.Variant.Picks)}]")
            .Order()
            .ToList();

    [Fact]
    public void ChickenOnly_SinglesAreCheaperThanPacksWithUnwantedSides()
    {
        var result = Find(("chicken", 8, 0));

        Assert.NotNull(result);
        Assert.Equal(2640, result.TotalPrice);
        Assert.Equal(["単品チキンx8[]"], Describe(result));
    }

    [Fact]
    public void ExactSetMatch()
    {
        var result = Find(("burger", 1, 0), ("potato_s", 1, 0), ("drink_s", 1, 0));

        Assert.NotNull(result);
        Assert.Equal(790, result.TotalPrice);
        Assert.Equal(["バーガーセットx1[drink_s]"], Describe(result));
    }

    [Fact]
    public void KidsSetPlusSingleBeatsTheTwoPieceSet()
    {
        // チキン2ピースセット（ランチ940円）より、キッズチキン + 単品チキン（920円）が安い
        var result = Find(("chicken", 2, 0), ("potato_s", 1, 0), ("drink_s", 1, 0));

        Assert.NotNull(result);
        Assert.Equal(920, result.TotalPrice);
        Assert.Equal(["キッズチキンx1[drink_s]", "単品チキンx1[]"], Describe(result));
    }

    [Fact]
    public void PackWithDuplicateSideChoice()
    {
        var result = Find(("chicken", 4, 0), ("crispy", 2, 0));

        Assert.NotNull(result);
        Assert.Equal(1590, result.TotalPrice);
        Assert.Equal(["4ピースパックx1[crispy,crispy]"], Describe(result));
    }

    [Fact]
    public void EightPackWithFourPotatoes()
    {
        var result = Find(("chicken", 8, 0), ("potato_s", 4, 0));

        Assert.NotNull(result);
        Assert.Equal(2990, result.TotalPrice);
        Assert.Equal(["8ピースパックx1[potato_s,potato_s,potato_s,potato_s]"], Describe(result));
    }

    [Fact]
    public void WithoutSurplus_BuysSingles()
    {
        var result = Find(("chicken", 1, 0), ("potato_s", 1, 0), ("biscuit", 1, 0));

        Assert.NotNull(result);
        Assert.Equal(910, result.TotalPrice);
        Assert.Equal(["ビスケットx1[]", "ポテトSx1[]", "単品チキンx1[]"], Describe(result));
    }

    [Fact]
    public void WithDrinkSurplus_SetWithExtraDrinkIsCheaper()
    {
        var result = Find(("chicken", 1, 0), ("potato_s", 1, 0), ("biscuit", 1, 0), ("drink_s", 0, 1));

        Assert.NotNull(result);
        Assert.Equal(590, result.TotalPrice);
        Assert.Equal(["チキン&ビスケットセットx1[drink_s]"], Describe(result));
    }

    [Fact]
    public void FamilyOrder()
    {
        var result = Find(("chicken", 8, 0), ("potato_s", 2, 0), ("drink_s", 2, 0), ("burger", 1, 0), ("biscuit", 2, 0));

        Assert.NotNull(result);
        Assert.Equal(3650, result.TotalPrice);
        Assert.Equal(["チキン&ビスケットセットx2[drink_s]", "バーガーx1[]", "単品チキンx6[]"], Describe(result));
    }

    [Fact]
    public void TiedPrices_ReturnsEitherCheapest()
    {
        var result = Find(("chicken", 10, 0));

        Assert.NotNull(result);
        Assert.Equal(3300, result.TotalPrice);
    }

    [Fact]
    public void NothingRequired_ReturnsEmptyAndZero()
    {
        var result = Find();

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalPrice);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void NoCombination_ReturnsNull()
    {
        var products = new List<Product> { new() { Id = "a", Required = 1, MaxSurplus = 0 } };
        var pairOnly = new MenuOption { Name = "ペア", Price = 100, Includes = new() { ["a"] = 2 } };

        var result = CheapestComboFinder.FindCheapest(VariantExpander.Expand([pairOnly]), products);

        Assert.Null(result);
    }

    [Fact]
    public void Regular_SetCostsTheRegularPrice()
    {
        var result = Find(regular, ("burger", 1, 0), ("potato_s", 1, 0), ("drink_s", 1, 0));

        Assert.NotNull(result);
        Assert.Equal(890, result.TotalPrice);
        Assert.Equal(["バーガーセットx1[drink_s]"], Describe(result));
    }

    [Fact]
    public void Regular_LunchOnlySetIsNotUsed()
    {
        // ランチならチキン&ビスケットセット（590円）。通常時間は注文できないので キッズチキン + ビスケット
        var result = Find(regular, ("chicken", 1, 0), ("potato_s", 1, 0), ("biscuit", 1, 0), ("drink_s", 0, 1));

        Assert.NotNull(result);
        Assert.Equal(880, result.TotalPrice);
        Assert.Equal(["キッズチキンx1[drink_s]", "ビスケットx1[]"], Describe(result));
    }

    [Fact]
    public void Regular_FamilyOrder()
    {
        var result = Find(regular, ("chicken", 8, 0), ("potato_s", 2, 0), ("drink_s", 2, 0), ("burger", 1, 0), ("biscuit", 2, 0));

        Assert.NotNull(result);
        Assert.Equal(3920, result.TotalPrice);
    }
}
