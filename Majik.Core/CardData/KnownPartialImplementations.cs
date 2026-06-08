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
                "Ability-grant static mostly implemented: an imprinted creature's MANA abilities "
                + "('{T}: Add …') AND the common soundly-reconstructable NON-mana activated "
                + "abilities are re-homed to each +1/+1-countered creature you control, sourced on "
                + "the bearer (the cost taps/sacrifices the bearer; 'this creature' = bearer). "
                + "Non-mana shapes covered (OracleActivatedAbilityBinder): firebreathing / self-pump "
                + "('{cost}: This creature gets +X/+Y until end of turn'), pingers ('{cost}: This "
                + "creature deals N damage to any target / target creature / target player'), and "
                + "sacrifice-self pingers ('Sacrifice this creature: It deals N damage to …'), with a "
                + "', '-separated mana+{T} cost grammar. Residual (skipped, NOT emitted broken): "
                + "bespoke abilities outside that set — tutors, token makers, modal/'choose one', "
                + "anthem grants, '{T}: Draw', loyalty-style; abilities with unmodellable cost tokens "
                + "({X}, energy {E}, snow {S}, Phyrexian, 'Pay N life', 'Discard a card'); 'Activate "
                + "only …' riders; restricted damage targets. Fully closing the residual waits on a "
                + "re-bindable ability model. Targeting + Legendary supertype + mana-colour-substitution "
                + "done (#2497)."),

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
                "Loyalty abilities + zone-conditional CDA bound; only the −2 target-prompt gap remains. The CDA ('As long as Grist isn't on the battlefield, it's a 1/1 Insect creature in addition to its other types', CR 604.3) is fully modelled via Card.SetOffBattlefieldCharacteristics — off the battlefield Grist is a 1/1 Insect creature (tutors/reanimation/delirium see it); on the battlefield it is only a Planeswalker. The +1 (Insect token + mill loop + loyalty counters) and −5 (each opponent loses life per creature card in graveyard) are fully implemented; the −2 sacrifice/destroy runs deterministically through resolvers (no agent target prompt — same loyalty-ability gap as Koth/Liliana). That −2 prompt is the only residual."),
        };

    /// <summary>True when <paramref name="name"/> has a recorded gap.</summary>
    public static bool TryGet(string name, out CardGap gap)
        => ByName.TryGetValue(name, out gap!);
}
