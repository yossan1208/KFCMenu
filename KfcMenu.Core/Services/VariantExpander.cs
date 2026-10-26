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
