using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Grenade (Fallen Empires, {R}).
///
/// Sorcery. Oracle text:
///   "As an additional cost to cast this spell, sacrifice a Goblin.
///    Goblin Grenade deals 5 damage to any target."
///
/// ## Why it gets its own factory
/// Goblin Grenade is the Modern Goblin / 8-Whack reach card par excellence
/// — 5 damage for {R} crammed onto a Sorcery, gated only by "sacrifice a
/// Goblin". Pairs with cheap Goblin generators (Goblin Bushwhacker,
/// Mogg War Marshal, Krenko's Command tokens, Munitions Expert ETB) to
/// turn one of N redundant 1/1s into the burn finisher that closes out
/// a game from 5+. Same role as Lightning Bolt for a tribal deck, but
/// at +2 damage in exchange for the sacrifice requirement.
///
/// The shape is structurally a cousin of <see cref="BoneShardsFactory"/>
/// (additional cost + 1..1 target) and <see cref="LavaSpikeFactory"/>
/// (Sorcery {R} → fixed damage) — Goblin Grenade just swaps the cost
/// for a subtype-restricted sacrifice (<see cref="SacrificeAGoblinAdditionalCost"/>)
/// and the target widens to "any target" (CR 115.3 — creature, player,
/// planeswalker, or battle).
///
/// ## Implemented (v1)
/// - <b>Sorcery</b> shape, mana cost {R}.
/// - <b>Additional cost (CR 601.2f)</b>:
///   <see cref="SacrificeAGoblinAdditionalCost"/> — the caster sacrifices
///   one Goblin creature they control. The cast flow's pre-check
///   (<see cref="SpellCastFlow"/>) rejects the cast when no eligible
///   Goblin is on the caster's battlefield (CR 601.2g — additional cost
///   that can't be paid → cast is illegal). v1 picks deterministically
///   (first Goblin in battlefield-iteration order); the agent-driven
///   "which Goblin to sacrifice" prompt is deferred (same posture as
///   <see cref="BoneShardsFactory"/>'s sacrifice picker and
///   <see cref="SkirkProspectorFactory"/>'s mana-ability picker).
/// - <b>Damage clause</b> — single 1..1 "any target"
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>
///   for opponent-life-pressure ranking). On resolution the chosen target
///   takes <see cref="Damage"/> (5) damage via <see cref="Fx.DealDamageAny"/>
///   — Player → life loss (CR 120.3), Creature → marked damage
///   (CR 119.3), Planeswalker → loyalty removal (CR 306.7). Same dispatch
///   surface Shock / Lightning Bolt / Burst Lightning use.
///
/// ## Sacrifice timing vs target legality
/// CR 601.2f orders payment of additional costs at announcement (before
/// resolution). The sacrificed Goblin is therefore in the graveyard by
/// the time the 5-damage clause resolves — so a caster cannot target
/// their own about-to-be-sacrificed Goblin (it has no zone-of-origin
/// legality issue on the stack since it's no longer a creature on the
/// battlefield by resolution). This is the printed behaviour: the
/// sacrificed Goblin is the "fuel", the chosen target is the "ordnance
/// recipient".
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice target prompt</b>: v1 picks the first eligible Goblin
///   deterministically. A real "choose a Goblin to sacrifice" prompt is
///   queued behind the same agent-prompt MVP that Bone Shards' picker
///   waits on.
/// - <b>Damage prevention / replacement</b>: damage flows through
///   <see cref="Fx.DealDamageAny"/> directly; prevention replacement
///   effects (CR 615) are not wired here — matches Shock / Lava Spike
///   posture.
/// </summary>
[CardName("Goblin Grenade")]
public static class GoblinGrenadeFactory
{
    public const string CardName = "Goblin Grenade";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 5;

    /// <summary>
    /// Build a Goblin Grenade sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the additional-cost + target-request + damage
    /// body are supplied at cast time via <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Goblin Grenade is
    /// cast. Declares the <see cref="SacrificeAGoblinAdditionalCost"/>
    /// additional cost (CR 601.2f) alongside a single 1..1 "any target"
    /// <see cref="TargetRequest"/>; on resolution the chosen target takes
    /// <see cref="Damage"/> (5) damage through <see cref="Fx.DealDamageAny"/>.
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
                new SacrificeAGoblinAdditionalCost(),
            });
    }
}
