using System.Text.Json.Serialization;

namespace BdoComboSetup.Core.Combo;

// JSON shapes written by bdo_combo.guides (schema 1). Internal: the public types are the records in ComboGuideModels.cs.

internal sealed class CatalogJson
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; }

    [JsonPropertyName("classes")]
    public List<ClassJson>? Classes { get; set; }
}

internal sealed class ClassJson
{
    [JsonPropertyName("classType")]
    public int ClassType { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("name")]
    public NamesJson? Name { get; set; }

    [JsonPropertyName("specs")]
    public List<SpecJson>? Specs { get; set; }
}

internal sealed class SpecJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("displayName")]
    public NamesJson? DisplayName { get; set; }

    [JsonPropertyName("guide")]
    public string? Guide { get; set; }
}

internal sealed class NamesJson
{
    [JsonPropertyName("en")]
    public string? En { get; set; }

    [JsonPropertyName("es")]
    public string? Es { get; set; }
}

internal sealed class GuideJson
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("chains")]
    public List<ChainJson>? Chains { get; set; }

    [JsonPropertyName("notes")]
    public List<NoteJson>? Notes { get; set; }

    [JsonPropertyName("addons")]
    public List<AddonJson>? Addons { get; set; }
}

internal sealed class ChainJson
{
    [JsonPropertyName("section")]
    public string? Section { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("notes")]
    public List<string>? Notes { get; set; }

    [JsonPropertyName("steps")]
    public List<StepJson>? Steps { get; set; }
}

internal sealed class StepJson
{
    [JsonPropertyName("keys")]
    public string? Keys { get; set; }

    [JsonPropertyName("skills")]
    public List<SkillRefJson>? Skills { get; set; }

    [JsonPropertyName("extra")]
    public string? Extra { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

internal sealed class NoteJson
{
    [JsonPropertyName("section")]
    public string? Section { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("skills")]
    public List<SkillRefJson>? Skills { get; set; }
}

internal sealed class AddonJson
{
    [JsonPropertyName("section")]
    public string? Section { get; set; }

    [JsonPropertyName("skill")]
    public SkillRefJson? Skill { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("effects")]
    public List<EffectJson>? Effects { get; set; }
}

internal sealed class EffectJson
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class SkillRefJson
{
    [JsonPropertyName("skillNo")]
    public int SkillNo { get; set; }

    [JsonPropertyName("nameEn")]
    public string? NameEn { get; set; }

    [JsonPropertyName("nameEs")]
    public string? NameEs { get; set; }
}

/// <summary>An entry of skills.json: the lookup for names and descriptions, keyed by skillNo in the file.</summary>
internal sealed class SkillEntryJson
{
    [JsonPropertyName("nameEn")]
    public string? NameEn { get; set; }

    [JsonPropertyName("nameEs")]
    public string? NameEs { get; set; }

    [JsonPropertyName("descriptionEs")]
    public string? DescriptionEs { get; set; }
}
