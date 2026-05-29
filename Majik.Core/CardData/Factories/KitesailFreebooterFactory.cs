using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kitesail Freebooter (Ixalan, {1}{B}).
///
/// Creature — Human Pirate 1/2. Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, target opponent reveals their hand. You
///    choose a noncreature, nonland card from it. Exile that card until
///    this creature leaves the battlefield."
///
/// Kitesail Freebooter is the evasive hand-attack body in the
/// Tidehollow Sculler / Brain Maggot / Mesmeric Fiend family —
/// "exile-on-ETB / return-on-LTB" — narrowed to a <i>noncreature,
/// nonland</i> target (so it grabs removal / counters / planeswalkers
/// rather than the opponent's threats) and given Flying.
///
/// The base shape (name, Creature, Human + Pirate subtypes, {1}{B}, 1/2)
/// is materialised from the embedded JSON definition
/// (<c>kitesail-freebooter.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers or the
/// exile-until-leaves triggered abilities, so Flying + both triggers are
/// layered on here (same posture as <see cref="TwinSilkSpiderFactory"/>
/// for the JSON+keyword split, and <see cref="BrainMaggotFactory"/> for
/// the two-trigger exile/return mechanic).
///
/// ## Implemented (v1)
/// - 1/2 <see cref="Creature"/> — Human Pirate at {1}{B}.
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker (same shape as <see cref="SpellstutterSpriteFactory"/>'s
///   Flying) — combat blocking restriction.
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.16 / CR 701.21):
///   single 1..1 "target opponent" <see cref="TargetRequest"/>. On
///   resolve, the target opponent's hand is "revealed" (CR 701.16 — the
///   engine's hand state is already observable; the public reveal is a UI
///   concern surfaced via the outer event bus) and the first
///   <i>noncreature, nonland</i> card is exiled (CR 701.21). v1 picks the
///   first matching card deterministically (mirrors
///   <see cref="BrainMaggotFactory"/> — caster-choice prompt deferred).
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires whenever
///   Kitesail Freebooter moves OUT of the battlefield (any destination —
///   dies + bounce + flicker + exile). On resolve, if a card was exiled
///   and is still in exile, it returns to its owner's hand.
///
/// ## Deferred (v1 gaps — same posture as <see cref="BrainMaggotFactory"/>)
/// - <b>Caster's-choice prompt</b>: CR 701.16 — "you choose a noncreature,
///   nonland card". v1 picks the first matching card deterministically.
/// - <b>Public reveal event</b>: no dedicated <c>CardRevealedEvent</c> is
///   synthesised by the shell path; the target's hand state is already
///   publicly inspectable when a live event bus is wired.
/// - <b>Empty / no-matching-card hand</b>: v1 leaves the LTB return as a
///   no-op when no exile occurred (no card → no return).
/// </summary>
[CardName("Kitesail Freebooter")]
public static class KitesailFreebooterFactory
{
    public const string CardName = "Kitesail Freebooter";
    public const string Slug = "kitesail-freebooter";
    private const string FlyingKeyword = "Flying";

    /// <summary>
    /// Construct Kitesail Freebooter with no runtime services. Flying plus
    /// both triggered abilities are attached to the card shape; neither
    /// trigger is registered with a <see cref="TriggerManager"/>. Suitable
    /// for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct Kitesail Freebooter with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both ETB and LTB abilities
    /// are registered so the bus drives them via
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human + Pirate subtypes, {1}{B}, 1/2). The JSON carries no
        // abilities — Flying + the exile/return triggers are layered on.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. Keyword marker only; combat blocking
        // restriction (same shape as Spellstutter Sprite's Flying).
        card.AddAbility(new KeywordAbility(FlyingKeyword, card, owner));

        // Shared closure: ETB writes (the exiled card + its owner),
        // LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.16 / CR 701.21.
        //   "When this creature enters, target opponent reveals their
        //    hand. You choose a noncreature, nonland card from it. Exile
        //    that card until this creature leaves the battlefield."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: target opponent reveals hand; exile a noncreature, nonland card until this leaves",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Player targetOpponent) return;

                // CR 109.5 — "target opponent" must be a player other than
                // the source's controller at resolution time.
                if (ReferenceEquals(targetOpponent, card.Controller ?? owner)) return;

                // CR 701.16 — "reveals their hand" is a public state
                // transition. The engine's hand state is already
                // observable; the outer event bus / UI surfaces the public
                // reveal separately.

                // v1 deterministic pick — first noncreature, nonland card
                // in the target's hand. Agent-driven caster-choice deferred
                // (same posture as Brain Maggot). CR 701.21 — the printed
                // filter excludes both creatures and lands.
                var pick = targetOpponent.Zones.Hand.GetCards()
                    .FirstOrDefault(c => !c.HasType(CardType.Creature)
                                         && !c.HasType(CardType.Land));

                if (pick == null) return; // no matching card → no exile.

                // CR 701.21 — exile from hand. Routed through the target's
                // own zones (the card's owner is the target).
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
        //   "Exile that card until this creature leaves the battlefield."
        // Fires whenever Kitesail Freebooter moves OUT of the battlefield
        // (any destination — dies + bounce + flicker + exile).
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{CardName}: return the exiled card to its owner's hand",
            () =>
            {
                if (exiled == null || exiledOwner == null) return;
                // CR 400.7 — if the exiled card has since left exile, skip.
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
            // CR 603.6d — LTB triggers see the permanent as it last existed
            // on the battlefield ("looks back" semantics).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return card;
    }
}
