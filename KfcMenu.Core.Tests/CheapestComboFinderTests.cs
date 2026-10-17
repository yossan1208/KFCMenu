using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

public class CheapestComboFinderTests
{
    private readonly MenuData menu = TestMenu.Load();
    private readonly List<Variant> variants;

    public CheapestComboFinderTests()
    {
        variants = VariantExpander.Expand(menu.Options);
    }

    private ComboResult? Find(params (string Id, int Required, int MaxSurplus)[] conditions) =>
        CheapestComboFinder.FindCheapest(variants, TestMenu.WithConditions(menu, conditions));

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
        Assert.Equal(["単x8[]"], Describe(result));
    }

    [Fact]
    public void ExactSetMatch()
    {
        var result = Find(("chicken", 2, 0), ("potato_s", 1, 0), ("drink_s", 1, 0));

        Assert.NotNull(result);
        Assert.Equal(890, result.TotalPrice);
        Assert.Equal(["2セットx1[drink_s]"], Describe(result));
    }

    [Fact]
    public void PackWithDuplicateSideChoice()
    {
        var result = Find(("chicken", 4, 0), ("crispy", 2, 0));

        Assert.NotNull(result);
        Assert.Equal(1590, result.TotalPrice);
        Assert.Equal(["4パックx1[crispy,crispy]"], Describe(result));
    }

    [Fact]
    public void EightPackWithFourPotatoes()
    {
        var result = Find(("chicken", 8, 0), ("potato_s", 4, 0));

        Assert.NotNull(result);
        Assert.Equal(2990, result.TotalPrice);
        Assert.Equal(["8パックx1[potato_s,potato_s,potato_s,potato_s]"], Describe(result));
    }

    [Fact]
    public void WithoutSurplus_BuysSingles()
    {
        var result = Find(("chicken", 1, 0), ("potato_s", 1, 0), ("biscuit", 1, 0));

        Assert.NotNull(result);
        Assert.Equal(910, result.TotalPrice);
        Assert.Equal(["ビスケットx1[]", "ポテsx1[]", "単x1[]"], Describe(result));
    }

    [Fact]
    public void WithDrinkSurplus_SetWithExtraDrinkIsCheaper()
    {
        var result = Find(("chicken", 1, 0), ("potato_s", 1, 0), ("biscuit", 1, 0), ("drink_s", 0, 1));

        Assert.NotNull(result);
        Assert.Equal(550, result.TotalPrice);
        Assert.Equal(["チキビスx1[drink_s]"], Describe(result));
    }

    [Fact]
    public void FamilyOrder()
    {
        var result = Find(("chicken", 8, 0), ("potato_s", 2, 0), ("drink_s", 2, 0), ("burger", 1, 0), ("biscuit", 2, 0));

        Assert.NotNull(result);
        Assert.Equal(3520, result.TotalPrice);
        Assert.Equal(["チキビスx2[drink_s]", "バーガーx1[]", "単x6[]"], Describe(result));
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
}
