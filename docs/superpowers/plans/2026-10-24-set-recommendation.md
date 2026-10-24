# セットのおすすめ表示 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 最安の買い方に加えて、セットを使った「おすすめ」を最大3つ（単品が少ない順・最安の1.5倍まで）表示し、「余剰の上限」の入力欄をなくす。

**Architecture:** 計算ロジック（KfcMenu.Core）に `SetRecommender` を追加する。注文ごとに選べる枠を「頼んだ商品」か「お好きなもの」に展開し、「各商品の合計個数（必要数で頭打ち）」を状態にしたメモ化探索で、状態ごとに（単品の個数, 金額）順の上位40件を k-way merge で保持する。最後に「セットを含む・無駄がない・最安の1.5倍以下・最安と別の組み合わせ」で絞って3件返す。既存の `CheapestComboFinder` は変えず、画面から余剰0で呼ぶ。

**Tech Stack:** C# / .NET 10、Blazor WebAssembly、xUnit

**Spec:** `docs/superpowers/specs/2026-10-24-set-recommendation-design.md`（元の設計書: `docs/superpowers/specs/2026-09-05-kfc-menu-optimizer-design.md`）

## Global Constraints

- 作業ディレクトリ: `/Users/yoshizawayuki/KFC/.claude/worktrees/kfc-menu-blazor`（git worktree、ブランチ `worktree-kfc-menu-blazor`）。`/Users/yoshizawayuki/KFC` を直接触らない。push しない、ブランチを作らない。
- おすすめの条件: 必要数以上が揃う / セット（品数2以上）を1つ以上含む / どの購入選択肢を1つ抜いても必要数を割る / 合計金額 ≤ 最安 × 3 / 2（整数の切り捨て。最安が null なら上限なし）/ 購入選択肢ごとの個数が最安と同じでない。並べ方は（単品の個数, 金額）の昇順、最大3つ。
- 選べる枠の席は「必要数が1以上の候補商品」か「お好きなもの（null）」。お好きなものは必要数に数えず、単品換算はその枠の候補の単品価格の最小値。表示は「お好きな{label}」。
- 入力は必要数だけ（0〜99）。範囲外のメッセージは `必要数には0〜99の数を入力してください。`
- `CheapestComboFinder`、`VariantExpander`、`MenuParser`、`KfcMenu.Web/wwwroot/data/menu.json` は変更しない。既存の18件のテストは変更せずに通ること。
- ビルド警告0件。KfcMenu.Core と KfcMenu.Core.Tests のファイルは改行コード LF、KfcMenu.Web のファイル（Home.razor, app.css）は CRLF のまま保つ。
- 名前空間: モデル `KfcMenu.Core.Models`、サービス `KfcMenu.Core.Services`、テスト `KfcMenu.Core.Tests`。

## Review Focus

- **必要数がすべて0**: 例外なく「セットを使ったおすすめはありません」（Task 1 `NothingRequired_ReturnsEmpty`、Task 2 ブラウザ確認 zero）。
- **頼んだものぴったりの最安が存在しない（最安が null）**: 上限なしでおすすめを出す（Task 1 `NoExactCheapest_RecommendsWithoutPriceCap`）。
- **頼んだ商品が選べる枠の中にしかない、または上限で全部落ちる**: クリスピー2 は空になる（Task 1 `NoSetUnderThePriceCap_ReturnsEmpty`）。
- **頼んだ商品と「お好きなもの」が同じ枠に混ざる**: 「サイド: ビスケット, お好きなサイド」と表示される（Task 2 ブラウザ確認 family4）。
- **大量注文の速さ**: スマホのブラウザで固まる時間。計画作成時の計測を下回らないこと（Task 2 で計測し、設計書に記録する）。

## 検証済みの事項（計画作成時）

この計画のコードは、計画作成時にプロジェクトの複製で実行して確認済み。
- `dotnet test`: 28件（既存18 + 新規10）すべて成功、ビルド警告0件
- 新規テストの期待値は、途中で打ち切らない総当たりの基準スクリプトの結果と一致（同額のときの選び方の違いを除く）
- 速さ（PC上の.NET、Release / ブラウザ = PC上のヘッドレスChrome、Release発行、`/kfc-menu/` 配信）:

| 注文 | PC: おすすめのみ | ブラウザ: 最安 + おすすめ |
|---|---|---|
| チキン8 | — | 47ms |
| 家族4人（チキン6・ポテトS2・ドリンクS2・バーガー1・ビスケット2） | 15ms | 43ms |
| G: チキン30・ポテトS10・ドリンクS10・ビスケット4 | 18ms | 396ms |
| H: チキン12・ポテトS6・ドリンクS6・ビスケット4・クリスピー3・チョコパイ2・コールスロー2・バーガー2 | 260ms | 1,054ms（うち最安探索が約0.9秒） |
| 全16商品を必要数2 | 9ms | — |

- 最初の素朴な実装では H が PC で67秒かかった。(1) k-way merge と連結リスト、(2) UInt128 の状態キーと状態配列の使い回し、(3) 含む候補が少ない商品から埋める順序、の3点で上の速さになった。この3点は外さないこと。

---

## Task 1: SetRecommender（おすすめの探索）

**Files:**
- Create: `KfcMenu.Core/Models/RecommendedItem.cs`
- Create: `KfcMenu.Core/Models/Recommendation.cs`
- Create: `KfcMenu.Core/Services/SetRecommender.cs`
- Create: `KfcMenu.Core.Tests/SetRecommenderTests.cs`

**Interfaces:**
- Consumes: `MenuOption`, `ChoiceSlot`, `Product`, `ComboResult`（既存）、`CheapestComboFinder.FindCheapest`, `VariantExpander.Expand`, `TestMenu.Load()`, `TestMenu.WithConditions(MenuData, params (string Id, int Required, int MaxSurplus)[])`（既存）
- Produces:
  - `RecommendedItem { required MenuOption Option; List<string?> Picks; int Count }`（Picks は枠の順、null はお好きなもの）
  - `Recommendation { List<RecommendedItem> Items; int TotalPrice; int SingleItemTotal; int Savings (= SingleItemTotal − TotalPrice); Dictionary<string,int> Surplus (商品ID → 必要数を超える個数); Dictionary<string,int> FreeChoices (枠のlabel → お好きなものの個数) }`
  - `static List<Recommendation> SetRecommender.Recommend(IReadOnlyList<MenuOption> options, IReadOnlyList<Product> products, ComboResult? cheapest)`。products の `Required` を必要数として使う（`MaxSurplus` は見ない）。`SetRecommender.MaxRecommendations = 3`

- [ ] **Step 1: 失敗するテストを書く**

`KfcMenu.Core.Tests/SetRecommenderTests.cs`:

```csharp
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
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: ビルドエラー（`Recommendation` や `SetRecommender` が見つからない）

- [ ] **Step 3: モデルを書く**

`KfcMenu.Core/Models/RecommendedItem.cs`:

```csharp
namespace KfcMenu.Core.Models;

public class RecommendedItem
{
    public required MenuOption Option { get; init; }

    // 選べる枠の順に並べた商品ID。null は「お好きなもの」（頼んだ商品として数えない席）
    public List<string?> Picks { get; init; } = new();

    public int Count { get; init; }
}
```

`KfcMenu.Core/Models/Recommendation.cs`:

```csharp
namespace KfcMenu.Core.Models;

public class Recommendation
{
    public List<RecommendedItem> Items { get; init; } = new();
    public int TotalPrice { get; init; }

    // 同じ中身をすべて単品価格で買った場合の合計
    public int SingleItemTotal { get; init; }

    public int Savings => SingleItemTotal - TotalPrice;

    // 商品ID → 必要数を超えて付いてくる個数
    public Dictionary<string, int> Surplus { get; init; } = new();

    // 選べる枠のラベル → 「お好きなもの」の個数
    public Dictionary<string, int> FreeChoices { get; init; } = new();
}
```

- [ ] **Step 4: SetRecommender を書く**

`KfcMenu.Core/Services/SetRecommender.cs`:

```csharp
using KfcMenu.Core.Models;

namespace KfcMenu.Core.Services;

public static class SetRecommender
{
    public const int MaxRecommendations = 3;

    // 状態ごとに残す候補の数。小さいほど速いが、絞り込みで候補が多く落ちる注文では上位を見落としうる
    private const int PoolSize = 40;

    public static List<Recommendation> Recommend(
        IReadOnlyList<MenuOption> options, IReadOnlyList<Product> products, ComboResult? cheapest)
    {
        var requested = products.Where(p => p.Required > 0).ToList();
        if (requested.Count == 0)
        {
            return [];
        }

        var priceCap = cheapest is null ? int.MaxValue : cheapest.TotalPrice * 3 / 2;
        var cheapestSignature = cheapest is null
            ? null
            : Signature(cheapest.Items.Select(i => (i.Variant.Option, i.Count)));

        var candidates = RequestVariant.ExpandAll(options, requested, SinglePrices(options))
            .Where(v => v.Price <= priceCap)
            .ToList();
        var need = requested.Select(p => p.Required).ToArray();
        var search = new Search(need, candidates, priceCap);
        var rootPlans = search.Solve(UInt128.Zero);

        var result = new List<Recommendation>();
        var seen = new HashSet<string>();
        foreach (var plan in rootPlans)
        {
            var bought = plan.ToSortedArray();
            if (!bought.Any(j => candidates[j].IsSet) || !IsMinimal(bought, candidates, need))
            {
                continue;
            }
            var signature = Signature(Group(bought).Select(g => (candidates[g.Index].Option, g.Count)));
            if (signature == cheapestSignature || !seen.Add(signature))
            {
                continue;
            }
            result.Add(Build(plan.Price, bought, candidates, products));
            if (result.Count == MaxRecommendations)
            {
                break;
            }
        }
        return result;
    }

    // 商品ID → 「選べる枠がなく、その商品1個だけ」の購入選択肢の最安値
    private static Dictionary<string, int> SinglePrices(IReadOnlyList<MenuOption> options)
    {
        var prices = new Dictionary<string, int>();
        foreach (var option in options)
        {
            if (option.Choices.Count == 0 && option.Includes.Count == 1)
            {
                var (id, count) = option.Includes.First();
                if (count == 1 && (!prices.TryGetValue(id, out var current) || option.Price < current))
                {
                    prices[id] = option.Price;
                }
            }
        }
        return prices;
    }

    private static bool IsMinimal(int[] plan, List<RequestVariant> candidates, int[] need)
    {
        var totals = new int[need.Length];
        foreach (var j in plan)
        {
            for (var k = 0; k < need.Length; k++)
            {
                totals[k] += candidates[j].Amounts[k];
            }
        }
        foreach (var j in plan.Distinct())
        {
            var stillCovered = true;
            for (var k = 0; k < need.Length; k++)
            {
                if (totals[k] - candidates[j].Amounts[k] < need[k])
                {
                    stillCovered = false;
                    break;
                }
            }
            if (stillCovered)
            {
                return false;
            }
        }
        return true;
    }

    // 購入選択肢ごとの個数が同じなら同じ組み合わせとみなす
    private static string Signature(IEnumerable<(MenuOption Option, int Count)> items) =>
        string.Join(",", items
            .GroupBy(i => i.Option.Id)
            .Select(g => $"{g.Key}x{g.Sum(i => i.Count)}")
            .Order(StringComparer.Ordinal));

    private static IEnumerable<(int Index, int Count)> Group(int[] plan) =>
        plan.GroupBy(j => j).Select(g => (g.Key, g.Count()));

    private static Recommendation Build(int price, int[] bought, List<RequestVariant> candidates, IReadOnlyList<Product> products)
    {
        var items = new List<RecommendedItem>();
        var contents = new Dictionary<string, int>();
        var freeChoices = new Dictionary<string, int>();
        var singleItemTotal = 0;
        foreach (var (index, count) in Group(bought))
        {
            var variant = candidates[index];
            items.Add(new RecommendedItem { Option = variant.Option, Picks = variant.Picks, Count = count });
            singleItemTotal += variant.SingleItemValue * count;
            foreach (var (id, n) in variant.Contents)
            {
                contents[id] = contents.GetValueOrDefault(id) + n * count;
            }
            foreach (var (label, n) in variant.FreeChoices)
            {
                freeChoices[label] = freeChoices.GetValueOrDefault(label) + n * count;
            }
        }

        var surplus = new Dictionary<string, int>();
        foreach (var product in products)
        {
            var extra = contents.GetValueOrDefault(product.Id) - product.Required;
            if (extra > 0)
            {
                surplus[product.Id] = extra;
            }
        }

        return new Recommendation
        {
            Items = items,
            TotalPrice = price,
            SingleItemTotal = singleItemTotal,
            Surplus = surplus,
            FreeChoices = freeChoices,
        };
    }

    // 選べる枠の席を「頼んだ商品」か「お好きなもの」に決めた購入選択肢
    private sealed class RequestVariant
    {
        public required MenuOption Option { get; init; }
        public required List<string?> Picks { get; init; }
        public required int[] Amounts { get; init; }
        public required Dictionary<string, int> Contents { get; init; }
        public required Dictionary<string, int> FreeChoices { get; init; }
        public required int SingleItemValue { get; init; }
        public required bool IsSet { get; init; }
        public required bool IsSingle { get; init; }
        public int Price => Option.Price;

        public static IEnumerable<RequestVariant> ExpandAll(
            IReadOnlyList<MenuOption> options, List<Product> requested, Dictionary<string, int> singlePrices)
        {
            var indexOf = new Dictionary<string, int>();
            for (var k = 0; k < requested.Count; k++)
            {
                indexOf[requested[k].Id] = k;
            }

            foreach (var option in options)
            {
                var itemCount = option.Includes.Values.Sum() + option.Choices.Sum(c => c.Count);
                foreach (var picks in ExpandSlots(option.Choices, 0, indexOf))
                {
                    var contents = new Dictionary<string, int>(option.Includes);
                    var freeChoices = new Dictionary<string, int>();
                    // 単品価格がない商品は単品換算に含めない
                    var value = option.Includes.Sum(kv => singlePrices.GetValueOrDefault(kv.Key) * kv.Value);
                    var seat = 0;
                    foreach (var slot in option.Choices)
                    {
                        for (var s = 0; s < slot.Count; s++, seat++)
                        {
                            var id = picks[seat];
                            if (id is null)
                            {
                                freeChoices[slot.Label] = freeChoices.GetValueOrDefault(slot.Label) + 1;
                                value += slot.From.Min(f => singlePrices.GetValueOrDefault(f));
                            }
                            else
                            {
                                contents[id] = contents.GetValueOrDefault(id) + 1;
                                value += singlePrices.GetValueOrDefault(id);
                            }
                        }
                    }

                    var amounts = new int[requested.Count];
                    foreach (var (id, count) in contents)
                    {
                        if (indexOf.TryGetValue(id, out var k))
                        {
                            amounts[k] = count;
                        }
                    }
                    if (amounts.All(a => a == 0))
                    {
                        continue;
                    }

                    yield return new RequestVariant
                    {
                        Option = option,
                        Picks = picks,
                        Amounts = amounts,
                        Contents = contents,
                        FreeChoices = freeChoices,
                        SingleItemValue = value,
                        IsSet = itemCount >= 2,
                        IsSingle = itemCount == 1,
                    };
                }
            }
        }

        private static IEnumerable<List<string?>> ExpandSlots(List<ChoiceSlot> slots, int slotIndex, Dictionary<string, int> requested)
        {
            if (slotIndex == slots.Count)
            {
                yield return [];
                yield break;
            }
            var slot = slots[slotIndex];
            var wanted = slot.From.Where(requested.ContainsKey).ToList();
            foreach (var head in SlotPicks(wanted, slot.Count, slot.AllowDuplicate))
            {
                foreach (var rest in ExpandSlots(slots, slotIndex + 1, requested))
                {
                    yield return [.. head, .. rest];
                }
            }
        }

        // 頼んだ商品を wanted の順に先に並べ、残りの席を null（お好きなもの）で埋める。順番違いの重複は出さない
        private static IEnumerable<List<string?>> SlotPicks(List<string> wanted, int count, bool allowDuplicate)
        {
            for (var used = 0; used <= count; used++)
            {
                foreach (var chosen in Combine(wanted, used, 0, allowDuplicate))
                {
                    yield return [.. chosen, .. Enumerable.Repeat<string?>(null, count - used)];
                }
            }
        }

        private static IEnumerable<List<string?>> Combine(List<string> from, int count, int start, bool allowDuplicate)
        {
            if (count == 0)
            {
                yield return [];
                yield break;
            }
            for (var i = start; i < from.Count; i++)
            {
                foreach (var rest in Combine(from, count - 1, allowDuplicate ? i : i + 1, allowDuplicate))
                {
                    yield return [from[i], .. rest];
                }
            }
        }
    }

    // 買い方。最後に足した候補（Variant）と、それ以前の買い方（Rest）をつないだ連結リストで持つ。
    // Hash は買った候補の多重集合のハッシュ（順番によらない）で、同じ組み合わせの重複を除くのに使う
    private sealed record Plan(int Singles, int Price, long Hash, int Variant, Plan? Rest)
    {
        public static readonly Plan Empty = new(0, 0, 0, -1, null);

        public int[] ToSortedArray()
        {
            var list = new List<int>();
            for (var plan = this; plan.Rest is not null; plan = plan.Rest)
            {
                list.Add(plan.Variant);
            }
            list.Sort();
            return [.. list];
        }
    }

    private sealed class Search
    {
        private readonly int[] need;
        private readonly List<RequestVariant> candidates;
        private readonly int priceCap;
        private readonly int[][] variantsWith;
        private readonly int[] fillOrder;
        private readonly long[] hashOf;

        // 状態（頼んだ商品ごとの合計個数、必要数で頭打ち）を1つの配列で持ち、足す→再帰→戻すで使い回す
        private readonly int[] state;

        // 候補ごと: 0個でない商品の番号と個数
        private readonly int[][] productsOf;
        private readonly int[][] countsOf;

        // 状態のキー = Σ state[k] × radix[k]、radix[k] = Π_{i<k} (need[i] + 1)。
        // 必要数は0〜99、商品は最大16種類なので、最大 100^16 − 1 < 2^107 で UInt128 に収まり、状態ごとに一意
        private readonly UInt128[] radix;
        private readonly Dictionary<UInt128, List<Plan>> memo = new();

        public Search(int[] need, List<RequestVariant> candidates, int priceCap)
        {
            this.need = need;
            this.candidates = candidates;
            this.priceCap = priceCap;
            state = new int[need.Length];
            variantsWith = new int[need.Length][];
            for (var k = 0; k < need.Length; k++)
            {
                var product = k;
                variantsWith[k] = Enumerable.Range(0, candidates.Count).Where(j => candidates[j].Amounts[product] > 0).ToArray();
            }

            // どの商品が足りなくても正しく探せるが、含む候補が少ない商品から埋めると分岐が減って速い
            fillOrder = Enumerable.Range(0, need.Length).OrderBy(k => variantsWith[k].Length).ToArray();

            productsOf = new int[candidates.Count][];
            countsOf = new int[candidates.Count][];
            for (var j = 0; j < candidates.Count; j++)
            {
                var amounts = candidates[j].Amounts;
                productsOf[j] = Enumerable.Range(0, amounts.Length).Where(k => amounts[k] > 0).ToArray();
                countsOf[j] = productsOf[j].Select(k => amounts[k]).ToArray();
            }

            radix = new UInt128[need.Length];
            var radixProduct = UInt128.One;
            for (var k = 0; k < need.Length; k++)
            {
                radix[k] = radixProduct;
                radixProduct *= (UInt128)(need[k] + 1);
            }

            // 候補ごとの乱数（SplitMix64）。多重集合のハッシュ = 買った候補の乱数の和（64bitなので衝突は事実上起きない）
            hashOf = new long[candidates.Count];
            var seed = 0x9E3779B97F4A7C15UL;
            for (var j = 0; j < candidates.Count; j++)
            {
                seed += 0x9E3779B97F4A7C15UL;
                var z = seed;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                hashOf[j] = (long)(z ^ (z >> 31));
            }
        }

        // key は今の state のキー。そこから必要数を満たすまでの買い方を上位 PoolSize 件返す
        public List<Plan> Solve(UInt128 key)
        {
            if (memo.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var lacking = -1;
            foreach (var k in fillOrder)
            {
                if (state[k] < need[k])
                {
                    lacking = k;
                    break;
                }
            }
            if (lacking < 0)
            {
                var done = new List<Plan> { Plan.Empty };
                memo[key] = done;
                return done;
            }

            // 足りない商品を含む候補のどれかは必ず買うので、それだけを試す。
            // 候補 j ごとに「j + 次の状態からの買い方」の列ができ、各列は（単品の個数, 金額）順に並んでいる。
            // それらを先頭から突き合わせて（k-way merge）、上位 PoolSize 件だけ取り出す
            var sources = new List<(int Variant, List<Plan> Plans)>();
            var added = new int[need.Length];
            foreach (var j in variantsWith[lacking])
            {
                var ids = productsOf[j];
                var counts = countsOf[j];
                var nextKey = key;
                for (var i = 0; i < ids.Length; i++)
                {
                    var k = ids[i];
                    added[i] = Math.Min(counts[i], need[k] - state[k]);
                    state[k] += added[i];
                    nextKey += (UInt128)added[i] * radix[k];
                }
                var plans = Solve(nextKey);
                for (var i = 0; i < ids.Length; i++)
                {
                    state[ids[i]] -= added[i];
                }
                sources.Add((j, plans));
            }

            var queue = new PriorityQueue<(int Source, int Position), (int Singles, int Price, int Source)>();
            for (var s = 0; s < sources.Count; s++)
            {
                if (sources[s].Plans.Count > 0)
                {
                    queue.Enqueue((s, 0), Priority(sources[s], s, 0));
                }
            }

            var top = new List<Plan>();
            var seen = new HashSet<long>();
            while (top.Count < PoolSize && queue.TryDequeue(out var at, out var priority))
            {
                var source = sources[at.Source];
                if (at.Position + 1 < source.Plans.Count)
                {
                    queue.Enqueue((at.Source, at.Position + 1), Priority(source, at.Source, at.Position + 1));
                }
                if (priority.Price > priceCap)
                {
                    continue;
                }
                var rest = source.Plans[at.Position];
                var hash = rest.Hash + hashOf[source.Variant];
                if (seen.Add(hash))
                {
                    top.Add(new Plan(priority.Singles, priority.Price, hash, source.Variant, rest));
                }
            }

            memo[key] = top;
            return top;
        }

        private (int Singles, int Price, int Source) Priority((int Variant, List<Plan> Plans) source, int index, int position)
        {
            var variant = candidates[source.Variant];
            var rest = source.Plans[position];
            return (rest.Singles + (variant.IsSingle ? 1 : 0), rest.Price + variant.Price, index);
        }
    }
}
```

- [ ] **Step 5: テストが通ることを確認する**

Run: `dotnet test KfcMenu.slnx`
Expected: `Passed!  - Failed: 0, Passed: 28`

Run: `dotnet build KfcMenu.slnx --no-incremental`
Expected: `0 Warning(s)` `0 Error(s)`

- [ ] **Step 6: コミット**

```bash
git add KfcMenu.Core/Models/RecommendedItem.cs KfcMenu.Core/Models/Recommendation.cs KfcMenu.Core/Services/SetRecommender.cs KfcMenu.Core.Tests/SetRecommenderTests.cs
git commit -m "Add SetRecommender to suggest set-based alternatives to the cheapest order"
```

---

## Task 2: 画面（おすすめの表示、余剰の入力欄の削除）と設計書の更新

**Files:**
- Modify: `KfcMenu.Web/Pages/Home.razor`（全体を置き換える。CRLF）
- Modify: `KfcMenu.Web/wwwroot/css/app.css`（1行変更 + 末尾に追記。CRLF）
- Modify: `docs/superpowers/specs/2026-09-05-kfc-menu-optimizer-design.md`
- Modify: `docs/superpowers/specs/2026-10-24-set-recommendation-design.md`

**Interfaces:**
- Consumes: `SetRecommender.Recommend`, `Recommendation`, `RecommendedItem`（Task 1）、`CheapestComboFinder.FindCheapest`, `VariantExpander.Expand`, `MenuParser.Parse`（既存）
- 入力欄の `aria-label` は `"{商品名}の必要数"` のまま（確認スクリプトが使う）。`<base href="/" />` は変えない（デプロイが書き換える）。

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
            variants = VariantExpander.Expand(menu.Options);
            productNames = menu.Products.ToDictionary(p => p.Id, p => p.Name);
        }
        catch (Exception ex)
        {
            loadError = ex.Message;
        }
    }

    private async Task Calculate()
    {
        if (calculating)
        {
            return;
        }
        calculated = false;
        result = null;
        recommendations = new();
        inputError = null;

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
            recommendations = SetRecommender.Recommend(menu.Options, menu.Products, result);
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

このファイルは CRLF なので、書いたあとに改行を CRLF にそろえる:

```bash
perl -pi -e 's/\r?\n/\r\n/' KfcMenu.Web/Pages/Home.razor
```

- [ ] **Step 2: app.css を変更する**

`KfcMenu.Web/wwwroot/css/app.css` の `.product-row` の
`grid-template-columns: 1fr 4.5rem 4.5rem;` を `grid-template-columns: 1fr 4.5rem;` に変え、末尾に追記する:

```css
.app h2 {
    font-size: 1.1rem;
    margin: 1.5rem 0 0.5rem;
}

.recommendation {
    border: 1px solid #ddd;
    border-radius: 0.5rem;
    padding: 0.5rem 0.8rem;
    margin-bottom: 0.8rem;
}

.recommendation .total {
    margin: 0.3rem 0;
}

.diff {
    font-size: 0.9rem;
    font-weight: normal;
    color: #666;
}

.savings {
    margin: 0.2rem 0;
    color: #c8102e;
    font-weight: bold;
}

.extras {
    margin: 0.2rem 0;
    font-size: 0.9rem;
    color: #444;
}
```

書いたあとに CRLF にそろえる:

```bash
perl -pi -e 's/\r?\n/\r\n/' KfcMenu.Web/wwwroot/css/app.css
```

- [ ] **Step 3: ビルドとテスト**

Run: `dotnet build KfcMenu.slnx --no-incremental && dotnet test KfcMenu.slnx`
Expected: 警告0件、エラー0件、テスト28件成功

Run: `git diff --stat`
Expected: Home.razor と app.css の差分が、変更した行だけになっている（ファイル全体の書き換えになっていない = 改行コードが保たれている）

- [ ] **Step 4: Release 発行・サブパス配信・ブラウザ確認**

一時ディレクトリは `/private/tmp/claude-501/-Users-yoshizawayuki-KFC/ba800926-6e56-4582-9a90-2439ad79cc3c/scratchpad/reco-task2`（作成してよい、中身は消してよい）。

```bash
dotnet publish KfcMenu.Web/KfcMenu.Web.csproj -c Release -o <一時>/publish
mkdir -p <一時>/site && cp -R <一時>/publish/wwwroot <一時>/site/kfc-menu
sed -i '' -e 's|<base href="/" />|<base href="/kfc-menu/" />|' -- <一時>/site/kfc-menu/index.html
python3 -m http.server 8766 --bind 127.0.0.1 --directory <一時>/site      # バックグラウンドで
open -na "Google Chrome" --args --headless=new --disable-gpu --no-first-run --user-data-dir=<一時>/chrome --remote-debugging-port=9333 about:blank
node /private/tmp/claude-501/-Users-yoshizawayuki-KFC/ba800926-6e56-4582-9a90-2439ad79cc3c/scratchpad/reco/reco-check.mjs http://127.0.0.1:8766/kfc-menu/ /private/tmp/claude-501/-Users-yoshizawayuki-KFC/ba800926-6e56-4582-9a90-2439ad79cc3c/scratchpad/reco/cases.json
```

`reco-check.mjs` は商品名で入力欄を探して必要数を入れ、「計算」を押し、`.cheapest` と `.recommendation` の文字・かかった時間（ms）を JSON で出す。

Expected（文字は空白を詰めたもの。時間は環境で変わる）:
- chicken8: 最安 `単 × 8 合計 2,640円`。おすすめ1 `8パック × 1（サイド: お好きなサイド ×4） 合計 2,990円 （最安より+350円）単品より810円お得追加で付くもの: お好きなサイド ×4`、2 `4パック × 2（サイド: お好きなサイド ×2） 合計 3,180円`…、3 `2セット × 1（ドリンク: お好きなドリンク）3セット × 2（ドリンク: お好きなドリンク） 合計 3,270円`…`追加で付くもの: ポテトS +3, お好きなドリンク ×3`
- c1p1b1: おすすめ1 `チキビス × 1（ドリンク: お好きなドリンク） 合計 550円 （最安より360円安い）単品より630円お得`
- burger2drinkM2: おすすめ1 `セット × 2（ドリンク: ドリンクM） 合計 1,380円 （最安より80円安い）単品より660円お得追加で付くもの: ポテトS +2`
- crispy2 と zero: `セットを使ったおすすめはありません`
- over99: `必要数には0〜99の数を入力してください。`
- family4: おすすめ1 に `4パック × 1（サイド: ビスケット, お好きなサイド）` が含まれる
- G と H: 例外なく結果が出る。時間を記録する

画面に「余剰の上限」の列がないことも確認する（`.product-row.header` の文字が `商品 必要数`）。確認後、HTTPサーバーと Chrome を止める（`pkill -f "<一時>/chrome"`、`pkill -f "http.server 8766"`）。

- [ ] **Step 5: 元の設計書を更新する**

`docs/superpowers/specs/2026-09-05-kfc-menu-optimizer-design.md`:

1. スコープの1行目を置き換える。
   - 旧: `- 画面に商品ごとの「必要数」と「余剰の上限個数」の入力欄を表示する（初期値はmenu.jsonの値）`
   - 新: `- 画面に商品ごとの「必要数」の入力欄を表示する（初期値はmenu.jsonの値）。「余剰の上限」の入力欄は2026-10-24に削除し、画面では余剰を常に0にして最安を求める（下の条件は計算ロジックの仕様として残る）`
2. スコープの `- 条件を満たす購入方法が存在しない場合はその旨を表示する` の次の行に追加する:
   `- 最安の下に、セットを使った「おすすめ」を最大3つ表示する（詳細は docs/superpowers/specs/2026-10-24-set-recommendation-design.md）`
3. 画面の節:
   - `- 商品ごとに1行: 商品名 / 必要数（数値入力） / 余剰の上限（数値入力）` → `- 商品ごとに1行: 商品名 / 必要数（数値入力）`
   - `  - 見つからない場合: 「条件を満たす組み合わせが見つかりませんでした」` の次の行に `- 最安の下に「セットでおすすめ」を表示する（詳細は docs/superpowers/specs/2026-10-24-set-recommendation-design.md）` を追加
   - `- 必要数・余剰の上限の入力は0〜99に限る（\`min="0" max="99"\`）。範囲外のときは計算せず、「必要数と余剰の上限には0〜99の数を入力してください。」と表示する` → `- 必要数の入力は0〜99に限る（\`min="0" max="99"\`）。範囲外のときは計算せず、「必要数には0〜99の数を入力してください。」と表示する`
4. エラー処理の節: `「必要数と余剰の上限には0〜99の数を入力してください。」` → `「必要数には0〜99の数を入力してください。」`
5. 性能の節:
   - `入力は必要数・余剰の上限ともに0〜99に限る。` → `入力は必要数を0〜99に限る。`
   - 既知の制限の段落の末尾に追記: `（2026-10-24以降は画面から余剰を指定できないため、上の表のH・Gの条件は画面からは再現できない。おすすめ追加後の計測は docs/superpowers/specs/2026-10-24-set-recommendation-design.md の「性能」にある）`

- [ ] **Step 6: おすすめの設計書を更新する**

`docs/superpowers/specs/2026-10-24-set-recommendation-design.md`:

1. `- ステータス: ユーザー確認待ち` → `- ステータス: 承認済み・実装済み`
2. 探索方法の節:
   - 旧: `- 状態は「各商品の合計個数（必要数で頭打ち）」。まだ必要数に届いていない最初の商品を選び、その商品を含む展開結果を1つ足して次の状態へ進む`
   - 新: `- 状態は「各商品の合計個数（必要数で頭打ち）」。まだ必要数に届いていない商品のうち、それを含む展開結果が最も少ない商品を選び（どれを選んでも漏れなく探せる。少ない商品からの方が分岐が減って速い）、その商品を含む展開結果を1つ足して次の状態へ進む`
   - その節の `- 状態ごとに…（メモ化）。同じ組み合わせは安い方だけ残す` の次に追加: `- 状態ごとの上位40件は、足す展開結果ごとの「次の状態の上位40件」（どれも並び順どおり）を先頭から突き合わせて（k-way merge）作る。買い方は「最後に足したもの＋それ以前の買い方」の連結リストで持ち、組み合わせの重複は順番によらない64bitハッシュで除く`
3. 性能の節の最後に、Step 4 で計測した値で表を追加する（計画作成時の値は上の「検証済みの事項」にある）:

```markdown
計測結果（YYYY-MM-DD、ブラウザはPC上のヘッドレスChrome・Release発行・`/kfc-menu/` 配信。「最安 + おすすめ」の合計時間）:

| 注文 | ブラウザ |
|---|---|
| チキン8 | ◯ms |
| 家族4人（チキン6・ポテトS2・ドリンクS2・バーガー1・ビスケット2） | ◯ms |
| G: チキン30・ポテトS10・ドリンクS10・ビスケット4 | ◯ms |
| H: チキン12・ポテトS6・ドリンクS6・ビスケット4・クリスピー3・チョコパイ2・コールスロー2・バーガー2 | ◯ms |

通常の注文は目標（1秒以内）を満たす。Hのように多くの商品を多く頼む注文は1秒前後かかり、スマホではさらに遅くなりうる（既知の制限。この間は「計算中...」を表示する）。
```
   ◯ と日付は実測値に置き換える（測っていない値は書かない）。

- [ ] **Step 7: コミット**

```bash
git add KfcMenu.Web/Pages/Home.razor KfcMenu.Web/wwwroot/css/app.css docs/superpowers/specs/2026-09-05-kfc-menu-optimizer-design.md docs/superpowers/specs/2026-10-24-set-recommendation-design.md
git status
git commit -m "Show set recommendations under the cheapest result and drop the surplus input"
```
