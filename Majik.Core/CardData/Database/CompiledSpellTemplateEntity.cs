namespace Majik.Core.CardData.Database;

/// <summary>
/// Pre-compiled spell-template match for a single card. Produced offline
/// by <c>Majik.Console -- compile-templates</c> walking every instant/
/// sorcery and recording the highest-priority <c>ISpellTemplate</c> that
/// matches the card's oracle text.
///
/// One row per card name — the registry's priority + name ordering
/// guarantees a stable "winner" per card. Runtime <c>OracleSpellBinder.Bind</c>
/// (PR-D) looks up this row by card name, falls back to walking the
/// live registry when no row exists (e.g. an unimported card).
/// </summary>
public class CompiledSpellTemplateEntity
{
    /// <summary>Card name (primary key — at most one compiled row per
    /// card; runtime always uses the highest-priority match).</summary>
    public string CardName { get; set; } = "";

    /// <summary>Stable identifier from <c>ISpellTemplate.Name</c>.</summary>
    public string TemplateName { get; set; } = "";

    /// <summary>Priority of the matched template at compile time. Stored
    /// so a follow-up tool can spot drift between the compiled DB and
    /// the in-process registry.</summary>
    public int Priority { get; set; }

    /// <summary>JSON-serialized <c>IReadOnlyDictionary&lt;string,string&gt;</c>
    /// produced by <c>ISpellTemplate.TryExtractParams</c>. Empty object
    /// (<c>"{}"</c>) when the template has no captures.</summary>
    public string ParamsJson { get; set; } = "{}";

    /// <summary>Unix-seconds timestamp when this row was written.</summary>
    public long CompiledAt { get; set; }
}
