using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Drownyard Temple (Shadows over Innistrad).
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {3}: Return this card from your graveyard to the battlefield tapped."
///
/// The base shape (name, Land type) and the <b>{T}: Add {C}</b> mana ability
/// are materialised from the embedded JSON definition
/// (<c>drownyard-temple.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="AgadeemTheUndercryptFactory"/>'s {T}: Add {B}). The
/// graveyard-recursion activated ability is layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express the
/// "return-this-from-graveyard" shape.
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype; owner / controller
///   wired.
///
/// - <b>{T}: Add {C}</b> — single <see cref="ManaAbility"/> producing one
///   colourless mana (CR 605.1 — mana ability, no stack), from JSON.
///
/// - <b>"{3}: Return this card from your graveyard to the battlefield tapped."
///   (CR 602)</b>: an <see cref="ActivatedAbility"/> sourced on this card. The
///   printed {3} mana cost is taken by the cost layer at activation
///   (<see cref="ManaCostCost"/>, CR 602.1b). The
///   "Return this card from your graveyard to the battlefield tapped" effect
///   runs inside the resolution closure (same posture as
///   <see cref="ScrapheapScroungerFactory"/>'s graveyard self-return).
///
///   The ability functions while the source is in the graveyard — the
///   activated-ability validator (<see cref="Majik.Core.Rules.ActionValidator"/>)
///   doesn't gate activation on the source's zone, so an ability whose effect
///   operates on the graveyard-resident source resolves correctly. The resolve
///   closure:
///     1. Verifies the Temple is still in the owner's graveyard
///        (CR 608.2b — if it has left, the ability does nothing).
///     2. Returns the Temple from the graveyard to the battlefield under its
///        owner's control (CR 110.1 / 400.7 — direct zone move, same primitive
///        as Scrapheap Scrounger's graveyard→battlefield put).
///     3. CR 701.21 — taps it ("enters the battlefield tapped"); the printed
///        rider says it returns <i>tapped</i>.
/// </summary>
[CardName("Drownyard Temple")]
public static class DrownyardTempleFactory
{
    public const string CardName = "Drownyard Temple";
    public const string Slug = "drownyard-temple";

    /// <summary>CR 602 — printed activation mana cost: {3}.</summary>
    public const string ActivationManaCost = "{3}";

    /// <summary>
    /// Construct Drownyard Temple. Identity + the {T}: Add {C} mana ability
    /// come from JSON; the {3} graveyard-recursion activated ability is
    /// attached here. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {C} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {3}: Return this card from your graveyard to the battlefield
        //   tapped. CR 602 — activated ability. The {3} mana cost is taken by
        //   the cost layer at activation; the return + tap are performed in
        //   the resolve closure. The closure short-circuits if the Temple has
        //   left the graveyard (CR 608.2b).
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return this from graveyard to the battlefield tapped",
            () =>
            {
                var graveyard = owner.Zones.Graveyard;

                // CR 608.2b — the Temple must still be in the graveyard for
                // the "Return this card from your graveyard" effect to do
                // anything.
                if (!graveyard.GetCards().Contains(land))
                {
                    return;
                }

                // Return the Temple from graveyard to the battlefield under
                // its owner's control (CR 110.1 / 400.7 — direct zone move,
                // same primitive as Scrapheap Scrounger's graveyard→battlefield
                // put).
                graveyard.RemoveCard(land);
                owner.Zones.Battlefield.AddCard(land);
                land.SetZone(ZoneType.Battlefield);
                land.SetController(owner);

                // CR 701.21 — the printed rider returns it TAPPED.
                if (!land.IsTapped)
                {
                    land.Tap();
                }
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivationManaCost) },
            effects: new IEffect[] { returnEffect }));

        return land;
    }
}
