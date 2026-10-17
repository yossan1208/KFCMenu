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
