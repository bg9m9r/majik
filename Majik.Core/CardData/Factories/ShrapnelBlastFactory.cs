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
/// Named-card factory for Shrapnel Blast (Fifth Dawn / Darksteel, {1}{R}).
///
/// Instant. Oracle text:
///   "As an additional cost to cast this spell, sacrifice an artifact.
///    Shrapnel Blast deals 5 damage to any target."
///
/// ## Why it gets its own factory
/// Shrapnel Blast is the artifact-deck reach / finisher analogue of
/// <see cref="GoblinGrenadeFactory"/> — 5 damage for {1}{R} on an Instant,
/// gated only by "sacrifice an artifact". Same affinity-for-artifacts
/// burn role the suggested analogue <see cref="GalvanicBlastFactory"/>
/// fills (any-target damage tied to the artifact substrate), but instead
/// of Galvanic Blast's Metalcraft <em>upgrade</em>, Shrapnel Blast pairs a
/// flat 5 damage with a <em>mandatory</em> sacrifice additional cost.
///
/// The shape is structurally identical to Goblin Grenade (additional cost
/// + 1..1 "any target" + flat 5 damage). It only swaps the subtype-
/// restricted "sacrifice a Goblin" cost for the existing
/// <see cref="SacrificeAnArtifactAdditionalCost"/> primitive (the same
/// cost Voltage Surge's optional rider builds), and the card type from
/// Sorcery to Instant.
///
/// ## Implemented (v1)
/// - <b>Instant</b> shape, mana cost {1}{R}.
/// - <b>Additional cost (CR 601.2f)</b>:
///   <see cref="SacrificeAnArtifactAdditionalCost"/> — the caster
///   sacrifices one artifact they control. The cast flow's pre-check
///   (<see cref="Services.SpellCastFlow"/>) rejects the cast when no
///   artifact is on the caster's battlefield (CR 601.2g — additional cost
///   that can't be paid → cast is illegal). v1 picks deterministically
///   (first artifact in battlefield-iteration order); the agent-driven
///   "which artifact to sacrifice" prompt is deferred (same posture as
///   <see cref="GoblinGrenadeFactory"/> / <see cref="BoneShardsFactory"/>).
/// - <b>Damage clause</b> — single 1..1 "any target"
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>).
///   On resolution the chosen target takes <see cref="Damage"/> (5) damage
///   via <see cref="Fx.DealDamageAny"/> — Player → life loss (CR 120.3),
///   Creature → marked damage (CR 119.3), Planeswalker → loyalty removal
///   (CR 306.7). Same dispatch surface Goblin Grenade / Shock /
///   Lightning Bolt use.
///
/// ## Sacrifice timing vs target legality
/// CR 601.2f orders payment of additional costs at announcement (before
/// resolution). The sacrificed artifact is therefore in the graveyard by
/// the time the 5-damage clause resolves — the sacrificed artifact is the
/// "fuel", the chosen target is the "ordnance recipient".
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice target prompt</b>: v1 picks the first eligible artifact
///   deterministically — same agent-prompt MVP queue as Goblin Grenade /
///   Bone Shards.
/// - <b>Damage prevention / replacement</b>: damage flows through
///   <see cref="Fx.DealDamageAny"/> directly; prevention replacement
///   effects (CR 615) are not wired here — matches Goblin Grenade / Shock
///   posture.
/// </summary>
[CardName("Shrapnel Blast")]
public static class ShrapnelBlastFactory
{
    public const string CardName = "Shrapnel Blast";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 5;

    /// <summary>
    /// Build a Shrapnel Blast instant owned by <paramref name="owner"/>.
    /// Card shape only — the additional-cost + target-request + damage
    /// body are supplied at cast time via <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Shrapnel Blast is
    /// cast. Declares the <see cref="SacrificeAnArtifactAdditionalCost"/>
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
                new SacrificeAnArtifactAdditionalCost(),
            });
    }
}
