namespace Nimbus.UI.Pages;

/// <summary>Pages are cached; this lets one reload its values when it's shown again (e.g. after a profile change).</summary>
internal interface IPage
{
    void OnShown();
}
