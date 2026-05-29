using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Roast (Dragons of Tarkir, {1}{R}).
///
/// Sorcery. Oracle text:
///   "Roast deals 5 damage to target creature without flying."
///
/// ## Implementation
///
/// - <b>Sorcery</b> shape, mana cost {1}{R}. The card shape (name, type,
///   cost) is data-driven: loaded from
///   <c>Majik.Core/CardData/Cards/roast.json</c> via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built by
///   <see cref="CardDefinitionFactory"/> (mirrors
///   <see cref="DredgersInsightFactory"/>). The targeted-damage resolution
///   is supplied in code via <see cref="BuildSpellDefinition"/> because the
///   JSON ability schema does not yet model a sorcery's targeted spell
///   effect (same posture as <see cref="FlameSlashFactory"/>).
/// - Single 1..1 "target creature without flying" request. CR 115.4 — the
///   "without flying" clause is a targeting restriction, so the candidate
///   gatherer excludes any creature with flying
///   (<see cref="CombatAbilities.HasFlying"/>); fliers are never offered as
///   legal targets.
/// - On resolution deals <see cref="Damage"/> (5) damage to the chosen
///   creature via <see cref="Fx.DealDamageAny"/> (CR 119.2).
/// - CR 608.2b — illegal-target re-check at resolution: if the resolved
///   object is not a creature, or is a creature that has flying (e.g. it
///   gained flying after being targeted), the effect is a no-op rather than
///   redirecting damage.
///
/// ## References
/// - <see cref="FlameSlashFactory"/> — same {target creature} damage-spell
///   shape ("Flame Slash deals 4 damage to target creature").
/// - <see cref="CombatAbilities.HasFlying"/> — flying keyword query reused
///   from the combat subsystem.
/// </summary>
[CardName("Roast")]
public static class RoastFactory
{
    public const string CardName = "Roast";
    public const int Damage = 5;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("roast");

    /// <summary>CardDef DSL — card shape only (Sorcery, {1}{R}). The damage
    /// body is supplied at cast time via <see cref="BuildSpellDefinition"/>
    /// (the runtime needs the caller's target resolver from the
    /// <see cref="GameContext"/>).</summary>
    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefinitionFactory.Build(Definition, owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Roast is cast.
    /// Single 1..1 "target creature without flying" request; on resolution
    /// deals <see cref="Damage"/> (5) damage to the chosen creature through
    /// <see cref="Fx.DealDamageAny"/>.
    ///
    /// CR 115.4 — the candidate gatherer offers only creatures that do NOT
    /// have flying. CR 608.2b — if the resolved object is not a creature, or
    /// is a creature with flying (illegal target due to a type/keyword change
    /// after targeting), the effect is silently skipped.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game
    /// object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature without flying",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CR 115.4 — only creatures WITHOUT flying are legal targets.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => !CombatAbilities.HasFlying(c))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Roast: 5 damage to target creature without flying", () =>
                    {
                        // CR 608.2b — only creatures without flying are legal
                        // targets; re-check at resolution.
                        if (target is not Creature creature) return;
                        if (CombatAbilities.HasFlying(creature)) return;
                        Fx.DealDamageAny(creature, Damage);
                    }),
                };
            });
    }
}
