using BdoComboSetup.Core.Combo;
using BdoComboSetup.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BdoComboSetup.Views.Controls;

/// <summary>
/// The hover tooltip of a skill: its icon, name, description and the game's lines (keys, cost, cooldown, damage). Set
/// <see cref="SkillProperty"/> (in XAML: <c>controls:SkillTooltips.Skill</c>) on any element that shows a skill. One template
/// draws every tooltip, and it is instantiated only when the tooltip opens.
/// The setting "Show skill tooltips" switches all of them off at once: <see cref="Refresh"/> re-applies them when it changes.
/// </summary>
public static class SkillTooltips
{
    /// <summary>The skill the element shows. Null (the default) means the element has no skill tooltip.</summary>
    public static readonly DependencyProperty SkillProperty = DependencyProperty.RegisterAttached(
        "Skill",
        typeof(ComboSkill),
        typeof(SkillTooltips),
        new PropertyMetadata(null, OnSkillChanged));

    private static readonly Lazy<DataTemplate> Template = new(() => LoadTemplate());

    /// <summary>The elements that have a skill tooltip. Held weakly, so a removed element is forgotten.</summary>
    private static readonly List<WeakReference<FrameworkElement>> Elements = [];

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
        ToolTipService.SetToolTip(
            element,
            skill is not null && app.SettingsView.ShowSkillTooltips ? CreateToolTip(skill, app) : null);
    }

    private static ToolTip CreateToolTip(ComboSkill skill, AppServices app) => new()
    {
        Content = SkillTooltipText.Build(skill, app.Localizer, app.Culture),
        ContentTemplate = Template.Value,
    };

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

    private static void Track(FrameworkElement element)
    {
        Elements.RemoveAll(reference => !reference.TryGetTarget(out _));
        Elements.Add(new WeakReference<FrameworkElement>(element));
    }

    /// <summary>The tracked elements that are still alive (the list is cleaned of the others).</summary>
    private static List<FrameworkElement> LiveElements()
    {
        Elements.RemoveAll(reference => !reference.TryGetTarget(out _));
        var live = new List<FrameworkElement>(Elements.Count);
        foreach (var reference in Elements)
        {
            if (reference.TryGetTarget(out var element))
            {
                live.Add(element);
            }
        }

        return live;
    }

    private static DataTemplate LoadTemplate()
    {
        var dictionary = new SkillTooltipTemplate();
        return (DataTemplate)dictionary["SkillTooltipTemplate"];
    }
}
