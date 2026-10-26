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
