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
/// Named-card factory for Tidehollow Sculler (Shards of Alara, {W}{B}).
///
/// Artifact Creature — Zombie 2/2. Oracle text (verified against Scryfall):
///   "When this creature enters, target opponent reveals their hand and
///    you choose a nonland card from it. Exile that card.
///    When this creature leaves the battlefield, return the exiled card
///    to its owner's hand."
///
/// Tidehollow Sculler is Brain Maggot's bigger brother — the same
/// "exile-on-ETB / return-on-LTB" hand-attack pair on a 2/2 Artifact
/// Creature body. The base shape (name, Creature, Zombie subtype, {W}{B},
/// 2/2, additive Artifact type) is materialised from the embedded JSON
/// definition (<c>tidehollow-sculler.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — the <c>types</c> array
/// <c>["Creature", "Artifact"]</c> stamps the Artifact card type through the
/// factory's multi-type seam (same path as
/// <see cref="BladeSplicerFactory"/> / Adaptive Automaton). The two printed
/// behaviours (ETB exile, LTB return) are layered on here because the JSON
/// <c>AbilityDefinition</c> schema doesn't express exile/return closures
/// (same posture as Brain Maggot).
///
/// ## Implemented (v1)
/// - 2/2 Artifact Creature — Zombie at {W}{B}. CR 301.1 / 302.1 multi-type
///   stamping handled by <see cref="CardDefinitionFactory"/>.
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.16 / CR 701.21):
///   <list type="bullet">
///     <item>Single 1..1 "target opponent" <see cref="TargetRequest"/>.</item>
///     <item>On resolve: the target opponent's hand is "revealed" (CR
///       701.16 — the engine's hand state is already observable to all
///       agents; the public reveal is a UI concern surfaced via the outer
///       event bus). v1 picks the first nonland card deterministically
///       (mirrors <see cref="BrainMaggotFactory"/> — caster-choice prompt
///       deferred).</item>
///     <item>The chosen card is exiled (CR 701.21) — moved Hand → Exile via
///       the card owner's (the target opponent's) zones. A reference to the
///       exiled card is captured in a per-Sculler closure shared with the
///       LTB ability.</item>
///   </list>
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires whenever
///   the Sculler moves OUT of the battlefield (any destination — dies +
///   bounce + flicker + exile, same posture as Brain Maggot). On resolve:
///   if a card was exiled and is still in exile, it is returned to its
///   owner's hand (Exile → Hand). If nothing was exiled (land-only / empty
///   hand), the LTB no-ops cleanly.
///
/// ## Deferred (v1 gaps)
/// - <b>Caster's choice prompt</b>: CR 701.16 / CR 701.21 — "you choose a
///   nonland card". v1 picks the first nonland card deterministically (same
///   posture as <see cref="BrainMaggotFactory"/> / <see cref="GriefFactory"/>).
/// - <b>Public reveal event</b>: a dedicated <c>CardRevealedEvent</c> for UI
///   fan-out is not synthesised by the factory shell path; the target's hand
///   state is already publicly inspectable when a live event bus is wired.
/// - <b>Empty / land-only hand</b>: v1 leaves the LTB return as a no-op when
///   no exile occurred, matching the printed "Exile that card" semantics
///   (no card → no return).
/// </summary>
[CardName("Tidehollow Sculler")]
public static class TidehollowScullerFactory
{
    public const string CardName = "Tidehollow Sculler";
    public const string Slug = "tidehollow-sculler";

    /// <summary>
    /// Construct Tidehollow Sculler with no runtime services. Both
    /// triggered abilities are attached to the card shape; neither is
    /// registered with a <see cref="TriggerManager"/>. Suitable for shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Tidehollow Sculler with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both ETB and LTB abilities
    /// are registered so the bus drives them via <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Zombie subtype, {W}{B}, 2/2, additive Artifact type). The JSON
        // carries no abilities — ETB exile + LTB return are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Shared closure: ETB writes (the exiled card + its owner), LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.16 / CR 701.21.
        //   "When this creature enters, target opponent reveals their hand
        //    and you choose a nonland card from it. Exile that card."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: target opponent reveals hand; exile a nonland card",
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
                // transition. The engine's hand state is already observable;
                // the outer event bus / UI surfaces the public reveal.

                // v1 deterministic pick — first nonland card in the target's
                // hand. Agent-driven caster-choice deferred (same posture as
                // Brain Maggot / GriefFactory).
                var pick = targetOpponent.Zones.Hand.GetCards()
                    .FirstOrDefault(c => !c.HasType(CardType.Land));

                if (pick == null) return; // empty / land-only hand → no exile.

                // CR 701.21 — exile from hand. Routed through the target's
                // own zones (the card's owner is the target opponent).
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
        //   "When this creature leaves the battlefield, return the exiled
        //    card to its owner's hand."
        // Fires whenever the Sculler moves OUT of the battlefield (any
        // destination — dies + bounce + flicker + exile, same posture as
        // Brain Maggot / Skyclave Apparition).
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
            // on the battlefield (same "looks back" semantics as Brain
            // Maggot / Skyclave Apparition).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return card;
    }
}
