using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grisly Salvage (Dragon's Maze, {B}{G}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Reveal the top five cards of your library. You may put a creature or
///    land card from among them into your hand. Put the rest into your
///    graveyard."
///
/// ## Implementation
///
/// Card shape comes from the embedded JSON (<c>grisly-salvage.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> (same data-only Instant shape as
/// <see cref="NoxiousRevivalFactory"/>). The resolve-time body lives in
/// <see cref="BuildResolveEffect"/> because the reveal-and-choose closure
/// needs the live caster (not expressible in the data-only JSON schema).
///
/// The reveal half routes through the shared
/// <see cref="RevealAndChoose.RevealTopAndChooseAsync"/> primitive — the same
/// CR 701.15 "reveal the top N cards, you may put one matching card into your
/// hand, rest into your graveyard" family entry point
/// <see cref="MalevolentRumbleFactory"/> uses. Grisly Salvage differs only in:
///   - <b>count = 5</b> (not 4),
///   - <b>eligible predicate = creature OR land</b> (CR 110.1 — not "any
///     permanent"; instants/sorceries/non-land artifacts/enchantments/
///     planeswalkers are ineligible and fall to the graveyard), and
///   - <b>no token half</b> (Malevolent Rumble additionally makes an Eldrazi
///     Spawn; Grisly Salvage does not).
///
/// The primitive handles library underflow (CR 701.15a — "top five" reveals
/// whatever is there when the library holds fewer than five), the printed
/// optional "you may" opt-out (CR 116.1b), agent prompting (including the
/// empty-eligible reveal so the player still sees the reveal pile), and routes
/// zone moves through <see cref="Majik.Core.Services.ZoneServiceRegistry"/>
/// when registered so the picked card's ETB / CardMovedEvent and the
/// graveyard-bound cards' leaves-library observers fire.
///
/// ## Deferred (v1 gaps)
/// - No <c>CardsRevealedEvent</c> is published — same gap as every other
///   reveal-and-choose factory (Malevolent Rumble, Ancient Stirrings).
/// </summary>
[CardName("Grisly Salvage")]
public static class GrislySalvageFactory
{
    public const string CardName = "Grisly Salvage";
    public const string Slug = "grisly-salvage";
    public const string PrintedManaCost = "{B}{G}";

    /// <summary>
    /// Build the Grisly Salvage card shape from the embedded JSON definition
    /// (name, Instant, {B}{G}). The resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build Grisly Salvage's resolve effect — reveal top 5, may put a
    /// creature or land card into hand, rest into graveyard. Routes through
    /// <see cref="RevealAndChoose.RevealTopAndChooseAsync"/> (CR 701.15).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect(
                "Grisly Salvage: reveal top 5, may put a creature or land card " +
                "into hand, rest into graveyard.",
                async ctx =>
                {
                    // CR 701.15 — reveal top 5, may put a creature or land card
                    // into hand, rest into graveyard. Shared helper handles
                    // library underflow, the "you may" opt-out, agent prompting
                    // (including the empty-eligible reveal so the player still
                    // sees the reveal pile), and routes zone moves through
                    // ZoneServiceRegistry when registered.
                    await RevealAndChoose.RevealTopAndChooseAsync(
                        ctx: ctx,
                        caster: caster,
                        count: 5,
                        eligiblePredicate: IsCreatureOrLand,
                        optional: true,
                        label: "Creature or land card to put into hand",
                        pickedDestination: ZoneType.Hand,
                        restDestination: ZoneType.Graveyard,
                        sourceTag: "grisly-salvage").ConfigureAwait(false);
                }),
        };
    }

    // CR 110.1 — "a creature or land card": only the Creature and Land card
    // types are eligible (note a creature land qualifies via either). Every
    // other revealed card (instant, sorcery, non-land artifact, enchantment,
    // planeswalker) goes to the graveyard.
    private static bool IsCreatureOrLand(ICard c) =>
        c.HasType(CardType.Creature) ||
        c.HasType(CardType.Land);
}
