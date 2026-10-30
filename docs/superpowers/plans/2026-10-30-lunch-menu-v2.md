# ver2.0: ランチ／通常メニューの切り替え Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** メニューを KFCメニュー.numbers（2026-10-26版）の正式名・新価格にし、画面の切り替えで「ランチ（10:00〜15:00）」と「通常」の購入選択肢を使い分けて最安とおすすめを計算する。

**Architecture:** 購入選択肢に省略可能な `availability`（`lunch` / `regular`）を持たせる。Core に `ServiceTime` と `MenuTime`（時刻→時間帯、時間帯で購入選択肢を絞る）を追加し、`MenuParser` で不正な値を弾く。最安・展開・おすすめの処理は変えず、画面が選ばれた時間帯で絞った購入選択肢を渡す。

**Tech Stack:** C# / .NET 10、Blazor WebAssembly、xUnit

**Spec:** `docs/superpowers/specs/2026-10-30-lunch-menu-v2-design.md`

## Global Constraints

- 作業ブランチ: `v2-lunch-menu`（main `3cf9cc8` から分岐済み）。作業場所はリポジトリ `/Users/yoshizawayuki/KFC`（実行時に worktree を使う場合はその中）。push しない。
- ランチ時間は 10:00 以上 15:00 未満（毎日）。それ以外は通常。
- `availability` の値は `"lunch"` / `"regular"` / 省略。それ以外は `MenuParser.Parse` で `InvalidOperationException`。
- menu.json の購入選択肢は36件。ランチで30件、通常で28件。バーガーコンビとチキン&ビスケットセットはランチのみ。
- ドリンク単品の名前は「ドリンク S」「ドリンク M」「ドリンク L」（半角スペース）と「ホットドリンク」。欲しい商品の名前もドリンクは同じ表記、「骨なし」は「骨なしチキン」。
- `CheapestComboFinder`、`VariantExpander`、`SetRecommender` は変更しない。
- ビルド警告0件。KfcMenu.Core / KfcMenu.Core.Tests / menu.json は LF、KfcMenu.Web の Home.razor と app.css は CRLF のまま保つ。
- 名前空間: モデル `KfcMenu.Core.Models`、サービス `KfcMenu.Core.Services`、テスト `KfcMenu.Core.Tests`。

## Review Focus

- **15:00 ちょうど・10:00 ちょうど**: 15:00 は通常、10:00 はランチ（Task 1 `At_LunchIsFromTenToBeforeFifteen`）。
- **menu.json の availability の書き間違い**: 黙ってどの時間帯にも出ないのではなく、読み込みエラーになる（Task 1 `Parse_UnknownAvailability_Throws`）。
- **通常時間にランチ専用セットが混ざる**: 最安にもおすすめにも出ない（Task 2 `Regular_LunchOnlySetIsNotUsed`、`Regular_NoLunchOnlySets`）。
- **切り替えたときに古い結果が残る**: 結果・おすすめ・エラーは消え、必要数の入力は残る（Task 3 ブラウザ確認）。
- **画面を開いたときの初期値**: スマホの時刻でランチ／通常が決まる（Task 3 ブラウザ確認。タイムゾーンを変えて 12時台と20時台を確認）。

## 検証済みの事項（計画作成時）

この計画のコードは、計画作成時にプロジェクトの複製で実行して確認済み。
- menu.json は numbers の36行と、名前・値段・時間帯・品数が1行ずつ一致（確認用スクリプトで照合）
- Task 1 だけを入れた状態: 52件成功、警告0件
- 全タスク後: 61件成功、警告0件
- 最安とおすすめの期待値は、途中で候補を絞らない総当たりスクリプト（ランチ・通常の両方）と一致。単品の個数と金額が同じ候補が複数あるところは、テストでは金額と単品の個数だけを確かめる
- Release 発行をサブパス（`/kfc-menu/`）で配信し、ヘッドレスChromeで切り替えの13項目をすべて確認。幅375pxで切り替えボタンのレイアウトも確認

---

## Task 1: 時間帯の扱い（Core）

**Files:**
- Modify: `KfcMenu.Core/Models/MenuOption.cs`
- Create: `KfcMenu.Core/Models/ServiceTime.cs`
- Create: `KfcMenu.Core/Services/MenuTime.cs`
- Modify: `KfcMenu.Core/Services/MenuParser.cs`
- Modify: `KfcMenu.Core.Tests/TestMenu.cs`
- Create: `KfcMenu.Core.Tests/MenuTimeTests.cs`
- Modify: `KfcMenu.Core.Tests/MenuParserTests.cs`（テストを2つ追加）

**Interfaces:**
- Produces:
  - `MenuOption.Availability`（`string?`）
  - `enum ServiceTime { Lunch, Regular }`
  - `MenuTime.LunchValue = "lunch"`, `MenuTime.RegularValue = "regular"`, `MenuTime.LunchStart = 10:00`, `MenuTime.LunchEnd = 15:00`
  - `static ServiceTime MenuTime.At(TimeOnly time)`
  - `static List<MenuOption> MenuTime.AvailableAt(IEnumerable<MenuOption> options, ServiceTime time)`
  - `MenuParser.Parse` は availability が不正なら `InvalidOperationException`（メッセージに購入選択肢名と値を含む）
  - テスト用: `TestMenu.Load(ServiceTime time)`（その時間帯の購入選択肢だけを残した `MenuData`）

- [ ] **Step 1: 失敗するテストを書く**

`KfcMenu.Core.Tests/MenuTimeTests.cs`:

```csharp
using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

public class MenuTimeTests
{
    [Theory]
    [InlineData(0, 0, ServiceTime.Regular)]
    [InlineData(9, 59, ServiceTime.Regular)]
    [InlineData(10, 0, ServiceTime.Lunch)]
    [InlineData(12, 30, ServiceTime.Lunch)]
    [InlineData(14, 59, ServiceTime.Lunch)]
    [InlineData(15, 0, ServiceTime.Regular)]
    [InlineData(23, 59, ServiceTime.Regular)]
    public void At_LunchIsFromTenToBeforeFifteen(int hour, int minute, ServiceTime expected)
    {
        Assert.Equal(expected, MenuTime.At(new TimeOnly(hour, minute)));
    }

    [Fact]
    public void At_OneSecondBeforeFifteenIsStillLunch()
    {
        Assert.Equal(ServiceTime.Lunch, MenuTime.At(new TimeOnly(14, 59, 59)));
    }

    [Fact]
    public void AvailableAt_KeepsAnytimeAndMatchingOptions()
    {
        var any = new MenuOption { Id = "any" };
        var lunch = new MenuOption { Id = "lunch", Availability = "lunch" };
        var regular = new MenuOption { Id = "regular", Availability = "regular" };

        Assert.Equal(["any", "lunch"], MenuTime.AvailableAt([any, lunch, regular], ServiceTime.Lunch).Select(o => o.Id));
        Assert.Equal(["any", "regular"], MenuTime.AvailableAt([any, lunch, regular], ServiceTime.Regular).Select(o => o.Id));
    }
}
```

`KfcMenu.Core.Tests/MenuParserTests.cs` の `Parse_NullJson_Throws` の直前に追加する:

```csharp
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
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: ビルドエラー（`ServiceTime`、`MenuTime`、`Availability` が見つからない）

- [ ] **Step 3: モデルを書く**

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

    // 注文できる時間帯。"lunch" / "regular"、null ならいつでも
    public string? Availability { get; set; }
}
```

`KfcMenu.Core/Models/ServiceTime.cs`:

```csharp
namespace KfcMenu.Core.Models;

public enum ServiceTime
{
    Lunch,
    Regular,
}
```

- [ ] **Step 4: MenuTime と MenuParser を書く**

`KfcMenu.Core/Services/MenuTime.cs`:

```csharp
using KfcMenu.Core.Models;

namespace KfcMenu.Core.Services;

public static class MenuTime
{
    // menu.json の availability に書く値
    public const string LunchValue = "lunch";
    public const string RegularValue = "regular";

    public static readonly TimeOnly LunchStart = new(10, 0);
    public static readonly TimeOnly LunchEnd = new(15, 0);

    // 10:00 以上 15:00 未満がランチ
    public static ServiceTime At(TimeOnly time) =>
        time >= LunchStart && time < LunchEnd ? ServiceTime.Lunch : ServiceTime.Regular;

    public static List<MenuOption> AvailableAt(IEnumerable<MenuOption> options, ServiceTime time)
    {
        var value = time == ServiceTime.Lunch ? LunchValue : RegularValue;
        return options.Where(o => o.Availability is null || o.Availability == value).ToList();
    }
}
```

`KfcMenu.Core/Services/MenuParser.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using KfcMenu.Core.Models;

namespace KfcMenu.Core.Services;

public static class MenuParser
{
    public static MenuData Parse(string json)
    {
        var menu = JsonSerializer.Deserialize(json, MenuJsonContext.Default.MenuData)
            ?? throw new InvalidOperationException("menu.json の中身が空です。");

        // 書き間違いの時間帯は、黙ってどの時間帯にも出さないのではなく、読み込みエラーにして気づけるようにする
        var invalid = menu.Options.FirstOrDefault(o =>
            o.Availability is not null and not MenuTime.LunchValue and not MenuTime.RegularValue);
        if (invalid is not null)
        {
            throw new InvalidOperationException(
                $"menu.json の「{invalid.Name}」の availability「{invalid.Availability}」は " +
                $"{MenuTime.LunchValue} か {MenuTime.RegularValue} にするか、省略してください。");
        }
        return menu;
    }
}

// Blazor WebAssemblyの発行時のトリミングでプロパティが消されないよう、ソース生成を使う
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(MenuData))]
internal partial class MenuJsonContext : JsonSerializerContext;
```

- [ ] **Step 5: TestMenu に時間帯つきの Load を追加する**

`KfcMenu.Core.Tests/TestMenu.cs`:

```csharp
using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

internal static class TestMenu
{
    public static MenuData Load() =>
        MenuParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "menu.json")));

    // 指定した時間帯で注文できる購入選択肢だけを残したメニュー
    public static MenuData Load(ServiceTime time)
    {
        var menu = Load();
        menu.Options = MenuTime.AvailableAt(menu.Options, time);
        return menu;
    }

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

- [ ] **Step 6: テストが通ることを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: `Passed!  - Failed: 0, Passed: 52`（menu.json はまだ旧版のままなので、既存のテストはそのまま通る）

Run: `dotnet build KfcMenu.slnx --no-incremental`
Expected: `0 Warning(s)` `0 Error(s)`

- [ ] **Step 7: コミット**

```bash
git add KfcMenu.Core/Models/MenuOption.cs KfcMenu.Core/Models/ServiceTime.cs KfcMenu.Core/Services/MenuTime.cs KfcMenu.Core/Services/MenuParser.cs KfcMenu.Core.Tests/TestMenu.cs KfcMenu.Core.Tests/MenuTimeTests.cs KfcMenu.Core.Tests/MenuParserTests.cs
git commit -m "Add lunch/regular availability to menu options"
```

---

## Task 2: v2 のメニューデータと、本物のメニューを使うテストの更新

**Files:**
- Modify: `KfcMenu.Web/wwwroot/data/menu.json`（全体を置き換える。LF）
- Modify: `KfcMenu.Core.Tests/MenuParserTests.cs`（`Parse_RealMenu` を置き換える）
- Modify: `KfcMenu.Core.Tests/MenuTimeTests.cs`（テストを1つ追加）
- Modify: `KfcMenu.Core.Tests/VariantExpanderTests.cs`（全体を置き換える）
- Modify: `KfcMenu.Core.Tests/CheapestComboFinderTests.cs`（全体を置き換える）
- Modify: `KfcMenu.Core.Tests/SetRecommenderTests.cs`（全体を置き換える）

**Interfaces:**
- Consumes: `TestMenu.Load(ServiceTime)`, `MenuTime.AvailableAt`, `ServiceTime`（Task 1）。`RecommendationChecker`（既存、変更しない）
- Produces: v2 の menu.json。購入選択肢の `id`（テストで使う）: `pack4`, `pack6`, `pack8`, `pack10`, `chicken_single`, `boneless_single`, ランチ `burger_combi_lunch`, `burger_set_lunch`, `twister_set_lunch`, `chicken_biscuit_set_lunch`, `chicken2_set_lunch`, `yokubari_burger_set_lunch`, `yokubari_twister_set_lunch`, `chicken3_set_lunch`, 通常 `burger_set_regular`, `twister_set_regular`, `chicken2_set_regular`, `yokubari_burger_set_regular`, `yokubari_twister_set_regular`, `chicken3_set_regular`, 単品 `burger_single`, `twister_single`, `potato_s_single`, `potato_l_single`, `potato_box_single`, `crispy_single`, `chocopie_single`, `biscuit_single`, `nugget_single`, `coleslaw_single`, `drink_s_single`, `drink_m_single`, `drink_l_single`, `drink_hot_single`, `kids_nugget`, `kids_chicken`

- [ ] **Step 1: テストを v2 の期待値に書き換える**

テストは、本物の menu.json をランチ（一部は通常）の時間帯で読み込むように変わる。期待値は計画作成時に総当たりで照合した値。

`KfcMenu.Core.Tests/MenuParserTests.cs` の `Parse_RealMenu` を次に置き換える:

```csharp
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
```

`KfcMenu.Core.Tests/MenuTimeTests.cs` のクラスの最後（`AvailableAt_KeepsAnytimeAndMatchingOptions` の後）に追加する:

```csharp
    [Fact]
    public void AvailableAt_RealMenu()
    {
        var lunch = TestMenu.Load(ServiceTime.Lunch).Options;
        var regular = TestMenu.Load(ServiceTime.Regular).Options;

        Assert.Equal(30, lunch.Count);
        Assert.Equal(28, regular.Count);

        Assert.Contains(lunch, o => o.Name == "バーガーコンビ");
        Assert.Contains(lunch, o => o.Name == "チキン&ビスケットセット");
        Assert.DoesNotContain(regular, o => o.Name == "バーガーコンビ");
        Assert.DoesNotContain(regular, o => o.Name == "チキン&ビスケットセット");

        Assert.Equal(790, lunch.Single(o => o.Name == "バーガーセット").Price);
        Assert.Equal(890, regular.Single(o => o.Name == "バーガーセット").Price);
        Assert.Equal(590, regular.Single(o => o.Name == "キッズチキン").Price);
    }
```

`KfcMenu.Core.Tests/VariantExpanderTests.cs` の中身を次に置き換える:

```csharp
using KfcMenu.Core.Models;
using KfcMenu.Core.Services;

namespace KfcMenu.Core.Tests;

public class VariantExpanderTests
{
    [Fact]
    public void Expand_RealLunchMenu_Counts()
    {
        var variants = VariantExpander.Expand(TestMenu.Load(ServiceTime.Lunch).Options);

        Assert.Equal(174, variants.Count);
        Assert.Equal(15, variants.Count(v => v.Option.Name == "4ピースパック"));
        Assert.Equal(35, variants.Count(v => v.Option.Name == "6ピースパック"));
        Assert.Equal(70, variants.Count(v => v.Option.Name == "8ピースパック"));
        Assert.Equal(4, variants.Count(v => v.Option.Name == "バーガーセット"));
        Assert.Equal(1, variants.Count(v => v.Option.Name == "バーガーコンビ"));
        Assert.Equal(1, variants.Count(v => v.Option.Name == "単品チキン"));
    }

    [Fact]
    public void Expand_RealRegularMenu_Counts()
    {
        var variants = VariantExpander.Expand(TestMenu.Load(ServiceTime.Regular).Options);

        // ランチより、バーガーコンビ（1通り）とチキン&ビスケットセット（ドリンク4通り）の分だけ少ない
        Assert.Equal(169, variants.Count);
        Assert.Equal(4, variants.Count(v => v.Option.Name == "バーガーセット"));
        Assert.Equal(0, variants.Count(v => v.Option.Name == "バーガーコンビ"));
        Assert.Equal(0, variants.Count(v => v.Option.Name == "チキン&ビスケットセット"));
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

`KfcMenu.Core.Tests/CheapestComboFinderTests.cs` の中身を次に置き換える:

```csharp
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
```

`KfcMenu.Core.Tests/SetRecommenderTests.cs` の中身を次に置き換える:

```csharp
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
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: 失敗が多数（menu.json がまだ旧版なので、「4ピースパック」などの名前や新しい値段が見つからない）。ビルドエラーにはならない

- [ ] **Step 3: menu.json を v2 にする**

`KfcMenu.Web/wwwroot/data/menu.json` の中身を次に置き換える（先頭の `//` コメントも含めてこのとおりにする）:

```json
// メニューの一覧が記載されたjsonファイル
// products: 商品の一覧
    // id: メニューid
    // name: メニュー名
    // required: 必要数
    // MaxSurplus: 余剰の上限個数

// options: 購入選択肢の一覧
    // id: 内部識別子
    // name: 表示名
    // price: 価格
    // includes: オブジェクト
    // choices: 選べる枠。省略可
        // count: 枠の数
        // from: 候補の商品ID
        // allowDuplicate: 重複可否
        // label: 結果表示用（処理には使用しない）
    // availability: 注文できる時間帯。省略可
        // "lunch": ランチ時間（10:00〜15:00）だけ / "regular": ランチ時間外だけ / 省略: いつでも

{
    "products": [
        { "id": "chicken",    "name": "チキン",         "required": 0, "MaxSurplus": 0 },
        { "id": "boneless",   "name": "骨なしチキン",   "required": 0, "MaxSurplus": 0 },
        { "id": "burger",     "name": "バーガー",       "required": 0, "MaxSurplus": 0 },
        { "id": "twister",    "name": "ツイスター",     "required": 0, "MaxSurplus": 0 },
        { "id": "potato_s",   "name": "ポテトS",        "required": 0, "MaxSurplus": 0 },
        { "id": "potato_l",   "name": "ポテトL",        "required": 0, "MaxSurplus": 0 },
        { "id": "potato_box", "name": "ポテトBOX",      "required": 0, "MaxSurplus": 0 },
        { "id": "crispy",     "name": "クリスピー",     "required": 0, "MaxSurplus": 0 },
        { "id": "chocopie",   "name": "チョコパイ",     "required": 0, "MaxSurplus": 0 },
        { "id": "biscuit",    "name": "ビスケット",     "required": 0, "MaxSurplus": 0 },
        { "id": "nugget",     "name": "ナゲット",       "required": 0, "MaxSurplus": 0 },
        { "id": "coleslaw",   "name": "コールスロー",   "required": 0, "MaxSurplus": 0 },
        { "id": "drink_s",    "name": "ドリンク S",     "required": 0, "MaxSurplus": 0 },
        { "id": "drink_m",    "name": "ドリンク M",     "required": 0, "MaxSurplus": 0 },
        { "id": "drink_l",    "name": "ドリンク L",     "required": 0, "MaxSurplus": 0 },
        { "id": "drink_hot",  "name": "ホットドリンク", "required": 0, "MaxSurplus": 0 }
    ],

    "options": [
        {
            "id": "pack4", "name": "4ピースパック", "price": 1590,
            "includes": { "chicken": 4 },
            "choices": [
                { "count": 2, "from": ["potato_s", "crispy", "chocopie", "biscuit", "coleslaw"], "allowDuplicate": true, "label": "サイド" }
            ]
        },
        {
            "id": "pack6", "name": "6ピースパック", "price": 2390,
            "includes": { "chicken": 6 },
            "choices": [
                { "count": 3, "from": ["potato_s", "crispy", "chocopie", "biscuit", "coleslaw"], "allowDuplicate": true, "label": "サイド" }
            ]
        },
        {
            "id": "pack8", "name": "8ピースパック", "price": 2990,
            "includes": { "chicken": 8 },
            "choices": [
                { "count": 4, "from": ["potato_s", "crispy", "chocopie", "biscuit", "coleslaw"], "allowDuplicate": true, "label": "サイド" }
            ]
        },
        { "id": "pack10",          "name": "10ピースパック", "price": 3300, "includes": { "chicken": 10 } },
        { "id": "chicken_single",  "name": "単品チキン",     "price": 330,  "includes": { "chicken": 1 } },
        { "id": "boneless_single", "name": "骨なしチキン",   "price": 330,  "includes": { "boneless": 1 } },

        { "id": "burger_combi_lunch", "name": "バーガーコンビ", "price": 590, "availability": "lunch", "includes": { "burger": 1, "potato_s": 1 } },
        {
            "id": "burger_set_lunch", "name": "バーガーセット", "price": 790, "availability": "lunch",
            "includes": { "burger": 1, "potato_s": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "twister_set_lunch", "name": "ツイスターセット", "price": 690, "availability": "lunch",
            "includes": { "twister": 1, "potato_s": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "chicken_biscuit_set_lunch", "name": "チキン&ビスケットセット", "price": 590, "availability": "lunch",
            "includes": { "chicken": 1, "potato_s": 1, "biscuit": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "chicken2_set_lunch", "name": "チキン2ピースセット", "price": 940, "availability": "lunch",
            "includes": { "chicken": 2, "potato_s": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "yokubari_burger_set_lunch", "name": "欲張りバーガーセット", "price": 1090, "availability": "lunch",
            "includes": { "chicken": 1, "potato_s": 1, "burger": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "yokubari_twister_set_lunch", "name": "欲張りツイスターセット", "price": 990, "availability": "lunch",
            "includes": { "chicken": 1, "potato_s": 1, "twister": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "chicken3_set_lunch", "name": "チキン3ピースセット", "price": 1240, "availability": "lunch",
            "includes": { "chicken": 3, "potato_s": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },

        {
            "id": "burger_set_regular", "name": "バーガーセット", "price": 890, "availability": "regular",
            "includes": { "burger": 1, "potato_s": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "twister_set_regular", "name": "ツイスターセット", "price": 790, "availability": "regular",
            "includes": { "twister": 1, "potato_s": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "chicken2_set_regular", "name": "チキン2ピースセット", "price": 1020, "availability": "regular",
            "includes": { "chicken": 2, "potato_s": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "yokubari_burger_set_regular", "name": "欲張りバーガーセット", "price": 1190, "availability": "regular",
            "includes": { "chicken": 1, "potato_s": 1, "burger": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "yokubari_twister_set_regular", "name": "欲張りツイスターセット", "price": 1090, "availability": "regular",
            "includes": { "chicken": 1, "potato_s": 1, "twister": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "chicken3_set_regular", "name": "チキン3ピースセット", "price": 1320, "availability": "regular",
            "includes": { "chicken": 3, "potato_s": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },

        { "id": "burger_single",  "name": "バーガー",   "price": 490, "includes": { "burger": 1 } },
        { "id": "twister_single", "name": "ツイスター", "price": 390, "includes": { "twister": 1 } },

        { "id": "potato_s_single",   "name": "ポテトS",      "price": 290, "includes": { "potato_s": 1 } },
        { "id": "potato_l_single",   "name": "ポテトL",      "price": 490, "includes": { "potato_l": 1 } },
        { "id": "potato_box_single", "name": "ポテトBOX",    "price": 990, "includes": { "potato_box": 1 } },
        { "id": "crispy_single",     "name": "クリスピー",   "price": 290, "includes": { "crispy": 1 } },
        { "id": "chocopie_single",   "name": "チョコパイ",   "price": 290, "includes": { "chocopie": 1 } },
        { "id": "biscuit_single",    "name": "ビスケット",   "price": 290, "includes": { "biscuit": 1 } },
        { "id": "nugget_single",     "name": "ナゲット",     "price": 480, "includes": { "nugget": 1 } },
        { "id": "coleslaw_single",   "name": "コールスロー", "price": 290, "includes": { "coleslaw": 1 } },

        { "id": "drink_s_single",   "name": "ドリンク S",     "price": 270, "includes": { "drink_s": 1 } },
        { "id": "drink_m_single",   "name": "ドリンク M",     "price": 290, "includes": { "drink_m": 1 } },
        { "id": "drink_l_single",   "name": "ドリンク L",     "price": 340, "includes": { "drink_l": 1 } },
        { "id": "drink_hot_single", "name": "ホットドリンク", "price": 290, "includes": { "drink_hot": 1 } },

        {
            "id": "kids_nugget", "name": "キッズナゲット", "price": 590,
            "includes": { "nugget": 1, "potato_s": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        },
        {
            "id": "kids_chicken", "name": "キッズチキン", "price": 590,
            "includes": { "chicken": 1, "potato_s": 1 },
            "choices": [
                { "count": 1, "from": ["drink_s", "drink_m", "drink_l", "drink_hot"], "allowDuplicate": false, "label": "ドリンク" }
            ]
        }
    ]
}
```

- [ ] **Step 4: テストが通ることを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: `Passed!  - Failed: 0, Passed: 61`

Run: `dotnet build KfcMenu.slnx --no-incremental`
Expected: `0 Warning(s)` `0 Error(s)`

- [ ] **Step 5: コミット**

```bash
git add KfcMenu.Web/wwwroot/data/menu.json KfcMenu.Core.Tests/MenuParserTests.cs KfcMenu.Core.Tests/MenuTimeTests.cs KfcMenu.Core.Tests/VariantExpanderTests.cs KfcMenu.Core.Tests/CheapestComboFinderTests.cs KfcMenu.Core.Tests/SetRecommenderTests.cs
git commit -m "Update menu to the v2 names and prices with lunch and regular sets"
```

---

## Task 3: 画面の切り替えと設計書の更新

**Files:**
- Modify: `KfcMenu.Web/Pages/Home.razor`（全体を置き換える。CRLF）
- Modify: `KfcMenu.Web/wwwroot/css/app.css`（末尾に追記。CRLF）
- Modify: `docs/superpowers/specs/2026-10-30-lunch-menu-v2-design.md`
- Modify: `docs/superpowers/specs/2026-09-05-kfc-menu-optimizer-design.md`

**Interfaces:**
- Consumes: `MenuTime.At`, `MenuTime.AvailableAt`, `ServiceTime`（Task 1）、v2 の menu.json（Task 2）、`CheapestComboFinder`, `VariantExpander`, `SetRecommender`, `MenuParser`（既存）
- 入力欄の `aria-label` は `"{商品名}の必要数"` のまま。切り替えボタンは `.service-time` の中の `button` 2つで、選ばれている方に `selected` クラスと `aria-pressed="true"`。`<base href="/" />` は変えない。

- [ ] **Step 1: Home.razor を置き換える**

`KfcMenu.Web/Pages/Home.razor` の中身を次にする:

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
            </div>
            @foreach (var product in menu.Products)
            {
                <div class="product-row">
                    <span class="name">@product.Name</span>
                    <input type="number" min="0" max="99" inputmode="numeric" aria-label="@($"{product.Name}の必要数")" @bind="product.Required" />
                </div>
            }
        </div>

        <button class="calc" @onclick="Calculate" disabled="@calculating">計算</button>

        <div class="service-time" role="group" aria-label="時間帯">
            <button type="button" class="@(serviceTime == ServiceTime.Lunch ? "selected" : "")"
                    aria-pressed="@(serviceTime == ServiceTime.Lunch ? "true" : "false")"
                    disabled="@calculating" @onclick="() => SelectServiceTime(ServiceTime.Lunch)">ランチ<span class="hours">10:00〜15:00</span></button>
            <button type="button" class="@(serviceTime == ServiceTime.Regular ? "selected" : "")"
                    aria-pressed="@(serviceTime == ServiceTime.Regular ? "true" : "false")"
                    disabled="@calculating" @onclick="() => SelectServiceTime(ServiceTime.Regular)">通常</button>
        </div>

        @if (calculating)
        {
            <p class="calculating">計算中...</p>
        }
        else if (inputError is not null)
        {
            <p class="error">@inputError</p>
        }
        else if (calculated)
        {
            <section class="cheapest">
                <h2>最安</h2>
                @if (result is null)
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
            </section>

            <section class="recommendations">
                <h2>セットでおすすめ</h2>
                @if (recommendations.Count == 0)
                {
                    <p class="result">セットを使ったおすすめはありません</p>
                }
                @foreach (var recommendation in recommendations)
                {
                    <div class="recommendation">
                        <ul class="result">
                            @foreach (var item in recommendation.Items)
                            {
                                <li>@item.Option.Name × @item.Count@DescribePicks(item)</li>
                            }
                        </ul>
                        <p class="total">合計 @recommendation.TotalPrice.ToString("N0")円 <span class="diff">@DescribeDifference(recommendation)</span></p>
                        @if (recommendation.Savings > 0)
                        {
                            <p class="savings">単品より@(recommendation.Savings.ToString("N0"))円お得</p>
                        }
                        @if (DescribeExtras(recommendation) is { Length: > 0 } extras)
                        {
                            <p class="extras">追加で付くもの: @extras</p>
                        }
                    </div>
                }
            </section>
        }
    }
</main>

@code {
    private MenuData? menu;
    private ServiceTime serviceTime;
    private List<MenuOption> options = new();
    private List<Variant> variants = new();
    private Dictionary<string, string> productNames = new();
    private string? loadError;
    private string? inputError;
    private ComboResult? result;
    private List<Recommendation> recommendations = new();
    private bool calculated;
    private bool calculating;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var json = await Http.GetStringAsync("data/menu.json");
            menu = MenuParser.Parse(json);
            productNames = menu.Products.ToDictionary(p => p.Id, p => p.Name);
            // 開いたときの時間帯は、スマホ（ブラウザ）の現在時刻で決める
            serviceTime = MenuTime.At(TimeOnly.FromDateTime(DateTime.Now));
            ApplyServiceTime();
        }
        catch (Exception ex)
        {
            loadError = ex.Message;
        }
    }

    // 選んだ時間帯で注文できる購入選択肢だけで計算する
    private void ApplyServiceTime()
    {
        options = MenuTime.AvailableAt(menu!.Options, serviceTime);
        variants = VariantExpander.Expand(options);
    }

    // 時間帯を切り替えたら、前の時間帯で出した結果は消す（必要数の入力は残す）
    private void SelectServiceTime(ServiceTime time)
    {
        if (calculating || time == serviceTime)
        {
            return;
        }
        serviceTime = time;
        ApplyServiceTime();
        ClearResults();
    }

    private void ClearResults()
    {
        calculated = false;
        result = null;
        recommendations = new();
        inputError = null;
    }

    private async Task Calculate()
    {
        if (calculating)
        {
            return;
        }
        ClearResults();

        // 0〜99に限ることで、探索の深さと状態数が抑えられる（大きすぎる数でスタックが溢れない）
        if (menu!.Products.Any(p => p.Required is < 0 or > 99))
        {
            inputError = "必要数には0〜99の数を入力してください。";
            return;
        }

        // 最安は「頼んだものだけでちょうど」なので、余剰は0にする
        foreach (var product in menu.Products)
        {
            product.MaxSurplus = 0;
        }

        // 計算は同期で重いことがあるので、先に「計算中...」を描画させてから始める
        calculating = true;
        try
        {
            StateHasChanged();
            await Task.Delay(1);
            result = CheapestComboFinder.FindCheapest(variants, menu.Products);
            recommendations = SetRecommender.Recommend(options, menu.Products, result);
            calculated = true;
        }
        finally
        {
            calculating = false;
        }
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

    // おすすめでは、同じものが続くときは「お好きなサイド ×4」のようにまとめる
    private string DescribePicks(RecommendedItem item)
    {
        if (item.Picks.Count == 0)
        {
            return "";
        }
        var parts = new List<string>();
        var offset = 0;
        foreach (var slot in item.Option.Choices)
        {
            var names = item.Picks.Skip(offset).Take(slot.Count)
                .Select(id => id is null ? $"お好きな{slot.Label}" : productNames.GetValueOrDefault(id, id));
            var grouped = names
                .GroupBy(name => name)
                .Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ×{g.Count()}");
            parts.Add($"{slot.Label}: {string.Join(", ", grouped)}");
            offset += slot.Count;
        }
        return $"（{string.Join(" / ", parts)}）";
    }

    private string DescribeDifference(Recommendation recommendation)
    {
        if (result is null)
        {
            return "";
        }
        var difference = recommendation.TotalPrice - result.TotalPrice;
        return difference switch
        {
            > 0 => $"（最安より+{difference:N0}円）",
            < 0 => $"（最安より{-difference:N0}円安い）",
            _ => "（最安と同額）",
        };
    }

    private string DescribeExtras(Recommendation recommendation)
    {
        var parts = recommendation.Surplus
            .Select(kv => $"{productNames.GetValueOrDefault(kv.Key, kv.Key)} +{kv.Value}")
            .Concat(recommendation.FreeChoices.Select(kv => $"お好きな{kv.Key} ×{kv.Value}"));
        return string.Join(", ", parts);
    }
}
```

書いたあとに改行を CRLF にそろえる:

```bash
perl -pi -e 's/\r?\n/\r\n/' KfcMenu.Web/Pages/Home.razor
```

- [ ] **Step 2: app.css に追記する**

`KfcMenu.Web/wwwroot/css/app.css` の末尾に追記する:

```css
.service-time {
    display: flex;
    gap: 0.5rem;
    margin: -0.5rem 0 1rem;
}

.service-time button {
    flex: 1;
    padding: 0.6rem 0.3rem;
    font-size: 0.95rem;
    border: 1px solid #c8102e;
    border-radius: 0.5rem;
    background: #fff;
    color: #c8102e;
}

.service-time button.selected {
    background: #c8102e;
    color: #fff;
    font-weight: bold;
}

.service-time button:disabled {
    opacity: 0.6;
}

.service-time .hours {
    display: block;
    font-size: 0.75rem;
    font-weight: normal;
}
```

書いたあとに CRLF にそろえる:

```bash
perl -pi -e 's/\r?\n/\r\n/' KfcMenu.Web/wwwroot/css/app.css
```

- [ ] **Step 3: ビルドとテスト**

Run: `dotnet build KfcMenu.slnx --no-incremental && dotnet test KfcMenu.slnx`
Expected: 警告0件、エラー0件、テスト61件成功

Run: `git diff --stat`
Expected: Home.razor と app.css の差分が変更した行だけ（改行コードが保たれている）

- [ ] **Step 4: Release 発行・サブパス配信・ブラウザ確認**

一時ディレクトリ `<一時>` は `/private/tmp/claude-501/-Users-yoshizawayuki-KFC/ba800926-6e56-4582-9a90-2439ad79cc3c/scratchpad/v2-task3`（作成してよい）。

```bash
dotnet publish KfcMenu.Web/KfcMenu.Web.csproj -c Release -o <一時>/publish
mkdir -p <一時>/site && cp -R <一時>/publish/wwwroot <一時>/site/kfc-menu
sed -i '' -e 's|<base href="/" />|<base href="/kfc-menu/" />|' -- <一時>/site/kfc-menu/index.html
python3 -m http.server 8766 --bind 127.0.0.1 --directory <一時>/site      # バックグラウンドで
open -na "Google Chrome" --args --headless=new --disable-gpu --no-first-run --user-data-dir=<一時>/chrome --remote-debugging-port=9333 about:blank
node /private/tmp/claude-501/-Users-yoshizawayuki-KFC/ba800926-6e56-4582-9a90-2439ad79cc3c/scratchpad/v2/v2-check.mjs http://127.0.0.1:8766/kfc-menu/
node /private/tmp/claude-501/-Users-yoshizawayuki-KFC/ba800926-6e56-4582-9a90-2439ad79cc3c/scratchpad/v2/shot.mjs http://127.0.0.1:8766/kfc-menu/ <一時>/phone.png
```

`v2-check.mjs` は、ブラウザのタイムゾーンを変えて開いたときの初期値（12時台→ランチ、20時台→通常）、ランチと通常のバーガーセットの値段（790円／890円）、切り替えで結果が消えて入力が残ること、通常でチキン&ビスケットセットが出ないこと、ドリンクの名前などを確かめる。

Expected: 最後の行が `ALL PASSED`。`phone.png`（幅375px）を開き、計算ボタンの下に「ランチ / 10:00〜15:00」と「通常」の2つのボタンが横に並び、選ばれている方が赤く塗られていることを目で確認する。確認後、HTTPサーバーと Chrome を止める（`pkill -f "<一時>/chrome"`、`pkill -f "http.server 8766"`）。

- [ ] **Step 5: 設計書を更新する**

`docs/superpowers/specs/2026-10-30-lunch-menu-v2-design.md`:
1. `- ステータス: ユーザー確認待ち` → `- ステータス: 承認済み・実装済み`
2. 画面の節の `- 「計算」ボタンのすぐ下に、2択の切り替えを置く: 「ランチ（10:00〜15:00）」「通常」。選ばれている方がわかる見た目にする` → `- 「計算」ボタンのすぐ下に、2択の切り替えを置く: 「ランチ」（下に小さく「10:00〜15:00」）と「通常」。選ばれている方を塗りつぶして表示する（スマホ幅で時刻が途中で折り返さないよう、時刻は2行目に分けた）`

`docs/superpowers/specs/2026-09-05-kfc-menu-optimizer-design.md` の「画面（Home.razor）」の節の最後の行（`- スマホで見やすいように、1列の縦並びにする`）の次に追加する:
`- 2026-10-30（ver2.0）から、計算ボタンの下にランチ／通常の切り替えがある（詳細は docs/superpowers/specs/2026-10-30-lunch-menu-v2-design.md）`

- [ ] **Step 6: コミット**

```bash
git add KfcMenu.Web/Pages/Home.razor KfcMenu.Web/wwwroot/css/app.css docs/superpowers/specs/2026-10-30-lunch-menu-v2-design.md docs/superpowers/specs/2026-09-05-kfc-menu-optimizer-design.md
git status
git commit -m "Add the lunch/regular switch below the calculate button"
```

---

## リリース（全タスクとレビューの後。ユーザーの確認を取りながらコントローラーが行う）

1. main の `3cf9cc8` に `v1.0` タグを付ける: `git tag -a v1.0 3cf9cc8 -m "ver1.0"`
2. `v2-lunch-menu` を push し、プルリクエストを作る（本文はコントローラーが用意し、ユーザーが作成・マージする）
3. マージ後、手元の main を更新して `v2.0` タグを付ける: `git tag -a v2.0 -m "ver2.0: ランチ／通常メニューの切り替え"`
4. タグを送る: `git push origin v1.0 v2.0`
5. GitHub Actions の公開が成功し、https://yossan1208.github.io/KFCMenu/ で切り替えが動くことを確認する
