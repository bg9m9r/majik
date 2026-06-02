using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Wardriver (Magic 2012 — Creature — Goblin
/// Warrior {R}{R} 2/2).
///
/// Oracle text (verified against Scryfall):
///   "Battle cry (Whenever this creature attacks, each other attacking
///    creature gets +1/+0 until end of turn.)"
///
/// The base shape (name, Creature — Goblin Warrior, {R}{R}, 2/2) is
/// materialised from the embedded JSON definition
/// (<c>goblin-wardriver.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single printed keyword is
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express the battle-cry per-attacker pump, so it lives in the factory (same
/// posture as <see cref="HeroOfBladeholdFactory"/>, the battle-cry analogue).
///
/// ## Implemented
///
/// ### Battle cry (CR 702.92) — near-vanilla, only keyword on the card.
/// Wired exactly as <see cref="HeroOfBladeholdFactory"/>'s battle cry, minus
/// the token rider Hero also carries:
/// - A <see cref="KeywordAbility"/> marker is attached so
///   <c>ICard.Abilities</c> reflects the printed Battle cry line and Scryfall
///   keyword parsing matches.
/// - A <see cref="Triggers.OnAttackSelf"/> <see cref="TriggeredAbility"/>
///   that, on resolution, registers a <see cref="PumpUntilEndOfTurnEffect"/>
///   of +1/+0 (CR 514.2 cleanup expiry) on every OTHER attacking creature.
///   The "each other attacking creature" set is read from the supplied
///   <paramref name="attackingCreaturesSource"/> closure (the engine doesn't
///   expose a global "currently attacking creatures" view from inside an
///   effect closure — same source-closure shape as Hero). Goblin Wardriver
///   itself is skipped (CR 702.92a — "each OTHER attacker"). Each target's
///   pump is registered on its own <see cref="Creature.ActiveEffects"/>; a
///   creature without a service silently no-ops.
///
/// ## Source closure injection
/// Same shape as <see cref="HeroOfBladeholdFactory"/> — when
/// <paramref name="attackingCreaturesSource"/> is null the battle-cry pump is
/// a no-op (the keyword marker + trigger are still attached so the card shape
/// is correct for dispatcher / structural tests).
/// </summary>
[CardName("Goblin Wardriver")]
public static class GoblinWardriverFactory
{
    public const string CardName = "Goblin Wardriver";
    public const string Slug = "goblin-wardriver";

    /// <summary>
    /// Construct Goblin Wardriver with no live wiring — the battle-cry trigger
    /// is attached but its pump is a no-op (no attackers source). Suitable for
    /// dispatcher / card-shape tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct Goblin Wardriver with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the battle-cry attack trigger is
    /// registered so a <see cref="CreatureAttacksEvent"/> for Goblin Wardriver
    /// lands it on the stack automatically.</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker creature list, called at battle-cry resolution. May be null —
    /// the battle-cry pump is then a no-op.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Creature — Goblin
        // Warrior, {R}{R}, 2/2).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.92 — Battle cry keyword marker so ICard.Abilities reflects the
        // printed line and Scryfall keyword parsing matches. The functional
        // pump is the trigger below.
        card.AddAbility(new KeywordAbility("Battle cry", card, owner));

        // CR 702.92a — "Whenever this creature attacks, each other attacking
        // creature gets +1/+0 until end of turn."
        var battleCryEffect = new Effect(
            $"{CardName}: Battle cry — each other attacking creature +1/+0 EOT",
            () =>
            {
                if (attackingCreaturesSource == null) return;
                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    // "each OTHER attacking creature" (CR 702.92a) — skip self.
                    if (ReferenceEquals(atk, card)) continue;
                    // Each creature computes P/T from its own service; without
                    // one the grant silently no-ops.
                    if (atk.ActiveEffects == null) continue;
                    atk.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(atk, 1, 0));
                }
            });

        var battleCryTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { battleCryEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(battleCryTrigger);
        triggers?.RegisterTriggeredAbility(battleCryTrigger);

        return card;
    }
}
