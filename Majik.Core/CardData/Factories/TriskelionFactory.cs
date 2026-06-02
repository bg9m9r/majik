using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Triskelion (Antiquities, {6}).
///
/// Triskelion is an Artifact Creature — Construct 1/1.
/// Oracle text:
///   "This creature enters with three +1/+1 counters on it.
///    Remove a +1/+1 counter from this creature: It deals 1 damage to any target."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/triskelion.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Both clauses
/// are JSON-driven:
/// <list type="bullet">
///   <item><b>Enters with three +1/+1 counters</b> — modelled as an
///     <c>etb_self</c> triggered ability whose effect puts three +1/+1
///     counters on Triskelion (<c>put_counter amount:3 target:self</c>).
///     CR 614.1d describes the printed "enters with" clause as a replacement
///     effect; the engine's <c>etb_self</c> trigger reaches the same
///     observable battlefield state (three counters present once it has
///     entered). Routed through <see cref="Services.CountersService.Add"/> so
///     +1/+1 replacements (Hardened Scales / Doubling Season — CR 614) can
///     rewrite the count. Because the count is a fixed literal 3 (not X),
///     this sidesteps the still-deferred ChosenSpellParams.X ETB plumbing
///     called out on <see cref="WalkingBallistaFactory"/>.</item>
///   <item><b>Ping</b> — "Remove a +1/+1 counter from this creature: It deals
///     1 damage to any target." A remove-+1/+1-counter activated ability whose
///     <c>deal_damage</c> effect declares a 1..1 "any target"
///     <see cref="Majik.Core.Players.Agents.TargetRequest"/>; the shared
///     <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline collects
///     the pick and 1 damage is routed via
///     <see cref="Majik.Core.Primitives.Fx.DealDamageAny"/> at resolution
///     (CR 115.3 / 306.7 / 608.2b). Identical in shape to Walking Ballista's
///     ping.</item>
/// </list>
/// </summary>
[CardName("Triskelion")]
public static class TriskelionFactory
{
    /// <summary>Canonical printed name.</summary>
    public const string CardName = "Triskelion";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("triskelion");

    /// <summary>
    /// Construct a Triskelion for the given owner. The returned
    /// <see cref="Creature"/> also carries
    /// <see cref="Cards.Types.CardType.Artifact"/> (multi-type — CR 301.1 /
    /// 302.1), the enters-with-counters ETB trigger, and the ping ability
    /// described in the class xmldoc.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Triskelion with optional replacement-bus wiring. When
    /// <paramref name="replacements"/> is supplied, the JSON-driven
    /// enters-with-three-+1/+1-counters effect is routed through
    /// <see cref="Services.CountersService.Add"/> so +1/+1 replacements
    /// (Hardened Scales / Doubling Season) can rewrite the count (CR 614).
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacements) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner, replacements);
}
