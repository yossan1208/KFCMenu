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

        // 中身を商品の並び順の配列にしておく。上限を超える商品や、productsにない商品を含むVariantは使えないので除外する。
        // 上限が負の商品があると、合計0個の時点で上限を超えているのでどのVariantも買えない
        var usable = new List<(Variant Variant, int[] Amounts)>();
        if (upper.All(u => u >= 0))
        {
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
        }

        var search = new Search(lower, upper, usable);
        var total = search.Solve(0);
        if (total == Infeasible)
        {
            return null;
        }

        // 初期状態からmemoに残した「次に買うVariant」をたどる
        var items = new List<(Variant Variant, int Count)>();
        var key = 0L;
        while (true)
        {
            var next = search.NextOf(key);
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
            key = search.Buy(next, key);
        }

        return new ComboResult { Items = items, TotalPrice = total };
    }

    // 探索の状態（今の合計個数）を1つの配列で持ち、買う→再帰→戻すで使い回す
    private sealed class Search
    {
        private readonly int[] lower;
        private readonly int[] upper;
        private readonly int[] totals;

        // Variantごと: 価格、0個でない商品の番号と個数、合計個数をキーに足したときの増分
        private readonly int[] price;
        private readonly int[][] productsOf;
        private readonly int[][] countsOf;
        private readonly long[] keyDelta;

        // 商品ごと: その商品を含むVariantの番号（昇順）
        private readonly int[][] variantsWith;

        // memo: 「今の合計個数」のキー →（ここから下限を満たすまでの最小追加金額, そのとき次に買うVariantの番号）
        private readonly Dictionary<long, (int Cost, int Next)> memo = new();

        // 混合基数のキーがlongに収まらないとき（または個数が負のVariantがあるとき）は、
        // 合計個数の文字列ごとに通し番号を振ってキーにする（遅いが衝突しない）
        private readonly bool radixKey;
        private readonly Dictionary<string, long> fallbackIds = new();

        public Search(int[] lower, int[] upper, List<(Variant Variant, int[] Amounts)> usable)
        {
            this.lower = lower;
            this.upper = upper;
            totals = new int[lower.Length];

            price = new int[usable.Count];
            productsOf = new int[usable.Count][];
            countsOf = new int[usable.Count][];
            var hasNegative = false;
            for (var j = 0; j < usable.Count; j++)
            {
                price[j] = usable[j].Variant.Option.Price;
                var amounts = usable[j].Amounts;
                var nonZero = Enumerable.Range(0, amounts.Length).Where(i => amounts[i] != 0).ToArray();
                productsOf[j] = nonZero;
                countsOf[j] = nonZero.Select(i => amounts[i]).ToArray();
                hasNegative |= countsOf[j].Any(c => c < 0);
            }

            variantsWith = new int[lower.Length][];
            for (var i = 0; i < lower.Length; i++)
            {
                var product = i;
                variantsWith[i] = Enumerable.Range(0, usable.Count).Where(j => usable[j].Amounts[product] != 0).ToArray();
            }

            // キー = Σ totals[i] × radix[i]、radix[i] = Π_{k<i} (upper[k] + 1)。
            // 合計個数は 0〜upper に収まるので、基数の積がlongに収まればキーは状態ごとに一意。
            // （上限が負の商品があるときは usable が空で、合計個数は0のまま）
            var radix = new long[lower.Length];
            radixKey = !hasNegative;
            var radixProduct = 1L;
            for (var i = 0; i < lower.Length && radixKey; i++)
            {
                radix[i] = radixProduct;
                var size = Math.Max(upper[i], 0) + 1L;
                if (radixProduct > long.MaxValue / size)
                {
                    radixKey = false;
                }
                else
                {
                    radixProduct *= size;
                }
            }

            keyDelta = new long[usable.Count];
            if (radixKey)
            {
                for (var j = 0; j < usable.Count; j++)
                {
                    for (var k = 0; k < productsOf[j].Length; k++)
                    {
                        keyDelta[j] += countsOf[j][k] * radix[productsOf[j][k]];
                    }
                }
            }
        }

        // key は今の totals に対応するキー（radixKey のときだけ意味を持つ）
        public int Solve(long key)
        {
            var memoKey = MemoKey(key);
            if (memo.TryGetValue(memoKey, out var cached))
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
                memo[memoKey] = (0, -1);
                return 0;
            }

            // 足りない商品を含むVariantのどれかは必ず買うことになるので、それだけを試す
            var best = (Cost: Infeasible, Next: -1);
            foreach (var j in variantsWith[lacking])
            {
                if (!Fits(j))
                {
                    continue;
                }
                Add(j, 1);
                var rest = Solve(key + keyDelta[j]);
                Add(j, -1);
                if (rest == Infeasible)
                {
                    continue;
                }
                var cost = price[j] + rest;
                if (cost < best.Cost)
                {
                    best = (cost, j);
                }
            }
            memo[memoKey] = best;
            return best.Cost;
        }

        public int NextOf(long key) => memo[MemoKey(key)].Next;

        // 復元用: Variant j を買った状態に進め、新しいキーを返す
        public long Buy(int j, long key)
        {
            Add(j, 1);
            return key + keyDelta[j];
        }

        private long MemoKey(long key)
        {
            if (radixKey)
            {
                return key;
            }
            var text = string.Join(",", totals);
            if (!fallbackIds.TryGetValue(text, out var id))
            {
                id = fallbackIds.Count;
                fallbackIds[text] = id;
            }
            return id;
        }

        // 0個の商品は変わらず、totals は常に上限以下なので、0個でない商品だけ見ればよい
        private bool Fits(int j)
        {
            var ids = productsOf[j];
            var counts = countsOf[j];
            for (var k = 0; k < ids.Length; k++)
            {
                if (totals[ids[k]] + counts[k] > upper[ids[k]])
                {
                    return false;
                }
            }
            return true;
        }

        private void Add(int j, int sign)
        {
            var ids = productsOf[j];
            var counts = countsOf[j];
            for (var k = 0; k < ids.Length; k++)
            {
                totals[ids[k]] += sign * counts[k];
            }
        }
    }
}
