using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scorching Dragonfire (Adventures in the Forgotten
/// Realms / Wilds of Eldraine, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Scorching Dragonfire deals 3 damage to target creature or planeswalker.
///    If that creature or planeswalker would die this turn, exile it instead."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{R}, red.
/// - Single 1..1 "target creature or planeswalker" request (same target shape
///   as <see cref="MagmaticSinkholeFactory"/>). On resolution deals
///   <see cref="Damage"/> (3) via <see cref="Fx.DealDamageAny(object, int)"/>
///   (CR 119 / CR 306.7 — damage to a planeswalker removes that much loyalty).
///   CR 608.2b — a target that is no longer a creature/planeswalker is a no-op.
/// - <b>Exile rider</b>: when a <see cref="ReplacementBus"/> is supplied AND
///   the chosen target is a <see cref="Creature"/>, register an EOT-expirable
///   <see cref="AngerOfTheGodsExileInsteadReplacement"/> (the shared
///   "damaged-this-way dies → exile" replacement, CR 700.3 / CR 514.2) scoped
///   to the single damaged creature. Null bus → damage only (shape tests).
///   A planeswalker dying loses loyalty rather than taking marked damage and
///   its death-to-exile is not modeled by the engine's death-replacement
///   plumbing, so — matching the established posture of
///   <see cref="PillarOfFlameFactory"/> / <see cref="SpikefieldHazardFactory"/>
///   — only a <see cref="Creature"/> target arms the exile rider.
/// </summary>
[CardName("Scorching Dragonfire")]
public static class ScorchingDragonfireFactory
{
    public const string CardName = "Scorching Dragonfire";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>Damage dealt to the chosen creature or planeswalker.</summary>
    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape (Instant {1}{R}, red).</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature or planeswalker" request; on resolution the chosen
    /// permanent is dealt 3 damage (CR 119 / CR 306.7) via
    /// <see cref="Fx.DealDamageAny(object, int)"/>, and — when a creature target
    /// and a <paramref name="replacements"/> bus is supplied — its lethal
    /// battlefield→graveyard move is rewritten to exile until end of turn
    /// (CR 700.3 / CR 514.2).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object). Pass
    /// <c>o =&gt; o</c> for tests that hand permanents directly.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> the
    /// exile rider registers onto; null → damage only.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature or planeswalker",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal,
                    // Agent-prompt: every creature + planeswalker on the
                    // battlefield across all players (CR 302 / CR 306).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                                 || c.HasType(CardType.Planeswalker))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: {Damage} damage to target creature or planeswalker; if that creature would die this turn, exile it instead.",
                        () =>
                        {
                            // CR 608.2b — only creatures and planeswalkers are
                            // legal targets; anything else (target left the
                            // battlefield / changed type) is a no-op.
                            if (target is not (Creature or Planeswalker)) return;

                            // CR 119 / CR 306.7 — deal 3 damage; a planeswalker
                            // target loses that much loyalty via DealDamageAny.
                            Fx.DealDamageAny(target, Damage);

                            // CR 700.3 / CR 514.2 — exile-instead rider, scoped
                            // to the single damaged creature. A planeswalker
                            // loses loyalty (not marked damage) and its
                            // death-to-exile is not modeled by the death
                            // replacement plumbing, so only a Creature target
                            // arms the rider — same posture as Pillar of Flame /
                            // Spikefield Hazard.
                            if (replacements != null && target is Creature creature)
                            {
                                replacements.Register<ZoneMoveIntent>(
                                    new AngerOfTheGodsExileInsteadReplacement(
                                        new HashSet<Creature> { creature }));
                            }
                        }),
                };
            });
    }
}
