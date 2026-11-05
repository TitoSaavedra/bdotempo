using System.Runtime.CompilerServices;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace BdoComboSetup.Views.Controls;

/// <summary>
/// The hover tooltip of a skill: its icon, name, description and the game's lines (keys, cost, cooldown, damage). Set
/// <see cref="SkillProperty"/> (in XAML: <c>controls:SkillTooltips.Skill</c>) on the visible element that shows a skill, the
/// icon or the card. One template draws every tooltip, and it is instantiated only when the tooltip opens.
/// The setting "Show skill tooltips" switches all of them off at once: <see cref="Refresh"/> re-applies them when it changes.
/// </summary>
/// <remarks>
/// WinUI raises a tooltip only on elements that receive pointer input. A panel, border or user control without a
/// background is not hit-testable (a user control only over its drawn children), so <see cref="EnsureHitTestable"/> gives such an element
/// a transparent background when the skill is set. The tooltip itself has no chrome of its own: the template draws the card, so
/// its corners and padding are the template's.
/// </remarks>
public static class SkillTooltips
{
    /// <summary>The skill the element shows. Null (the default) means the element has no skill tooltip.</summary>
    public static readonly DependencyProperty SkillProperty = DependencyProperty.RegisterAttached(
        "Skill",
        typeof(ComboSkill),
        typeof(SkillTooltips),
        new PropertyMetadata(null, OnSkillChanged));

    /// <summary>The card's maximum width, in device-independent pixels (the text wraps inside it).</summary>
    private const double MaxCardWidth = 320;

    private static readonly Lazy<DataTemplate> Template = new(() => LoadTemplate());

    /// <summary>The elements that have a skill tooltip. Held weakly, so a removed element is forgotten, and each one once.</summary>
    private static readonly ConditionalWeakTable<FrameworkElement, object> Elements = new();

    private static readonly object Tracked = new();

    private static bool _subscribed;

    public static ComboSkill? GetSkill(DependencyObject element) => (ComboSkill?)element.GetValue(SkillProperty);

    public static void SetSkill(DependencyObject element, ComboSkill? skill) => element.SetValue(SkillProperty, skill);

    /// <summary>Visibility for a template binding: visible when the flag is true.</summary>
    public static Visibility VisibilityOf(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Applies the setting to every element that has a skill: tooltips are set when shown, removed when hidden.</summary>
    public static void Refresh()
    {
        foreach (var element in LiveElements())
        {
            Apply(element);
        }
    }

    private static void OnSkillChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        Subscribe();
        Track(element);
        Apply(element);
    }

    private static void Apply(FrameworkElement element)
    {
        var skill = GetSkill(element);
        var app = App.Services;
        var view = skill is not null && app.SettingsView.ShowSkillTooltips
            ? SkillTooltipText.Build(skill, app.Localizer, app.Culture)
            : null;

        if (view is null || !view.HasContent)
        {
            ToolTipService.SetToolTip(element, null);
            return;
        }

        EnsureHitTestable(element);
        ToolTipService.SetPlacement(element, PlacementMode.Mouse);
        ToolTipService.SetToolTip(element, CreateToolTip(view));
    }

    /// <summary>
    /// The tooltip carries no chrome of its own (the template draws the card, with its corners, border and padding), so the
    /// default tooltip frame around it is cleared here.
    /// </summary>
    private static ToolTip CreateToolTip(SkillTooltipView view) => new()
    {
        Content = view,
        ContentTemplate = Template.Value,
        Background = new SolidColorBrush(Colors.Transparent),
        BorderBrush = new SolidColorBrush(Colors.Transparent),
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        MaxWidth = MaxCardWidth,
    };

    /// <summary>
    /// Gives the element a transparent background when it has none, so the pointer hits it over its whole area (a user control
    /// only over its drawn children). Elements that already have a background
    /// are left as they are, and so are buttons and list items, whose templates own their look.
    /// </summary>
    private static void EnsureHitTestable(FrameworkElement element)
    {
        switch (element)
        {
            case Panel { Background: null } panel:
                panel.Background = new SolidColorBrush(Colors.Transparent);
                break;
            case Border { Background: null } border:
                border.Background = new SolidColorBrush(Colors.Transparent);
                break;
            case UserControl { Background: null } control:
                control.Background = new SolidColorBrush(Colors.Transparent);
                break;
        }
    }

    /// <summary>Subscribes once to the setting, so a change re-applies the tooltips on screen.</summary>
    private static void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        App.Services.SettingsView.SkillTooltipsChanged += (_, _) => Refresh();
    }

    private static void Track(FrameworkElement element) => Elements.AddOrUpdate(element, Tracked);

    /// <summary>The tracked elements that are still alive. The table drops the collected ones by itself.</summary>
    private static List<FrameworkElement> LiveElements()
    {
        var live = new List<FrameworkElement>();
        foreach (var pair in Elements)
        {
            live.Add(pair.Key);
        }

        return live;
    }

    private static DataTemplate LoadTemplate()
    {
        var dictionary = new SkillTooltipTemplate();
        return (DataTemplate)dictionary["SkillTooltipTemplate"];
    }
}
