using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Searing Blood (Born of the Gods / Modern Horizons, {R}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Searing Blood deals 2 damage to target creature. When that creature
///    dies this turn, Searing Blood deals 3 damage to the creature's
///    controller."
///
/// ## Implementation
///
/// Same damage-to-creature + delayed "when that creature dies this turn"
/// damage-to-controller shape as <see cref="SearingBlazeFactory"/> /
/// <see cref="BerserkFactory"/>, but with fixed amounts (2 then 3) and no
/// landfall pump.
///
/// Card shape comes from the embedded JSON (<c>searing-blood.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver + trigger manager supplied by the caller's
/// <see cref="GameContext"/> (not expressible in the data-only JSON schema).
///
/// On resolution:
///   1. Deal 2 damage to the chosen target creature (CR 119 →
///      <see cref="Fx.DealDamageAny(object, int)"/>).
///   2. Capture the creature's controller as it last existed on the
///      battlefield (CR 603.10e — last-known information). This is sampled
///      NOW because when a creature dies it routes to its OWNER's graveyard
///      and <see cref="ZoneService"/> resets Controller=Owner before the
///      CardMovedEvent publishes — so the live Controller at trigger-fire
///      time is unreliable.
///   3. Register a one-shot <see cref="DelayedTriggeredAbility"/> (CR 603.7)
///      watching <see cref="CardMovedEvent"/> for the exact targeted
///      creature moving Battlefield→Graveyard with a post-resolution
///      timestamp fence. CR 700.4 — a creature put into a graveyard from the
///      battlefield is "dies". On fire, deal 3 damage to the captured
///      controller.
///
/// When no <see cref="TriggerManager"/> is supplied (shape / dispatcher
/// tests) the delayed clause is skipped — the 2 damage still applies. Same
/// posture every other delayed-trigger card uses.
/// </summary>
[CardName("Searing Blood")]
public static class SearingBloodFactory
{
    public const string CardName = "Searing Blood";
    public const string Slug = "searing-blood";

    /// <summary>CR 119 — fixed 2 damage to the target creature.</summary>
    public const int CreatureDamage = 2;

    /// <summary>CR 119 — fixed 3 damage to the creature's controller when it
    /// dies this turn.</summary>
    public const int ControllerDamage = 3;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time SpellDefinition. Single 1..1 "target creature"
    /// request. On resolution: 2 damage to the target; arm a delayed
    /// end-of-life trigger dealing 3 to the creature's (resolution-time)
    /// controller if it dies this turn.
    /// </summary>
    /// <param name="controller">Spell controller (unused for the damage
    /// amounts — fixed — but kept for signature parity with the analogue
    /// and for future timing hooks).</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="triggers">Optional trigger manager. When supplied the
    /// delayed "dies this turn → 3 to controller" clause is registered
    /// (CR 603.7). When null the clause is skipped (2 damage still applies —
    /// shape-only tests).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect($"{CardName}: 2 to target creature + delayed 3 to its controller on death", () =>
                        Resolve(raw, triggers)),
                };
            });
    }

    private static void Resolve(object raw, TriggerManager? triggers)
    {
        // CR 608.2b — illegal target / non-Creature resolver → no-op.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // 1. CR 119 — 2 damage to the target creature.
        Fx.DealDamageAny(target, CreatureDamage);

        // No trigger manager (shape-only path) — skip the delayed clause.
        if (triggers == null) return;

        // 2. CR 603.10e — capture the creature's controller as it last
        // existed on the battlefield (last-known information). Sampled NOW
        // because the death move resets Controller=Owner (ZoneService) before
        // CardMovedEvent publishes, so reading it at fire time is unreliable.
        var controllerAtResolution = target.Controller ?? target.Owner;
        if (controllerAtResolution == null) return;

        // 3. CR 603.7 — one-shot delayed triggered ability. Fires on the
        // first CardMovedEvent of the exact targeted creature moving
        // Battlefield→Graveyard strictly after this resolution (timestamp
        // fence — a creature that already died earlier this turn isn't
        // retroactively counted). CR 700.4 — Battlefield→Graveyard = dies.
        var resolvedAt = DateTime.UtcNow;
        var damageEffect = new Effect(
            $"{CardName}: 3 damage to {controllerAtResolution.Name} ({target.Name} died this turn)",
            // CR 119 — this is *damage* to the player (relevant for lifelink /
            // redirection), not a bare life-loss. Route through DealDamageAny,
            // matching Searing Blaze's player branch.
            () => Fx.DealDamageAny(controllerAtResolution, ControllerDamage));

        var delayed = new DelayedTriggeredAbility(
            source: target,
            controller: controllerAtResolution,
            condition: new EventTriggerCondition<CardMovedEvent>(
                (e, _) => ReferenceEquals(e.Card, target)
                          && e.FromZone == ZoneType.Battlefield
                          && e.ToZone == ZoneType.Graveyard
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { damageEffect });

        triggers.RegisterDelayed(delayed);
    }
}
