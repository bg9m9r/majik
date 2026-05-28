using Majik.Core.Abilities;
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
/// Named-card factory for March of Wretched Sorrow (Strixhaven: School of
/// Mages, {X}{B}).
///
/// Instant. Oracle text:
///   "As an additional cost to cast this spell, you may exile any number
///    of black cards from your hand. This spell costs {2} less to cast
///    for each card exiled this way.
///    March of Wretched Sorrow deals X damage to target creature or
///    planeswalker and you gain X life."
///
/// ## Implemented (v1)
///
/// - <b>Instant</b> at <c>{X}{B}</c>, owner/controller wired.
/// - <b>March additional cost (CR 601.2f + CR 117.7c)</b> — surfaced via
///   the reusable <see cref="MarchAdditionalCost"/> primitive. The cost
///   is OPTIONAL (the caster may exile zero black cards). For each black
///   hand card exiled, the cast's generic cost is reduced by {2}, floored
///   at zero. The reduction is applied AFTER X is folded into Generic
///   per <see cref="SpellCastFlow.ComputeAndApplyTotalCost"/>, so an
///   {X=5}{B} cast with 2 black cards exiled reduces 5 → 1 generic (the
///   {B} pip is preserved).
///   Helper: <see cref="BuildAdditionalCost"/> wires
///   the spell + the chosen exile list into a <see cref="MarchAdditionalCost"/>
///   the caller hands to <see cref="SpellCastFlow.CastAsync"/> via the
///   <c>additionalCosts</c> list — same pattern as Convoke/Improvise.
/// - <b>X-keyed damage to creature-or-planeswalker</b> — built via
///   <see cref="BuildSpellDefinition"/>. <see cref="SpellDefinition.HasVariableX"/>
///   = true so the cast flow prompts for X. One 1..1 "target creature or
///   planeswalker" request (same target shape as
///   <see cref="BitterTriumphFactory"/>). Resolution reads
///   <c>ChosenSpellParams.X</c> as the damage amount and uses
///   <see cref="Fx.DealDamageAny"/> so a Planeswalker target loses X
///   loyalty (CR 306.7) and a Creature target takes X damage (CR 119.2).
/// - <b>You gain X life</b> — <see cref="Fx.GainLife"/> on the caster
///   with the same X value (CR 119.4 — life gain is unrelated to the
///   damage being dealt; both resolve in a single resolution step).
///
/// ## Design references
///
/// - X-spell damage shape: <see cref="BonfireOfTheDamnedFactory"/> /
///   <see cref="ChordOfCallingFactory"/> for the
///   <see cref="SpellDefinition.HasVariableX"/> idiom.
/// - Damage to "creature or planeswalker" target: <see cref="BitterTriumphFactory"/>
///   for the candidate-gatherer shape.
/// - Additional-cost-with-reduction: <see cref="ConvokeAdditionalCost"/>
///   / <see cref="ImproviseAdditionalCost"/> wiring through the
///   cast-flow's additional-cost rail.
/// - Hand-exile additional cost: <see cref="MarchAdditionalCost"/>
///   (the new reusable primitive — black/white/red/green/blue cycle).
///
/// ## Sibling cards (cycle)
///
/// March of Wretched Sorrow is one of five "March of …" cards from
/// Strixhaven. The colour-mirror siblings all reuse
/// <see cref="MarchAdditionalCost"/> with a different
/// <see cref="ManaColor"/>:
///   * <i>March of Otherworldly Light</i> — {X}{W} — exile white cards;
///     "Exile target nonland permanent if X is its mana value or less."
///   * <i>March of Burgeoning Life</i> — {X}{G} — exile green cards;
///     "Search your library for a creature card …"
///   * <i>March of Reckless Joy</i> — {X}{R} — exile red cards;
///     "Reveal the top X cards of your library …"
///   * <i>March of Swirling Mist</i> — {X}{U} — exile blue cards;
///     "Phase out any number of target creatures."
/// </summary>
[CardName("March of Wretched Sorrow")]
public static class MarchOfWretchedSorrowFactory
{
    public const string CardName = "March of Wretched Sorrow";
    public const string PrintedManaCost = "{X}{B}";

    /// <summary>The colour of the cards eligible for the March exile —
    /// black for this card. Surfaced for the bot's
    /// <see cref="MarchAdditionalCost.AvailableHandCards"/> probe.</summary>
    public const ManaColor MarchExileColor = ManaColor.Black;

    /// <summary>Construct the runtime card shape. The damage + life-gain
    /// body is built on demand via <see cref="BuildSpellDefinition"/>
    /// because the resolution needs the caller's target resolver.
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
    /// Build the reusable <see cref="MarchAdditionalCost"/> for this
    /// spell with the caller-selected hand cards. Mirrors
    /// <see cref="KappaCannoneerFactory.BuildAdditionalCost"/> for
    /// Improvise. Pass an empty list when the caster declines the
    /// optional cost (the spell still casts at full {X}{B}).
    /// </summary>
    public static MarchAdditionalCost BuildAdditionalCost(
        ICard source, IReadOnlyList<ICard> exiledHandCards)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(exiledHandCards);
        return new MarchAdditionalCost(source, MarchExileColor, exiledHandCards);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when March of
    /// Wretched Sorrow is cast. <see cref="SpellDefinition.HasVariableX"/>
    /// is true so the cast flow prompts for X at cast time; resolution
    /// reads <c>ChosenSpellParams.X</c> as the damage value, deals it
    /// to the chosen creature/planeswalker via
    /// <see cref="Fx.DealDamageAny"/>, then gains X life on the caster
    /// via <see cref="Fx.GainLife"/>.
    /// </summary>
    /// <param name="caster">Spell controller — used as the life-gain
    /// target (CR 119.4).</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game
    /// object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer — every creature + planeswalker on
                    // the battlefield across every player. Mirrors
                    // BitterTriumphFactory's gatherer. The bot's score
                    // function handles the ownership flip so opponent
                    // permanents rank ahead of own permanents for
                    // Removal intent.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                            || c.HasType(CardType.Planeswalker))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var x = chosen.X ?? 0;
                var rawTarget = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? resolver(chosen.Targets[0][0])
                    : null;

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: X={x} damage to target creature/PW + caster gains {x} life.",
                        () =>
                        {
                            if (x <= 0) return;

                            // CR 119.2 + CR 306.7 — primary damage. Both
                            // happen "and" in the printed body — a
                            // single resolution step. CR 117.7 — illegal
                            // target → damage half no-ops, life-gain
                            // still resolves per CR 608.2b (the spell
                            // partially resolves).
                            if (rawTarget != null)
                            {
                                Fx.DealDamageAny(rawTarget, x);
                            }

                            // CR 119.4 — caster gains X life. Pure
                            // life-gain, no damage relationship.
                            Fx.GainLife(caster, x);
                        }),
                };
            });
    }
}
