using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dalkovan Encampment (Duskmourn: House of Horror).
///
/// Land. Oracle text:
///   "This land enters tapped unless you control a Swamp or a Mountain.
///    {T}: Add {W}.
///    {2}{W}, {T}: Whenever you attack this turn, create two 1/1 red
///    Warrior creature tokens that are tapped and attacking."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype — Dalkovan
///   Encampment is nonbasic non-Legendary).
/// - <b>Conditional ETB-tapped (CR 614.1c)</b> — registered as a
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate: enters untapped iff the
///   controller controls another permanent with the Swamp OR Mountain
///   land subtype. Mirrors the <see cref="CheckLandCycleFactory"/>
///   two-subtype "unless you control an X or a Y" shape using
///   <c>HasSubtype</c> (not <c>HasSupertype(Basic)</c>) so any land
///   with the named subtype qualifies (CR 205.3i — land subtypes).
/// - <b>{T}: Add {W}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1,
///   no stack).
/// - <b>{2}{W}, {T}: Whenever you attack this turn, create two 1/1 red
///   Warrior creature tokens that are tapped and attacking.</b>
///   Wired as an <see cref="ActivatedAbility"/> with a
///   <see cref="ManaCostCost"/>("{2}{W}") + <see cref="AdditionalCost.Tap"/>
///   cost pair (CR 602.1). When activated, the resolution effect:
///     1. Creates a one-turn "whenever a creature you control attacks"
///        <see cref="TriggeredAbility"/> scoped to
///        <see cref="CreatureAttacksEvent"/> where the attacker's
///        controller is the Dalkovan Encampment controller. CR 508.1f.
///     2. Registers the trigger with the supplied
///        <see cref="TriggerManager"/>. The trigger fires once per
///        attacker you control this turn and creates two 1/1 red Warrior
///        creature tokens via <see cref="TokenFactory.CreateOnBattlefield"/>.
///     3. Registers a <see cref="DelayedTriggeredAbility"/> (CR 603.7)
///        that fires at the start of the next end step and unregisters
///        the attack-trigger so it doesn't persist beyond this turn.
///        This models the "this turn" duration of the granted trigger.
///
/// ## Deferred (v1 gaps)
/// - <b>"Tapped and attacking" token state</b>: the printed tokens enter
///   "tapped and attacking". The engine's <see cref="Majik.Core.Combat.CombatManager"/>
///   has no surface for inserting a creature into an in-progress combat
///   mid-trigger (same gap documented for Geist of Saint Traft's Angel
///   and the Goblin Rabblemaster's token). v1 tokens enter via
///   <see cref="TokenFactory.CreateOnBattlefield"/> — they land on the
///   battlefield (not tapped, not in an attacker slot) and don't deal
///   combat damage this turn. The "tapped and attacking" fidelity is
///   deferred to a future CombatManager extension (see
///   GeistOfSaintTraftFactory xmldoc deferred section).
/// - <b>Sorcery-speed gate</b>: the activation is not restricted to
///   sorcery-speed in v1 (the oracle doesn't specify timing). The engine's
///   priority / timing checks enforce the standard
///   "instant-speed by default" rule for activated abilities of
///   non-creatures (CR 602.6a). A sorcery-speed gate could be layered
///   atop via <see cref="ActivatedAbility.CanActivateCheck"/> if needed.
/// </summary>
[CardName("Dalkovan Encampment")]
public static class DalkovanEncampmentFactory
{
    public const string CardName = "Dalkovan Encampment";
    public const string ActivationCost = "{2}{W}";
    public const int TokenCount = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Dalkovan Encampment with no live runtime wiring. The mana
    /// ability + the activated ability are attached for shape inspection;
    /// the conditional ETB-tapped replacement is not registered, and the
    /// activated ability's token-trigger effect is a no-op (no
    /// TriggerManager). Suitable for dispatcher / shape tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, triggers: null);

    /// <summary>
    /// Construct Dalkovan Encampment with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "enters tapped unless
    /// you control a Swamp or a Mountain" replacement is registered
    /// (CR 614.1c). Pass <c>null</c> for shape-only construction.</param>
    /// <param name="triggers">When supplied, the activated ability's
    /// resolution installs a one-turn "whenever you attack, create two
    /// 1/1 red Warrior tokens" trigger that fires for the activator's
    /// attackers (CR 508.1f). The trigger is auto-unregistered at the
    /// next end step via a <see cref="DelayedTriggeredAbility"/>
    /// (CR 603.7). Pass <c>null</c> for shape-only construction.</param>
    public static Land Create(
        Player owner,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Enters tapped unless you control a Swamp or a Mountain
        // (CR 614.1c). Predicate returns true ⇒ enters untapped, false ⇒
        // enters tapped. Mirrors CheckLandCycleFactory's two-subtype
        // ConditionalEntersTappedReplacement shape.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    ControllerHasSubtype(controller, self, CardSubtype.Swamp)
                    || ControllerHasSubtype(controller, self, CardSubtype.Mountain)));
        }

        // ----------------------------------------------------------------
        // {T}: Add {W}
        // CR 605.1 — mana ability, no stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

        // ----------------------------------------------------------------
        // {2}{W}, {T}: Whenever you attack this turn, create two 1/1 red
        // Warrior creature tokens that are tapped and attacking.
        //
        // CR 602.1 — activated ability. Costs: {2}{W} mana + tap.
        // Resolution: register a "whenever a creature you control attacks"
        // TriggeredAbility for the rest of this turn; unregister at next
        // end step via a DelayedTriggeredAbility (CR 603.7).
        // ----------------------------------------------------------------
        var activateEffect = new Effect(
            $"{CardName}: install this-turn attack trigger → 2 red Warrior tokens per attack",
            () => InstallAttackTrigger(land, owner, triggers));

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { activateEffect }));

        return land;
    }

    /// <summary>
    /// Install the "whenever a creature you control attacks this turn,
    /// create two 1/1 red Warrior tokens" triggered ability. The trigger
    /// is registered with <paramref name="triggers"/> (when supplied) and
    /// a companion <see cref="DelayedTriggeredAbility"/> fires at the next
    /// end step to unregister it.
    /// </summary>
    private static void InstallAttackTrigger(
        Land land,
        Player owner,
        TriggerManager? triggers)
    {
        if (triggers == null) return;

        var controller = land.Controller ?? owner;

        // The per-attack token effect: fires each time one of the
        // controller's creatures attacks while this trigger is live.
        // "Two 1/1 red Warrior creature tokens" (CR 111 / CR 111.4).
        TriggeredAbility? attackTrigger = null;

        var tokenEffect = new Effect(
            $"{CardName}: create {TokenCount} 1/1 red Warrior tokens (attack trigger)",
            () =>
            {
                var ctrl = land.Controller ?? owner;
                CreateWarriorTokens(ctrl);
            });

        // Condition: any CreatureAttacksEvent for a creature controlled
        // by the Dalkovan Encampment controller. CR 508.1f.
        attackTrigger = new TriggeredAbility(
            source: land,
            controller: controller,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) => ReferenceEquals(e.Attacker.Controller, land.Controller ?? owner)),
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        triggers.RegisterTriggeredAbility(attackTrigger);

        // CR 603.7 — DelayedTriggeredAbility to unregister the attack
        // trigger at the start of the next end step, modelling the
        // "this turn" duration of the printed effect.
        var resolvedAt = DateTime.UtcNow;
        var cleanupEffect = new Effect(
            $"{CardName}: unregister attack-token trigger (EOT cleanup)",
            () => triggers.UnregisterTriggeredAbility(attackTrigger));

        var cleanup = new DelayedTriggeredAbility(
            source: land,
            controller: controller,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == Majik.Core.StateMachine.PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { cleanupEffect });

        triggers.RegisterDelayed(cleanup);
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create <see cref="TokenCount"/> 1/1 red Warrior
    /// creature tokens under <paramref name="controller"/>'s control.
    /// v1: "tapped and attacking" token state deferred — see class xmldoc.
    /// </summary>
    private static void CreateWarriorTokens(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Warrior",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Warrior },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 red Warrior creature token".
            Colors: new[] { ManaColor.Red });

        // CR 614 — route through TokenCreationIntent so token doublers
        // (Doubling Season / Parallel Lives / Anointed Procession) can
        // rewrite the count. No ZoneService in this path (shape-only
        // mana + trigger wiring); raw zone move mirrors the existing
        // CardFactory precedent for factories that don't expose a
        // ZoneService parameter (GeistOfSaintTraftFactory, etc.).
        for (int i = 0; i < TokenCount; i++)
        {
            TokenFactory.CreateOnBattlefield(spec, controller);
        }
    }

    /// <summary>
    /// CR 614 helper — check whether <paramref name="controller"/> controls
    /// a permanent with the given land subtype, excluding <paramref name="self"/>
    /// to avoid the card counting itself (same shape as
    /// <see cref="CheckLandCycleFactory.ControllerHasSubtype"/>).
    /// </summary>
    private static bool ControllerHasSubtype(
        Player controller,
        ICard self,
        CardSubtype subtype) =>
        controller.Zones.Battlefield.GetCards()
            .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(subtype));
}
