using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glorybringer (Amonkhet, {3}{R}{R}). Creature —
/// Dragon 4/4. Oracle text (verified against Scryfall):
///   "Flying, haste
///    You may exert this creature as it attacks. When you do, it deals 4
///    damage to target non-Dragon creature an opponent controls. (An
///    exerted creature won't untap during your next untap step.)"
///
/// The base shape (name, Creature, Dragon, {3}{R}{R}, 4/4) is materialised
/// from the embedded JSON definition (<c>glorybringer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Flying/Haste keyword
/// markers and the exert attack trigger are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express attack triggers,
/// the exert reflexive choice, or targeted damage (same posture as
/// <see cref="IntiSeneschalOfTheSunFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>4/4 Creature — Dragon at {3}{R}{R}</b>, owner/controller wired.
/// - <b>Flying (CR 702.9) + Haste (CR 702.10)</b> — keyword markers via
///   <see cref="KeywordAbility"/>, read by the combat/block subsystem the
///   same way <see cref="StormbreathDragonFactory"/> wires them.
/// - <b>Exert attack trigger (CR 702.139 + CR 508.1f + CR 603.1)</b> —
///   "You may exert this creature as it attacks. When you do, it deals 4
///   damage to target non-Dragon creature an opponent controls." Fires on
///   <see cref="AttackersDeclaredEvent"/> when Glorybringer's controller is
///   the attacking player AND Glorybringer is among the declared attackers
///   ("as it attacks", CR 508.1f / 702.139a). On resolve it asks the
///   optional "you may exert" chooser (<paramref name="mayExert"/>); when
///   the controller exerts:
///   <list type="number">
///     <item><description>CR 702.139c — the creature is registered with
///       <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/> so it
///       "won't untap during your next untap step." The rider lifts on the
///       controller's next <see cref="PhaseStateType.Untap"/> step when an
///       event bus is wired (mirrors <see cref="ArenaOfGloryFactory"/>'s exert
///       cleanup).</description></item>
///     <item><description>CR 603.1 reflexive "when you do" — Glorybringer
///       deals 4 damage (<see cref="Creature.TakeDamage"/>, CR 119.3) to a
///       target non-Dragon creature an opponent controls. The candidate pool
///       is supplied by <paramref name="opponentCreaturesResolver"/>; the
///       factory itself enforces the "non-Dragon" + "opponent controls"
///       legality gate (CR 115.4) before picking the first eligible target —
///       so a Dragon-only or own-creature pool yields no damage.</description></item>
///   </list>
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only (dispatcher / structural
///   tests). The trigger is attached for shape observability but never exerts
///   (the default <paramref name="mayExert"/> declines without a resolver).
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, Func{bool}?, Func{Combat, IReadOnlyList{Creature}}?)"/>
///   — supplies runtime services (untap-skip cleanup bus, trigger manager,
///   the exert chooser, and the opponent-creature target pool resolver).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Agent-driven exert choice + target pick</b>: the exert decision is a
///   <see cref="bool"/> chooser and the target is the first eligible creature
///   from the resolver pool, rather than a full agent-driven
///   <see cref="Majik.Core.Targeting"/> selection. Same closure-injection
///   posture as Inti, Seneschal of the Sun's "target attacking creature".
/// - <b>Exert as a first-class combat keyword</b>: the "may exert as it
///   attacks" choice + the untap-skip rider live inside this factory. No
///   shared Exert primitive is published on <see cref="Creature"/> for other
///   cards (e.g. "untapped creatures you control" payoffs) to read — same
///   posture as Arena of Glory's per-card exert.
/// </summary>
[CardName("Glorybringer")]
public static class GlorybringerFactory
{
    public const string CardName = "Glorybringer";
    public const string Slug = "glorybringer";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>CR 119.3 — damage dealt by the exert reflexive trigger.</summary>
    public const int ExertDamage = 4;

    /// <summary>
    /// Construct Glorybringer with no live wiring (the shape / dispatcher
    /// path). The exert trigger is attached for shape observability but never
    /// exerts (no chooser, no target resolver). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, mayExert: null,
            opponentCreaturesResolver: null);

    /// <summary>
    /// Construct Glorybringer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, the exert "won't untap" rider
    /// clears on the controller's next <see cref="PhaseStateType.Untap"/>
    /// step (CR 702.139c / 514.2).</param>
    /// <param name="triggers">TriggerManager the exert attack trigger is
    /// registered with so it surfaces as pending. May be null.</param>
    /// <param name="mayExert">"You may exert this creature as it attacks"
    /// chooser. Returns true to exert. May be null — defaults to declining
    /// (the safe shape-only posture; without a target resolver there is no
    /// upside to exerting).</param>
    /// <param name="opponentCreaturesResolver">Closure returning the candidate
    /// pool of opponent creatures for the reflexive damage, given the live
    /// <see cref="Combat"/>. The factory filters this pool to non-Dragon
    /// creatures an opponent controls (CR 115.4) before picking the first.
    /// May be null — no resolver means no legal target, so no damage.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<bool>? mayExert = null,
        Func<Majik.Core.Combat.Combat, IReadOnlyList<Creature>>? opponentCreaturesResolver = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Dragon, {3}{R}{R}, 4/4). No abilities in the JSON — the keyword
        // markers + exert trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 / 702.10 — Flying + Haste keyword markers.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        AddExertAttackTrigger(card, owner, eventBus, mayExert, opponentCreaturesResolver, triggers);

        return card;
    }

    // -----------------------------------------------------------------------
    // Exert attack trigger — "You may exert this creature as it attacks. When
    // you do, it deals 4 damage to target non-Dragon creature an opponent
    // controls." (CR 702.139 + CR 508.1f + CR 603.1.)
    // -----------------------------------------------------------------------
    private static void AddExertAttackTrigger(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        Func<bool>? mayExert,
        Func<Majik.Core.Combat.Combat, IReadOnlyList<Creature>>? opponentCreaturesResolver,
        TriggerManager? triggers)
    {
        // Capture the combat from the triggering event so the resolve body can
        // read the declared attackers (CR 603.2 — a triggered ability is
        // associated with the event that triggered it).
        Majik.Core.Combat.Combat? capturedCombat = null;

        var condition = new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
        {
            // "As it attacks" (CR 508.1f / 702.139a) — only when this card's
            // controller is the attacking player AND this card is among the
            // declared attackers.
            var controller = card.Controller ?? owner;
            if (!ReferenceEquals(e.Combat.AttackingPlayer, controller)) return false;
            if (!e.Combat.Attackers.Any(a => ReferenceEquals(a?.Creature, card))) return false;
            capturedCombat = e.Combat;
            return true;
        });

        var exertEffect = new Effect(
            $"{CardName}: you may exert as it attacks; when you do, 4 damage to target non-Dragon opponent creature",
            () =>
            {
                var combat = capturedCombat;
                capturedCombat = null;
                ResolveExert(combat, card, owner, eventBus, mayExert, opponentCreaturesResolver);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { exertEffect },
            // CR 113.6 — the trigger functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    private static void ResolveExert(
        Majik.Core.Combat.Combat? combat,
        Creature card,
        Player owner,
        IEventBus? eventBus,
        Func<bool>? mayExert,
        Func<Majik.Core.Combat.Combat, IReadOnlyList<Creature>>? opponentCreaturesResolver)
    {
        if (combat == null) return;
        var controller = card.Controller ?? owner;

        // "You may exert this creature as it attacks." CR 702.139a. Default:
        // decline (shape-only posture — no upside without a target resolver).
        var wantsExert = mayExert?.Invoke() ?? false;
        if (!wantsExert) return;

        // CR 702.139c — the exert rider: "this creature won't untap during
        // your next untap step." One-shot per-permanent skip keyed by the card
        // itself so a repeat exert is idempotent; lifts on the controller's
        // next Untap step when a bus is available (mirrors Arena of Glory).
        UntapStepRestrictions.MarkPermanentDoesNotUntap(card, card);
        ScheduleNextUntapClear(card, controller, eventBus);

        // CR 603.1 reflexive "when you do": deal 4 damage to target non-Dragon
        // creature an opponent controls. The resolver supplies the candidate
        // pool; enforce the legality gate (CR 115.4) here.
        var pool = opponentCreaturesResolver?.Invoke(combat);
        var target = PickTarget(pool, controller);
        if (target == null) return; // no legal target — reflexive trigger does nothing.

        // CR 119.3 — Glorybringer deals 4 damage to the chosen creature.
        target.TakeDamage(ExertDamage);
    }

    private static Creature? PickTarget(IReadOnlyList<Creature>? pool, Player controller)
    {
        if (pool == null) return null;
        foreach (var c in pool)
        {
            if (c == null) continue;
            // "target non-Dragon creature an opponent controls" — CR 115.4.
            if (c.HasSubtype(CardSubtype.Dragon)) continue;
            if (ReferenceEquals(c.Controller, controller)) continue;
            return c;
        }
        return null;
    }

    private static void ScheduleNextUntapClear(Creature card, Player controller, IEventBus? eventBus)
    {
        if (eventBus == null) return;

        Action<StepStartedEvent>? handler = null;
        handler = (e) =>
        {
            if (e.StepType != PhaseStateType.Untap) return;
            if (!ReferenceEquals(e.Player, controller)) return;

            UntapStepRestrictions.RemoveAll(card);
            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }
}
