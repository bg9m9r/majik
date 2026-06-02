using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hired Claw (Outlaws of Thunder Junction, {R}).
///
/// Creature — Lizard Mercenary 1/1. Oracle text (verified against Scryfall):
///   "Whenever you attack with one or more Lizards, this creature deals
///    1 damage to target opponent.
///    {1}{R}: Put a +1/+1 counter on this creature. Activate only if an
///    opponent lost life this turn and only once each turn."
///
/// The base shape (name, Creature, Lizard + Mercenary subtypes, {R}, 1/1)
/// is materialised from the embedded JSON definition (<c>hired-claw.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The attack trigger and the
/// gated +1/+1-counter activated ability are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express either shape (same
/// posture as <see cref="EmberheartChallengerFactory"/>).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Lizard Mercenary at printed cost {R}, owner/controller
///   wired.
/// - <b>Attack-with-Lizards trigger (CR 603.1 / CR 508.1f)</b>: an
///   <see cref="EventTriggerCondition{TEvent}"/> over
///   <see cref="AttackersDeclaredEvent"/> that fires once per declare-attackers
///   step when the controller is the attacking player AND at least one declared
///   attacker is a Lizard (CR 700.2 — "one or more" satisfies on a count of
///   one or more; Hired Claw itself is a Lizard, so attacking with it alone
///   counts). On resolution it deals 1 damage to a target opponent
///   (CR 119.3) via <see cref="Fx.DealDamage"/>. Same attack-trigger + 1..1
///   "target opponent" shape as <see cref="SoaringThoughtThiefFactory"/>;
///   same damage-to-opponent primitive as
///   <see cref="ElectrostaticFieldFactory"/>.
/// - <b>{1}{R}: Put a +1/+1 counter on this creature (CR 602.1 / 121.1)</b>:
///   a standard <see cref="ActivatedAbility"/> with a single
///   <see cref="ManaCostCost"/> and a resolve body that routes through
///   <see cref="CountersService.Add"/> (so Hardened Scales / Doubling Season
///   replacements and the post-commit <see cref="CounterAddedEvent"/> apply).
///   * <b>"Activate only if an opponent lost life this turn" (CR 602.5c)</b>
///     — modelled as the ability's <c>canActivateCheck</c> gate: true iff some
///     opponent's <see cref="Player.LifeLostThisTurn"/> is &gt; 0. The opponent
///     set is read live through <paramref name="opponentResolver"/>.
///   * <b>"only once each turn" (CR 602.5e)</b> — an <c>int[1]{0}</c> per-turn
///     lock folded into the same <c>canActivateCheck</c> gate, flipped to 1 by
///     the resolve body and reset to 0 by a <see cref="TurnStartedEvent"/>
///     handler (CR 500.1). Same lock shape as
///     <see cref="WirewoodSymbioteFactory"/>, but folded into the activation
///     gate rather than a cost because the cost here is plain mana.
///
/// ## Deferred (v1 gaps)
/// - <b>Live "target opponent" / opponent enumeration</b>: no
///   <c>Player.Opponents</c> accessor exists at v1, so both the attack
///   trigger's damage half and the activation's "an opponent lost life"
///   gate read the opponent set through <paramref name="opponentResolver"/>.
///   Without a resolver the damage half no-ops (the trigger still fires and is
///   observable as pending) and the activation gate treats "an opponent lost
///   life" as false (the ability simply can't be activated). Same
///   resolver-injection posture as <see cref="ElectrostaticFieldFactory"/> /
///   <see cref="SoaringThoughtThiefFactory"/>.
/// - <b>Trigger-on-stack targeting</b>: the attack trigger's target opponent
///   is honoured from <see cref="TriggeredAbility.ChosenTargets"/> when the
///   trigger was dispatched with one, else the first opponent in the resolver
///   list (same fallback as <see cref="SoaringThoughtThiefFactory"/>).
/// </summary>
[CardName("Hired Claw")]
public static class HiredClawFactory
{
    public const string CardName = "Hired Claw";
    public const string Slug = "hired-claw";
    public const int DamageAmount = 1;

    /// <summary>CR 121.1 — counters added per activation.</summary>
    public const int CounterAmount = 1;

    /// <summary>
    /// Construct Hired Claw with no live runtime services. Both abilities are
    /// attached to the card shape for dispatcher / structural tests; the attack
    /// trigger's damage half no-ops (no opponent resolver), the +1/+1 ability's
    /// "an opponent lost life this turn" gate evaluates false (no opponent
    /// resolver), and the once-per-turn lock is never reset (no event bus).
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null, opponentResolver: null);

    /// <summary>
    /// Construct Hired Claw with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, a <see cref="TurnStartedEvent"/>
    /// handler resets the once-per-turn activation lock (CR 500.1), and the
    /// +1/+1 counter placement publishes <see cref="CounterAddedEvent"/>.</param>
    /// <param name="triggers">TriggerManager the attack trigger registers with
    /// so it surfaces as pending. May be null — the trigger is still attached
    /// to the card shape.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> routed
    /// through <see cref="CountersService.Add"/> for the +1/+1 placement
    /// (Hardened Scales / Doubling Season — CR 614).</param>
    /// <param name="opponentResolver">Live enumerator of the player list. Used
    /// to pick the attack trigger's target opponent AND to evaluate the
    /// activation's "an opponent lost life this turn" gate. Without a resolver
    /// the damage half no-ops and the activation gate is false.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Lizard + Mercenary subtypes, {R}, 1/1). The JSON carries no
        // abilities — both are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        BuildAttackTrigger(card, owner, triggers, opponentResolver);
        BuildCounterAbility(card, owner, eventBus, replacements, opponentResolver);

        return card;
    }

    // --- Attack-with-Lizards trigger (CR 508.1f / 119.3) -------------------

    private static void BuildAttackTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        // CR 603.1 / CR 508.1f — "Whenever you attack with one or more Lizards,
        // this creature deals 1 damage to target opponent." Fires on
        // AttackersDeclaredEvent where the attacking player is this card's
        // controller AND at least one declared attacker is a Lizard.
        TriggeredAbility? attackTrigger = null;
        var damageEffect = new Effect(
            $"{CardName}: deal {DamageAmount} damage to target opponent (whenever you attack with one or more Lizards)",
            () =>
            {
                // CR 119.3 — damage to a player reduces their life total;
                // Fx.DealDamage routes Player → Player.LoseLife (CR 119.8).
                // Without a resolver the player aggregate exposes no opponents
                // list at v1, so the damage half no-ops.
                var opponent = ResolveTargetOpponent(attackTrigger, card, owner, opponentResolver);
                if (opponent == null) return;
                Fx.DealDamage(opponent, DamageAmount);
            });

        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<AttackersDeclaredEvent>(
                (e, _) => IsAttackWithLizardsMatch(e, card, owner)),
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);
    }

    private static bool IsAttackWithLizardsMatch(AttackersDeclaredEvent e, Creature card, Player owner)
    {
        var controller = card.Controller ?? owner;
        if (!ReferenceEquals(e.Combat.AttackingPlayer, controller)) return false;
        // CR 700.2 — "one or more Lizards" satisfies on a count of ≥ 1.
        foreach (var atk in e.Combat.Attackers)
        {
            if (atk?.Creature == null) continue;
            if (atk.Creature.HasSubtype(CardSubtype.Lizard)) return true;
        }
        return false;
    }

    private static Player? ResolveTargetOpponent(
        TriggeredAbility? attackTrigger,
        Creature card,
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        var controller = card.Controller ?? owner;

        // CR 115 — honour an explicit target if the trigger was dispatched with
        // one (ChosenTargets[0][0] is the agent / resolver-picked opponent).
        if (attackTrigger != null
            && attackTrigger.ChosenTargets.Count > 0
            && attackTrigger.ChosenTargets[0].Count > 0
            && attackTrigger.ChosenTargets[0][0] is Player chosenPlayer
            && !ReferenceEquals(chosenPlayer, controller))
        {
            return chosenPlayer;
        }

        // v1 fallback — first opponent in the resolver list.
        var players = opponentResolver?.Invoke();
        if (players == null) return null;
        foreach (var p in players)
        {
            if (ReferenceEquals(p, controller)) continue;
            return p;
        }
        return null;
    }

    // --- {1}{R}: +1/+1 counter (CR 602.1 / 121.1 / 602.5c / 602.5e) --------

    private static void BuildCounterAbility(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        ReplacementBus? replacements,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        // CR 602.5e — "Activate only ... once each turn." Closure shared
        // between the activation gate and the TurnStartedEvent reset handler.
        var usedThisTurn = new int[] { 0 };

        // CR 602.5c — "Activate only if an opponent lost life this turn, and
        // only once each turn." Both riders fold into a single canActivateCheck
        // gate evaluated against live state on every consult.
        bool CanActivate()
        {
            if (usedThisTurn[0] != 0) return false; // once-per-turn lock closed.
            return OpponentLostLifeThisTurn(card, owner, opponentResolver);
        }

        // CR 121.1 — "Put a +1/+1 counter on this creature." Routes through
        // CountersService.Add so replacements (Hardened Scales / Doubling
        // Season — CR 614) rewrite the count and the post-commit
        // CounterAddedEvent publishes. The resolve body also flips the
        // once-per-turn lock (CR 602.5e).
        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on this creature",
            () =>
            {
                CountersService.Add(
                    card,
                    CounterType.PlusOnePlusOne,
                    CounterAmount,
                    replacements,
                    eventBus);

                // CR 602.5e — record this turn's single permitted activation.
                usedThisTurn[0] = 1;
            });

        var counterAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{R}") },
            effects: new IEffect[] { counterEffect },
            canActivateCheck: CanActivate);

        card.AddAbility(counterAbility);

        // CR 500.1 — reset the per-turn activation lock at the start of each
        // turn. Without an event bus the lock stays set after the first
        // activation (acceptable for shape / single-turn tests).
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => usedThisTurn[0] = 0);
        }
    }

    /// <summary>
    /// CR 602.5c — true iff at least one opponent of the source's controller
    /// has lost life this turn (<see cref="Player.LifeLostThisTurn"/> &gt; 0).
    /// Opponents are read live through <paramref name="opponentResolver"/>;
    /// without a resolver this is false (the ability can't be activated).
    /// </summary>
    private static bool OpponentLostLifeThisTurn(
        Creature card,
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        var players = opponentResolver?.Invoke();
        if (players == null) return false;

        var controller = card.Controller ?? owner;
        foreach (var p in players)
        {
            if (ReferenceEquals(p, controller)) continue;
            if (p.LifeLostThisTurn > 0) return true;
        }
        return false;
    }
}
