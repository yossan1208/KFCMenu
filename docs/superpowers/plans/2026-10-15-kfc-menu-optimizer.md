# KFCメニュー最安値セット計算アプリ（Blazor WebAssembly版） Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 商品ごとの必要数と余剰の上限を入力すると、KFCメニューの購入選択肢を組み合わせた最安の買い方を、スマホのブラウザ内で計算して表示するBlazor WebAssemblyアプリを作り、GitHub Pagesで公開する。

**Architecture:** 計算ロジックはクラスライブラリ `KfcMenu.Core` に置き、xUnitの `KfcMenu.Core.Tests` でテストする。`MenuParser` がJSONを読み（ソース生成のJSONシリアライザー）、`VariantExpander` が選べる枠の選び方をすべて展開し、`CheapestComboFinder` が「足りない商品を含むVariantだけを試す、メモ化付きの再帰探索」で最安を求める。`KfcMenu.Web`（Blazor WebAssembly）の `Home.razor` がmenu.jsonを取得してCoreを呼ぶ。

**Tech Stack:** C# / .NET 10、Blazor WebAssembly（Standalone、`--empty`テンプレート）、`System.Text.Json`（ソース生成）、xUnit、GitHub Pages + GitHub Actions

**Spec:** `docs/superpowers/specs/2026-09-05-kfc-menu-optimizer-design.md`

## Global Constraints

- 作業ディレクトリ: `/Users/yoshizawayuki/KFC`。ソリューションは `KfcMenu.slnx`。
- 条件: 各商品の合計個数が `Required` 以上 `Required + MaxSurplus` 以下。最小の合計金額を返す。同額が複数あればどれでもよい。
- menu.jsonは `KfcMenu.Web/wwwroot/data/menu.json`（products 16件、options 30件）。中身は変更しない。
- CoreはHttpClientやBlazorに依存しない。JSON文字列を受け取って処理する。
- 名前空間: モデルは `KfcMenu.Core.Models`、サービスは `KfcMenu.Core.Services`、テストは `KfcMenu.Core.Tests`。
- ビルド時の警告は0件を保つ（`Nullable` 有効）。
- gitリポジトリはまだない。Task 5でユーザーの確認を取ってから作る。それまでは「コミット」の手順はない。
- `git push`、GitHubでのリポジトリ作成、GitHub Pagesの設定は、ユーザーの確認なしに行わない。

## Review Focus

- **JSONで `choices` が省略された購入選択肢**: `Choices` がnullにならず空リストになる（Task 1の `Parse_ReadsCommentsAndCaseInsensitiveKeys` で確認）。
- **余剰を許したときに、余計なものが付いたセットの方が安くなるケース**: セットを選ぶ（Task 3の `WithDrinkSurplus_SetWithExtraDrinkIsCheaper` で確認）。
- **条件を満たす組み合わせがないケース**: 例外ではなく `null` を返す（Task 3の `NoCombination_ReturnsNull` で確認）。
- **発行（Release）後のトリミングでJSONの読み込みが壊れる**: ソース生成のJSONシリアライザーを使い、Task 4で発行したサイトの表示を確認する。
- **GitHub Pagesのサブパス配信**: `<base href>` を書き換えないと画面が真っ白になる（Task 5で扱う）。

## 検証済みの事項（計画作成時）

この計画のコードは、計画作成時にプロジェクトの複製で実行して確認済み。
- `dotnet test`: 18件すべて成功、ビルド警告0件
- 期待値は、menu.jsonを使った別の総当たりスクリプトの結果と一致
- 性能: 全16商品を「必要数2・余剰の上限2」にした重い条件でも約56ms
- Release発行したサイトを `/kfc-menu/` のサブパスで配信し、ヘッドレスChromeで表示・入力・計算・エラー表示・幅375pxでのレイアウトを確認

---

## Task 0: 環境構築（実施済み）

- `KfcMenu.slnx`、`KfcMenu.Core`（classlib）、`KfcMenu.Web`（`dotnet new blazorwasm --empty`）を作成し、WebからCoreを参照済み
- menu.jsonを `KfcMenu.Web/wwwroot/data/menu.json` に配置済み
- 以前の構成は `_archive/` に退避済み（ユーザーが手動で削除してよい）

---

## Task 1: テストプロジェクト・モデル・MenuParser

**Files:**
- Delete: `KfcMenu.Core/Models/Products.cs`（ユーザーが作った空のファイル。`Product.cs` に置き換える）
- Create: `KfcMenu.Core.Tests/`（xUnit）
- Create: `KfcMenu.Core.Tests/TestMenu.cs`
- Create: `KfcMenu.Core.Tests/MenuParserTests.cs`
- Create: `KfcMenu.Core/Models/Product.cs`, `MenuOption.cs`, `ChoiceSlot.cs`, `MenuData.cs`, `Variant.cs`, `ComboResult.cs`
- Create: `KfcMenu.Core/Services/MenuParser.cs`

**Interfaces:**
- Produces:
  - `Product { string Id; string Name; int Required; int MaxSurplus }`（すべて get/set）
  - `MenuOption { string Id; string Name; int Price; Dictionary<string,int> Includes; List<ChoiceSlot> Choices }`
  - `ChoiceSlot { int Count; List<string> From; bool AllowDuplicate; string Label }`
  - `MenuData { List<Product> Products; List<MenuOption> Options }`
  - `Variant { required MenuOption Option; List<string> Picks; Dictionary<string,int> Contents }`（init）
  - `ComboResult { List<(Variant Variant, int Count)> Items; int TotalPrice }`（init）
  - `static MenuData MenuParser.Parse(string json)`（結果がnullなら `InvalidOperationException`）
  - テスト用: `TestMenu.Load()`, `TestMenu.WithConditions(MenuData, params (string Id, int Required, int MaxSurplus)[])`

- [ ] **Step 1: 空のファイルを削除し、テストプロジェクトを作る**

```bash
cd /Users/yoshizawayuki/KFC
rm KfcMenu.Core/Models/Products.cs
dotnet new xunit -o KfcMenu.Core.Tests -n KfcMenu.Core.Tests
rm KfcMenu.Core.Tests/UnitTest1.cs
dotnet sln KfcMenu.slnx add KfcMenu.Core.Tests/KfcMenu.Core.Tests.csproj
dotnet add KfcMenu.Core.Tests reference KfcMenu.Core/KfcMenu.Core.csproj
```

`KfcMenu.Core.Tests/KfcMenu.Core.Tests.csproj` の `</Project>` の直前に、本物のmenu.jsonをテストの出力先にコピーする設定を追加する:

```xml
  <ItemGroup>
    <None Include="..\KfcMenu.Web\wwwroot\data\menu.json" Link="menu.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: 失敗するテストを書く**

`KfcMenu.Core.Tests/TestMenu.cs`:

```csharp
using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

internal static class TestMenu
{
    public static MenuData Load() =>
        MenuParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "menu.json")));

    public static List<Product> WithConditions(MenuData menu, params (string Id, int Required, int MaxSurplus)[] conditions)
    {
        foreach (var product in menu.Products)
        {
            product.Required = 0;
            product.MaxSurplus = 0;
        }
        foreach (var (id, required, maxSurplus) in conditions)
        {
            var product = menu.Products.Single(p => p.Id == id);
            product.Required = required;
            product.MaxSurplus = maxSurplus;
        }
        return menu.Products;
    }
}
```

`KfcMenu.Core.Tests/MenuParserTests.cs`:

```csharp
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
```

- [ ] **Step 3: テストが失敗することを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: ビルドエラー（`KfcMenu.Core.Services` や `MenuParser` が見つからない）

- [ ] **Step 4: モデルを書く**

`KfcMenu.Core/Models/Product.cs`:

```csharp
namespace KfcMenu.Core.Models;

public class Product
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Required { get; set; }
    public int MaxSurplus { get; set; }
}
```

`KfcMenu.Core/Models/MenuOption.cs`:

```csharp
namespace KfcMenu.Core.Models;

public class MenuOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Price { get; set; }
    public Dictionary<string, int> Includes { get; set; } = new();
    public List<ChoiceSlot> Choices { get; set; } = new();
}
```

`KfcMenu.Core/Models/ChoiceSlot.cs`:

```csharp
namespace KfcMenu.Core.Models;

public class ChoiceSlot
{
    public int Count { get; set; }
    public List<string> From { get; set; } = new();
    public bool AllowDuplicate { get; set; }
    public string Label { get; set; } = "";
}
```

`KfcMenu.Core/Models/MenuData.cs`:

```csharp
namespace KfcMenu.Core.Models;

public class MenuData
{
    public List<Product> Products { get; set; } = new();
    public List<MenuOption> Options { get; set; } = new();
}
```

`KfcMenu.Core/Models/Variant.cs`:

```csharp
namespace KfcMenu.Core.Models;

public class Variant
{
    public required MenuOption Option { get; init; }
    public List<string> Picks { get; init; } = new();
    public Dictionary<string, int> Contents { get; init; } = new();
}
```

`KfcMenu.Core/Models/ComboResult.cs`:

```csharp
namespace KfcMenu.Core.Models;

public class ComboResult
{
    public List<(Variant Variant, int Count)> Items { get; init; } = new();
    public int TotalPrice { get; init; }
}
```

- [ ] **Step 5: MenuParserを書く**

`KfcMenu.Core/Services/MenuParser.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using KfcMenu.Core.Models;

namespace KfcMenu.Core.Services;

public static class MenuParser
{
    public static MenuData Parse(string json) =>
        JsonSerializer.Deserialize(json, MenuJsonContext.Default.MenuData)
        ?? throw new InvalidOperationException("menu.json の中身が空です。");
}

// Blazor WebAssemblyの発行時のトリミングでプロパティが消されないよう、ソース生成を使う
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(MenuData))]
internal partial class MenuJsonContext : JsonSerializerContext;
```

- [ ] **Step 6: テストが通ることを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: `Passed!  - Failed: 0, Passed: 3`、ビルド警告0件

---

## Task 2: VariantExpander（選べる枠の展開）

**Files:**
- Create: `KfcMenu.Core.Tests/VariantExpanderTests.cs`
- Create: `KfcMenu.Core/Services/VariantExpander.cs`

**Interfaces:**
- Consumes: `MenuOption`, `ChoiceSlot`, `Variant`, `TestMenu.Load()`（Task 1）
- Produces: `static List<Variant> VariantExpander.Expand(IEnumerable<MenuOption> options)`。`Picks` は枠の順に並び、枠の中では `From` の順で、順番違いの重複は出さない。

- [ ] **Step 1: 失敗するテストを書く**

`KfcMenu.Core.Tests/VariantExpanderTests.cs`:

```csharp
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
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: ビルドエラー（`VariantExpander` が見つからない）

- [ ] **Step 3: VariantExpanderを書く**

`KfcMenu.Core/Services/VariantExpander.cs`:

```csharp
using KfcMenu.Core.Models;

namespace KfcMenu.Core.Services;

public static class VariantExpander
{
    public static List<Variant> Expand(IEnumerable<MenuOption> options)
    {
        var variants = new List<Variant>();
        foreach (var option in options)
        {
            foreach (var picks in ExpandSlots(option.Choices, 0))
            {
                var contents = new Dictionary<string, int>(option.Includes);
                foreach (var id in picks)
                {
                    contents[id] = contents.GetValueOrDefault(id) + 1;
                }
                variants.Add(new Variant { Option = option, Picks = picks, Contents = contents });
            }
        }
        return variants;
    }

    private static IEnumerable<List<string>> ExpandSlots(List<ChoiceSlot> slots, int slotIndex)
    {
        if (slotIndex == slots.Count)
        {
            yield return [];
            yield break;
        }
        var slot = slots[slotIndex];
        foreach (var head in Combine(slot.From, slot.Count, 0, slot.AllowDuplicate))
        {
            foreach (var rest in ExpandSlots(slots, slotIndex + 1))
            {
                yield return [.. head, .. rest];
            }
        }
    }

    // from[start..] から count 個選ぶ。開始位置を後ろにずらしていくので、順番違いの重複は出ない
    private static IEnumerable<List<string>> Combine(List<string> from, int count, int start, bool allowDuplicate)
    {
        if (count == 0)
        {
            yield return [];
            yield break;
        }
        for (var i = start; i < from.Count; i++)
        {
            var nextStart = allowDuplicate ? i : i + 1;
            foreach (var rest in Combine(from, count - 1, nextStart, allowDuplicate))
            {
                yield return [from[i], .. rest];
            }
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: `Passed!  - Failed: 0, Passed: 8`

---

## Task 3: CheapestComboFinder（最安の探索）

**Files:**
- Create: `KfcMenu.Core.Tests/CheapestComboFinderTests.cs`
- Create: `KfcMenu.Core/Services/CheapestComboFinder.cs`

**Interfaces:**
- Consumes: `Variant`, `Product`, `ComboResult`, `VariantExpander.Expand`, `TestMenu`（Task 1・2）
- Produces: `static ComboResult? CheapestComboFinder.FindCheapest(IReadOnlyList<Variant> variants, IReadOnlyList<Product> products)`（見つからなければ `null`。`Items` は買う順、同じVariantはまとめて個数にする）

**アルゴリズム:** 「今の合計個数」を状態とし、まだ下限に届いていない最初の商品を選ぶ。最適な買い方には、その商品を含むVariantが必ず1つ以上入っているので、それだけを試して再帰する。同じ状態はメモ化して再計算しない。上限を超える中身を持つVariantは最初に除外する。

- [ ] **Step 1: 失敗するテストを書く**

`KfcMenu.Core.Tests/CheapestComboFinderTests.cs`:

```csharp
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
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: ビルドエラー（`CheapestComboFinder` が見つからない）

- [ ] **Step 3: CheapestComboFinderを書く**

`KfcMenu.Core/Services/CheapestComboFinder.cs`:

```csharp
using KfcMenu.Core.Models;

namespace KfcMenu.Core.Services;

public static class CheapestComboFinder
{
    private const int Infeasible = int.MaxValue;

    public static ComboResult? FindCheapest(IReadOnlyList<Variant> variants, IReadOnlyList<Product> products)
    {
        var indexOf = new Dictionary<string, int>();
        for (var i = 0; i < products.Count; i++)
        {
            indexOf[products[i].Id] = i;
        }
        var lower = products.Select(p => p.Required).ToArray();
        var upper = products.Select(p => p.Required + p.MaxSurplus).ToArray();

        // 中身を商品の並び順の配列にしておく。上限を超える商品や、productsにない商品を含むVariantは使えないので除外する
        var usable = new List<(Variant Variant, int[] Amounts)>();
        foreach (var variant in variants)
        {
            var amounts = new int[products.Count];
            var ok = true;
            foreach (var (id, count) in variant.Contents)
            {
                if (!indexOf.TryGetValue(id, out var i) || count > upper[i])
                {
                    ok = false;
                    break;
                }
                amounts[i] = count;
            }
            if (ok)
            {
                usable.Add((variant, amounts));
            }
        }

        // memo: 「今の合計個数」→（ここから下限を満たすまでの最小追加金額, そのとき次に買うusableの番号）
        var memo = new Dictionary<string, (int Cost, int Next)>();

        int Solve(int[] totals)
        {
            var key = string.Join(",", totals);
            if (memo.TryGetValue(key, out var cached))
            {
                return cached.Cost;
            }

            var lacking = -1;
            for (var i = 0; i < totals.Length; i++)
            {
                if (totals[i] < lower[i])
                {
                    lacking = i;
                    break;
                }
            }
            if (lacking < 0)
            {
                memo[key] = (0, -1);
                return 0;
            }

            // 足りない商品を含むVariantのどれかは必ず買うことになるので、それだけを試す
            var best = (Cost: Infeasible, Next: -1);
            for (var j = 0; j < usable.Count; j++)
            {
                var amounts = usable[j].Amounts;
                if (amounts[lacking] == 0 || !Fits(totals, amounts, upper))
                {
                    continue;
                }
                var rest = Solve(Add(totals, amounts));
                if (rest == Infeasible)
                {
                    continue;
                }
                var cost = usable[j].Variant.Option.Price + rest;
                if (cost < best.Cost)
                {
                    best = (cost, j);
                }
            }
            memo[key] = best;
            return best.Cost;
        }

        var current = new int[products.Count];
        var total = Solve(current);
        if (total == Infeasible)
        {
            return null;
        }

        var items = new List<(Variant Variant, int Count)>();
        while (true)
        {
            var next = memo[string.Join(",", current)].Next;
            if (next < 0)
            {
                break;
            }
            var variant = usable[next].Variant;
            var index = items.FindIndex(item => item.Variant == variant);
            if (index < 0)
            {
                items.Add((variant, 1));
            }
            else
            {
                items[index] = (variant, items[index].Count + 1);
            }
            current = Add(current, usable[next].Amounts);
        }

        return new ComboResult { Items = items, TotalPrice = total };
    }

    private static bool Fits(int[] totals, int[] amounts, int[] upper)
    {
        for (var i = 0; i < totals.Length; i++)
        {
            if (totals[i] + amounts[i] > upper[i])
            {
                return false;
            }
        }
        return true;
    }

    private static int[] Add(int[] totals, int[] amounts)
    {
        var result = new int[totals.Length];
        for (var i = 0; i < totals.Length; i++)
        {
            result[i] = totals[i] + amounts[i];
        }
        return result;
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: `Passed!  - Failed: 0, Passed: 18`、ビルド警告0件

---

## Task 4: 画面（Home.razor）

**Files:**
- Modify: `KfcMenu.Web/Pages/Home.razor`（全体を置き換える）
- Modify: `KfcMenu.Web/wwwroot/css/app.css`（末尾に追記）
- Modify: `KfcMenu.Web/wwwroot/index.html`（`<title>` のみ）

**Interfaces:**
- Consumes: `MenuParser.Parse`, `VariantExpander.Expand`, `CheapestComboFinder.FindCheapest`（Task 1〜3）
- 入力欄の `aria-label` は `"{商品名}の必要数"` / `"{商品名}の余剰の上限"`（動作確認スクリプトがこれで入力欄を探す）

- [ ] **Step 1: Home.razorを書く**

`KfcMenu.Web/Pages/Home.razor` の中身を次に置き換える:

```razor
@page "/"
@using KfcMenu.Core.Models
@using KfcMenu.Core.Services
@inject HttpClient Http

<PageTitle>KFC最安計算</PageTitle>

<main class="app">
    <h1>KFC最安計算</h1>

    @if (loadError is not null)
    {
        <p class="error">メニューを読み込めませんでした: @loadError</p>
    }
    else if (menu is null)
    {
        <p>読み込み中...</p>
    }
    else
    {
        <div class="products">
            <div class="product-row header">
                <span>商品</span>
                <span>必要数</span>
                <span>余剰の上限</span>
            </div>
            @foreach (var product in menu.Products)
            {
                <div class="product-row">
                    <span class="name">@product.Name</span>
                    <input type="number" min="0" inputmode="numeric" aria-label="@($"{product.Name}の必要数")" @bind="product.Required" />
                    <input type="number" min="0" inputmode="numeric" aria-label="@($"{product.Name}の余剰の上限")" @bind="product.MaxSurplus" />
                </div>
            }
        </div>

        <button class="calc" @onclick="Calculate">計算</button>

        @if (inputError is not null)
        {
            <p class="error">@inputError</p>
        }
        else if (calculated)
        {
            if (result is null)
            {
                <p class="result">条件を満たす組み合わせが見つかりませんでした</p>
            }
            else
            {
                <ul class="result">
                    @foreach (var (variant, count) in result.Items)
                    {
                        <li>@variant.Option.Name × @count@DescribePicks(variant)</li>
                    }
                </ul>
                <p class="total">合計 @result.TotalPrice.ToString("N0")円</p>
            }
        }
    }
</main>

@code {
    private MenuData? menu;
    private List<Variant> variants = new();
    private Dictionary<string, string> productNames = new();
    private string? loadError;
    private string? inputError;
    private ComboResult? result;
    private bool calculated;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var json = await Http.GetStringAsync("data/menu.json");
            menu = MenuParser.Parse(json);
            variants = VariantExpander.Expand(menu.Options);
            productNames = menu.Products.ToDictionary(p => p.Id, p => p.Name);
        }
        catch (Exception ex)
        {
            loadError = ex.Message;
        }
    }

    private void Calculate()
    {
        calculated = false;
        result = null;
        inputError = null;

        if (menu!.Products.Any(p => p.Required < 0 || p.MaxSurplus < 0))
        {
            inputError = "必要数と余剰の上限には0以上の数を入力してください。";
            return;
        }

        result = CheapestComboFinder.FindCheapest(variants, menu.Products);
        calculated = true;
    }

    // Picks は枠の順に並んでいるので、枠の Count ずつ区切ると枠ごとの選択になる
    private string DescribePicks(Variant variant)
    {
        if (variant.Picks.Count == 0)
        {
            return "";
        }
        var parts = new List<string>();
        var offset = 0;
        foreach (var slot in variant.Option.Choices)
        {
            var names = variant.Picks.Skip(offset).Take(slot.Count).Select(id => productNames.GetValueOrDefault(id, id));
            parts.Add($"{slot.Label}: {string.Join(", ", names)}");
            offset += slot.Count;
        }
        return $"（{string.Join(" / ", parts)}）";
    }
}
```

- [ ] **Step 2: スタイルとタイトルを追加する**

`KfcMenu.Web/wwwroot/css/app.css` の末尾に追記する:

```css
body {
    margin: 0;
    font-family: system-ui, -apple-system, "Hiragino Sans", sans-serif;
    background: #fff;
    color: #222;
}

.app {
    max-width: 32rem;
    margin: 0 auto;
    padding: 1rem;
}

.app h1 {
    font-size: 1.4rem;
    margin: 0 0 1rem;
}

.app h1:focus {
    outline: none;
}

.product-row {
    display: grid;
    grid-template-columns: 1fr 4.5rem 4.5rem;
    gap: 0.5rem;
    align-items: center;
    padding: 0.25rem 0;
    border-bottom: 1px solid #eee;
}

.product-row.header {
    font-size: 0.8rem;
    color: #666;
}

.product-row input {
    width: 100%;
    box-sizing: border-box;
    font-size: 1rem;
    padding: 0.3rem;
}

.calc {
    display: block;
    width: 100%;
    margin: 1rem 0;
    padding: 0.8rem;
    font-size: 1.1rem;
    border: none;
    border-radius: 0.5rem;
    background: #c8102e;
    color: #fff;
}

.result {
    padding-left: 1.2rem;
}

.total {
    font-size: 1.3rem;
    font-weight: bold;
}

.error {
    color: #c8102e;
}
```

`KfcMenu.Web/wwwroot/index.html` の `<title>KfcMenu.Web</title>` を `<title>KFC最安計算</title>` に変える。

- [ ] **Step 3: ビルドとテストを確認する**

Run: `dotnet build KfcMenu.slnx && dotnet test KfcMenu.slnx`
Expected: 警告0件、エラー0件、テスト18件成功

- [ ] **Step 4: ブラウザで動作確認する**

`cd KfcMenu.Web && dotnet run --launch-profile http` で起動し、`http://localhost:5127` を開く（ヘッドレスChromeやスクリプトで操作してもよい）。
- 全商品の入力欄（16行）と「計算」ボタンが表示される
- チキン 必要数2、ポテトS 必要数1、ドリンクS 必要数1 → `2セット × 1（ドリンク: ドリンクS）`、`合計 890円`
- チキン 必要数1、ポテトS 必要数1、ビスケット 必要数1 → `合計 910円`。さらにドリンクSの余剰の上限を1 → `チキビス × 1（ドリンク: ドリンクS）`、`合計 550円`
- チキン 必要数4、クリスピー 必要数2 → `4パック × 1（サイド: クリスピー, クリスピー）`、`合計 1,590円`
- チキンの必要数を -1 → `必要数と余剰の上限には0以上の数を入力してください。`
- 幅375px程度で横にはみ出さない（Chromeのデスクトップ版はウィンドウ幅を500px未満にできないので、ヘッドレスで確認する場合は幅375pxのiframeに埋め込んで撮る）

確認が終わったらサーバーを止める。

- [ ] **Step 5: Release発行とサブパス配信で確認する**

GitHub Pagesと同じ条件（トリミングあり・サブパス配信）で動くことを確認する。

```bash
cd /Users/yoshizawayuki/KFC
dotnet publish KfcMenu.Web/KfcMenu.Web.csproj -c Release -o <一時ディレクトリ>/publish
mkdir -p <一時ディレクトリ>/site && cp -R <一時ディレクトリ>/publish/wwwroot <一時ディレクトリ>/site/kfc-menu
sed -i '' 's|<base href="/" />|<base href="/kfc-menu/" />|' <一時ディレクトリ>/site/kfc-menu/index.html
cd <一時ディレクトリ>/site && python3 -m http.server 8765
```

`http://127.0.0.1:8765/kfc-menu/` を開き、Step 4と同じ結果になることを確認する。確認後にサーバーを止める。

---

## Task 5: GitHub Pagesへのデプロイ

**開始前に必ずユーザーに確認すること:** gitリポジトリを作ってよいか、GitHubのユーザー名、リポジトリ名（例: `kfc-menu`）、公開リポジトリでよいか、`_archive/` を含めるか。

**リポジトリ名の注意:** リポジトリ名は `kfc-menu` のようなプロジェクト用の名前にする。`<ユーザー名>.github.io` という名前にすると、サイトがルート（`/`）で公開されて `<base href>` を `/<リポジトリ名>/` に書き換える前提が崩れ、画面が真っ白になる。

**Files:**
- Create: `.gitignore`
- Create: `.github/workflows/deploy.yml`

- [ ] **Step 1: gitリポジトリを作る**

```bash
cd /Users/yoshizawayuki/KFC
git init -b main
dotnet new gitignore
```

`_archive/` を含めない場合は `.gitignore` の末尾に `_archive/` を追加する。`menu_/` と `KFCメニュー.numbers` を含めるかもユーザーの回答に従う。

- [ ] **Step 2: ワークフローを書く**

`.github/workflows/deploy.yml`:

```yaml
name: Deploy to GitHub Pages

on:
  push:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: pages
  cancel-in-progress: true

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet test KfcMenu.slnx
      - run: dotnet publish KfcMenu.Web/KfcMenu.Web.csproj -c Release -o publish
      - name: Set base href to the repository subpath
        run: sed -i 's|<base href="/" />|<base href="/${{ github.event.repository.name }}/" />|' publish/wwwroot/index.html
      - run: cp publish/wwwroot/index.html publish/wwwroot/404.html
      - run: touch publish/wwwroot/.nojekyll
      - uses: actions/upload-pages-artifact@v3
        with:
          path: publish/wwwroot

  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - id: deployment
        uses: actions/deploy-pages@v4
```

- `sed`: GitHub Pagesは `https://<ユーザー名>.github.io/<リポジトリ名>/` で公開されるため、`<base href>` をリポジトリ名入りにする
- `404.html`: URLを直接開いてもアプリが表示されるようにする
- `.nojekyll`: `_framework` フォルダ（`_` で始まる）をGitHub Pagesに無視させない

- [ ] **Step 3: 最初のコミット**

```bash
git add .
git status
```

`git status` の一覧に、秘密情報や公開したくないファイルがないことを確認してからコミットする:

```bash
git commit -m "KFCメニュー最安計算アプリを追加"
```

- [ ] **Step 4: GitHubへpushする（ユーザーの確認後）**

ユーザーがGitHubで空のリポジトリを作成した後（`gh` CLIが使えてユーザーが許可すれば `gh repo create` でもよい）:

```bash
git remote add origin https://github.com/<ユーザー名>/<リポジトリ名>.git
git push -u origin main
```

- [ ] **Step 5: GitHub Pagesを有効にする（ユーザーが行う）**

リポジトリの Settings → Pages → Build and deployment → Source を「GitHub Actions」にしてもらう。Actionsタブでワークフローの成功を確認する。

- [ ] **Step 6: 公開サイトを確認する**

`https://<ユーザー名>.github.io/<リポジトリ名>/` を開き、Task 4 Step 4と同じ結果になることを確認する。スマホでの確認はユーザーに依頼する。

**価格を変えるとき:** `KfcMenu.Web/wwwroot/data/menu.json` を書き換えてcommit・pushすると、自動でテスト・再ビルド・再公開される。テストの期待値は現在の価格に基づくので、価格を変えたらテストも合わせて直す。
