using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Frenzied Goblin (Time Spiral / Ravnica, {R}).
///
/// Creature — Goblin Berserker 1/1. Oracle text (verified against Scryfall):
///   "Whenever this creature attacks, you may pay {R}. If you do, target
///    creature can't block this turn."
///
/// The base shape (name, Creature, Goblin + Berserker subtypes, {R}, 1/1) is
/// materialised from the embedded JSON definition (<c>frenzied-goblin.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single attack-trigger
/// behaviour is layered on here — the JSON <c>AbilityDefinition</c> schema
/// doesn't yet express an optional-cost attack trigger (same posture as
/// <see cref="HiredClawFactory"/> / <see cref="MentorOfTheMeekFactory"/>).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Goblin Berserker at printed cost {R}, owner/controller
///   wired.
/// - <b>Attack trigger (CR 508.1f / CR 603.1)</b>: a
///   <see cref="EventTriggerCondition{TEvent}"/> over
///   <see cref="CreatureAttacksEvent"/> via
///   <see cref="Triggers.OnAttackSelf"/> — fires when Frenzied Goblin itself
///   is declared as an attacker. Declares a single 1..1
///   <see cref="TargetRequest"/> ("target creature") whose chosen target is
///   read from <see cref="TriggeredAbility.ChosenTargets"/> at resolution
///   (same target-on-trigger shape as
///   <see cref="EarthshakerKhenraFactory"/>'s ETB rider).
/// - <b>"You may pay {R}" optional rider (CR 117.5 / 601.2h)</b>: on
///   resolution the controller's <see cref="IPlayerAgent"/> is consulted via
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/>; agent-less callers auto-pay
///   if able (Mentor of the Meek / Animation Module posture).
///   <see cref="Player.PayMana"/> returns false when the pool can't satisfy
///   {R}, in which case the rider fizzles harmlessly and no restriction is
///   applied ("If you do" — CR 117.12).
/// - <b>"Target creature can't block this turn" (CR 509.1c)</b>: when the pay
///   succeeds AND the chosen target is still a <see cref="Creature"/> on the
///   battlefield (CR 608.2b illegal-target recheck), a
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBlock"/> is registered against the
///   target creature's <see cref="Creature.ActiveEffects"/>. The default
///   <c>expiresAtEndOfTurn: true</c> matches the printed "this turn" rider
///   (CR 514.2). Same restriction shape as
///   <see cref="EarthshakerKhenraFactory"/>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The attack trigger is
///   attached for dispatcher / structural tests but not registered with any
///   <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired; the attack
///   trigger registers so a matching <see cref="CreatureAttacksEvent"/>
///   queues it.
///
/// ## Notes
/// - When the target creature has no live
///   <see cref="ContinuousEffectsService"/> wired (shape-only tests) the
///   restriction registration is a no-op and the effect body exits cleanly,
///   matching <see cref="EarthshakerKhenraFactory"/>.
/// </summary>
[CardName("Frenzied Goblin")]
public static class FrenziedGoblinFactory
{
    public const string CardName = "Frenzied Goblin";
    public const string Slug = "frenzied-goblin";

    /// <summary>CR 117.5 — the optional cost the controller may pay.</summary>
    public const string OptionalManaCost = "{R}";

    /// <summary>
    /// Construct Frenzied Goblin with no live wiring. The attack trigger is
    /// attached to the card shape for dispatcher / structural tests; not
    /// registered with any <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Frenzied Goblin with optional <see cref="TriggerManager"/>
    /// wiring. When supplied, the attack trigger registers so a matching
    /// <see cref="CreatureAttacksEvent"/> (Frenzied Goblin itself attacking)
    /// automatically queues the may-pay-then-lock effect.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Goblin + Berserker subtypes, {R}, 1/1). The JSON carries no
        // abilities — the attack trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 508.1f / CR 603.1 — "Whenever this creature attacks, you may pay
        // {R}. If you do, target creature can't block this turn."
        TriggeredAbility? attackTrigger = null;
        var attackEffect = new Effect(
            $"{CardName}: may pay {OptionalManaCost} → target creature can't block this turn",
            async ctx =>
            {
                if (attackTrigger == null) return;

                var triggerController = card.Controller ?? owner;

                // "You may pay {R}" — consult the controller's agent. Agent-less
                // fallback: auto-pay if able (Mentor of the Meek posture).
                var agent = ctx.Agent ?? AgentRegistry.Get(triggerController);
                bool pay;
                if (agent != null)
                {
                    pay = await agent.ChooseYesNoAsync(
                        $"Pay {OptionalManaCost} so target creature can't block this turn?",
                        BotIntent.Removal).ConfigureAwait(false);
                }
                else
                {
                    pay = true;
                }

                if (!pay) return;

                // CR 117.5 / 117.12 — optional may-pay; the "If you do" clause
                // only fires when the mana is actually paid. PayMana returns
                // false when the pool can't satisfy {R}.
                if (!triggerController.PayMana(ManaCost.Parse(OptionalManaCost))) return;

                // CR 115 — read the chosen target out of ChosenTargets.
                var chosen = attackTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Creature target) return;

                // CR 608.2b — recheck target legality at resolution.
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 509.1c — register CannotBlock scoped to the target. The
                // default ExpiresAtEndOfTurn matches the printed "this turn"
                // rider (CR 514.2). The restriction lives on the target
                // creature's ContinuousEffectsService (Creature.ActiveEffects);
                // the combat validator queries there. When ActiveEffects is
                // null (shape tests) the grant silently no-ops.
                if (target.ActiveEffects == null) return;
                target.ActiveEffects.Register(
                    new CombatRestrictionEffect(CombatRestriction.CannotBlock, target));
            });

        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
