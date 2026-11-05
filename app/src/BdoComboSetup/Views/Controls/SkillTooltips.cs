using System.Runtime.CompilerServices;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace BdoComboSetup.Views.Controls;

/// <summary>
/// The hover card of a skill: its icon, name, description and the game's lines (keys, level, cooldown, costs, damage). Set
/// <see cref="SkillProperty"/> (in XAML: <c>controls:SkillTooltips.Skill</c>) on the visible element that shows a skill, the
/// icon or the card. The setting "Show skill tooltips" switches all of them off at once: <see cref="Refresh"/> applies it live.
/// </summary>
/// <remarks>
/// The cards are not WinUI tooltips: their content was not reliable there (empty or clipped cards). One <see cref="SkillHoverCard"/>
/// per window sits in a non-interactive <see cref="Popup"/>, and the pointer events of the skill elements drive it. The card opens
/// after a short rest on a skill, moves at once from one skill to the next, and closes when the pointer leaves, presses, scrolls, a
/// drag starts or the element unloads. The popup never takes the pointer. <see cref="EnsureHitTestable"/> gives a panel, border or
/// user control a transparent background, so it receives the pointer over its whole area; buttons and list items keep their templates.
/// </remarks>
public static class SkillTooltips
{
    /// <summary>The skill the element shows. Null (the default) means the element has no skill card.</summary>
    public static readonly DependencyProperty SkillProperty = DependencyProperty.RegisterAttached(
        "Skill",
        typeof(ComboSkill),
        typeof(SkillTooltips),
        new PropertyMetadata(null, OnSkillChanged));

    /// <summary>How long the pointer rests on a skill before its card opens.</summary>
    private static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>The gap between the pointer and the card, in device-independent pixels.</summary>
    private const double PointerGap = 14;

    /// <summary>The elements that have a skill, each one wired once. Held weakly, so a removed element is forgotten.</summary>
    private static readonly ConditionalWeakTable<FrameworkElement, object> Elements = new();

    private static readonly object Wired = new();

    /// <summary>One card per window, created when a skill is first shown in it.</summary>
    private static readonly ConditionalWeakTable<XamlRoot, CardHost> Hosts = new();

    private static bool _subscribed;

    /// <summary>The skill element under the pointer, held weakly. There is one pointer, so one element at a time.</summary>
    private static WeakReference<FrameworkElement>? _hovered;

    /// <summary>The last pointer position, relative to the window content.</summary>
    private static Windows.Foundation.Point _pointer;

    /// <summary>The window whose card is open, or null.</summary>
    private static CardHost? _shown;

    /// <summary>Counts the rest on a skill before the card opens.</summary>
    private static DispatcherQueueTimer? _delay;

    public static ComboSkill? GetSkill(DependencyObject element) => (ComboSkill?)element.GetValue(SkillProperty);

    public static void SetSkill(DependencyObject element, ComboSkill? skill) => element.SetValue(SkillProperty, skill);

    /// <summary>Applies the setting to every element that has a skill: the card is closed when it is off, and the areas made hit-testable.</summary>
    public static void Refresh()
    {
        if (!Enabled)
        {
            HideNow();
        }

        foreach (var element in LiveElements())
        {
            Prepare(element);
        }
    }

    private static bool Enabled => App.Services.SettingsView.ShowSkillTooltips;

    private static void OnSkillChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        Subscribe();
        if (!Elements.TryGetValue(element, out _))
        {
            Elements.Add(element, Wired);
            Wire(element);
        }

        Prepare(element);
    }

    /// <summary>Makes the element receive the pointer when it shows a skill and the setting is on.</summary>
    private static void Prepare(FrameworkElement element)
    {
        if (Enabled && GetSkill(element) is not null)
        {
            EnsureHitTestable(element);
        }
    }

    /// <summary>Subscribes the element's pointer events, once. Handlers run even when a template already handled the event.</summary>
    private static void Wire(FrameworkElement element)
    {
        element.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(OnPointerEntered), true);
        element.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnPointerExited), true);
        element.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        element.DragStarting += (_, _) => HideNow();
        element.Unloaded += OnUnloaded;
    }

    private static void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || !Enabled || GetSkill(element) is null || element.XamlRoot is null)
        {
            return;
        }

        _hovered = new WeakReference<FrameworkElement>(element);
        _pointer = PointerIn(e, element);
        if (_shown is not null)
        {
            // The card is open: moving onto the next skill switches it at once, without the rest delay.
            Show(element);
            return;
        }

        RestartDelay(element);
    }

    private static void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || !IsHovered(element))
        {
            return;
        }

        _hovered = null;
        _delay?.Stop();

        // The pointer may go straight onto the next skill: its Entered follows this Exited. The close waits for the dispatcher to
        // finish that move; if a skill was entered meanwhile, the card stays and switches to it.
        element.DispatcherQueue.TryEnqueue(() =>
        {
            if (Hovered() is null)
            {
                HideNow();
            }
        });
    }

    private static void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && IsHovered(element))
        {
            _pointer = PointerIn(e, element);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (IsHovered(sender))
        {
            HideNow();
        }
    }

    /// <summary>
    /// Closes the card on any press, wheel turn or lost capture in the window, wherever it happens: a scroll moves the skills under a
    /// resting pointer without an Exited, and a press may start a drag from a parent container.
    /// </summary>
    private static void ListenInWindow(XamlRoot root)
    {
        if (root.Content is not UIElement content)
        {
            return;
        }

        content.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(HideOnPointerAction), true);
        content.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(HideOnPointerAction), true);
        content.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(HideOnPointerAction), true);
        content.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(HideOnPointerAction), true);
    }

    private static void HideOnPointerAction(object sender, PointerRoutedEventArgs e) => HideNow();

    private static void RestartDelay(FrameworkElement element)
    {
        _delay ??= CreateDelay(element.DispatcherQueue);
        _delay.Stop();
        _delay.Start();
    }

    private static DispatcherQueueTimer CreateDelay(DispatcherQueue queue)
    {
        var timer = queue.CreateTimer();
        timer.Interval = OpenDelay;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (Hovered() is { } element)
            {
                Show(element);
            }
        };
        return timer;
    }

    /// <summary>Opens or updates the card for the element's skill, next to the last pointer position. Closes it when there is nothing to show.</summary>
    private static void Show(FrameworkElement element)
    {
        var skill = GetSkill(element);
        var root = element.XamlRoot;
        if (skill is null || root is null || !Enabled)
        {
            HideNow();
            return;
        }

        var app = App.Services;
        var view = SkillTooltipText.Build(skill, app.Localizer, app.Culture);
        if (!view.HasContent)
        {
            HideNow();
            return;
        }

        var host = Hosts.GetValue(root, CreateHost);
        if (!ReferenceEquals(_shown, host))
        {
            _shown?.Hide();
        }

        var (x, y) = host.Show(view, _pointer);
        _shown = host;
        app.Logger.Debug("skill.tooltip.shown", "A skill hover card was shown", new Dictionary<string, object?>
        {
            ["name"] = view.Name,
            ["rows"] = view.Lines.Count,
            ["x"] = Math.Round(x),
            ["y"] = Math.Round(y),
        });
    }

    /// <summary>Closes the card and forgets the hovered element.</summary>
    private static void HideNow()
    {
        _delay?.Stop();
        _hovered = null;
        _shown?.Hide();
        _shown = null;
    }

    private static CardHost CreateHost(XamlRoot root)
    {
        var host = new CardHost(root);
        ListenInWindow(root);
        return host;
    }

    private static FrameworkElement? Hovered() => _hovered is not null && _hovered.TryGetTarget(out var element) ? element : null;

    private static bool IsHovered(object sender) => ReferenceEquals(Hovered(), sender);

    private static Windows.Foundation.Point PointerIn(PointerRoutedEventArgs e, FrameworkElement element) =>
        e.GetCurrentPoint(element.XamlRoot?.Content).Position;

    /// <summary>Places the card on one axis: after the pointer, or before it when it would leave the window; then kept inside the window.</summary>
    private static double Place(double pointer, double size, double limit)
    {
        var after = pointer + PointerGap;
        var place = after + size <= limit ? after : pointer - PointerGap - size;
        return Math.Clamp(place, 0, Math.Max(0, limit - size));
    }

    /// <summary>
    /// Gives the element a transparent background when it has none, so the pointer hits it over its whole area (a user control
    /// only over its drawn children). Elements that already have a background are left as they are, and so are buttons and list
    /// items, whose templates own their look.
    /// </summary>
    private static void EnsureHitTestable(FrameworkElement element)
    {
        switch (element)
        {
            case Panel { Background: null } panel:
                panel.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                break;
            case Border { Background: null } border:
                border.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                break;
            case UserControl { Background: null } control:
                control.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                break;
        }
    }

    /// <summary>Subscribes once to the setting, so a change applies at once to the card on screen.</summary>
    private static void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        App.Services.SettingsView.SkillTooltipsChanged += (_, _) => Refresh();
    }

    /// <summary>The wired elements that are still alive. The table drops the collected ones by itself.</summary>
    private static List<FrameworkElement> LiveElements()
    {
        var live = new List<FrameworkElement>();
        foreach (var pair in Elements)
        {
            live.Add(pair.Key);
        }

        return live;
    }

    /// <summary>The window's card: one popup and one card, reused for every skill. The popup never takes the pointer.</summary>
    private sealed class CardHost
    {
        private readonly XamlRoot _root;
        private readonly SkillHoverCard _card = new();
        private readonly Popup _popup;

        public CardHost(XamlRoot root)
        {
            _root = root;
            _popup = new Popup
            {
                XamlRoot = root,
                IsLightDismissEnabled = false,
                IsHitTestVisible = false,
                Child = _card,
            };
        }

        /// <summary>Fills the card with the skill and opens it next to the pointer, inside the window. Returns the position it took.</summary>
        public (double X, double Y) Show(SkillTooltipView view, Windows.Foundation.Point pointer)
        {
            _card.RequestedTheme = _root.Content is FrameworkElement content ? content.ActualTheme : ElementTheme.Default;
            _card.Fill(view);
            _popup.IsOpen = true;

            // Measured after the layout pass, so the flip near the window edges uses the real size of the card.
            _card.UpdateLayout();
            var window = _root.Size;
            var x = Place(pointer.X, _card.ActualWidth, window.Width);
            var y = Place(pointer.Y, _card.ActualHeight, window.Height);
            _popup.HorizontalOffset = x;
            _popup.VerticalOffset = y;
            return (x, y);
        }

        public void Hide() => _popup.IsOpen = false;
    }
}
