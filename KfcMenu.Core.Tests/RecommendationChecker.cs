using KfcMenu.Core.Models;

namespace KfcMenu.Core.Tests;

// おすすめが設計書の条件1〜5を満たすかを、SetRecommender とは別の書き方で確かめる
internal static class RecommendationChecker
{
    // 満たさない条件の説明。すべて満たせば null
    public static string? Problem(Recommendation recommendation, IReadOnlyList<Product> products, ComboResult? cheapest)
    {
        var items = recommendation.Items;
        if (recommendation.TotalPrice != items.Sum(i => i.Option.Price * i.Count))
        {
            return $"合計金額 {recommendation.TotalPrice} が中身の合計と違う";
        }
        foreach (var item in items)
        {
            var slots = item.Option.Choices;
            if (item.Picks.Count != slots.Sum(c => c.Count))
            {
                return $"{item.Option.Name} の席の数が違う";
            }
            var seat = 0;
            foreach (var slot in slots)
            {
                var picks = item.Picks.Skip(seat).Take(slot.Count).ToList();
                seat += slot.Count;
                if (picks.Any(p => p is not null && !slot.From.Contains(p)))
                {
                    return $"{item.Option.Name} の枠に候補にない商品がある";
                }
                if (!slot.AllowDuplicate && picks.Where(p => p is not null).GroupBy(p => p).Any(g => g.Count() > 1))
                {
                    return $"{item.Option.Name} の重複不可の枠に同じ商品がある";
                }
            }
        }

        // 条件1: 自分の選び方で必要数以上
        foreach (var product in products.Where(p => p.Required > 0))
        {
            var total = items.Sum(i => i.Count * (i.Option.Includes.GetValueOrDefault(product.Id) + i.Picks.Count(p => p == product.Id)));
            if (total < product.Required)
            {
                return $"{product.Id} が {total} 個で必要数 {product.Required} に足りない";
            }
        }

        // 条件2
        if (!items.Any(i => ItemCount(i.Option) >= 2))
        {
            return "セットがない";
        }

        // 条件3
        var options = items.Select(i => (i.Option, i.Count)).ToList();
        if (!IsFlexiblyMinimal(options, products))
        {
            return "無駄がある（どれかを1つ取り除いても、選べる枠の選び方を変えれば足りる）";
        }

        // 条件4
        if (cheapest is not null && recommendation.TotalPrice > cheapest.TotalPrice * 3 / 2)
        {
            return $"合計金額 {recommendation.TotalPrice} が上限 {cheapest.TotalPrice * 3 / 2} を超える";
        }

        // 条件5
        if (cheapest is not null && Signature(options) == Signature(cheapest.Items.Select(i => (i.Variant.Option, i.Count))))
        {
            return "最安と同じ組み合わせ";
        }
        return null;
    }

    public static int ItemCount(MenuOption option) => option.Includes.Values.Sum() + option.Choices.Sum(c => c.Count);

    public static string Signature(IEnumerable<(MenuOption Option, int Count)> items) =>
        string.Join(",", items
            .GroupBy(i => i.Option.Id)
            .Select(g => $"{g.Key}x{g.Sum(i => i.Count)}")
            .Order(StringComparer.Ordinal));

    // 条件3（無駄がない）: どの購入選択肢を1つ取り除いても、残りの選べる枠の選び方をどう変えても、どれかの商品が必要数を下回る
    public static bool IsFlexiblyMinimal(IReadOnlyList<(MenuOption Option, int Count)> items, IReadOnlyList<Product> products)
    {
        for (var removed = 0; removed < items.Count; removed++)
        {
            var rest = items.Select((item, i) => (item.Option, Count: i == removed ? item.Count - 1 : item.Count)).ToList();
            if (CanCover(rest, products))
            {
                return false;
            }
        }
        return true;
    }

    // 席1つずつと、足りない分1個ずつの二部マッチング（増加路法）で、席の選び方を変えて必要数を満たせるかを調べる
    private static bool CanCover(List<(MenuOption Option, int Count)> items, IReadOnlyList<Product> products)
    {
        var deficitUnits = new List<string>();
        foreach (var product in products.Where(p => p.Required > 0))
        {
            var fixedAmount = items.Sum(i => i.Count * i.Option.Includes.GetValueOrDefault(product.Id));
            for (var n = fixedAmount; n < product.Required; n++)
            {
                deficitUnits.Add(product.Id);
            }
        }

        var seats = new List<List<string>>();
        foreach (var (option, count) in items)
        {
            foreach (var slot in option.Choices)
            {
                // 席を1つずつ独立に扱うので、重複不可で2席以上の枠は表せない（今のメニューにはない）
                if (!slot.AllowDuplicate && slot.Count > 1)
                {
                    throw new NotSupportedException("重複不可で2席以上の枠には対応していない");
                }
                for (var n = 0; n < count * slot.Count; n++)
                {
                    seats.Add(slot.From);
                }
            }
        }
        if (deficitUnits.Count > seats.Count)
        {
            return false;
        }

        var seatOf = new int[deficitUnits.Count];
        var unitOf = Enumerable.Repeat(-1, seats.Count).ToArray();
        for (var u = 0; u < deficitUnits.Count; u++)
        {
            if (!Augment(u, new bool[seats.Count], deficitUnits, seats, unitOf))
            {
                return false;
            }
        }
        return true;
    }

    private static bool Augment(int unit, bool[] visited, List<string> deficitUnits, List<List<string>> seats, int[] unitOf)
    {
        for (var s = 0; s < seats.Count; s++)
        {
            if (visited[s] || !seats[s].Contains(deficitUnits[unit]))
            {
                continue;
            }
            visited[s] = true;
            if (unitOf[s] < 0 || Augment(unitOf[s], visited, deficitUnits, seats, unitOf))
            {
                unitOf[s] = unit;
                return true;
            }
        }
        return false;
    }

    // 「セット（の席の選び方）1つ + 残りを単品」の買い方のうち、条件1〜5を満たすもの（シグネチャ → 金額）
    public static Dictionary<string, int> ValidOneSetPlusSingles(IReadOnlyList<MenuOption> menuOptions, IReadOnlyList<Product> products, ComboResult? cheapest)
    {
        var requested = products.Where(p => p.Required > 0).ToList();
        var found = new Dictionary<string, int>();
        if (requested.Count == 0)
        {
            return found;
        }

        var singles = new Dictionary<string, MenuOption>();
        foreach (var option in menuOptions.Where(o => o.Choices.Count == 0 && o.Includes.Count == 1 && o.Includes.First().Value == 1))
        {
            var id = option.Includes.First().Key;
            if (!singles.TryGetValue(id, out var current) || option.Price < current.Price)
            {
                singles[id] = option;
            }
        }

        foreach (var set in menuOptions.Where(o => ItemCount(o) >= 2))
        {
            foreach (var picks in SeatPicks(set.Choices, 0, requested.Select(p => p.Id).ToHashSet()))
            {
                var items = new List<RecommendedItem> { new() { Option = set, Picks = picks, Count = 1 } };
                var feasible = true;
                foreach (var product in requested)
                {
                    var fromSet = set.Includes.GetValueOrDefault(product.Id) + picks.Count(p => p == product.Id);
                    var rest = product.Required - fromSet;
                    if (rest <= 0)
                    {
                        continue;
                    }
                    if (!singles.TryGetValue(product.Id, out var single))
                    {
                        feasible = false;
                        break;
                    }
                    items.Add(new RecommendedItem { Option = single, Count = rest });
                }
                if (!feasible)
                {
                    continue;
                }
                var plan = new Recommendation { Items = items, TotalPrice = items.Sum(i => i.Option.Price * i.Count) };
                if (Problem(plan, products, cheapest) is null)
                {
                    var signature = Signature(items.Select(i => (i.Option, i.Count)));
                    found[signature] = plan.TotalPrice;
                }
            }
        }
        return found;
    }

    private static IEnumerable<List<string?>> SeatPicks(List<ChoiceSlot> slots, int index, HashSet<string> requested)
    {
        if (index == slots.Count)
        {
            yield return [];
            yield break;
        }
        var slot = slots[index];
        var choices = slot.From.Where(requested.Contains).Select(id => (string?)id).Append(null).ToList();
        foreach (var head in Choose(choices, slot.Count, 0, slot.AllowDuplicate))
        {
            foreach (var rest in SeatPicks(slots, index + 1, requested))
            {
                yield return [.. head, .. rest];
            }
        }
    }

    // 候補から count 個選ぶ（順番違いは出さない）。null（お好きなもの）は重複不可の枠でも何席でも選べる
    private static IEnumerable<List<string?>> Choose(List<string?> choices, int count, int start, bool allowDuplicate)
    {
        if (count == 0)
        {
            yield return [];
            yield break;
        }
        for (var i = start; i < choices.Count; i++)
        {
            var next = allowDuplicate || choices[i] is null ? i : i + 1;
            foreach (var rest in Choose(choices, count - 1, next, allowDuplicate))
            {
                yield return [choices[i], .. rest];
            }
        }
    }
}
