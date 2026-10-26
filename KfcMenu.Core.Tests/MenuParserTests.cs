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
        Assert.Equal(30, menu.Options.Count);
        Assert.Equal("チキン", menu.Products[0].Name);
        Assert.Equal("4パック", menu.Options[0].Name);
        Assert.Equal(1590, menu.Options[0].Price);
    }

    [Fact]
    public void Parse_NullJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => MenuParser.Parse("null"));
    }
}
