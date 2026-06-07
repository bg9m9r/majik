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

            // --- Seeded from the first BotDeckImplementationAuditTests run ---
            // (audit materializes cards via the LIVE engine path: non-land
            // factory-backed cards route through their [CardName] factory, so
            // these are the cards that genuinely do nothing in real play.)

            // Fetchlands — no [CardName] factory and the binder chain does not
            // bind the "{T}, Pay 1 life, Sacrifice: search a typed land" fetch
            // activated ability, so they build as a do-nothing land. Lands are
            // never routed through named factories (GameFacade), so this gap is
            // real in production. See MEMORY: fetchland-resolution is a known
            // engine gap.
            ["Arid Mesa"] = new CardGap(CardGapSeverity.Stub,
                "Fetchland: sacrifice-to-search activated ability not bound by the binder chain (no factory; lands aren't routed) — vanilla land in play."),
            ["Bloodstained Mire"] = new CardGap(CardGapSeverity.Stub,
                "Fetchland: sacrifice-to-search activated ability not bound by the binder chain (no factory; lands aren't routed) — vanilla land in play."),
            ["Flooded Strand"] = new CardGap(CardGapSeverity.Stub,
                "Fetchland: sacrifice-to-search activated ability not bound by the binder chain (no factory; lands aren't routed) — vanilla land in play."),
            ["Marsh Flats"] = new CardGap(CardGapSeverity.Stub,
                "Fetchland: sacrifice-to-search activated ability not bound by the binder chain (no factory; lands aren't routed) — vanilla land in play."),
            ["Misty Rainforest"] = new CardGap(CardGapSeverity.Stub,
                "Fetchland: sacrifice-to-search activated ability not bound by the binder chain (no factory; lands aren't routed) — vanilla land in play."),
            ["Polluted Delta"] = new CardGap(CardGapSeverity.Stub,
                "Fetchland: sacrifice-to-search activated ability not bound by the binder chain (no factory; lands aren't routed) — vanilla land in play."),
            ["Scalding Tarn"] = new CardGap(CardGapSeverity.Stub,
                "Fetchland: sacrifice-to-search activated ability not bound by the binder chain (no factory; lands aren't routed) — vanilla land in play."),
            ["Verdant Catacombs"] = new CardGap(CardGapSeverity.Stub,
                "Fetchland: sacrifice-to-search activated ability not bound by the binder chain (no factory; lands aren't routed) — vanilla land in play."),
            ["Windswept Heath"] = new CardGap(CardGapSeverity.Stub,
                "Fetchland: sacrifice-to-search activated ability not bound by the binder chain (no factory; lands aren't routed) — vanilla land in play."),
            ["Wooded Foothills"] = new CardGap(CardGapSeverity.Stub,
                "Fetchland: sacrifice-to-search activated ability not bound by the binder chain (no factory; lands aren't routed) — vanilla land in play."),
            ["Prismatic Vista"] = new CardGap(CardGapSeverity.Stub,
                "Basic-fetch land: has a PrismaticVistaFactory but lands are never routed through named factories, and the binder chain doesn't bind the fetch ability — vanilla land in play."),

            // Horizon Canopy cycle (pain-mana + sac-to-draw). The binder chain
            // binds neither the pain mana ability nor the sac-to-draw activated
            // ability, so they currently produce a do-nothing land.
            ["Fiery Islet"] = new CardGap(CardGapSeverity.Stub,
                "Horizon land: pain-mana + '{1},{T},Sacrifice: draw' not bound by the binder chain (no factory) — vanilla land in play."),
            ["Sunbaked Canyon"] = new CardGap(CardGapSeverity.Stub,
                "Horizon land: pain-mana + '{1},{T},Sacrifice: draw' not bound by the binder chain (no factory) — vanilla land in play."),

            // Lands with a working mana ability but a missing triggered ability.
            // Lands are never routed through named factories, so their bespoke
            // factory triggers (if any) don't run in play; the binder chain
            // doesn't bind these triggers either.
            ["Bojuka Bog"] = new CardGap(CardGapSeverity.Partial,
                "Mana ability + enters-tapped work; the 'When this land enters, exile target player's graveyard' triggered ability is not bound (lands aren't routed)."),
            ["Sanctum of Ugin"] = new CardGap(CardGapSeverity.Partial,
                "Colorless mana ability works; the 'Whenever you cast a colorless spell with mana value 7+' sacrifice-to-tutor triggered ability is not bound (lands aren't routed)."),

            // Non-land factory-backed cards (routed in production) whose factory
            // builds part of the card but not the implied triggered ability.
            ["Leyline Binding"] = new CardGap(CardGapSeverity.Partial,
                "Domain cost reduction (Flash + cheaper-per-basic-type) works via the factory; the 'When this enchantment enters, exile target nonland permanent...' O-Ring ETB triggered ability is not bound."),
            ["Necrodominance"] = new CardGap(CardGapSeverity.Partial,
                "Skip-draw, max-hand-size-5 and graveyard-replacement statics work; the 'At the beginning of your end step, you may pay any amount of life: draw that many cards' triggered ability is not bound."),
            ["Utopia Sprawl"] = new CardGap(CardGapSeverity.Partial,
                "Aura attaches (Enchant Forest); the 'Whenever enchanted Forest is tapped for mana, add an additional mana of the chosen color' triggered ability isn't wired on the routed build because the 'As this Aura enters, choose a color' prompt is deferred engine-wide."),
        };

    /// <summary>True when <paramref name="name"/> has a recorded gap.</summary>
    public static bool TryGet(string name, out CardGap gap)
        => ByName.TryGetValue(name, out gap!);
}
