using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tinybones Joins Up (Final Fantasy Commander, {B}).
///
/// Legendary Enchantment. Oracle text (verified against Scryfall 2026-06-24):
///   "When Tinybones Joins Up enters, any number of target players each
///    discard a card.
///    Whenever a legendary creature you control enters, any number of target
///    players each mill a card and lose 1 life."
///
/// The base shape (name, single Enchantment card type, Legendary supertype,
/// {B}) is materialised from the embedded JSON definition
/// (<c>tinybones-joins-up.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="WeddingAnnouncementFactory"/> / <see cref="BeastWhispererFactory"/>.
/// Both printed triggered abilities are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither an ETB target-player
/// discard nor a legendary-creature-enters mill/lose-life trigger.
///
/// ## Implemented (v1)
/// - Card identity: Legendary Enchantment, mana cost {B}, black, owner /
///   controller wiring.
/// - <b>ETB trigger</b> (CR 603.1, fires off the enchantment's own
///   <see cref="CardMovedEvent"/> to the battlefield —
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>): "any number of target
///   players each discard a card." A 0..many "any number of target players"
///   <see cref="TargetRequest"/> (<c>MinTargets: 0, MaxTargets: int.MaxValue</c>
///   — same variable-count shape as <see cref="CauldronOfSoulsFactory"/>'s
///   "any number of target creatures") is attached; the controller's agent
///   populates <see cref="TriggeredAbility.ChosenTargets"/> before resolution.
///   On resolution each chosen player still in the game discards one card of
///   their own choice (CR 701.8 — routed through <see cref="Fx.Discard"/>,
///   v1 deterministic first-card pick; empty hand is a clean no-op,
///   CR 701.8d "can't discard what you don't have").
/// - <b>Legendary-creature-enters trigger</b> (CR 603.1, fires off a
///   <see cref="CardMovedEvent"/> to the battlefield filtered to a creature
///   that is Legendary AND controlled by this card's controller): "any number
///   of target players each mill a card and lose 1 life." Same 0..many
///   target-player request shape. On resolution each chosen player still in
///   the game mills one card (CR 701.13 — <see cref="Fx.Mill"/>) and loses 1
///   life (CR 119.3 — <see cref="Fx.LoseLife"/>).
///
/// ## Self-trigger note
/// Tinybones Joins Up is an Enchantment, not a creature, so it can never
/// satisfy its own "legendary creature you control enters" trigger. The
/// legendary-creature trigger is only active while Tinybones Joins Up is on
/// the battlefield (CR 603.6a; <c>activeZones = {Battlefield}</c>), so a
/// legendary creature entering before Tinybones Joins Up does not retro-fire.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both triggers are attached for
///   inspection; neither is registered (no live trigger manager). This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully wired.
///   Both triggers are registered with <paramref name="triggers"/> so a
///   matching <see cref="CardMovedEvent"/> queues the ability on the stack.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven discard pick</b>: each target player's discard uses the
///   deterministic first-card-in-hand pick inside <see cref="Fx.Discard"/>
///   (same gap as Mind Rot's no-agent fallback / Liliana of the Veil +1).
/// </summary>
[CardName("Tinybones Joins Up")]
public static class TinybonesJoinsUpFactory
{
    public const string CardName = "Tinybones Joins Up";
    public const string Slug = "tinybones-joins-up";
    public const string PrintedManaCost = "{B}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Tinybones Joins Up with no live wiring. Both triggered
    /// abilities are attached to the card shape for inspection but not
    /// registered with a <see cref="TriggerManager"/>. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Tinybones Joins Up with optional runtime services. When
    /// <paramref name="triggers"/> is supplied both triggered abilities are
    /// registered so a matching <see cref="CardMovedEvent"/> queues the
    /// ability on the stack.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Legendary Enchantment, {B}) from the embedded JSON
        // def. The JSON carries no abilities — both triggers are layered below.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.1.
        //   "When Tinybones Joins Up enters, any number of target players
        //    each discard a card."
        // 0..many "any number of target players" request (CR 601.2c). The
        // controller's agent supplies the chosen players before resolution;
        // each still in the game discards one card (CR 701.8).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{CardName}: each chosen target player discards a card",
            () =>
            {
                if (etbTrigger == null || etbTrigger.ChosenTargets.Count == 0) return;
                foreach (var raw in etbTrigger.ChosenTargets[0])
                {
                    if (raw is not Player victim) continue; // CR 608.2b illegal-target filter
                    Fx.Discard(victim, 1);                  // CR 701.8 (empty hand → no-op)
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // CR 603.6d — an ETB trigger looks back in time; the trigger is
            // evaluated for the zone-change that just happened, so it is not
            // gated by activeZones on the post-move (battlefield) state.
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any number of target players",
                    MinTargets: 0,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Legendary-creature-enters trigger — CR 603.1.
        //   "Whenever a legendary creature you control enters, any number of
        //    target players each mill a card and lose 1 life."
        // Matches any CardMovedEvent to the battlefield whose card is a
        // Legendary creature controlled by this card's controller. Only active
        // while Tinybones Joins Up is on the battlefield (CR 603.6a) — so it
        // never retro-fires for legendary creatures that entered earlier, and
        // (being an enchantment) Tinybones Joins Up can never satisfy it itself.
        // ----------------------------------------------------------------
        TriggeredAbility? legendTrigger = null;
        var legendEffect = new Effect(
            $"{CardName}: each chosen target player mills a card and loses 1 life",
            () =>
            {
                if (legendTrigger == null || legendTrigger.ChosenTargets.Count == 0) return;
                foreach (var raw in legendTrigger.ChosenTargets[0])
                {
                    if (raw is not Player victim) continue; // CR 608.2b illegal-target filter
                    Fx.Mill(victim, 1);     // CR 701.13
                    Fx.LoseLife(victim, 1); // CR 119.3
                }
            });

        legendTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.ToZone == ZoneType.Battlefield
                && e.Card.HasType(CardType.Creature)
                && e.Card.HasSupertype(CardSupertype.Legendary)
                && ReferenceEquals(e.Card.Controller, card.Controller ?? owner)),
            effects: new IEffect[] { legendEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any number of target players",
                    MinTargets: 0,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(legendTrigger);
        triggers?.RegisterTriggeredAbility(legendTrigger);

        return card;
    }
}
