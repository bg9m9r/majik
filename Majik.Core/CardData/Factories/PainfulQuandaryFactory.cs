using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Painful Quandary (Scars of Mirrodin, {3}{B}{B}).
///
/// Enchantment. Oracle text (verified against Scryfall 2026-06-24):
///   "Whenever an opponent casts a spell, that player loses 5 life unless
///    they discard a card."
///
/// ## Shape source
/// The base shape (name, Enchantment, {3}{B}{B}) is materialised from the
/// embedded JSON definition (<c>painful-quandary.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="SmallpoxFactory"/>. The opponent-cast triggered ability is
/// layered on here (the JSON ability schema does not express an
/// opponent-scoped spell-cast trigger with a per-player "unless you discard"
/// choice).
///
/// ## Implemented (v1)
/// - <b>Enchantment</b> shape, mana cost {3}{B}{B}, black (from JSON).
/// - <b>Opponent-cast trigger (CR 603.1)</b>: an
///   <see cref="EventTriggerCondition{TEvent}"/> over
///   <see cref="SpellCastEvent"/> whose spell's controller is NOT Painful
///   Quandary's controller (CR 109.5 — "an opponent" reads against the
///   trigger's controller). Unlike <see cref="KambalConsulOfAllocationFactory"/>
///   (which gates on a NONcreature spell), Painful Quandary fires on ANY spell
///   the opponent casts — no type gate. The casting opponent's identity is
///   boxed in a single-element array so the resolve body routes the choice to
///   the correct player (Kambal-style closure).
/// - <b>"That player loses 5 life unless they discard a card."</b>: on
///   resolution the AFFECTED OPPONENT (not Painful Quandary's controller —
///   CR 608.2 "they/their" refers back to "that player") decides. Their own
///   agent is prompted whether to discard a card (yes/no, intent
///   <see cref="BotIntent.Discard"/>); choosing to discard moves a card of
///   their choice hand→graveyard (CR 701.8) and they lose NO life. Declining —
///   OR an empty hand (no card available to discard, so the "unless" cost can't
///   be paid) — makes that player lose 5 life (CR 119.3, via
///   <see cref="Player.LoseLife"/> so <c>LifeLostThisTurn</c> ticks). The
///   discard-or-penalty resolution mirrors
///   <see cref="SolitaryConfinementFactory.ResolveUpkeep"/>, except the chooser
///   is the affected opponent and the penalty is life loss rather than a
///   sacrifice.
///
/// ## Rules citations
/// - CR 603.1 — triggered ability over SpellCastEvent.
/// - CR 109.5 — "an opponent" reads against the trigger's controller.
/// - CR 701.8 — "discard a card."
/// - CR 119.3 — "loses 5 life" (life loss, not damage; no prevention / lifelink).
/// - CR 608.2 — "unless" is a choice for the affected player at resolution; an
///   empty hand cannot pay the discard cost, so the life loss applies.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The trigger is attached for
///   shape inspection but not registered with a <see cref="TriggerManager"/>.
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, IEventBus?, Func{Player, IPlayerAgent?}?)"/>
///   — fully wired (bus-driven firing + per-player agent override for tests).
/// </summary>
[CardName("Painful Quandary")]
public static class PainfulQuandaryFactory
{
    public const string CardName = "Painful Quandary";
    public const string Slug = "painful-quandary";

    /// <summary>CR 119.3 — the "unless they discard" penalty.</summary>
    public const int LifeLoss = 5;

    /// <summary>
    /// Construct Painful Quandary with no live <see cref="TriggerManager"/>
    /// wiring. The trigger is attached to the card shape so dispatcher tests
    /// see it; pass the wired overload to register it for live
    /// <see cref="SpellCastEvent"/> dispatch. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null, agentSelector: null);

    /// <summary>
    /// Construct Painful Quandary with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the opponent-cast trigger is
    /// registered for bus-driven firing. <paramref name="eventBus"/> backs the
    /// discard move's zone-change publication. <paramref name="agentSelector"/>
    /// overrides the affected opponent's agent for deterministic tests; null
    /// reads each affected player's live agent from
    /// <see cref="AgentRegistry"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Enchantment,
        // {3}{B}{B}). The JSON carries no abilities — the trigger is layered on.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Enchantment)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Whenever an opponent casts a spell, that player loses 5 life
        //  unless they discard a card." CR 603.1.
        // Predicate gates ONLY on caster != Painful Quandary's controller
        // (CR 109.5) — ANY spell qualifies (no type gate, unlike Kambal).
        // The opponent's identity is captured in a single-element array so
        // the resolve body routes the choice to the correct player.
        // ----------------------------------------------------------------
        var pendingCaster = new Player?[] { null };

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var caster = e.Spell.Controller;
            if (caster is null) return false;

            // CR 109.5 — "an opponent" reads against the trigger's controller.
            // Painful Quandary's own casts (the controller's) never fire it.
            if (ReferenceEquals(caster, card.Controller ?? owner)) return false;

            pendingCaster[0] = caster;
            return true;
        });

        var quandaryEffect = new Effect(
            $"{CardName}: that player loses {LifeLoss} life unless they discard a card",
            () =>
            {
                var caster = pendingCaster[0];
                pendingCaster[0] = null;
                if (caster is null) return;
                Resolve(caster, eventBus, agentSelector);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { quandaryEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 608.2 — resolve the "loses 5 life unless they discard a card" choice
    /// for the affected opponent <paramref name="caster"/>. The affected player
    /// (not Painful Quandary's controller — "they/their" refers to "that
    /// player") chooses via THEIR own agent whether to discard. An empty hand
    /// cannot pay the discard cost, so the life loss applies. Exposed for tests
    /// / bots.
    /// </summary>
    /// <param name="agentSelector">Optional per-player agent selector. Null
    /// reads the affected player's live agent from <see cref="AgentRegistry"/>.
    /// The agent is the AFFECTED OPPONENT's agent — they make the choice.</param>
    public static void Resolve(
        Player caster,
        IEventBus? eventBus = null,
        Func<Player, IPlayerAgent?>? agentSelector = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var hand = caster.Zones.Hand.GetCards().ToList();

        // CR 608.2 — with no card in hand the "unless they discard" cost can't
        // be paid, so the player loses 5 life.
        if (hand.Count == 0)
        {
            caster.LoseLife(LifeLoss);
            return;
        }

        var agent = agentSelector?.Invoke(caster) ?? AgentRegistry.Get(caster);

        // With no agent the default is to discard (pay the cheaper cost — keep
        // the 5 life), discarding the first hand card. A live agent decides.
        bool willDiscard = true;
        ICard pick = hand[0];

        if (agent != null)
        {
            willDiscard = agent
                .ChooseYesNoAsync(
                    $"{CardName}: discard a card? (otherwise you lose {LifeLoss} life)",
                    BotIntent.Discard)
                .GetAwaiter().GetResult();

            if (willDiscard)
            {
                var chosen = agent
                    .ChooseFromHandAsync(caster, hand.Cast<ICard>().ToList(), BotIntent.Discard)
                    .GetAwaiter().GetResult();
                if (chosen != null && chosen.Zone == ZoneType.Hand) pick = chosen;
            }
        }

        if (!willDiscard)
        {
            // CR 119.3 — life loss, not damage. Routes through Player.LoseLife
            // so LifeLostThisTurn ticks (no prevention / lifelink engage).
            caster.LoseLife(LifeLoss);
            return;
        }

        // CR 701.8 — discard the chosen card (hand → graveyard); no life lost.
        caster.Zones.Hand.RemoveCard(pick);
        caster.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }
}
