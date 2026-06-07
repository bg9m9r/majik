namespace Majik.Core.CardData;

/// <summary>How incomplete a card's implementation is.</summary>
public enum CardGapSeverity
{
    /// <summary>The card currently does nothing it should — a vanilla shell in
    /// a bot deck (<see cref="Majik.Core.Cards.ICard.IsVanillaShell"/>).</summary>
    Stub,

    /// <summary>The card implements some of its text but has a documented gap
    /// (e.g. Agatha's Soul Cauldron's ability-grant static).</summary>
    Partial,
}

/// <summary>One known implementation gap for a card.</summary>
public sealed record CardGap(CardGapSeverity Severity, string Reason);

/// <summary>
/// Machine-readable registry of cards with a KNOWN implementation gap. The
/// bot-deck implementation audit (<c>BotDeckImplementationAuditTests</c>) gates
/// against this: a newly-detected Stub / MissingTrigger card that is NOT here
/// fails the build, and a <see cref="CardGapSeverity.Stub"/> entry that is no
/// longer detected as a shell fails as "stale". Lives in prod (not test) so the
/// portal/runtime can later surface a "partial coverage" badge.
///
/// <para><see cref="CardGapSeverity.Partial"/> entries are documentation only —
/// the card does something, so there is no cheap signal that the remaining part
/// is still missing.</para>
/// </summary>
public static class KnownPartialImplementations
{
    public static readonly IReadOnlyDictionary<string, CardGap> ByName =
        new Dictionary<string, CardGap>(StringComparer.Ordinal)
        {
            ["Agatha's Soul Cauldron"] = new CardGap(
                CardGapSeverity.Partial,
                "Ability-grant static deferred (closure re-home blocker, v1-deferrals #5); "
                + "real targeting + Legendary supertype done (#2497)."),
            // Further entries are seeded in Task 3 from the first audit run.
        };

    /// <summary>True when <paramref name="name"/> has a recorded gap.</summary>
    public static bool TryGet(string name, out CardGap gap)
        => ByName.TryGetValue(name, out gap!);
}
