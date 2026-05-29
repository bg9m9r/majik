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
/// Named-card factory for Demand Answers (Murders at Karlov Manor, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "As an additional cost to cast this spell, sacrifice an artifact or
///    discard a card.
///    Draw two cards."
///
/// ## Why it gets its own factory
/// Demand Answers is the artifact-friendly cantrip-plus — a two-mana
/// instant that pitches an artifact OR a card to draw two. It combines the
/// disjunctive "sacrifice an artifact or discard a card" additional cost
/// (CR 601.2f) with the plain "draw two cards" resolve of
/// <see cref="QuickStudyFactory"/> / <see cref="BigScoreFactory"/>. Both
/// primitives already ship: the additional-cost disjunction mirrors
/// <see cref="SacrificeCreatureOrDiscardCardAdditionalCost"/> (Bone Shards),
/// re-pointed at artifacts via
/// <see cref="SacrificeAnArtifactOrDiscardCardAdditionalCost"/>, and the
/// draw uses <see cref="Fx.DrawCards"/>. No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{R}, red. Card shape comes from the
///   embedded JSON (<c>demand-answers.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Additional cost (CR 601.2f)</b>:
///   <see cref="SacrificeAnArtifactOrDiscardCardAdditionalCost"/> —
///   disjunctive payment that prefers sacrificing an artifact when one is
///   available and falls back to discarding a card otherwise. The cast
///   flow's pre-check (<see cref="SpellCastFlow"/>) rejects the cast when
///   NEITHER mode is payable (CR 601.2g — additional cost that can't be
///   paid → cast is illegal). Same posture as <see cref="BoneShardsFactory"/>.
/// - <b>Resolve (CR 121.1)</b>: the caster draws two cards via
///   <see cref="Fx.DrawCards"/>. Each draw routes through the replacement
///   bus (Dredge etc.) and an empty library stamps the SBA loss flag
///   (CR 704.5b) without throwing. No targets — the spell resolves entirely
///   on the caster.
///
/// ## Deferred (v1 gaps)
/// - <b>Mode prompt</b>. The agent doesn't choose between sacrifice and
///   discard at announcement; the cost defaults to sacrificing an artifact
///   when one is available, otherwise discards. Same queue as
///   <see cref="SacrificeCreatureOrDiscardCardAdditionalCost"/>'s deferred
///   mode prompt.
/// </summary>
[CardName("Demand Answers")]
public static class DemandAnswersFactory
{
    public const string CardName = "Demand Answers";
    public const string Slug = "demand-answers";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>CR 121.1 — "Draw two cards."</summary>
    public const int DrawAmount = 2;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Demand Answers. Declares
    /// the disjunctive sacrifice-an-artifact-or-discard-a-card additional
    /// cost (CR 601.2f); no modes, no X, no target requests — the resolve
    /// body draws two cards for the caster (CR 121.1).
    /// </summary>
    /// <param name="caster">The player who cast Demand Answers; pays the
    /// additional cost and draws the two cards.</param>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster),
            AdditionalCosts: new IAdditionalCost[]
            {
                new SacrificeAnArtifactOrDiscardCardAdditionalCost(),
            });
    }

    /// <summary>
    /// Build the resolve effect: caster draws two cards (CR 121.1). The
    /// additional cost (sacrifice an artifact or discard a card) is paid at
    /// announcement by the cast flow, NOT here — so a countered Demand
    /// Answers still consumed its additional cost, matching the printed
    /// "additional cost to cast" wording (CR 601.2f).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: draw two cards.",
                () =>
                {
                    // CR 121.1 — draw 2. Replacement bus per-draw; empty
                    // library stamps the SBA loss flag (CR 704.5b).
                    Fx.DrawCards(caster, DrawAmount);
                }),
        };
    }
}
