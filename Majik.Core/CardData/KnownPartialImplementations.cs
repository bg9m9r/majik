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
                "Ability-grant static partially implemented: the MANA-ability slice is granted "
                + "(an imprinted creature's '{T}: Add …' is re-homed to each +1/+1-countered "
                + "creature you control, sourced on the bearer — taps the bearer, not the exiled "
                + "card). Still deferred: NON-mana activated abilities of imprinted creatures "
                + "('{2}: this gets +1/+1', '{T}: deal 1 damage', etc.) — no general re-source-able "
                + "oracle→activated-ability binder exists, so an arbitrary creature's non-mana "
                + "ability can't be soundly rebuilt against a new source. Targeting + Legendary "
                + "supertype + mana-colour-substitution done (#2497)."),

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

            // Horizon Canopy cycle (pain-mana + sac-to-draw) is now bound on the
            // prod binder path: OracleManaBinder recognises "{T}, Pay 1 life: Add
            // {A} or {B}" and OracleLandActivatedAbilityBinder recognises "{1},
            // {T}, Sacrifice this land: Draw a card" — both route through
            // HorizonLandBinder. Fiery Islet + Sunbaked Canyon removed.

            // Non-land factory-backed cards (routed in production) whose factory
            // builds part of the card but not the implied triggered ability.
            ["Grist, the Hunger Tide"] = new CardGap(CardGapSeverity.Partial,
                "CDA (1/1 Insect off-battlefield) deferred; loyalty abilities now bound. The +1 (Insect token + mill loop + loyalty counters) and −5 (each opponent loses life per creature card in graveyard) are fully implemented; the −2 sacrifice/destroy runs deterministically through resolvers (no agent target prompt — same loyalty-ability gap as Koth/Liliana). The CDA's conditional 'creature only while not on the battlefield' toggle needs a zone-conditional layer-4/7b CDA primitive the engine lacks (CDAs apply on-battlefield only today); Creature type is added unconditionally so creature tutors still find Grist."),
        };

    /// <summary>True when <paramref name="name"/> has a recorded gap.</summary>
    public static bool TryGet(string name, out CardGap gap)
        => ByName.TryGetValue(name, out gap!);
}
