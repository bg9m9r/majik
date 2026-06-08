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

            // --- Re-derived from the faithful BotDeckImplementationAuditTests
            // run (cards built via the real GameFacade.Create + PopulateSideboard
            // path, full binder set incl. OracleLandActivatedAbilityBinder +
            // live services). These are the cards that genuinely do nothing in
            // real play. NOTE: most fetchlands are NOW implemented — the land
            // binder runs on the faithful path — so only the ones the binder's
            // regex still misses remain Stubs.

            // NOTE: all 10 fetchlands are NOW bound by OracleLandActivatedAbilityBinder
            // on the faithful path. The "an <vowel-basic>" fetchlands (Polluted
            // Delta, Scalding Tarn) used to be missed because the FetchLand regex
            // required a consonant article "a <Basic>"; the regex now accepts
            // "an?" so both are bound. Prismatic Vista's "a basic land card" form
            // is bound via the BasicLandFetch branch. All three were removed from
            // this registry.

            // Horizon Canopy cycle (pain-mana + sac-to-draw). The binder chain
            // binds neither the pain mana ability nor the sac-to-draw activated
            // ability, so they currently produce a do-nothing land.
            ["Fiery Islet"] = new CardGap(CardGapSeverity.Stub,
                "Horizon land: pain-mana + '{1},{T},Sacrifice: draw' not bound by the binder chain (no factory) — vanilla land in play."),
            ["Sunbaked Canyon"] = new CardGap(CardGapSeverity.Stub,
                "Horizon land: pain-mana + '{1},{T},Sacrifice: draw' not bound by the binder chain (no factory) — vanilla land in play."),

            // Non-land factory-backed cards (routed in production) whose factory
            // builds part of the card but not the implied triggered ability.
            ["Utopia Sprawl"] = new CardGap(CardGapSeverity.Partial,
                "Aura attaches (Enchant Forest); the 'Whenever enchanted Forest is tapped for mana, add an additional mana of the chosen color' triggered ability isn't wired on the routed build because the 'As this Aura enters, choose a color' prompt is deferred engine-wide."),
            ["Grist, the Hunger Tide"] = new CardGap(CardGapSeverity.Partial,
                "Loyalty abilities dropped on routed planeswalker build; CDA deferred."),
        };

    /// <summary>True when <paramref name="name"/> has a recorded gap.</summary>
    public static bool TryGet(string name, out CardGap gap)
        => ByName.TryGetValue(name, out gap!);
}
