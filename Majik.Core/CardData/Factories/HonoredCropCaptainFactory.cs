using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Honored Crop-Captain (Aether Revolt, {R}{W}).
/// Creature — Human Warrior, 3/2. Oracle text (verified against Scryfall):
///   "Whenever this creature attacks, other attacking creatures get +1/+0
///    until end of turn."
///
/// This is the functional core of Battle cry (CR 702.92a) written out as a
/// plain triggered ability — but note the card does <b>not</b> have the
/// "Battle cry" keyword printed on it (no reminder text / keyword line), so
/// unlike <see cref="HeroOfBladeholdFactory"/> no
/// <see cref="KeywordAbility"/> "Battle cry" marker is attached. The trigger
/// shape is otherwise identical to Hero's battle-cry rider (minus the Soldier
/// token rider).
///
/// The base shape (name, type, Human Warrior subtypes, {R}{W}, 3/2) is
/// materialised from the embedded JSON definition
/// (<c>honored-crop-captain.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="LegionLoyalistFactory"/>). The attack trigger is layered on top
/// here — the JSON <c>AbilityDefinition</c> schema doesn't express attack
/// triggers.
///
/// ## Implemented (v1)
/// - 3/2 red-white Human Warrior at {R}{W}, owner / controller wired
///   (CR 105 — red+white from the {R}/{W} pips, carried by the JSON shape).
/// - <b>"Whenever this creature attacks, other attacking creatures get +1/+0
///   until end of turn." (CR 508.2 attack trigger; CR 514.2 cleanup
///   expiry)</b> — an <see cref="Triggers.OnAttackSelf"/>
///   <see cref="TriggeredAbility"/> that, on resolution, registers a
///   <see cref="PumpUntilEndOfTurnEffect"/> of +1/+0 on every OTHER attacking
///   creature. The "other attacking creatures" set is read from the supplied
///   <paramref name="attackingCreaturesSource"/> closure (same source-closure
///   shape as <see cref="HeroOfBladeholdFactory"/> — the engine doesn't yet
///   expose a global "currently attacking creatures" view from inside an
///   effect closure). The pump is registered on each target's own
///   <see cref="Creature.ActiveEffects"/>. Honored Crop-Captain itself is
///   skipped ("OTHER attacking creatures").
///
/// ## Source closure injection
/// Same shape as <see cref="HeroOfBladeholdFactory"/> — when
/// <paramref name="attackingCreaturesSource"/> is null the pump is a no-op.
/// </summary>
[CardName("Honored Crop-Captain")]
public static class HonoredCropCaptainFactory
{
    public const string CardName = "Honored Crop-Captain";
    public const string Slug = "honored-crop-captain";

    /// <summary>+1/+0 to each other attacking creature.</summary>
    public const int PumpPower = 1;
    public const int PumpToughness = 0;

    /// <summary>
    /// Construct Honored Crop-Captain with no live runtime wiring. The attack
    /// trigger is attached to the card shape but its pump is a no-op (no
    /// attackers source). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct Honored Crop-Captain with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the attack trigger is registered
    /// so a <see cref="CreatureAttacksEvent"/> for this creature lands it on
    /// the stack automatically.</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker creature list, called at trigger resolution. May be null —
    /// the pump is then a no-op.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human
        // Warrior, {R}{W}, 3/2). The JSON carries no abilities — the attack
        // trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 508.2 — "Whenever this creature attacks, other attacking
        // creatures get +1/+0 until end of turn."
        var pumpEffect = new Effect(
            $"{CardName}: other attacking creatures get +1/+0 until end of turn",
            () =>
            {
                if (attackingCreaturesSource == null) return;
                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    // "OTHER attacking creatures" — skip this creature itself.
                    if (ReferenceEquals(atk, card)) continue;
                    // Each creature computes P/T from its own service; without
                    // one the grant silently no-ops (same posture as
                    // HeroOfBladeholdFactory's battle-cry pump).
                    if (atk.ActiveEffects == null) continue;
                    atk.ActiveEffects.Register(
                        new PumpUntilEndOfTurnEffect(atk, PumpPower, PumpToughness));
                }
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
