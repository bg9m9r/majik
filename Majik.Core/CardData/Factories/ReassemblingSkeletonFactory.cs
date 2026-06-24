using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reassembling Skeleton (Magic 2010 / Modern
/// reprints, {1}{B}).
///
/// Creature — Skeleton Warrior 1/1. Oracle text (verified against Scryfall):
///   "{1}{B}: Return this card from your graveyard to the battlefield
///    tapped."
///
/// The base shape (name, Creature type, Skeleton + Warrior subtypes,
/// {1}{B}, 1/1) is materialised from the embedded JSON definition
/// (<c>reassembling-skeleton.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single printed behaviour
/// (graveyard-recursion activated ability) is layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express the
/// "return-from-graveyard" shape.
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Skeleton Warrior at printed cost {1}{B}; owner /
///   controller wired.
///
/// - <b>"{1}{B}: Return this card from your graveyard to the battlefield
///   tapped." (CR 602)</b>: an <see cref="ActivatedAbility"/> sourced on
///   this card. The printed {1}{B} mana cost is taken by the cost layer at
///   activation (<see cref="ManaCostCost"/>, CR 602.1b). The "Return this
///   card from your graveyard to the battlefield tapped" effect is performed
///   inside the resolution closure (same posture as
///   <see cref="ScrapheapScroungerFactory"/>'s graveyard-recursion, minus
///   the exile cost).
///
///   The ability functions while the source is in the graveyard — the
///   activated-ability validator (<see cref="Majik.Core.Rules.ActionValidator"/>)
///   doesn't gate activation on the source's zone, so an ability whose
///   effect operates on the graveyard-resident source resolves correctly
///   (identical posture to Scrapheap Scrounger). The resolve closure:
///     1. Verifies the Skeleton is still in the owner's graveyard
///        (CR 608.2b — if it has left, the ability does nothing).
///     2. Returns the Skeleton from the graveyard to the battlefield under
///        its owner's control (CR 110.1 / 400.7 — direct zone move, same
///        primitive as Scrapheap Scrounger's graveyard→battlefield put).
///     3. Taps it on entry (CR 701.21 — "to the battlefield tapped"). Unlike
///        a triggered/replacement "enters tapped", the printed effect simply
///        puts the card onto the battlefield already tapped; the closure taps
///        it immediately after the move.
///
/// ## Deferred (v1 gaps)
///
/// - <b>None for this card's printed behaviour.</b> There is no can't-block
///   rider and no additional cost, so the single-arg
///   <see cref="Create(Player)"/> path is fully faithful — there is no
///   effects-service-dependent rider to skip (unlike Gravecrawler /
///   Scrapheap Scrounger).
/// </summary>
[CardName("Reassembling Skeleton")]
public static class ReassemblingSkeletonFactory
{
    public const string CardName = "Reassembling Skeleton";
    public const string Slug = "reassembling-skeleton";

    /// <summary>CR 602 — printed activation mana cost: {1}{B}.</summary>
    public const string ActivationManaCost = "{1}{B}";

    /// <summary>
    /// Construct Reassembling Skeleton (name, Creature type, Skeleton +
    /// Warrior subtypes, {1}{B}, 1/1) with its graveyard-recursion activated
    /// ability attached. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to. There is no continuous-effects-dependent rider, so no
    /// effects-service overload is needed.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Skeleton + Warrior subtypes, {1}{B}, 1/1).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {1}{B}: Return this card from your graveyard to the battlefield
        //   tapped.
        // CR 602 — activated ability. The {1}{B} mana cost is taken by the
        // cost layer at activation; the return + tap are performed in the
        // resolve closure. The closure short-circuits if the Skeleton has
        // left the graveyard (CR 608.2b).
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return this from graveyard to the battlefield tapped",
            () =>
            {
                var graveyard = owner.Zones.Graveyard;

                // CR 608.2b — the Skeleton must still be in the graveyard for
                // the "Return this card from your graveyard" effect to do
                // anything.
                if (!graveyard.GetCards().Contains(card))
                {
                    return;
                }

                // Return the Skeleton from graveyard to the battlefield under
                // its owner's control (CR 110.1 / 400.7 — direct zone move,
                // same primitive as Scrapheap Scrounger's graveyard→battlefield
                // put).
                graveyard.RemoveCard(card);
                owner.Zones.Battlefield.AddCard(card);
                card.SetZone(ZoneType.Battlefield);
                card.SetController(owner);

                // CR 701.21 — "to the battlefield tapped". Tap the Skeleton on
                // entry. Guard against a double-tap (the card mints untapped,
                // but a returned permanent could theoretically already be
                // tapped via some other path).
                if (!card.IsTapped)
                {
                    card.Tap();
                }
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivationManaCost) },
            effects: new IEffect[] { returnEffect }));

        return card;
    }
}
