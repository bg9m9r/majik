using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shrapnel Blast (Mirrodin, {1}{R}).
///
/// Instant. Oracle text:
///   "As an additional cost to cast this spell, sacrifice an artifact.
///    Shrapnel Blast deals 5 damage to any target."
///
/// ## Why a named factory
/// Shrapnel Blast is the canonical Modern Affinity finisher — a two-mana
/// "Lightning Helix to the face" burn whose body is gated behind a
/// mandatory sacrifice-an-artifact additional cost (CR 601.2f). Unlike
/// <see cref="VoltageSurgeFactory"/> (optional sac for upgraded
/// damage) or <see cref="GalvanicBlastFactory"/> (state-read
/// Metalcraft branch), Shrapnel Blast's sacrifice is *required* — the
/// cast flow refuses the cast when the caster controls no artifact
/// (CR 601.2g — additional cost that can't be paid → cast is
/// illegal). The card pays for itself with cheap-fodder (Mishra's
/// Bauble, Chromatic Star, Ornithopter, a token from Thopter Foundry,
/// etc.) and racks up the 5 damage that closes Affinity games.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{R}.
/// - Mandatory additional cost (CR 601.2f):
///   <see cref="SacrificeAnArtifactAdditionalCost"/> stamped in
///   <see cref="SpellDefinition.AdditionalCosts"/> — the cast flow
///   pre-check (<see cref="SpellCastFlow"/>) rejects the cast when the
///   caster controls no artifact, matching the
///   <see cref="TrashForTreasureFactory"/> posture for mandatory
///   artifact-sac additional costs.
/// - <b>Damage</b>: single 1..1 "any target"
///   <see cref="TargetRequest"/> (Intent:
///   <see cref="BotIntent.Removal"/>) — same shape as
///   <see cref="GalvanicBlastFactory"/> / Lightning Bolt. On
///   resolution deals <see cref="Damage"/> (5) through
///   <see cref="Fx.DealDamageAny"/> so Planeswalker targets convert
///   to loyalty removal (CR 306.7).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice target prompt</b>: the
///   <see cref="SacrificeAnArtifactAdditionalCost"/> picker chooses
///   the first artifact on the caster's battlefield deterministically.
///   Real agent-driven sacrifice prompting awaits the
///   ITarget / TargetResolver pipeline (same v1 posture as
///   <see cref="TrashForTreasureFactory"/> / <see cref="VoltageSurgeFactory"/>).
/// </summary>
[CardName("Shrapnel Blast")]
public static class ShrapnelBlastFactory
{
    public const string CardName = "Shrapnel Blast";
    public const string PrintedManaCost = "{1}{R}";

    public const int Damage = 5;

    /// <summary>CardDef DSL — card shape only. The damage body is built
    /// via <see cref="BuildSpellDefinition"/> so the caller can wire its
    /// own target resolver.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Shrapnel Blast
    /// is cast. Declares the mandatory sacrifice-an-artifact additional
    /// cost (CR 601.2f) plus a single 1..1 "any target" request
    /// (Intent: <see cref="BotIntent.Removal"/>); on resolution deals
    /// <see cref="Damage"/> (5) through <see cref="Fx.DealDamageAny"/>.
    /// </summary>
    /// <param name="caster">Spell controller — sourced for the cast
    /// flow's additional-cost legality check (CR 601.2g).</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "any target", 1, 1, Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: deal {Damage} damage to any target",
                        () => Fx.DealDamageAny(target, Damage)),
                };
            },
            AdditionalCosts: new IAdditionalCost[]
            {
                new SacrificeAnArtifactAdditionalCost(),
            });
    }
}
