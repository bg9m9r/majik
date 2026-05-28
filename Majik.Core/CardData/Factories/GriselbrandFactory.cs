using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Griselbrand (Avacyn Restored, {4}{B}{B}{B}{B}).
///
/// Legendary Creature — Demon 7/7. Oracle text:
///   "Flying
///    Lifelink
///    Pay 7 life: Draw seven cards."
///
/// ## Implemented (v1)
/// - 7/7 Legendary Creature — Demon at printed cost {4}{B}{B}{B}{B} (MV 8,
///   CR 202.3). <see cref="CardSupertype.Legendary"/> supertype so the
///   Legend Rule (CR 704.5j) applies when a second Griselbrand enters.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker — combat
///   helpers in <see cref="Majik.Core.Combat.CombatAbilities"/> read it
///   for blocking / evasion.
/// - <b>Lifelink (CR 702.15)</b>: <see cref="KeywordAbility"/> marker —
///   combat helpers accumulate life gain when Griselbrand deals damage.
/// - <b>Activated ability — "Pay 7 life: Draw seven cards."</b>
///   Cost = <see cref="AdditionalCost.PayLife"/>(7); no mana cost (CR 605).
///   <see cref="AdditionalCost.CanPay"/> requires controller life > 7 —
///   this is the engine's posture for life costs (strictly greater, same
///   as Necropotence / Spellskite). Effect calls
///   <see cref="Fx.DrawCards"/>(controller, 7) so CR 121.1
///   per-draw replacement logic (Dredge, etc.) fires for each of the
///   seven draws, and an empty library flags the "tried to draw from
///   empty library" SBA (CR 704.5b).
///
/// ## Deferred (v1 gaps)
/// - <b>Activated-ability speed restriction</b>: per oracle text Griselbrand's
///   ability has no speed restriction — it is legal at any time the
///   controller has priority (including opponent's turn). v1 has no
///   per-ability timing gate, so this is already correct by default.
/// - <b>Summoning sickness</b>: activated abilities that don't use {T} or
///   {Q} are not gated by summoning sickness (CR 302.6), so the ability
///   can be used the turn Griselbrand enters. v1 engine posture matches.
/// - <b>Lifelink damage trigger</b>: the combat lifelink wiring is handled
///   by <see cref="Majik.Core.Combat.CombatAbilities"/> reading the
///   KeywordAbility marker — the factory only attaches the marker.
/// </summary>
[CardName("Griselbrand")]
public static class GriselbrandFactory
{
    public const string CardName = "Griselbrand";
    public const string PrintedManaCost = "{4}{B}{B}{B}{B}";
    public const int Power = 7;
    public const int Toughness = 7;

    /// <summary>
    /// Construct Griselbrand fully populated: 7/7 Legendary Creature — Demon,
    /// Flying, Lifelink markers, and the activated "Pay 7 life: Draw seven
    /// cards" ability.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Demon });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker. Combat helpers read this for
        // evasion and blocker-legality checks.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.15 — Lifelink keyword marker. Combat helpers accumulate
        // life gain equal to damage dealt by Griselbrand.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // --------------------------------------------------------------------
        // Activated ability — "Pay 7 life: Draw seven cards."
        //
        // CR 605 — not a mana ability; no {T} in the cost; no speed
        // restriction on the oracle text (legal at any time the controller
        // has priority). Cost = AdditionalCost.PayLife(7); CanPay enforces
        // life > 7 (engine posture). Effect = Fx.DrawCards(controller, 7)
        // so every per-draw CR 121.1 replacement (Dredge, Leyline of the
        // Void, etc.) fires for each draw, and an empty library sets the
        // tried-to-draw SBA flag (CR 704.5b).
        // --------------------------------------------------------------------
        var drawAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.PayLife(7),
            },
            effects: new IEffect[]
            {
                new Effect(
                    "Griselbrand: draw seven cards",
                    () =>
                    {
                        // CR 113.6 — activated abilities only function while
                        // the source is on the battlefield (or where specified
                        // by the ability; Griselbrand specifies no zone override).
                        if (card.Zone != ZoneType.Battlefield) return;

                        var controller = card.Controller ?? owner;
                        Fx.DrawCards(controller, 7);
                    }),
            });

        card.AddAbility(drawAbility);

        return card;
    }
}
