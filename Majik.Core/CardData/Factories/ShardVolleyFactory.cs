using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shard Volley (Time Spiral, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "As an additional cost to cast this spell, sacrifice a land.
///    Shard Volley deals 3 damage to any target."
///
/// ## Implementation
///
/// Structurally the Instant cousin of <see cref="GoblinGrenadeFactory"/>
/// (additional sacrifice cost + flat damage to any target) and a functional
/// {R} sibling of <see cref="SearingSpearFactory"/> (3 damage to any target),
/// with the sacrifice cost swapped from "a Goblin" to "a land".
///
/// Card shape comes from the embedded JSON (<c>shard-volley.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body (additional
/// cost + target request + damage) lives in <see cref="BuildSpellDefinition"/>
/// because a <see cref="SpellDefinition"/> needs a target resolver supplied
/// by the caller's <see cref="GameContext"/> (not expressible in the
/// data-only JSON schema).
///
/// ## Implemented (v1)
/// - <b>Instant</b> shape, mana cost {R}.
/// - <b>Additional cost (CR 601.2f)</b>:
///   <see cref="SacrificeALandAdditionalCost"/> — the caster sacrifices one
///   land they control (any land, basic or nonbasic — CR 305). The cast
///   flow's pre-check (<see cref="SpellCastFlow"/>) rejects the cast when no
///   land is on the caster's battlefield (CR 601.2g — additional cost that
///   can't be paid → cast is illegal). v1 picks deterministically (first
///   land in battlefield-iteration order); the agent-driven "which land to
///   sacrifice" prompt is deferred (same posture as
///   <see cref="GoblinGrenadeFactory"/>'s sacrifice picker).
/// - <b>Damage clause</b> — single 1..1 "any target"
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>).
///   On resolution the chosen target takes <see cref="Damage"/> (3) damage
///   via <see cref="Fx.DealDamageAny"/> — Player → life loss (CR 120.3),
///   Creature → marked damage (CR 119.3), Planeswalker → loyalty removal
///   (CR 306.7), Battle (CR 115.3). Same dispatch surface Searing Spear /
///   Lightning Strike use.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice target prompt</b>: v1 picks the first eligible land
///   deterministically; a real "choose a land to sacrifice" prompt waits on
///   the same agent-prompt MVP the other sacrifice-picker costs wait on.
/// - <b>Damage prevention / replacement</b>: damage flows through
///   <see cref="Fx.DealDamageAny"/> directly; prevention replacement effects
///   (CR 615) are not wired here — matches Searing Spear / Goblin Grenade.
/// </summary>
[CardName("Shard Volley")]
public static class ShardVolleyFactory
{
    public const string CardName = "Shard Volley";
    public const string Slug = "shard-volley";
    public const string PrintedManaCost = "{R}";

    /// <summary>CR 119 — fixed 3 damage to any target.</summary>
    public const int Damage = 3;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Shard Volley is cast.
    /// Declares the <see cref="SacrificeALandAdditionalCost"/> additional cost
    /// (CR 601.2f) alongside a single 1..1 "any target"
    /// <see cref="TargetRequest"/>; on resolution the chosen target takes
    /// <see cref="Damage"/> (3) damage through <see cref="Fx.DealDamageAny"/>.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline($"{CardName}: {Damage} damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            },
            AdditionalCosts: new IAdditionalCost[]
            {
                new SacrificeALandAdditionalCost(),
            });
    }
}
