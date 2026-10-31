using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Search;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Tests;

/// <summary>The searchable texts of each selector: names in both languages, numbers, and no pcid.</summary>
public sealed class SearchFieldsTests
{
    private static readonly ClassInfo Tamer = new(16, "tamer", new BilingualText("Tamer", "Domadora"), []);
    private static readonly ClassInfo Sorceress = new(9, "sorceress", new BilingualText("Sorceress", "Hechicera"), []);
    private static readonly ClassInfo Warrior = new(0, "warrior", new BilingualText("Warrior", "Guerrero"), []);
    private static readonly ClassInfo Mystic = new(20, "mystic", new BilingualText("Mystic", "Mística"), []);

    [Theory]
    [InlineData("domadora", "Lola")]
    [InlineData("tamer", "Lola")]
    [InlineData("Lo", "Lola")]
    [InlineData("hechicera", "Maga")]
    [InlineData("sorceress", "Maga")]
    [InlineData("guerrero", "Bruno")]
    [InlineData("warrior", "Bruno")]
    [InlineData("mistica", "Celia")]
    [InlineData("mystic", "Celia")]
    [InlineData("MÍSTICA", "Celia")]
    public void A_character_is_found_by_its_name_or_its_class_in_either_language(string query, string expectedName)
    {
        var characters = new[]
        {
            (Character: new CharacterOption("Lola", "10000000000000011", 16), Class: Tamer),
            (Character: new CharacterOption("Maga", "10000000000000012", 9), Class: Sorceress),
            (Character: new CharacterOption("Bruno", "10000000000000013", 0), Class: Warrior),
            (Character: new CharacterOption("Celia", "10000000000000014", 20), Class: Mystic),
        };

        var found = SearchText.Filter(characters, query, pair => SearchFields.ForCharacter(pair.Character, pair.Class));

        Assert.Equal([expectedName], found.Select(pair => pair.Character.Name));
    }

    [Fact]
    public void A_character_without_a_known_class_is_found_by_name_only()
    {
        var fields = SearchFields.ForCharacter(new CharacterOption("Nadie", "10000000000000099", null), null);

        Assert.True(SearchText.Matches("nadie", fields));
        Assert.False(SearchText.Matches("tamer", fields));
    }

    [Fact]
    public void A_pcid_is_never_searched()
    {
        var fields = SearchFields.ForCharacter(new CharacterOption("Lola", "10000000000000011", 16), Tamer).ToList();

        Assert.DoesNotContain(fields, field => field is not null && field.Contains("10000000000000011", StringComparison.Ordinal));
        Assert.False(SearchText.Matches("10000000000000011", fields));
    }

    [Theory]
    [InlineData("guerrero", 0)]
    [InlineData("Hechicera", 9)]
    [InlineData("mistica", 20)]
    public void A_class_is_found_by_its_name_in_either_language(string query, int expectedClassType)
    {
        var classes = new[] { Tamer, Sorceress, Warrior, Mystic };

        var found = SearchText.Filter(classes, query, SearchFields.ForClass);

        Assert.Equal([expectedClassType], found.Select(info => info.ClassType));
    }

    [Fact]
    public void A_skill_is_found_by_its_english_or_spanish_name_and_by_its_number()
    {
        var skill = new ComboSkill(1926, new BilingualText("Moonlight Dash", "Carrera lunar"), null, () => null);
        var option = new SkillOption(skill, "Carrera lunar");

        Assert.True(SearchText.Matches("moonlight", SearchFields.ForSkill(option)));
        Assert.True(SearchText.Matches("CARRERA", SearchFields.ForSkill(option)));
        Assert.True(SearchText.Matches("1926", SearchFields.ForSkill(option)));
        Assert.False(SearchText.Matches("1927", SearchFields.ForSkill(option)));
    }

    [Fact]
    public void An_account_is_found_by_its_number()
    {
        Assert.True(SearchText.Matches("1000", SearchFields.ForAccount("100000001")));
        Assert.False(SearchText.Matches("2000", SearchFields.ForAccount("100000001")));
    }

    [Fact]
    public void Class_names_are_found_in_both_languages_with_the_class_choice_names()
    {
        var names = SearchFields.ForNames(new BilingualText("Dark Knight", "Caballero oscuro"));

        Assert.True(SearchText.Matches("oscuro", names));
        Assert.True(SearchText.Matches("dark knight", names));
    }
}
