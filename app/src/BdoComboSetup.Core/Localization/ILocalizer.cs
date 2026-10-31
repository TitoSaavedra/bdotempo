namespace BdoComboSetup.Core.Localization;

/// <summary>
/// Resolves user-visible text from the app's .resw resources. Core and the view models
/// never contain visible text of their own; every string goes through a resource key.
/// </summary>
public interface ILocalizer
{
    string Get(string key);

    string Format(string key, params object[] args);
}
