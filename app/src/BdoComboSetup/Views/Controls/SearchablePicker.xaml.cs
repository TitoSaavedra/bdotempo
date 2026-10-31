using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace BdoComboSetup.Views.Controls;

/// <summary>
/// The searchable selector: shows <see cref="SelectedItem"/> with <see cref="SelectedTemplate"/> and opens a flyout with a
/// <see cref="SearchableList"/> of <see cref="ItemsSource"/> rows drawn with <see cref="ItemTemplate"/>.
/// The selection belongs to the owner: it handles <see cref="Chosen"/> and updates <see cref="SelectedItem"/>.
/// </summary>
public sealed partial class SearchablePicker : UserControl
{
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(object),
        typeof(SearchablePicker),
        new PropertyMetadata(null, OnSelectedChanged));

    public static readonly DependencyProperty SelectedTemplateProperty = DependencyProperty.Register(
        nameof(SelectedTemplate),
        typeof(DataTemplate),
        typeof(SearchablePicker),
        new PropertyMetadata(null, OnSelectedChanged));

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(SearchablePicker),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(SearchablePicker),
        new PropertyMetadata(null, OnItemTemplateChanged));

    public SearchablePicker()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user picks an item. The owner decides what the choice does.</summary>
    public event EventHandler<object>? Chosen;

    /// <summary>The item shown on the button, or null (the placeholder text is shown).</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>The template of the item shown on the button.</summary>
    public DataTemplate? SelectedTemplate
    {
        get => (DataTemplate?)GetValue(SelectedTemplateProperty);
        set => SetValue(SelectedTemplateProperty, value);
    }

    /// <summary>The items the flyout offers.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>The template of one row in the flyout.</summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>The texts each item is searched by (see <see cref="SearchableList.FieldsOf"/>). Set once by the owner.</summary>
    public void SetFields(Func<object, IEnumerable<string?>> fieldsOf) => List.SetFields(fieldsOf);

    /// <summary>Opens the flyout (the search box gets the focus).</summary>
    public void Open() => PickerFlyout.ShowAt(Trigger);

    private static void OnSelectedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((SearchablePicker)sender).ShowSelection();

    private static void OnItemsSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((SearchablePicker)sender).List.ItemsSource = e.NewValue as IEnumerable;

    private static void OnItemTemplateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((SearchablePicker)sender).List.ItemTemplate = e.NewValue as DataTemplate;

    private void ShowSelection()
    {
        SelectedPresenter.ContentTemplate = SelectedTemplate;
        SelectedPresenter.Content = SelectedItem;
        PlaceholderText.Visibility = SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PickerFlyout_Opened(object? sender, object e)
    {
        List.Reset();
        List.FocusSearch();
    }

    private void List_ItemChosen(object? sender, object item)
    {
        PickerFlyout.Hide();
        Chosen?.Invoke(this, item);
    }
}
