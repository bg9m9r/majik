using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hand That Feeds (Modern Horizons 3, {1}{R}).
///
/// Creature — Mutant 2/2. Oracle text (verified against Scryfall 2026-06-24):
///   "Delirium — Whenever this creature attacks while there are four or more
///    card types among cards in your graveyard, it gets +2/+0 and gains menace
///    until end of turn. (It can't be blocked except by two or more
///    creatures.)"
///
/// ## Shape source
///
/// Card identity (name, {1}{R}, 2/2, Creature — Mutant) is loaded from
/// <c>Majik.Core/CardData/Cards/hand-that-feeds.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The delirium-gated attack trigger is
/// wired in code below (the trigger + intervening-if condition + UEOT pump /
/// keyword-grant body are not expressible in the JSON AbilityDefinition
/// schema).
///
/// ## Implementation
///
/// Hand That Feeds is the attack-trigger analogue of the delirium
/// (CR 702.105) card-type-count discipline shared with
/// <see cref="GrimFlayerFactory"/> / <see cref="DragonsRageChannelerFactory"/>,
/// but its bonus is a <em>one-shot until-end-of-turn</em> effect granted on
/// attack rather than a continuous static. The two differences from a plain
/// "whenever this attacks" pump are (a) the intervening-if delirium gate, and
/// (b) the keyword grant rides alongside the +2/+0.
///
/// - <b>Delirium intervening-if attack trigger (CR 603.4 + CR 508.1f +
///   CR 702.105)</b>: an <see cref="Triggers.OnAttackSelf"/>-shaped
///   <see cref="TriggeredAbility"/> whose condition additionally requires the
///   controller's graveyard to hold four or more distinct
///   <see cref="CardType"/> values at the moment the creature is declared as
///   an attacker. Because "while there are four or more card types …" is an
///   intervening-if clause (CR 603.4), the delirium count is sampled live off
///   the controller's graveyard inside the trigger condition (reusing
///   <see cref="TarmogoyfFactory.CountDistinctCardTypes"/> via
///   <see cref="IsDeliriumActive"/>) — when delirium is not met at declaration
///   the ability simply does not trigger. (CR 603.4 also re-checks on
///   resolution; the bonus is a single attack-declared one-shot, so the
///   declaration-time check is the load-bearing one.)
///
/// - <b>+2/+0 and menace until end of turn (CR 613 / CR 514.2 / CR 702.111)</b>:
///   on resolution the effect registers a <see cref="PumpUntilEndOfTurnEffect"/>
///   (+2/+0, Layer 7c) and a <see cref="GrantKeywordUntilEndOfTurnEffect"/>
///   ("Menace", Layer 6) on the creature's own
///   <see cref="Creature.ActiveEffects"/> when one is wired. Both expire in the
///   cleanup step automatically via their <c>ExpiresAtEndOfTurn</c> flag
///   (CR 514.2). The granted "Menace" keyword is read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/> through the
///   computed keyword set (Layer-6 grant), so the can't-be-blocked-except-by-
///   two rule (CR 702.111b) applies for this combat. Same raw +X/+0 EOT pump
///   shape as <see cref="SlickshotShowOffFactory"/>, with an added keyword
///   grant. The +0 toughness is deliberate — the printed body is +2/+0.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only (the attack trigger is
///   attached for observability but not registered with a TriggerManager, and
///   without a <see cref="ContinuousEffectsService"/> on
///   <see cref="Creature.ActiveEffects"/> the pump / grant body silently
///   no-ops). Suitable for dispatcher / structural tests. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired. The trigger fires from the bus when a trigger manager is
///   supplied; the effects service is bound onto the card's
///   <see cref="Creature.ActiveEffects"/> so the UEOT pump / keyword grant apply
///   through the layers compute.
/// </summary>
[CardName("Hand That Feeds")]
public static class HandThatFeedsFactory
{
    public const string CardName = "Hand That Feeds";
    public const string Slug = "hand-that-feeds";
    public const int DeliriumThreshold = 4;
    public const int PumpPower = 2;
    public const int PumpToughness = 0;
    public const string GrantedKeyword = "Menace";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Hand That Feeds with no live wiring. The delirium attack
    /// trigger is attached for shape observability; without a
    /// <see cref="TriggerManager"/> the bus won't surface it, and without a
    /// <see cref="ContinuousEffectsService"/> on
    /// <see cref="Creature.ActiveEffects"/> the +2/+0 / menace body silently
    /// no-ops. Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Hand That Feeds with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for future lifecycle subscribers; the
    /// EOT expiry is automatic via the effects' <c>ExpiresAtEndOfTurn</c> flag,
    /// so the bus is not consumed directly today.</param>
    /// <param name="triggers">When supplied, the delirium attack trigger is
    /// registered so a <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
    /// for this creature lands it on the stack (subject to the delirium gate).</param>
    /// <param name="effects">When supplied, bound onto the card's
    /// <see cref="Creature.ActiveEffects"/> so the +2/+0 EOT pump and the
    /// Menace keyword grant apply through the layers compute.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // Bind the effects service onto the card so live P/T + keyword reads
        // through ActiveEffects flow through the layers compute (mirrors
        // Slickshot Show-Off). The resolve closure also reads card.ActiveEffects
        // so a late-bound service still works; this is a fast-path.
        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // ----------------------------------------------------------------
        // CR 603.4 intervening-if + CR 508.1f attack trigger + CR 702.105
        // delirium — "Whenever this creature attacks while there are four or
        // more card types among cards in your graveyard, it gets +2/+0 and
        // gains menace until end of turn." The delirium count is sampled in
        // the trigger condition off the controller's graveyard, so the ability
        // only triggers when the four-card-type threshold is met at the moment
        // the creature is declared as an attacker.
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>(
            (e, _) =>
                ReferenceEquals(e.Attacker, card)
                && IsDeliriumActive(card.Controller ?? owner));

        var pumpEffect = new Effect(
            $"{CardName}: +{PumpPower}/+{PumpToughness} and gains menace until end of turn (delirium attack)",
            () =>
            {
                // CR 514.2 — both effects self-expire at end of turn via their
                // ExpiresAtEndOfTurn flag. Without a live effects service the
                // body silently no-ops; the runtime overload binds one onto
                // card.ActiveEffects.
                var active = card.ActiveEffects;
                if (active == null) return;

                // CR 613 Layer 7c — +2/+0.
                active.Register(new PumpUntilEndOfTurnEffect(card, PumpPower, PumpToughness));
                // CR 613 Layer 6 / CR 702.111 — gains menace until end of turn.
                active.Register(new GrantKeywordUntilEndOfTurnEffect(card, GrantedKeyword));
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// Sample the controller's graveyard for delirium (CR 702.105): true iff
    /// there are <see cref="DeliriumThreshold"/>+ distinct
    /// <see cref="CardType"/> values across cards in
    /// <paramref name="controller"/>'s graveyard. Reuses
    /// <see cref="TarmogoyfFactory.CountDistinctCardTypes"/> (same discipline as
    /// <see cref="GrimFlayerFactory.IsDeliriumActive"/>).
    /// </summary>
    public static bool IsDeliriumActive(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return TarmogoyfFactory.CountDistinctCardTypes(
            controller.Zones.Graveyard.GetCards()) >= DeliriumThreshold;
    }
}
