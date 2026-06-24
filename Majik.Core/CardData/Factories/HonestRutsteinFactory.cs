using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Honest Rutstein (Murders at Karlov Manor, {1}{B}{G}).
///
/// Legendary Creature — Human Warlock 3/2. Oracle text (verified against
/// Scryfall):
///   "When Honest Rutstein enters, return target creature card from your
///    graveyard to your hand.
///    Creature spells you cast cost {1} less to cast."
///
/// The base shape (name, Legendary supertype, Creature, Human + Warlock
/// subtypes, {1}{B}{G}, 3/2) is materialised from the embedded JSON
/// definition (<c>honest-rutstein.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two abilities are layered
/// on here — the JSON <c>AbilityDefinition</c> schema carries neither a
/// graveyard-target ETB nor a parameterised spell-cost reducer.
///
/// ## Implemented (v1)
///
/// - <b>ETB "return target creature card from your graveyard to your hand"
///   (CR 603.6a)</b> — a single <see cref="TriggeredAbility"/> wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with a bespoke 1..1
///   <see cref="TargetRequest"/> whose candidate pool is the CREATURE cards
///   in the controller's graveyard (CR 700.6 — singular "target"). This
///   mirrors <see cref="EternalWitnessFactory"/>'s graveyard-return shape
///   but narrows the filter to creature cards (CR 109.2 — "creature card"
///   matches printed card type, the Animate-Dead-style target). The resolve
///   body honours an agent-set <see cref="TriggeredAbility.ChosenTargets"/>,
///   else falls back to the first creature card in the graveyard
///   deterministically (same posture as Eternal Witness / Wishclaw Talisman),
///   re-validates the pick is still a creature card in the controller's
///   graveyard at resolution (CR 608.2b — illegal target → clean no-op), and
///   moves Graveyard → Hand via <see cref="ZoneService.MoveCard"/> when
///   supplied so any "leaves graveyard" triggers fire (CR 701.20).
///
/// - <b>"Creature spells you cast cost {1} less to cast." (CR 117.7)</b> —
///   wired via <see cref="SpellCostReductionAbility"/>, the same subtractive
///   battlefield-rider shape Danitha Capashen / Goblin Electromancer use.
///   The predicate gates on the spell carrying CardType.Creature; the
///   reduction is a flat 1 generic per cast.
///   <see cref="Majik.Core.Costs.CostReduction.GetEffectiveCost"/> scans only
///   the caster's battlefield for this rider, so the "you cast" scope
///   (CR 117.7) is enforced by the cost-calc helper. Coloured pips are
///   untouched (CR 117.7c) and the cost floors at zero in the cost-calc
///   helper.
///
/// ## Deferred (v1 gaps)
/// - <b>Real agent-driven target prompt</b>: production callers wire
///   <see cref="TriggeredAbility.SetChosenTargets"/> from an agent prompt
///   before triggers resolve. The factory's first-creature fallback is the
///   dispatcher-path safety net — same gap as Eternal Witness.
/// </summary>
[CardName("Honest Rutstein")]
public static class HonestRutsteinFactory
{
    public const string CardName = "Honest Rutstein";
    public const string Slug = "honest-rutstein";

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "When Honest Rutstein enters, return target creature card from your " +
        "graveyard to your hand.\n" +
        "Creature spells you cast cost {1} less to cast.";

    /// <summary>
    /// Construct Honest Rutstein with no runtime wiring. Produces the correct
    /// card identity + ETB trigger shape + cost reducer for dispatcher /
    /// shape tests; the trigger is NOT registered with a
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Honest Rutstein with optional runtime wiring. When
    /// <paramref name="triggers"/> is supplied the ETB ability is registered
    /// for bus-driven firing; when <paramref name="zoneService"/> is supplied
    /// the Graveyard → Hand move routes through
    /// <see cref="ZoneService.MoveCard"/> so any downstream zone-change
    /// triggers fire (CR 603.6a / CR 701.20).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Human + Warlock, {1}{B}{G}, 3/2). No abilities in the
        // JSON — the ETB trigger + cost reducer are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When Honest Rutstein enters, return target creature card from
        //    your graveyard to your hand."
        //
        // Bespoke 1..1 TargetRequest over the CREATURE cards in the
        // controller's graveyard (CR 109.2 — "creature card" = printed type).
        // Production callers refresh LegalCandidates / ChosenTargets at
        // resolve time via the agent prompt (same posture as Eternal Witness).
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: return target creature card from your graveyard to your hand",
            () => ResolveReturnCreatureToHand(card, owner, etb, zoneService));

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: owner.Zones.Graveyard.GetCards()
                        .Where(c => c.HasType(CardType.Creature))
                        .Cast<object>().ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // CR 117.7 — "Creature spells you cast cost {1} less to cast."
        // Predicate gates on the spell's Creature type; reduction is a flat
        // 1 generic. The "you cast" scope is enforced by
        // CostReduction.GetEffectiveCost, which scans only the caster's
        // battlefield for this rider.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasType(CardType.Creature),
            reduction: (_, _) => 1,
            description: "Creature spells you cast cost {1} less to cast."));

        return card;
    }

    /// <summary>
    /// Shared resolution helper for the ETB return. Reads the trigger's
    /// <see cref="TriggeredAbility.ChosenTargets"/>; falls back to the first
    /// CREATURE card in the controller's graveyard when no target was set
    /// (deterministic single-arg dispatcher posture — mirrors
    /// <see cref="EternalWitnessFactory"/>). Re-validates the chosen card is
    /// STILL a creature card in the controller's graveyard at resolution
    /// (CR 608.2b — illegal target → clean no-op). Moves Graveyard → Hand via
    /// <see cref="ZoneService.MoveCard"/> when supplied; otherwise direct-zone
    /// mutation.
    /// </summary>
    private static void ResolveReturnCreatureToHand(
        Creature rutstein,
        Player owner,
        TriggeredAbility? etb,
        ZoneService? zoneService)
    {
        // CR 110.2 — "your graveyard" is the controller's graveyard;
        // Rutstein's controller is the source of truth at resolve time.
        var controller = rutstein.Controller ?? owner;

        bool IsCreatureInGraveyard(ICard c) =>
            c.Zone == ZoneType.Graveyard
            && c.HasType(CardType.Creature)
            && controller.Zones.Graveyard.GetCards().Contains(c);

        ICard? picked = null;

        // 1) Honour the agent-set target if present (production path).
        if (etb != null && etb.ChosenTargets.Count > 0
            && etb.ChosenTargets[0].Count > 0
            && etb.ChosenTargets[0][0] is ICard chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first creature card in the
        // controller's graveyard (single-arg dispatcher / no-agent posture).
        picked ??= controller.Zones.Graveyard.GetCards()
            .FirstOrDefault(c => c.HasType(CardType.Creature));

        // Empty / no legal creature card → clean no-op (CR 608.2b).
        if (picked == null) return;

        // CR 608.2b illegal-on-resolution check — target must still be a
        // creature card in the controller's graveyard.
        if (!IsCreatureInGraveyard(picked)) return;

        // Move Graveyard → Hand. ZoneService path publishes a CardMovedEvent
        // so any "leaves graveyard" triggers fire (CR 603.6a / CR 701.20).
        if (zoneService != null)
        {
            zoneService.MoveCard(picked, ZoneType.Graveyard, ZoneType.Hand, controller);
        }
        else
        {
            controller.Zones.Graveyard.RemoveCard(picked);
            controller.Zones.Hand.AddCard(picked);
            picked.SetZone(ZoneType.Hand);
        }
    }
}
