using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brain Maggot (Journey into Nyx, {1}{B}).
///
/// Enchantment Creature — Insect 1/1. Oracle text:
///   "When this creature enters, target opponent reveals their hand and
///    you choose a nonland card from it. Exile that card until this
///    creature leaves the battlefield."
///
/// Brain Maggot is the Theros / Modern budget hand-attack creature —
/// half a Tidehollow Sculler stapled to a 1/1 body. Shares the
/// "exile-on-ETB / return-on-LTB" pattern with Sculler, Brain Maggot's
/// big brother Mesmeric Fiend, and Skyclave Apparition's
/// exile-and-spawn-token variant.
///
/// ## Implemented (v1)
/// - 1/1 Enchantment Creature — Insect at {1}{B}. CR 301.1 / 302.1
///   multi-type stamping: the base <see cref="Creature"/> registers
///   <see cref="CardType.Creature"/>; the factory additively stamps
///   <see cref="CardType.Enchantment"/> via
///   <see cref="Card.AddCardType"/> (same shape used by
///   <see cref="HeliodSunCrownedFactory"/>).
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21):
///   <list type="bullet">
///     <item>Single 1..1 "target opponent" <see cref="TargetRequest"/>.
///       The candidate gatherer enumerates every player other than
///       Brain Maggot's controller (CR 109.5 / CR 608.2b — opponents
///       only).</item>
///     <item>On resolve: the target opponent's hand is "revealed" (CR
///       701.16 — the engine's hand state is already observable to all
///       agents; the public reveal is a UI concern surfaced via the
///       outer event bus). v1 picks the first nonland card in that
///       hand deterministically (mirrors
///       <see cref="GriefFactory"/>'s discard pick — caster-choice
///       prompt deferred).</item>
///     <item>The chosen card is exiled (CR 701.21) — moved Hand →
///       Exile via raw zone manipulation routed through the card's
///       owner (the target opponent). A reference to the exiled card
///       is captured in a per-Maggot closure shared with the LTB
///       ability so the return half can read it.</item>
///   </list>
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires
///   whenever Brain Maggot moves OUT of the battlefield (any
///   destination — covers dies + bounce + flicker + exile, same as
///   <see cref="SpellQuellerFactory"/> / Skyclave Apparition). On
///   resolve: if a card was exiled and is still in exile, it is
///   returned to its owner's hand (Exile → Hand via raw zone moves).
///   If no card was exiled (e.g. the target had only lands in hand,
///   or the target's hand was empty), the LTB no-ops cleanly.
///
/// ## Deferred (v1 gaps)
/// - <b>Caster's choice prompt</b>: CR 701.16 / CR 701.21 — "you
///   choose a nonland card". v1 picks the first nonland card
///   deterministically (same posture as
///   <see cref="GriefFactory"/>). An agent-driven prompt for the
///   caster to pick any nonland card from the revealed hand is
///   deferred.
/// - <b>Public reveal event</b>: a dedicated <c>CardRevealedEvent</c>
///   for UI fan-out is not synthesised by the factory shell path; the
///   target's hand state is already publicly inspectable when a live
///   event bus is wired at the game level.
/// - <b>Empty / land-only hand</b>: v1 leaves the LTB return as a
///   no-op when no exile occurred, matching the printed "Exile that
///   card" semantics (no card → no return).
/// </summary>
[CardName("Brain Maggot")]
public static class BrainMaggotFactory
{
    public const string CardName = "Brain Maggot";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Brain Maggot with no runtime services. Both triggered
    /// abilities are attached to the card shape; neither is registered
    /// with a <see cref="TriggerManager"/>. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Brain Maggot with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both ETB and LTB
    /// abilities are registered so the bus drives them via
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Insect });

        // CR 301.1 / 302.1 — Brain Maggot is an Enchantment Creature.
        // The base Creature ctor only registers CardType.Creature;
        // additively flag the Enchantment type for HasType-based
        // lookups (mirrors Heliod, Sun-Crowned's multi-type shape).
        card.AddCardType(CardType.Enchantment);

        card.SetOwner(owner);
        card.SetController(owner);

        // Shared closure: ETB writes (the exiled card + its owner),
        // LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.16 / CR 701.21.
        //   "When this creature enters, target opponent reveals their
        //    hand and you choose a nonland card from it. Exile that
        //    card until this creature leaves the battlefield."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: target opponent reveals hand; exile a nonland card until this leaves",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Player targetOpponent) return;

                // CR 109.5 — "target opponent" must be a player other
                // than the source's controller at resolution time.
                if (ReferenceEquals(targetOpponent, card.Controller ?? owner)) return;

                // CR 701.16 — "reveals their hand" is a public state
                // transition. The engine's hand state is already
                // observable; the outer event bus / UI surfaces the
                // public reveal separately.

                // v1 deterministic pick — first nonland card in the
                // target's hand. Agent-driven caster-choice deferred
                // (same posture as GriefFactory).
                var pick = targetOpponent.Zones.Hand.GetCards()
                    .FirstOrDefault(c => !c.HasType(CardType.Land));

                if (pick == null) return; // empty / land-only hand → no exile.

                // CR 701.21 — exile from hand. Routed through the
                // target's own zones (the card's owner is the target).
                targetOpponent.Zones.Hand.RemoveCard(pick);
                targetOpponent.Zones.Exile.AddCard(pick);
                pick.SetZone(ZoneType.Exile);

                exiled = pick;
                exiledOwner = targetOpponent;
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "Exile that card until this creature leaves the
        //    battlefield."
        // Fires whenever Brain Maggot moves OUT of the battlefield
        // (any destination — dies + bounce + flicker + exile, same
        // posture as Skyclave Apparition).
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{CardName}: return the exiled card to its owner's hand",
            () =>
            {
                if (exiled == null || exiledOwner == null) return;
                // CR 400.7 — if the exiled card has since left exile
                // (extraction, processed by Eldrazi, etc.), skip.
                if (exiled.Zone != ZoneType.Exile) return;

                exiledOwner.Zones.Exile.RemoveCard(exiled);
                exiledOwner.Zones.Hand.AddCard(exiled);
                exiled.SetZone(ZoneType.Hand);
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last
            // existed on the battlefield (same "looks back" semantics
            // used by Spell Queller, Skyclave Apparition).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return card;
    }
}
