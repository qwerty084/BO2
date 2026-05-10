using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BO2.Services
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(AppPreferences))]
    [JsonSerializable(typeof(GameHistoryDocument))]
    [JsonSerializable(typeof(GameHistoryEntry))]
    [JsonSerializable(typeof(GameHistorySavedGame))]
    [JsonSerializable(typeof(GameHistoryMapIdentity))]
    [JsonSerializable(typeof(GameHistoryStats))]
    [JsonSerializable(typeof(GameHistoryRound))]
    [JsonSerializable(typeof(GameHistoryBoxEvent))]
    [JsonSerializable(typeof(GameMapIdentity))]
    [JsonSerializable(typeof(Dictionary<string, WidgetSettings>))]
    [JsonSerializable(typeof(ThemeMode))]
    [JsonSerializable(typeof(WidgetSettings))]
    [JsonSerializable(typeof(WidgetSettingsDocument))]
    internal sealed partial class SettingsJsonSerializerContext : JsonSerializerContext
    {
    }
}
