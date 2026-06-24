using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aetherize (Gatecrash, {3}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Return all attacking creatures to their owner's hand."
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {3}{U}, Blue. The base shape
///   (name / Instant type / {3}{U} cost) is materialised from the embedded
///   JSON definition (<c>aetherize.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="CyclonicRiftFactory"/> (the JSON <c>SpellDefinition</c>
///   schema does not yet express a "return all attacking creatures" sweep,
///   so the resolve behaviour is layered on here via
///   <see cref="BuildSpellDefinition"/>).
/// - <b>Return all attacking creatures (no target — CR 506.2 / CR 701.20)</b>
///   — <see cref="BuildSpellDefinition"/> returns a <see cref="SpellDefinition"/>
///   with NO <see cref="TargetRequest"/>s (the oracle text has no "target"
///   word — every attacking creature in the current combat is affected). On
///   resolution it snapshots every creature the live combat reports as
///   attacking (CR 506.2 — "attacking creature" = a creature declared as an
///   attacker this combat that hasn't been removed from combat), re-checks
///   each is still a <see cref="Creature"/> on the battlefield (CR 608.2b),
///   and returns each to its OWNER's hand (CR 701.20).
///
/// ## How "attacking creatures" are read
///
/// The attacking-creature snapshot is supplied by the caller via
/// <paramref name="attackerLookup"/>, exactly as <see cref="CondemnFactory"/>
/// / <see cref="SettleTheWreckageFactory"/> inject combat state:
///   <list type="bullet">
///     <item>Production callers (the <c>AetherizeTemplate</c> binder bridge)
///       wire the default delegate, which reads the live per-game
///       <see cref="CombatMembershipRegistryProvider.Current"/> — the same
///       "who is attacking right now" surface <see cref="WildwoodMentorFactory"/>
///       consults mid-combat.</item>
///     <item>Test callers inject the list directly — no global registry to
///       mock.</item>
///     <item>A null lookup (or one returning null / empty) means no attackers
///       — the spell still resolves (CR 608.2 — full resolution) but moves
///       nothing.</item>
///   </list>
///
/// ## CR notes
/// - CR 506.2 — attacking creatures are those declared as attackers this
///   combat that haven't been removed from combat.
/// - CR 701.20 — return to owner's hand. Aetherize is NOT a destroy effect,
///   so indestructible / regeneration shields are irrelevant (a bounce moves
///   the permanent to hand regardless).
/// - CR 608.2b — resolution-time re-check (still a Creature on the
///   battlefield before the move).
/// </summary>
[CardName("Aetherize")]
public static class AetherizeFactory
{
    public const string CardName = "Aetherize";
    public const string PrintedManaCost = "{3}{U}";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "aetherize";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {3}{U}) from the
    /// embedded JSON definition. Resolve behaviour (return all attacking
    /// creatures to their owner's hand) is built on demand via
    /// <see cref="BuildSpellDefinition"/>, mirroring
    /// <see cref="CyclonicRiftFactory"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the Aetherize <see cref="SpellDefinition"/> — no targets; on
    /// resolution every attacking creature is returned to its owner's hand
    /// (CR 506.2 / CR 701.20).
    /// </summary>
    /// <param name="attackerLookup">Returns the creatures attacking in the
    /// current combat. Defaults to reading the live per-game
    /// <see cref="CombatMembershipRegistryProvider.Current"/> (the production
    /// path); tests inject a list directly. Returning null / empty means no
    /// attackers — the spell resolves but moves nothing.</param>
    /// <param name="zoneService">Optional ZoneService for replacement-bus-aware
    /// zone moves. When null, raw zone manipulation is used (mirrors
    /// <see cref="CyclonicRiftFactory"/>).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<IReadOnlyList<Creature>>? attackerLookup = null,
        ZoneService? zoneService = null)
    {
        var lookup = attackerLookup ?? DefaultAttackerLookup;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect(
                    $"{CardName}: return all attacking creatures to their owner's hand.",
                    () =>
                    {
                        // CR 506.2 — snapshot the attacking creatures BEFORE
                        // any move so same-step zone changes don't disturb
                        // enumeration. Defensive filters per CR 608.2b: only
                        // creatures still on the battlefield at resolution.
                        var attackers = (lookup() ?? Array.Empty<Creature>())
                            .Where(c => c != null)
                            .Where(c => c.Zone == ZoneType.Battlefield)
                            .Distinct()
                            .ToList();

                        foreach (var creature in attackers)
                        {
                            // CR 701.20 — return to OWNER's hand.
                            ReturnToOwnersHand(creature, zoneService);
                        }
                    }),
            });
    }

    /// <summary>
    /// Production attacker lookup: every creature the live per-game
    /// combat-membership registry reports as attacking right now (CR 506.2).
    /// Mirrors <see cref="WildwoodMentorFactory"/>'s read of the same surface.
    /// </summary>
    private static IReadOnlyList<Creature> DefaultAttackerLookup()
    {
        var registry = CombatMembershipRegistryProvider.Current;
        return registry.AttackingOrBlocking()
            .OfType<Creature>()
            .Where(registry.IsAttacking)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// CR 701.20 — return a single permanent to its owner's hand. When a
    /// <see cref="ZoneService"/> is supplied the move is routed through it so
    /// replacement effects / zone-change events fire; otherwise raw zone
    /// manipulation is used (same posture as <see cref="CyclonicRiftFactory"/>).
    /// </summary>
    private static void ReturnToOwnersHand(Creature creature, ZoneService? zoneService)
    {
        var owner = creature.Owner;
        if (owner == null) return;

        var controller = creature.Controller ?? owner;

        if (zoneService != null)
        {
            zoneService.MoveCard(creature, ZoneType.Battlefield, ZoneType.Hand);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(creature);
            owner.Zones.Hand.AddCard(creature);
            creature.SetZone(ZoneType.Hand);
            creature.SetController(owner);
        }
    }
}
