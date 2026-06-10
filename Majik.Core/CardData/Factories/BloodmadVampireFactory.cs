using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodmad Vampire (Innistrad, {2}{R}).
///
/// Creature — Vampire Berserker 4/1. Oracle text (Scryfall, verified):
///   "Whenever this creature deals combat damage to a player, put a +1/+1
///    counter on it.
///    Madness {1}{R}"
///
/// ## Shape source
/// Card identity (name, {2}{R}, 4/1, Creature — Vampire Berserker) is loaded
/// from <c>Majik.Core/CardData/Cards/bloodmad-vampire.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The combat-damage trigger is attached
/// in code below.
///
/// ## Madness (NOT wired here — intrinsic)
/// Madness {1}{R} works intrinsically for every catalogued card (CR 702.35)
/// via <see cref="Majik.Core.Keywords.MadnessCatalog"/> consulted by the
/// central discard funnel <see cref="Majik.Core.Primitives.Fx.DiscardCard"/>;
/// "Bloodmad Vampire" is catalogued at {1}{R}, so the madness line needs no
/// factory code.
///
/// ## Implemented (v1)
///
/// - <b>4/1 Creature — Vampire Berserker at {2}{R}.</b>
///
/// - <b>Combat-damage-to-a-player trigger (CR 510 / CR 603.1).</b>
///   "Whenever this creature deals combat damage to a player, put a +1/+1
///   counter on it." Fires on a <see cref="CombatDamageDealtEvent"/> whose
///   <see cref="DamageDealtEvent.SourceCard"/> is this card AND whose
///   <see cref="DamageDealtEvent.TargetPlayer"/> is non-null (combat damage to
///   a creature does NOT fire — mirrors <see cref="PsychicFrogFactory"/>). On
///   resolution one <see cref="CounterType.PlusOnePlusOne"/> counter is placed
///   on this creature via <see cref="CountersService.Add"/> (CR 122.1 — routed
///   through the optional <see cref="ReplacementBus"/> so Hardened Scales /
///   Doubling Season can rewrite the count per CR 614).
///
/// ## Lifecycle
///
/// - <see cref="Create(Player)"/> — shape only. The trigger ability is
///   attached for shape / dispatch tests but not registered with a
///   <see cref="TriggerManager"/>; callers may invoke the effect directly in
///   tests via <c>trigger.Effects[i].Execute()</c>.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?)"/> — when a
///   <see cref="TriggerManager"/> is supplied the combat-damage trigger is
///   registered so a <see cref="CombatDamageDealtEvent"/> from this card to a
///   player automatically queues the ability; the counter placement routes
///   through the optional <see cref="ReplacementBus"/>.
/// </summary>
[CardName("Bloodmad Vampire")]
public static class BloodmadVampireFactory
{
    public const string CardName = "Bloodmad Vampire";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("bloodmad-vampire");

    /// <summary>+1/+1 counters placed per combat-damage trigger (CR 122.1).</summary>
    public const int CountersPerHit = 1;

    /// <summary>
    /// Construct Bloodmad Vampire with no live <see cref="TriggerManager"/>
    /// wiring. The combat-damage trigger is attached for shape; it is NOT
    /// registered. Suitable for factory-shape / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Bloodmad Vampire. When <paramref name="triggers"/> is supplied
    /// the combat-damage trigger is registered so a
    /// <see cref="CombatDamageDealtEvent"/> from this card to a player
    /// automatically queues the ability. When <paramref name="replacements"/>
    /// is supplied the +1/+1 counter placement routes through it so Hardened
    /// Scales / Doubling Season can rewrite the count (CR 614).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510, CR 603.1.
        //   "Whenever this creature deals combat damage to a player, put a
        //    +1/+1 counter on it."
        // Fires only when this card deals combat damage to a PLAYER
        // (TargetPlayer != null); damage to a creature does not match
        // (mirrors PsychicFrogFactory).
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on it (dealt combat damage to a player)",
            () => CountersService.Add(
                card, CounterType.PlusOnePlusOne, CountersPerHit, replacements));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
                ReferenceEquals(e.SourceCard, card) && e.TargetPlayer != null),
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
