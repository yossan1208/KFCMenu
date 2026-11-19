using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

public class MenuParserTests
{
    [Fact]
    public void Parse_ReadsCommentsAndCaseInsensitiveKeys()
    {
        var json = """
            // コメント
            {
                "products": [ { "id": "chicken", "name": "チキン", "required": 2, "MaxSurplus": 1 } ],
                "options": [
                    { "id": "single", "name": "単", "price": 330, "includes": { "chicken": 1 } },
                    {
                        "id": "pack", "name": "パック", "price": 1590, "includes": { "chicken": 4 },
                        "choices": [ { "count": 2, "from": ["a", "b"], "allowDuplicate": true, "label": "サイド" } ]
                    }
                ]
            }
            """;

        var menu = MenuParser.Parse(json);

        var product = Assert.Single(menu.Products);
        Assert.Equal("chicken", product.Id);
        Assert.Equal("チキン", product.Name);
        Assert.Equal(2, product.Required);
        Assert.Equal(1, product.MaxSurplus);

        Assert.Equal(2, menu.Options.Count);
        Assert.Equal(330, menu.Options[0].Price);
        Assert.Equal(1, menu.Options[0].Includes["chicken"]);
        Assert.Empty(menu.Options[0].Choices);

        var slot = Assert.Single(menu.Options[1].Choices);
        Assert.Equal(2, slot.Count);
        Assert.Equal(["a", "b"], slot.From);
        Assert.True(slot.AllowDuplicate);
        Assert.Equal("サイド", slot.Label);
    }

    [Fact]
    public void Parse_RealMenu()
    {
        var menu = TestMenu.Load();

        Assert.Equal(16, menu.Products.Count);
        Assert.Equal(36, menu.Options.Count);
        Assert.Equal("チキン", menu.Products[0].Name);
        Assert.Equal("骨なしチキン", menu.Products.Single(p => p.Id == "boneless").Name);
        Assert.Equal("ドリンク S", menu.Products.Single(p => p.Id == "drink_s").Name);
        Assert.Equal("4ピースパック", menu.Options[0].Name);
        Assert.Equal(1590, menu.Options[0].Price);
        Assert.Equal(8, menu.Options.Count(o => o.Availability == "lunch"));
        Assert.Equal(6, menu.Options.Count(o => o.Availability == "regular"));
        Assert.Equal("ドリンク S", menu.Options.Single(o => o.Id == "drink_s_single").Name);
        Assert.Equal("ホットドリンク", menu.Options.Single(o => o.Id == "drink_hot_single").Name);
    }

    [Fact]
    public void Parse_ReadsAvailability()
    {
        var json = """
            {
                "products": [ { "id": "a", "name": "A" } ],
                "options": [
                    { "id": "any", "name": "いつでも", "price": 100, "includes": { "a": 1 } },
                    { "id": "lunch", "name": "ランチ", "price": 90, "availability": "lunch", "includes": { "a": 1 } },
                    { "id": "regular", "name": "通常", "price": 110, "availability": "regular", "includes": { "a": 1 } }
                ]
            }
            """;

        var menu = MenuParser.Parse(json);

        Assert.Equal([null, "lunch", "regular"], menu.Options.Select(o => o.Availability));
    }

    [Fact]
    public void Parse_UnknownAvailability_Throws()
    {
        var json = """
            {
                "products": [ { "id": "a", "name": "A" } ],
                "options": [ { "id": "x", "name": "書き間違い", "price": 100, "availability": "lunchtime", "includes": { "a": 1 } } ]
            }
            """;

        var error = Assert.Throws<InvalidOperationException>(() => MenuParser.Parse(json));

        Assert.Contains("書き間違い", error.Message);
        Assert.Contains("lunchtime", error.Message);
    }

    [Fact]
    public void Parse_NullJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => MenuParser.Parse("null"));
    }
}
