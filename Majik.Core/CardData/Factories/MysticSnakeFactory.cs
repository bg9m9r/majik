using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mystic Snake (Onslaught, {1}{G}{U}{U}).
///
/// Creature — Snake 2/2. Oracle text (verified against Scryfall):
///   "Flash
///    When this creature enters, counter target spell."
///
/// Now a thin declarative shell that loads
/// <c>Majik.Core/CardData/Cards/mystic-snake.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same
/// posture as <see cref="AetherAdeptFactory"/> / Karakas. The whole card is
/// declarative JSON: a <c>Flash</c> keyword (CR 702.8) plus an <c>etb_self</c>
/// trigger (CR 603.6a) carrying the <c>counter_target_spell</c> verb (CR 701.5)
/// with no type rider — Mystic Snake counters ANY spell.
///
/// <para>
/// This card previously hand-rolled the ETB triggered ability + a bespoke
/// raw-stack-removal counter because the declarative JSON schema only carried
/// <c>counter_target_spell</c> on the SPELL path (PR #2468). Wiring the verb
/// through the generic ability-/trigger-path materializer
/// (<see cref="CardDefAbilityEffects.Materialize"/>, via
/// <see cref="CounterTargetSpellEffectDef"/>'s <c>ToTargetRequest</c> /
/// <c>ToResolveEffect</c>) lets the verb run on a TRIGGERED ability, so the
/// factory collapses to the standard JSON-loading shell. The counter shares the
/// <see cref="Majik.Core.Primitives.Fx.Counter"/> primitive with the spell path
/// (CR 701.5b uncounterable veto + CR 608.2b resolution-time re-check), reaching
/// the live stack off <c>ResolutionContext.Game</c> the resolver threads in.
/// </para>
/// </summary>
[CardName("Mystic Snake")]
public static class MysticSnakeFactory
{
    public const string CardName = "Mystic Snake";
    public const string PrintedManaCost = "{1}{G}{U}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("mystic-snake");

    /// <summary>
    /// Construct Mystic Snake owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
