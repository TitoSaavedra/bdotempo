using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using BdoComboSetup.Core.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace BdoComboSetup.Views.Controls;

/// <summary>
/// A list with a search box above it. The items are filtered by the shared rule (<see cref="SearchText"/> in Core: accent-
/// and case-insensitive contains); the texts searched for each item come from <see cref="SetFields"/>.
/// Picking an item raises <see cref="ItemChosen"/>; the owner decides what that means.
/// </summary>
public sealed partial class SearchableList : UserControl
{
    private readonly ObservableCollection<object> _filtered = [];
    private IEnumerable? _source;
    private Func<object, IEnumerable<string?>> _fieldsOf = _ => [];

    public SearchableList()
    {
        InitializeComponent();
        Items.ItemsSource = _filtered;
    }

    /// <summary>Raised when the user picks an item (a click, Enter on the list, or Enter in the search box on the first match).</summary>
    public event EventHandler<object>? ItemChosen;

    /// <summary>The items to filter. An observable collection is followed: its changes filter the list again.</summary>
    public IEnumerable? ItemsSource
    {
        get => _source;
        set
        {
            if (_source is INotifyCollectionChanged previous)
            {
                previous.CollectionChanged -= OnSourceChanged;
            }

            _source = value;
            if (_source is INotifyCollectionChanged next)
            {
                next.CollectionChanged += OnSourceChanged;
            }

            Refresh();
        }
    }

    /// <summary>The template of one row (the owner's data template).</summary>
    public DataTemplate? ItemTemplate
    {
        get => Items.ItemTemplate;
        set => Items.ItemTemplate = value;
    }

    public static readonly DependencyProperty FieldsOfProperty = DependencyProperty.Register(
        nameof(FieldsOf),
        typeof(object),
        typeof(SearchableList),
        new PropertyMetadata(null, OnFieldsOfChanged));

    /// <summary>
    /// The texts an item is searched by, as a <c>Func&lt;object, IEnumerable&lt;string?&gt;&gt;</c>. Set from XAML when the list is
    /// declared there (x:Bind of a static function), or from code through <see cref="SetFields"/>.
    /// </summary>
    public object? FieldsOf
    {
        get => GetValue(FieldsOfProperty);
        set => SetValue(FieldsOfProperty, value);
    }

    private static void OnFieldsOfChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is Func<object, IEnumerable<string?>> fieldsOf)
        {
            ((SearchableList)sender).SetFields(fieldsOf);
        }
    }

    /// <summary>The texts an item is searched by (names in each language, numbers). Set once by the owner.</summary>
    public void SetFields(Func<object, IEnumerable<string?>> fieldsOf)
    {
        ArgumentNullException.ThrowIfNull(fieldsOf);
        _fieldsOf = fieldsOf;
        Refresh();
    }

    /// <summary>The text typed in the search box.</summary>
    public string SearchText => SearchBox.Text;

    /// <summary>The items that match the search now, in their order (what the list shows).</summary>
    public IReadOnlyList<object> Filtered => _filtered;

    /// <summary>Clears the search and shows every item; used when the list is opened.</summary>
    public void Reset()
    {
        SearchBox.Text = string.Empty;
        Refresh();
    }

    /// <summary>Puts the keyboard focus in the search box, so typing filters at once.</summary>
    public void FocusSearch() => SearchBox.Focus(FocusState.Programmatic);

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        _filtered.Clear();
        var items = _source?.Cast<object>() ?? [];
        foreach (var item in Core.Search.SearchText.Filter(items, SearchBox.Text, _fieldsOf))
        {
            _filtered.Add(item);
        }

        EmptyText.Visibility = _filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down && _filtered.Count > 0)
        {
            Items.SelectedIndex = 0;
            Items.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter && _filtered.Count > 0)
        {
            ItemChosen?.Invoke(this, _filtered[0]);
            e.Handled = true;
        }
    }

    private void Items_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && Items.SelectedItem is { } selected)
        {
            ItemChosen?.Invoke(this, selected);
            e.Handled = true;
        }
    }

    private void Items_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is { } clicked)
        {
            ItemChosen?.Invoke(this, clicked);
        }
    }
}
