using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ichorclaw Myr (Scars of Mirrodin, {2}).
///
/// Artifact Creature — Phyrexian Myr 1/1. Oracle text (verified against
/// Scryfall 2026-06-02):
///   "Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)
///    Whenever this creature becomes blocked, it gets +2/+2 until end of
///    turn."
///
/// ## Shape source
/// Card identity (name, {2}, 1/1, Artifact Creature — Phyrexian Myr) is loaded
/// from <c>Majik.Core/CardData/Cards/ichorclaw-myr.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-driven posture as
/// <see cref="PlatedGeopedeFactory"/> / <see cref="BottleGnomesFactory"/>. The
/// "Creature" type is listed first so <see cref="CardDefinitionFactory.Build"/>
/// materialises a <see cref="Creature"/>; the JSON's <c>types</c> array also
/// carries Artifact (CR 205.2a — a permanent can have multiple card types) so
/// artifact-matters effects see it. Infect + the becomes-blocked pump trigger
/// are layered on in code below.
///
/// ## Implemented (v1)
/// - 1/1 Artifact Creature — Phyrexian Myr at {2}, owner / controller wired.
/// - <b>Infect (CR 702.90)</b> — attached as a <see cref="KeywordAbility"/>
///   marker. The damage-replacement primitive (poison counters to players,
///   -1/-1 counters to creatures) is engine-side; this factory contributes a
///   structurally correct marker so combat / damage code can consult it once
///   the replacement lands. Same posture as
///   <see cref="GlistenerElfFactory"/> / <see cref="PlagueStingerFactory"/>.
/// - <b>"Whenever this creature becomes blocked, it gets +2/+2 until end of
///   turn" (CR 603.1 / CR 509.1h)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="BlockersDeclaredEvent"/>. A creature "becomes blocked" when
///   one or more creatures are declared to block it (CR 509.1h). The engine
///   fires a single <see cref="BlockersDeclaredEvent"/> carrying the whole
///   combat; the trigger condition fires when this card appears in the
///   combat's attacker list with at least one declared blocker — same
///   BlockersDeclaredEvent binding pattern as
///   <see cref="WallOfFrostFactory"/>'s blocks trigger, here filtered to the
///   attacker side. On resolution the effect registers a self-targeted
///   <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2) on the Myr's own
///   <see cref="Creature.ActiveEffects"/> (Layer 7c CR 613.1g; expiry
///   CR 514.2). When <see cref="Creature.ActiveEffects"/> is null (shape-only
///   tests with no live <see cref="Majik.Core.Services.ContinuousEffectsService"/>)
///   the registration is a no-op — mirrors <see cref="PlatedGeopedeFactory"/>.
///
/// ## Notes
/// - "Becomes blocked" fires once per combat regardless of how many creatures
///   block it (CR 509.1h — the trigger condition is the transition to the
///   blocked state, not per-blocker). The trigger here keys on "this card is a
///   declared attacker with ≥ 1 blocker", which is true exactly once per
///   declare-blockers step.
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — shape-only (no trigger-manager wiring).
///   Suitable for identity tests and the <see cref="NamedCardFactory"/>
///   dispatcher.
/// - <see cref="Create(Player, TriggerManager?)"/> — attaches the
///   becomes-blocked trigger to a <see cref="TriggerManager"/> for live firing.
/// </summary>
[CardName("Ichorclaw Myr")]
public static class IchorclawMyrFactory
{
    public const string CardName = "Ichorclaw Myr";
    public const string Slug = "ichorclaw-myr";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>Layer 7c +P/+T magnitude granted when the Myr becomes blocked
    /// (CR 613.1g).</summary>
    public const int PumpAmount = 2;

    /// <summary>
    /// Construct Ichorclaw Myr with no live <see cref="TriggerManager"/>
    /// wiring. Infect + the becomes-blocked trigger are attached for shape
    /// inspection; the trigger is not registered with a bus. Suitable for
    /// identity / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Ichorclaw Myr. When <paramref name="triggers"/> is supplied
    /// the becomes-blocked trigger is registered so a
    /// <see cref="BlockersDeclaredEvent"/> in which this Myr is a blocked
    /// attacker automatically queues the +2/+2 ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact
        // Creature — Phyrexian Myr, {2}, 1/1). The JSON carries no abilities —
        // Infect + the becomes-blocked trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.90 — Infect keyword marker. The damage-replacement primitive
        // (poison counters on players, -1/-1 counters on creatures) is
        // engine-side; this factory exposes the marker so combat code can
        // consult it once the replacement lands. Same posture as Glistener Elf.
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        // --------------------------------------------------------------------
        // "Whenever this creature becomes blocked, it gets +2/+2 until end of
        //  turn." (CR 603.1 / CR 509.1h)
        //
        // Engine hook: BlockersDeclaredEvent fires once per declare-blockers
        // step carrying the full combat. A creature becomes blocked when ≥ 1
        // creature is declared to block it (CR 509.1h). The condition fires
        // when this card is a declared attacker with at least one blocker.
        //
        // On resolve, register a self-targeted +2/+2 PumpUntilEndOfTurnEffect
        // (Layer 7c CR 613.1g; expiry CR 514.2) on the Myr's own ActiveEffects.
        // ActiveEffects is null in shape-only tests (no live
        // ContinuousEffectsService) — no-op, mirroring Plated Geopede.
        // --------------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: becomes blocked — this creature gets +{PumpAmount}/+{PumpAmount} until end of turn",
            () =>
            {
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, PumpAmount, PumpAmount));
            });

        var becomesBlockedTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<BlockersDeclaredEvent>((e, _) =>
                // CR 509.1h — "becomes blocked": this card is a declared
                // attacker that has at least one declared blocker.
                e.Combat.Attackers.Any(a =>
                    ReferenceEquals(a.Creature, card) && a.Blockers.Count > 0)),
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(becomesBlockedTrigger);
        triggers?.RegisterTriggeredAbility(becomesBlockedTrigger);

        return card;
    }
}
