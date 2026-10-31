using BdoComboSetup.Core.Search;

namespace BdoComboSetup.Tests;

/// <summary>The shared filter of every searchable selector: accents, case, multi-field, empty and mixed queries.</summary>
public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Mística", "mistica")]
    [InlineData("  Hechicera  ", "hechicera")]
    [InlineData("Ñandú", "nandu")]
    [InlineData("Dark   Knight", "dark knight")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_removes_accents_case_and_extra_spaces(string? text, string expected)
    {
        Assert.Equal(expected, SearchText.Normalize(text));
    }

    [Fact]
    public void Normalize_replaces_a_lone_surrogate_instead_of_throwing()
    {
        var broken = "ab" + (char)0xD800 + "cd";

        Assert.Equal("ab\uFFFDcd", SearchText.Normalize(broken));
    }

    [Fact]
    public void An_empty_query_matches_everything_and_no_fields()
    {
        Assert.True(SearchText.Matches(null, []));
        Assert.True(SearchText.Matches("  ", ["Tamer"]));
    }

    [Theory]
    [InlineData("mistica", "Mística")]
    [InlineData("MISTICA", "Mística")]
    [InlineData("Místíca", "mistica")]
    [InlineData("nan", "Ñandú")]
    public void Accents_and_case_are_ignored_on_both_sides(string query, string field)
    {
        Assert.True(SearchText.Matches(query, [field]));
    }

    [Fact]
    public void A_query_matches_when_any_field_contains_it()
    {
        // A character named "Lola", of class Tamer: the Spanish and English class names are both searchable.
        var fields = new[] { "Lola", "Domadora", "Tamer" };

        Assert.True(SearchText.Matches("domadora", fields));
        Assert.True(SearchText.Matches("tamer", fields));
        Assert.True(SearchText.Matches("ola", fields));
        Assert.False(SearchText.Matches("warrior", fields));
    }

    [Fact]
    public void Mixed_queries_need_the_text_as_one_contiguous_piece()
    {
        Assert.True(SearchText.Matches("dark knight", ["Dark Knight"]));
        Assert.False(SearchText.Matches("knight dark", ["Dark Knight"]));
    }

    [Fact]
    public void Numbers_are_searchable_like_any_field()
    {
        Assert.True(SearchText.Matches("1926", ["Shadow Step", "1926"]));
        Assert.False(SearchText.Matches("1926", ["Shadow Step"]));
    }

    [Fact]
    public void Filter_keeps_the_original_order_and_checks_every_field_of_each_item()
    {
        var people = new[]
        {
            (Name: "Ana", Clase: "Hechicera", Class: "Sorceress"),
            (Name: "Bruno", Clase: "Guerrero", Class: "Warrior"),
            (Name: "Celia", Clase: "Maga", Class: "Mystic"),
            (Name: "Dario", Clase: "Místic", Class: "Mystic"),
        };

        var found = SearchText.Filter(people, "mystic", p => [p.Name, p.Clase, p.Class]);

        Assert.Equal(["Celia", "Dario"], found.Select(p => p.Name));
    }

    [Fact]
    public void Filter_with_an_empty_query_returns_all_items_in_order()
    {
        var found = SearchText.Filter(new[] { 3, 1, 2 }, "", n => [n.ToString()]);

        Assert.Equal([3, 1, 2], found);
    }

    [Fact]
    public void Filter_with_no_match_returns_an_empty_list()
    {
        var found = SearchText.Filter(new[] { "a", "b" }, "zzz", s => [s]);

        Assert.Empty(found);
    }
}
