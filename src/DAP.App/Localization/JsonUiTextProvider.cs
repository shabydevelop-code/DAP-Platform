using System.Globalization;
using System.IO;
using System.Text.Json;
using DAP.Core.Localization;

namespace DAP.App.Localization;

public sealed class JsonUiTextProvider : IUiTextProvider
{
    private readonly IReadOnlyDictionary<string, string> _texts;

    private JsonUiTextProvider(
        string language,
        bool isRightToLeft,
        IReadOnlyDictionary<string, string> texts)
    {
        Language = language;
        IsRightToLeft = isRightToLeft;
        _texts = texts;
    }

    public string Language { get; }

    public bool IsRightToLeft { get; }

    public static JsonUiTextProvider LoadFromApplicationDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Localization");
        var settingsPath = Path.Combine(directory, "language.json");

        if (!File.Exists(settingsPath))
            throw new InvalidOperationException(
                $"Missing localization configuration file '{settingsPath}'.");

        using var settingsDocument = JsonDocument.Parse(File.ReadAllText(settingsPath));
        if (!settingsDocument.RootElement.TryGetProperty("language", out var languageElement)
            || string.IsNullOrWhiteSpace(languageElement.GetString()))
        {
            throw new InvalidOperationException(
                $"Localization configuration '{settingsPath}' must contain a non-empty 'language' value.");
        }

        var language = languageElement.GetString()!.Trim();
        var languagePath = Path.Combine(directory, $"{language}.json");

        if (!File.Exists(languagePath))
            throw new InvalidOperationException(
                $"Missing localization file '{languagePath}' for language '{language}'.");

        var texts = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(languagePath))
            ?? throw new InvalidOperationException(
                $"Localization file '{languagePath}' does not contain a JSON object.");

        if (!texts.TryGetValue("Ui.Direction", out var direction))
            throw new InvalidOperationException(
                $"Missing localization key 'Ui.Direction' in '{languagePath}'.");

        var isRightToLeft = direction switch
        {
            "rtl" => true,
            "ltr" => false,
            _ => throw new InvalidOperationException(
                $"Localization key 'Ui.Direction' in '{languagePath}' must be 'rtl' or 'ltr'.")
        };

        return new JsonUiTextProvider(language, isRightToLeft, texts);
    }

    public string Get(string key)
    {
        if (!_texts.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Missing localization key '{key}' for language '{Language}'.");

        return value;
    }

    public string Format(string key, params object?[] args)
        => string.Format(CultureInfo.InvariantCulture, Get(key), args);
}
