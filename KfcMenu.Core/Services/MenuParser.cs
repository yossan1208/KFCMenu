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
