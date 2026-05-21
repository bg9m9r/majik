namespace Majik.Core.CardData.SpellTemplates;

/// <summary>
/// Shared empty-but-non-null parameter dictionary for templates whose
/// <see cref="ISpellTemplate.TryExtractParams"/> result carries no
/// captures. Lets the compile pipeline distinguish "matched, no
/// parameters" from "no match" (null) without allocating a fresh
/// dictionary per matched card.
/// </summary>
internal static class EmptyParams
{
    public static readonly IReadOnlyDictionary<string, string> Instance =
        new Dictionary<string, string>(capacity: 0);
}
