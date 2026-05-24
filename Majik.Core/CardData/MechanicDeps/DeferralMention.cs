namespace Majik.Core.CardData.MechanicDeps;

/// <summary>
/// One raw deferral mention extracted from a factory source file. The
/// scanner emits one of these per matching sentence/comment line; the
/// clusterer is then responsible for collapsing them by canonical
/// primitive ID.
///
/// Kept as a flat record so the JSON sidecar serializes cleanly and the
/// downstream clusterer doesn't depend on Roslyn / file-IO types.
/// </summary>
public sealed record DeferralMention(
    string FactoryFile,
    string FactoryName,
    int LineNumber,
    string Sentence,
    string? CompRulesCitation);
