using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hardened Tactician (Murders at Karlov Manor,
/// {1}{W}{B}).
///
/// Creature — Human Warrior, 2/4. Oracle text (verified against Scryfall):
///   "{1}, Sacrifice a token: Draw a card."
///
/// ## Why it gets its own factory
/// Hardened Tactician is a token-aristocrats card-advantage engine: spend {1}
/// and feed a spent token to draw a card. The activated ability reuses the
/// exact "{N}, Sacrifice a token: Draw a card" shape already shipped by
/// <see cref="FountainportFactory"/> (whose second ability is
/// "{2}, {T}, Sacrifice a token: Draw a card"); Hardened Tactician's variant
/// drops the tap and uses {1}. No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Creature shape, mana cost {1}{W}{B}, 2/4, Human Warrior, multicolour
///   (white + black). Card shape comes from the embedded JSON
///   (<c>hardened-tactician.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>{1}, Sacrifice a token: Draw a card.</b> —
///   <see cref="ActivatedAbility"/> with cost stack
///   <c>[Mana({1}), SacrificeAToken]</c> (CR 602 — ordinary activated ability;
///   uses the stack). The token-sacrifice activation cost routes through the
///   shared <see cref="Primitives.Costs.SacrificeAToken"/> rail
///   (CR 111.8 / 701.16 → <see cref="SacrificeFilteredCost.ForToken"/>).
///   Resolution draws a single card through <see cref="Fx.DrawCards"/> so any
///   <c>DrawCardIntent</c> replacements participate (CR 120.6); an empty
///   library stamps the SBA loss flag (CR 704.5b) without throwing.
///
/// ## Rules citations
/// - CR 602 — activating an activated ability.
/// - CR 111.8 / 701.16 — "Sacrifice a token" (sacrificed to its owner's
///   graveyard; only a token the controller controls is a legal sacrifice).
/// - CR 121.1 / 120.6 — "Draw a card" through the per-draw replacement bus.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-token prompt</b>: when more than one token is controlled the
///   live activation dispatch prompts which to sacrifice
///   (<see cref="SacrificeFilteredCost"/> implements
///   <see cref="IChoosePermanentToSacrificeCost"/>); the factory-direct path
///   falls back to the first eligible token — the same deferred MVP the sibling
///   sacrifice-picker costs share.
/// </summary>
[CardName("Hardened Tactician")]
public static class HardenedTacticianFactory
{
    public const string CardName = "Hardened Tactician";
    public const string Slug = "hardened-tactician";

    /// <summary>CR 121.1 — "Draw a card."</summary>
    public const int DrawAmount = 1;

    /// <summary>
    /// Build Hardened Tactician. The Creature shape (name, {1}{W}{B}, 2/4,
    /// Human Warrior) is materialised from the embedded JSON definition; the
    /// activated ability is layered on here because the JSON
    /// <c>AbilityDefinition</c> schema does not yet express a
    /// sacrifice-a-token activation cost (same posture as
    /// <see cref="FountainportFactory"/>).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var creature = (Creature)CardDefinitionFactory.Build(def, owner);

        // ----------------------------------------------------------------
        // {1}, Sacrifice a token: Draw a card. (CR 602.)
        //
        // "Sacrifice a token" routes through the shared SacrificeAToken rail
        // (CR 111.8 / 701.16). Resolution draws one card through Fx.DrawCards
        // so DrawCardIntent replacements participate (CR 120.6) and an empty
        // library stamps the SBA loss flag (CR 704.5b).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card",
            () => Fx.DrawCards(creature.Controller ?? owner, DrawAmount));

        creature.AddAbility(new ActivatedAbility(
            source: creature,
            controller: owner,
            costs: new ICost[]
            {
                Primitives.Costs.Mana("{1}"),
                Primitives.Costs.SacrificeAToken(),
            },
            effects: new IEffect[] { drawEffect }));

        return creature;
    }
}
