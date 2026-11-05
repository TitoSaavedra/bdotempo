using BdoComboSetup.Core.Combo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace BdoComboSetup.Views.Controls;

/// <summary>
/// The card of a skill's hover: icon, name, description and up to <see cref="SkillTooltipText.MaxRows"/> "label: value" rows.
/// One card serves every skill: <see cref="Fill"/> sets its content each time it opens. It is drawn by <see cref="SkillTooltips"/>.
/// </summary>
public sealed partial class SkillHoverCard : UserControl
{
    private readonly (TextBlock Row, Run Label, Run Value)[] _rows;

    public SkillHoverCard()
    {
        InitializeComponent();
        _rows =
        [
            (Row0, Label0, Value0),
            (Row1, Label1, Value1),
            (Row2, Label2, Value2),
            (Row3, Label3, Value3),
            (Row4, Label4, Value4),
            (Row5, Label5, Value5),
            (Row6, Label6, Value6),
            (Row7, Label7, Value7),
            (Row8, Label8, Value8),
            (Row9, Label9, Value9),
        ];
    }

    /// <summary>Sets the icon, name, description and rows to the skill. Rows past the pool are not shown (the text builder never sends more).</summary>
    public void Fill(SkillTooltipView view)
    {
        SkillIcon.IconSource = view.OpenIcon;
        SkillIcon.Glyph = view.Glyph;
        NameText.Text = view.Name;

        DescriptionText.Text = view.Description ?? string.Empty;
        DescriptionText.Visibility = view.HasDescription ? Visibility.Visible : Visibility.Collapsed;

        for (var index = 0; index < _rows.Length; index++)
        {
            var (row, label, value) = _rows[index];
            var visible = index < view.Lines.Count;
            var line = visible ? view.Lines[index] : null;

            label.Text = line?.Label ?? string.Empty;
            value.Text = line?.Value ?? string.Empty;
            row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        RowsPanel.Visibility = view.HasLines ? Visibility.Visible : Visibility.Collapsed;
    }
}
