using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cache Grab (Bloomburrow, {1}{G}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Mill four cards. You may put a permanent card from among the cards
///    milled this way into your hand. If you control a Squirrel or returned a
///    Squirrel card to your hand this way, create a Food token. (To mill four
///    cards, put the top four cards of your library into your graveyard. A
///    Food token is an artifact with "{2}, {T}, Sacrifice this token: You gain
///    3 life.")"
///
/// ## Implementation
///
/// Card shape comes from the embedded JSON (<c>cache-grab.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> (same data-only Instant shape as
/// <see cref="GrislySalvageFactory"/>). The resolve-time body lives in
/// <see cref="BuildResolveEffect"/> because the mill-and-choose closure needs
/// the live caster (not expressible in the data-only JSON schema).
///
/// The mill-and-choose half routes through the shared
/// <see cref="RevealAndChoose.RevealTopAndChooseAsync"/> primitive — the same
/// entry point <see cref="MalevolentRumbleFactory"/> uses. CR 701.13b: "mill
/// four cards" = put the top four cards of the library into the graveyard.
/// Routing the unpicked cards to <see cref="ZoneType.Graveyard"/> (and the
/// chosen permanent to <see cref="ZoneType.Hand"/>) is mechanically identical
/// to "mill four, then put a permanent card from among the cards milled this
/// way into your hand" — the chosen card never has to pass through the
/// graveyard to be picked (CR 701.13c — "from among the cards milled this way"
/// is a selection over the four cards involved in the mill). The primitive
/// returns the picked card, which is exactly the hook the Squirrel rider needs
/// to detect "returned a Squirrel card to your hand this way".
///
/// Differs from Malevolent Rumble (which makes an unconditional Eldrazi Spawn)
/// only in the token half: Cache Grab's Food token is <b>conditional</b>
/// (CR 603.6 / printed "if" clause) — created only if, at resolution, the
/// caster <i>controls</i> a Squirrel (CR 205.3m subtype) OR the permanent put
/// into hand by this spell was itself a Squirrel card. The primitive's empty
/// library is still a clean no-op for the mill half, and the conditional is
/// evaluated independently afterwards.
///
/// ## Deferred (v1 gaps)
/// - No <c>CardsMilledEvent</c> / <c>CardsRevealedEvent</c> is published —
///   same gap as every reveal-and-choose / self-mill factory (Malevolent
///   Rumble, Grisly Salvage). No live observer cares yet.
/// </summary>
[CardName(CardName)]
public static class CacheGrabFactory
{
    public const string CardName = "Cache Grab";
    public const string Slug = "cache-grab";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>How many cards Cache Grab mills.</summary>
    public const int MillCount = 4;

    /// <summary>
    /// Build the Cache Grab card shape from the embedded JSON definition
    /// (name, Instant, {1}{G}). The resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build Cache Grab's resolve effect — mill four (top four library →
    /// graveyard), may put a permanent card from among them into hand, then
    /// create a Food token if the caster controls a Squirrel or the card put
    /// into hand was a Squirrel. Routes through
    /// <see cref="RevealAndChoose.RevealTopAndChooseAsync"/>.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect(
                "Cache Grab: mill 4, may put a permanent card into hand, then " +
                "create a Food token if you control a Squirrel or returned a " +
                "Squirrel card to your hand.",
                async ctx =>
                {
                    // CR 701.13b — "mill four cards": put the top four cards of
                    // the library into the graveyard. The shared helper moves
                    // the unpicked cards to the graveyard (= milled) and the
                    // chosen permanent to hand. It handles library underflow
                    // (fewer than four cards), the printed "you may" opt-out
                    // (CR 116.1b), agent prompting (including the empty-eligible
                    // mill so the player still sees the milled pile), and routes
                    // zone moves through ZoneServiceRegistry when registered.
                    // Returns the card put into hand (null when declined / no
                    // eligible permanent) — the hook for the Squirrel rider.
                    var putInHand = await RevealAndChoose.RevealTopAndChooseAsync(
                        ctx: ctx,
                        caster: caster,
                        count: MillCount,
                        eligiblePredicate: IsPermanentCard,
                        optional: true,
                        label: "Permanent card to put into hand",
                        pickedDestination: ZoneType.Hand,
                        restDestination: ZoneType.Graveyard,
                        sourceTag: Slug).ConfigureAwait(false);

                    // CR 205.3m — Squirrel subtype. The Food token is
                    // conditional (printed "if" clause): create it iff the
                    // caster controls a Squirrel at resolution OR the permanent
                    // card returned to hand by THIS spell was a Squirrel card.
                    var returnedSquirrel =
                        putInHand != null && putInHand.HasSubtype(CardSubtype.Squirrel);
                    var controlsSquirrel = ControlsSquirrel(caster);

                    if (returnedSquirrel || controlsSquirrel)
                    {
                        // CR 111.10 — Food token shape (incl. its own
                        // "{2},{T},Sac: gain 3 life") stamped by TokenFactory.
                        TokenFactory.CreateFood(caster);
                    }
                }),
        };
    }

    // CR 110.1 — permanent card types (artifact, creature, enchantment, land,
    // planeswalker). Mirrors MalevolentRumbleFactory.IsPermanentCard; battle
    // is in the printed permanent list but the engine's CardType enum predates
    // it, so it's omitted until shipped.
    private static bool IsPermanentCard(ICard c) =>
        c.HasType(CardType.Creature) ||
        c.HasType(CardType.Artifact) ||
        c.HasType(CardType.Enchantment) ||
        c.HasType(CardType.Land) ||
        c.HasType(CardType.Planeswalker);

    // CR 205.3m — "you control a Squirrel": any Squirrel-subtyped permanent on
    // the caster's battlefield (HasSubtype reads the effective subtypes, so a
    // creature granted the Squirrel type also qualifies).
    private static bool ControlsSquirrel(Player caster) =>
        caster.Zones.Battlefield.GetCards()
            .Any(c => c.HasSubtype(CardSubtype.Squirrel));
}
