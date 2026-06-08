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
                "Ability-grant static broadly implemented: an imprinted creature's MANA abilities "
                + "('{T}: Add …') AND its non-mana activated abilities are re-homed to each "
                + "+1/+1-countered creature you control, sourced on the bearer (the cost "
                + "taps/sacrifices the bearer; 'this creature' = bearer). PRIMARY mechanism is now "
                + "ActivatedAbility.RebindTo of the creature's REAL abilities (CR 707.2) — it covers "
                + "WHATEVER abilities the card actually has, not just oracle-parseable shapes, gated "
                + "on ActivatedAbility.RebindSafe (true for ALL data-driven CardDef abilities, whose "
                + "self-source verbs pump/connive/explore read ResolutionContext.Source, with the "
                + "rest scoped to controller/chosen targets). FALLBACK oracle-rebuild "
                + "(OracleActivatedAbilityBinder) runs only when RebindTo yields nothing, "
                + "reconstructing firebreathing / pinger / sacrifice-self-pinger from oracle text. "
                + "Residual (NOT emitted broken): bespoke [CardName]-factory activated abilities "
                + "whose effect closures still capture the original card (not yet RebindSafe) AND "
                + "whose oracle text is outside the fallback's firebreathing/pinger/sac set. As such "
                + "factories migrate their effects to ResolutionContext.Source + mark RebindSafe, the "
                + "RebindTo path picks them up automatically. Targeting + Legendary supertype + "
                + "mana-colour-substitution done (#2497)."),

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

            // Grist, the Hunger Tide is now FULLY implemented and was removed
            // from this registry: loyalty abilities are playable end-to-end
            // through the priority loop (sorcery-speed activation, stack
            // resolution), and the −2's destroy is a real agent-prompted target
            // (the last documented residual). The +1 (Insect token + mill loop +
            // loyalty counters), −2 (sacrifice + prompted destroy), −5 (each
            // opponent loses life per creature card in graveyard), and the
            // zone-conditional 1/1-Insect CDA (CR 604.3) are all complete.
        };

    /// <summary>True when <paramref name="name"/> has a recorded gap.</summary>
    public static bool TryGet(string name, out CardGap gap)
        => ByName.TryGetValue(name, out gap!);
}
