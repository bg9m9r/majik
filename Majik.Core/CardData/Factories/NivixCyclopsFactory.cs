using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nivix Cyclops (Return to Ravnica, {1}{U}{R}).
///
/// Creature — Cyclops 1/4. Oracle text (verified against Scryfall):
///   "Defender
///    Whenever you cast an instant or sorcery spell, this creature gets +3/+0
///    until end of turn and can attack this turn as though it didn't have
///    defender."
///
/// ## Implementation
///
/// - 1/4 Cyclops, mana cost {1}{U}{R} (red + blue from the {U}{R} pips).
/// - <b>Defender (CR 702.3)</b>: a <see cref="KeywordAbility"/> marker, read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/>. The
///   can't-attack rule (CR 702.3b) is enforced by
///   <see cref="Majik.Core.Combat.CombatValidator.CanAttack"/> /
///   <see cref="Majik.Core.Combat.BlockLegality.CanAttack"/> and the
///   <see cref="Majik.Core.Game.TurnDriver"/> eligible-attacker filter.
/// - <b>Cast-instant/sorcery rider (CR 603.1 / CR 508.1a relaxation)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that fires
///   whenever this card's controller casts an Instant or Sorcery spell
///   (CR 205.3). On resolve it:
///     * registers a <see cref="PumpUntilEndOfTurnEffect"/>(+3, 0) on Nivix
///       Cyclops' own <see cref="Creature.ActiveEffects"/> (Layer 7c +P/+T,
///       end-of-turn-expirable per CR 514.2) — same shape as
///       <see cref="WeeDragonautsFactory"/>'s +2/+0, here +3/+0;
///     * sets <see cref="Creature.CanAttackAsThoughItDidntHaveDefenderThisTurn"/>
///       = true so the Defender can't-attack rule (CR 702.3b) is relaxed for
///       this creature for the rest of the turn (CR 508.1a — "can attack this
///       turn as though it didn't have defender"). The flag is cleared at
///       cleanup (CR 514.2); the creature still HAS the defender keyword, it is
///       merely permitted to be declared as an attacker.
///   Each instant/sorcery cast stacks the +3/+0 additively (multiple Layer 7c
///   effects all apply — CR 613) and re-asserts the attack permission.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. Defender marker + the cast
///   rider are attached for shape observability, but without a
///   <see cref="TriggerManager"/> the bus won't fire it and without a
///   <see cref="ContinuousEffectsService"/> on
///   <see cref="Creature.ActiveEffects"/> the pump body silently no-ops on
///   execute (the attack-permission flag is still set — it lives on the
///   Creature, not the effects service). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired. Cast rider registered with <paramref name="triggers"/>;
///   <paramref name="effects"/> is bound onto the card's
///   <see cref="Creature.ActiveEffects"/> so live P/T reads flow through the
///   layers compute (CR 613 — Layer 7c).
/// </summary>
[CardName("Nivix Cyclops")]
public static class NivixCyclopsFactory
{
    public const string CardName = "Nivix Cyclops";
    public const string PrintedManaCost = "{1}{U}{R}";
    public const int Power = 1;
    public const int Toughness = 4;
    public const int PumpPower = 3;
    public const int PumpToughness = 0;

    /// <summary>
    /// Construct Nivix Cyclops with no live wiring. Defender marker + the cast
    /// rider are attached; the rider's pump silently no-ops without an effects
    /// service, and the trigger isn't registered with any
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, effects: null);

    /// <summary>
    /// Construct Nivix Cyclops with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager for the cast-instant/sorcery rider.
    /// May be null — the trigger is still attached to the card shape.</param>
    /// <param name="effects">ContinuousEffectsService for the +3/+0 EOT pump
    /// (CR 613 Layer 7c, CR 514.2 EOT cleanup). Bound onto the card's
    /// <see cref="Creature.ActiveEffects"/>. May be null — the pump body
    /// silently no-ops on execute (the defender-relaxation flag is still set).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Cyclops });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.3 — Defender keyword marker. Surfaces via
        // CombatAbilities.HasDefender; the can't-attack rule is enforced by the
        // combat validator / TurnDriver eligibility filter.
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // Bind the effects service onto the card so live P/T reads through
        // ActiveEffects flow through the layers compute (mirrors Wee
        // Dragonauts). Done before the trigger so the closure has a stable ref.
        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // CR 603.1 — "Whenever you cast an instant or sorcery spell, this
        // creature gets +3/+0 until end of turn and can attack this turn as
        // though it didn't have defender." Predicate: spell controller matches
        // AND the spell's card is an Instant or Sorcery (CR 205.3). Nivix
        // Cyclops' own cast does NOT trigger this — its SpellCastEvent fires
        // while it is on the stack as a Creature spell, failing the predicate.
        var riderCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && (e.Spell.Card.HasType(CardType.Instant)
                || e.Spell.Card.HasType(CardType.Sorcery)));

        var riderEffect = new Effect(
            $"{CardName}: +{PumpPower}/+{PumpToughness} until end of turn and can attack this turn as though it didn't have defender",
            () =>
            {
                // CR 514.2 — EOT cleanup is automatic via the pump's
                // ExpiresAtEndOfTurn flag (Layer 7c). Without a live effects
                // service the pump silently no-ops.
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, PumpPower, PumpToughness));

                // CR 508.1a relaxation — permit attacking as though no defender
                // for the rest of the turn. The flag lives on the Creature, so
                // it is set even when no effects service is wired. Cleared at
                // cleanup (CR 514.2).
                card.CanAttackAsThoughItDidntHaveDefenderThisTurn = true;
            });

        var riderTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: riderCondition,
            effects: new IEffect[] { riderEffect });

        card.AddAbility(riderTrigger);
        triggers?.RegisterTriggeredAbility(riderTrigger);

        return card;
    }
}
