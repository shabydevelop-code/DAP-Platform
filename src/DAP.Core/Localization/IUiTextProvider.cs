namespace DAP.Core.Localization;

public interface IUiTextProvider
{
    string Language { get; }
    bool IsRightToLeft { get; }

    string Get(string key);
    string Format(string key, params object?[] args);
}
