using KfcMenu.Core.Models;

namespace KfcMenu.Core.Services;

public static class SetRecommender
{
    public const int MaxRecommendations = 3;

    // 状態ごとに残す候補の数（（単品の個数, 金額）順の上位と、金額順の上位のそれぞれ）。
    // 小さいほど速いが、絞り込みで候補が多く落ちる注文では上位を見落としうる
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
        var root = search.Solve(UInt128.Zero);

        // 2つの上位を合わせ（同じ組み合わせは1つ）、（単品の個数, 金額）順に並べ直す。
        // OrderBy は安定ソートなので、同順位は（単品の個数, 金額）順の上位にあった並びのまま
        var union = new List<Plan>(root.BySingles);
        var inUnion = root.BySingles.Select(p => p.Hash).ToHashSet();
        union.AddRange(root.ByPrice.Where(p => inUnion.Add(p.Hash)));
        var rootPlans = union.OrderBy(p => p.Singles).ThenBy(p => p.Price);

        var result = new List<Recommendation>();
        var seen = new HashSet<string>();
        foreach (var plan in rootPlans)
        {
            var bought = plan.ToSortedArray();
            if (!bought.Any(j => candidates[j].IsSet) || !IsMinimal(bought, candidates, requested))
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

    // 条件3（無駄がない）: どの購入選択肢を1つ取り除いても、残りの選べる枠の選び方をどう変えても、どれかの商品が必要数を下回る。
    // 席の選び方は変えてよいので、買い方の中身ではなく購入選択肢ごとの個数だけで決まる
    private static bool IsMinimal(int[] plan, List<RequestVariant> candidates, List<Product> requested)
    {
        var counts = new Dictionary<MenuOption, int>();
        foreach (var j in plan)
        {
            var option = candidates[j].Option;
            counts[option] = counts.GetValueOrDefault(option) + 1;
        }
        foreach (var option in counts.Keys.ToList())
        {
            counts[option]--;
            var covered = CanCover(counts, requested);
            counts[option]++;
            if (covered)
            {
                return false;
            }
        }
        return true;
    }

    // 選べる枠の各席を、枠の候補にある頼んだ商品のどれか（またはお好きなもの）に選び直して、全商品の必要数を満たせるか。
    // includes で足りない分を席で埋める最大流（湧き出し → 枠 → 商品 → 吸い込み）で調べる。
    // 同じ購入選択肢の同じ枠はまとめて1つの頂点にする: 容量は 個数 × 席数、重複不可の枠は1個につき各商品1席まで
    private static bool CanCover(Dictionary<MenuOption, int> counts, List<Product> requested)
    {
        var deficit = new int[requested.Count];
        var totalDeficit = 0;
        for (var k = 0; k < requested.Count; k++)
        {
            var fixedAmount = 0;
            foreach (var (option, count) in counts)
            {
                fixedAmount += count * option.Includes.GetValueOrDefault(requested[k].Id);
            }
            deficit[k] = Math.Max(0, requested[k].Required - fixedAmount);
            totalDeficit += deficit[k];
        }
        if (totalDeficit == 0)
        {
            return true;
        }

        var slots = new List<(ChoiceSlot Slot, int Count)>();
        foreach (var (option, count) in counts)
        {
            if (count > 0)
            {
                slots.AddRange(option.Choices.Select(slot => (slot, count)));
            }
        }

        // 頂点: 0 = 湧き出し、1..slots = 枠、続いて商品、最後が吸い込み
        var productBase = 1 + slots.Count;
        var sink = productBase + requested.Count;
        var capacity = new int[sink + 1, sink + 1];
        for (var s = 0; s < slots.Count; s++)
        {
            var (slot, count) = slots[s];
            capacity[0, 1 + s] = count * slot.Count;
            for (var k = 0; k < requested.Count; k++)
            {
                if (deficit[k] > 0 && slot.From.Contains(requested[k].Id))
                {
                    capacity[1 + s, productBase + k] = count * (slot.AllowDuplicate ? slot.Count : 1);
                }
            }
        }
        for (var k = 0; k < requested.Count; k++)
        {
            capacity[productBase + k, sink] = deficit[k];
        }
        return MaxFlow(capacity, 0, sink) >= totalDeficit;
    }

    // 幅優先で増加路を探す最大流（Edmonds–Karp）。頂点は高々数十個
    private static int MaxFlow(int[,] capacity, int source, int sink)
    {
        var n = capacity.GetLength(0);
        var flow = 0;
        var parent = new int[n];
        while (true)
        {
            Array.Fill(parent, -1);
            parent[source] = source;
            var queue = new Queue<int>();
            queue.Enqueue(source);
            while (queue.Count > 0 && parent[sink] < 0)
            {
                var u = queue.Dequeue();
                for (var v = 0; v < n; v++)
                {
                    if (parent[v] < 0 && capacity[u, v] > 0)
                    {
                        parent[v] = u;
                        queue.Enqueue(v);
                    }
                }
            }
            if (parent[sink] < 0)
            {
                return flow;
            }
            var push = int.MaxValue;
            for (var v = sink; v != source; v = parent[v])
            {
                push = Math.Min(push, capacity[parent[v], v]);
            }
            for (var v = sink; v != source; v = parent[v])
            {
                capacity[parent[v], v] -= push;
                capacity[v, parent[v]] += push;
            }
            flow += push;
        }
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

    // 状態ごとに残す買い方: （単品の個数, 金額）順の上位 PoolSize 件と、金額順の上位 PoolSize 件。
    // 前者だけだと、単品は少ないが高い買い方で埋まり、親で品物を足すと1.5倍上限を超える注文で、
    // 上限内に収まる安い買い方がすでに切り捨てられている。後者がそれを残す
    private sealed record Pools(List<Plan> BySingles, List<Plan> ByPrice)
    {
        public static readonly Pools Done = new([Plan.Empty], [Plan.Empty]);
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

        // 状態のキー = Σ state[k] × radix[k]、radix[k] = Π_{i<k} (need[i] + 1)。キーの最大は Π (need[k] + 1) − 1 で、状態ごとに一意。
        // UInt128 に収まるかは頼んだ商品の数で決まる: 必要数がすべて99でも19種類まで（100^19 < 2^128）。
        // 今のメニューは16種類。積は checked で計算し、収まらないときは黙って衝突せず OverflowException にする
        private readonly UInt128[] radix;
        private readonly Dictionary<UInt128, Pools> memo = new();

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
                radixProduct = checked(radixProduct * (UInt128)(need[k] + 1));
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

        // key は今の state のキー。そこから必要数を満たすまでの買い方の上位（Pools）を返す
        public Pools Solve(UInt128 key)
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
                memo[key] = Pools.Done;
                return Pools.Done;
            }

            // 足りない商品を含む候補のどれかは必ず買うので、それだけを試す。
            // 候補 j ごとに「j + 次の状態からの買い方」の列ができる。次の状態の2つの上位はそれぞれの順に並んでいるので、
            // 同じ順どうしを先頭から突き合わせて（k-way merge）、それぞれ上位 PoolSize 件だけ取り出す
            var sources = new List<(int Variant, Pools Pools)>();
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
                var pools = Solve(nextKey);
                for (var i = 0; i < ids.Length; i++)
                {
                    state[ids[i]] -= added[i];
                }
                sources.Add((j, pools));
            }

            var result = new Pools(Merge(sources, byPrice: false), Merge(sources, byPrice: true));
            memo[key] = result;
            return result;
        }

        // byPrice が false なら（単品の個数, 金額）順、true なら（金額, 単品の個数）順。同順位は足す候補の順（決定的）
        private List<Plan> Merge(List<(int Variant, Pools Pools)> sources, bool byPrice)
        {
            var queue = new PriorityQueue<(int Source, int Position), (int First, int Second, int Source)>();
            for (var s = 0; s < sources.Count; s++)
            {
                if (List(sources[s], byPrice).Count > 0)
                {
                    queue.Enqueue((s, 0), Priority(sources[s], byPrice, s, 0));
                }
            }

            var top = new List<Plan>();
            var seen = new HashSet<long>();
            while (top.Count < PoolSize && queue.TryDequeue(out var at, out var priority))
            {
                var source = sources[at.Source];
                var plans = List(source, byPrice);
                var rest = plans[at.Position];
                var variant = candidates[source.Variant];
                var price = rest.Price + variant.Price;
                if (price > priceCap && byPrice)
                {
                    // 金額順なので、残りもすべて上限を超える
                    break;
                }
                if (at.Position + 1 < plans.Count)
                {
                    queue.Enqueue((at.Source, at.Position + 1), Priority(source, byPrice, at.Source, at.Position + 1));
                }
                if (price > priceCap)
                {
                    continue;
                }
                var hash = rest.Hash + hashOf[source.Variant];
                if (seen.Add(hash))
                {
                    top.Add(new Plan(rest.Singles + (variant.IsSingle ? 1 : 0), price, hash, source.Variant, rest));
                }
            }
            return top;
        }

        private static List<Plan> List((int Variant, Pools Pools) source, bool byPrice) =>
            byPrice ? source.Pools.ByPrice : source.Pools.BySingles;

        private (int First, int Second, int Source) Priority((int Variant, Pools Pools) source, bool byPrice, int index, int position)
        {
            var variant = candidates[source.Variant];
            var rest = List(source, byPrice)[position];
            var singles = rest.Singles + (variant.IsSingle ? 1 : 0);
            var price = rest.Price + variant.Price;
            return byPrice ? (price, singles, index) : (singles, price, index);
        }
    }
}
