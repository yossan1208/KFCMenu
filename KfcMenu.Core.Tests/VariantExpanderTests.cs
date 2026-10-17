using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

public class VariantExpanderTests
{
    [Fact]
    public void Expand_RealMenu_Counts()
    {
        var variants = VariantExpander.Expand(TestMenu.Load().Options);

        Assert.Equal(174, variants.Count);
        Assert.Equal(15, variants.Count(v => v.Option.Name == "4パック"));
        Assert.Equal(35, variants.Count(v => v.Option.Name == "6パック"));
        Assert.Equal(70, variants.Count(v => v.Option.Name == "8パック"));
        Assert.Equal(4, variants.Count(v => v.Option.Name == "セット"));
        Assert.Equal(1, variants.Count(v => v.Option.Name == "単"));
    }

    [Fact]
    public void Expand_NoChoices_ReturnsIncludesOnly()
    {
        var option = new MenuOption { Name = "単", Includes = new() { ["chicken"] = 1 } };

        var variant = Assert.Single(VariantExpander.Expand([option]));

        Assert.Same(option, variant.Option);
        Assert.Empty(variant.Picks);
        Assert.Equal(new Dictionary<string, int> { ["chicken"] = 1 }, variant.Contents);
    }

    [Fact]
    public void Expand_WithoutDuplicates_ListsCombinations()
    {
        var option = new MenuOption
        {
            Choices = [new ChoiceSlot { Count = 2, From = ["a", "b", "c"], AllowDuplicate = false }],
        };

        var picks = VariantExpander.Expand([option]).Select(v => string.Join(",", v.Picks)).ToList();

        Assert.Equal(["a,b", "a,c", "b,c"], picks);
    }

    [Fact]
    public void Expand_WithDuplicates_ListsCombinationsWithRepetition()
    {
        var option = new MenuOption
        {
            Includes = new() { ["chicken"] = 4 },
            Choices = [new ChoiceSlot { Count = 2, From = ["a", "b"], AllowDuplicate = true }],
        };

        var variants = VariantExpander.Expand([option]);

        Assert.Equal(["a,a", "a,b", "b,b"], variants.Select(v => string.Join(",", v.Picks)));
        Assert.Equal(new Dictionary<string, int> { ["chicken"] = 4, ["a"] = 2 }, variants[0].Contents);
    }

    [Fact]
    public void Expand_MultipleSlots_PicksFollowSlotOrder()
    {
        var option = new MenuOption
        {
            Choices =
            [
                new ChoiceSlot { Count = 1, From = ["x", "y"] },
                new ChoiceSlot { Count = 1, From = ["p", "q"] },
            ],
        };

        var picks = VariantExpander.Expand([option]).Select(v => string.Join(",", v.Picks)).ToList();

        Assert.Equal(["x,p", "x,q", "y,p", "y,q"], picks);
    }
}
