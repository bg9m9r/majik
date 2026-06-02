using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Costly Plunder (Hour of Devastation / reprints,
/// {1}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "As an additional cost to cast this spell, sacrifice an artifact or
///    creature.
///    Draw two cards."
///
/// ## Why it gets its own factory
/// Costly Plunder is the leaner cousin of Deadly Dispute: pitch a spent
/// artifact or an expendable creature, draw two — but with NO Treasure mint.
/// It reuses the "additional cost to cast" sacrifice shape of
/// <see cref="DeadlyDisputeFactory"/> (CR 601.2f — an artifact-OR-creature
/// disjunction via <see cref="SacrificeAnArtifactOrCreatureAdditionalCost"/>)
/// and the draw-two resolve, dropping Deadly Dispute's Treasure-mint clause.
/// Every primitive already ships — no new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}, black. Card shape comes from the
///   embedded JSON (<c>costly-plunder.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Additional cost (CR 601.2f)</b>:
///   <see cref="SacrificeAnArtifactOrCreatureAdditionalCost"/> — sacrifice an
///   artifact or a creature the caster controls. The cast flow's pre-check
///   rejects the cast when the caster controls no artifact and no creature
///   (CR 601.2g — additional cost that can't be paid → cast is illegal).
/// - <b>Resolve</b>: the caster draws two cards (CR 121.1) via
///   <see cref="Fx.DrawCards"/> (per-draw replacement bus; empty library
///   stamps the SBA loss flag — CR 704.5b — without throwing). No targets,
///   no token.
///
/// ## Rules citations
/// - CR 601.2f — "additional cost to cast" (the sacrifice).
/// - CR 121.1 — "Draw two cards."
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-target prompt</b>: the agent doesn't choose WHICH
///   artifact-or-creature to sacrifice at announcement; the cost picks the
///   first eligible permanent. Same queue as the sibling sacrifice-picker
///   costs' deferred prompts.
/// </summary>
[CardName("Costly Plunder")]
public static class CostlyPlunderFactory
{
    public const string CardName = "Costly Plunder";
    public const string Slug = "costly-plunder";
    public const string PrintedManaCost = "{1}{B}";

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
    /// Build the <see cref="SpellDefinition"/> for Costly Plunder. Declares the
    /// sacrifice-an-artifact-or-creature additional cost (CR 601.2f); no modes,
    /// no X, no target requests — the resolve body draws two cards.
    /// </summary>
    /// <param name="caster">The player who cast Costly Plunder; pays the
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
                new SacrificeAnArtifactOrCreatureAdditionalCost(),
            });
    }

    /// <summary>
    /// Build the resolve effect: caster draws two cards (CR 121.1). The
    /// additional cost (sacrifice an artifact or creature) is paid at
    /// announcement by the cast flow, NOT here — so a countered Costly Plunder
    /// still consumed its additional cost, matching the printed "additional
    /// cost to cast" wording (CR 601.2f).
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
