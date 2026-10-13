namespace PulseMouse.App.Views;

public interface IPageView
{
    void OnShown();
    void OnHidden();
    /// <summary>Re-applies header texts after a language change.</summary>
    void Relocalize();
    /// <summary>Reloads everything after the settings object was replaced (import).</summary>
    void Reload();
}
