using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Torch the Tower (Wilds of Eldraine, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Bargain (You may sacrifice an artifact, enchantment, or token as you
///    cast this spell.)
///    Torch the Tower deals 2 damage to target creature or planeswalker. If
///    this spell was bargained, instead it deals 3 damage to that permanent
///    and you scry 1.
///    If a permanent dealt damage by Torch the Tower would die this turn,
///    exile it instead."
///
/// ## Why a named factory (no template covers it)
/// Torch the Tower pairs three mechanics no single template binds together:
/// the <b>Bargain</b> optional additional cost (CR 702.169), a
/// bargain-conditional damage amount + scry rider, and the shared
/// "damaged-this-way dies → exile" replacement. It is the
/// bargain-sentinel analogue of <see cref="RoilEruptionFactory"/>'s kicker
/// branch (read <see cref="Card.WasBargained"/> at resolution to pick the
/// damage amount) grafted onto <see cref="ScorchingDragonfireFactory"/>'s
/// "target creature or planeswalker, exile-if-dies" body.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {R}, red. Card shape comes from the embedded
///   JSON (<c>torch-the-tower.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Bargain</b> (CR 702.169) — a real optional <see cref="IAdditionalCost"/>
///   primitive, <see cref="BargainAdditionalCost"/>. The factory exposes
///   <see cref="BuildAdditionalCost"/> so a caller (tests / bot) that has
///   decided to bargain can layer the cost onto the cast via
///   <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
///   <c>additionalCosts</c> parameter. The resolve body reads
///   <see cref="Card.WasBargained"/> off the cast card instance (CR 702.169b
///   — "if this spell was bargained" is checked at resolution; the cast-time
///   payment stamps the sentinel and <see cref="Majik.Core.Game.SpellCastFlow"/>
///   appends a cleanup effect that clears it after resolution, CR 400.7).
/// - <b>Bargain-conditional damage + scry</b> — un-bargained deals
///   <see cref="BaseDamage"/> (2); bargained deals <see cref="BargainedDamage"/>
///   (3) and the caster scrys <see cref="ScryAmount"/> (1) afterward
///   (CR 608.2e left-to-right; CR 701.20 scry). Damage routes through
///   <see cref="Fx.DealDamageAny(object, int)"/> (CR 119 / CR 306.7 — a
///   planeswalker target loses that much loyalty).
/// - <b>Exile rider</b> — when a <see cref="ReplacementBus"/> is supplied AND
///   the chosen target is a <see cref="Creature"/>, register an
///   EOT-expirable <see cref="AngerOfTheGodsExileInsteadReplacement"/> (the
///   shared "damaged-this-way dies → exile" replacement, CR 700.3 / CR 514.2)
///   scoped to the single damaged creature. A planeswalker dying loses
///   loyalty rather than marked damage and its death-to-exile is not modeled
///   by the death-replacement plumbing, so — matching the established posture
///   of <see cref="ScorchingDragonfireFactory"/> / <see cref="PillarOfFlameFactory"/>
///   — only a <see cref="Creature"/> target arms the rider. Null bus →
///   damage (+ scry) only (shape tests).
/// </summary>
[CardName("Torch the Tower")]
public static class TorchTheTowerFactory
{
    public const string CardName = "Torch the Tower";
    public const string Slug = "torch-the-tower";
    public const string PrintedManaCost = "{R}";

    /// <summary>Damage dealt when the spell was NOT bargained.</summary>
    public const int BaseDamage = 2;

    /// <summary>Damage dealt when the spell WAS bargained (CR 702.169b).</summary>
    public const int BargainedDamage = 3;

    /// <summary>Scry performed by the caster when the spell was bargained.</summary>
    public const int ScryAmount = 1;

    /// <summary>Construct Torch the Tower from its embedded JSON shape.</summary>
    public static Cards.Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Cards.Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Construct Torch the Tower's bargain <see cref="IAdditionalCost"/> for
    /// the supplied <paramref name="card"/> instance. Convenience builder for
    /// callers (tests, bot decision layer) that have already decided to
    /// bargain; layer the returned cost onto the cast via
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter. On payment it sacrifices an
    /// artifact / enchantment / token (CR 702.169a) and stamps
    /// <see cref="Card.WasBargained"/> so the resolve body's bargained branch
    /// fires (CR 702.169b).
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new BargainAdditionalCost(card);
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature or planeswalker" request. On resolution the chosen
    /// permanent is dealt <see cref="BaseDamage"/> (2) — or
    /// <see cref="BargainedDamage"/> (3) plus a caster scry of
    /// <see cref="ScryAmount"/> (1) when <paramref name="card"/>'s
    /// <see cref="Card.WasBargained"/> sentinel is set (CR 702.169b). When a
    /// creature target and a <paramref name="replacements"/> bus are supplied,
    /// the creature's lethal battlefield→graveyard move is rewritten to exile
    /// until end of turn (CR 700.3 / CR 514.2).
    /// </summary>
    /// <param name="card">The cast card instance — the resolve body reads
    /// <see cref="Card.WasBargained"/> off this same reference so the bargained
    /// branch fires only when the cast actually paid the rider (CR 702.169b).
    /// The caster (for scry) is read from the card's controller.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object). Pass
    /// <c>o =&gt; o</c> for tests that hand permanents directly.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> the
    /// exile rider registers onto; null → damage (+ scry) only.</param>
    public static SpellDefinition BuildSpellDefinition(
        ICard card,
        Func<object, object> resolver,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(card);
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

                // CR 702.169b — branch on the cast-time bargain stamp.
                // Card.WasBargained is set by BargainAdditionalCost.Pay during
                // SpellCastFlow's additional-cost loop and cleared by the
                // post-resolve cleanup effect the cast flow appends (CR 400.7).
                bool wasBargained = card is Card concrete && concrete.WasBargained;
                int amount = wasBargained ? BargainedDamage : BaseDamage;
                var caster = card.Controller;

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: {amount} damage to target creature or planeswalker"
                            + (wasBargained ? $", then scry {ScryAmount}" : string.Empty)
                            + "; if that creature would die this turn, exile it instead.",
                        async ctx =>
                        {
                            // CR 608.2b — only creatures and planeswalkers are
                            // legal targets; anything else (target left the
                            // battlefield / changed type) is a no-op.
                            if (target is not (Creature or Planeswalker)) return;

                            // CR 119 / CR 306.7 — deal the (bargain-conditional)
                            // damage; a planeswalker target loses that much
                            // loyalty via DealDamageAny.
                            Fx.DealDamageAny(target, amount);

                            // CR 700.3 / CR 514.2 — exile-instead rider, scoped
                            // to the single damaged creature. A planeswalker
                            // loses loyalty (not marked damage) and its
                            // death-to-exile is not modeled by the death
                            // replacement plumbing, so only a Creature target
                            // arms the rider — same posture as Scorching
                            // Dragonfire / Pillar of Flame.
                            if (replacements != null && target is Creature creature)
                            {
                                replacements.Register<ZoneMoveIntent>(
                                    new AngerOfTheGodsExileInsteadReplacement(
                                        new HashSet<Creature> { creature }));
                            }

                            // CR 608.2e / CR 701.20 — "and you scry 1" only when
                            // the spell was bargained. Resolves after the damage
                            // clause (left-to-right).
                            if (!wasBargained || caster == null) return;

                            var peeked = ScryAction.Peek(caster, ScryAmount);
                            if (peeked.Count == 0) return; // empty library — clean no-op.

                            var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                            ScryAction.ScryDecision decision;
                            if (agent != null)
                            {
                                decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked)
                                    .ConfigureAwait(false);
                            }
                            else
                            {
                                // Pre-agent default: send all peeked cards to the
                                // bottom (matching the Magma Jet / Serum Visions
                                // fallback posture).
                                decision = new ScryAction.ScryDecision(
                                    ToBottom: peeked.ToList(),
                                    TopOrder: Array.Empty<ICard>());
                            }

                            ScryAction.Apply(caster, peeked.Count, decision);
                        }),
                };
            });
    }
}
