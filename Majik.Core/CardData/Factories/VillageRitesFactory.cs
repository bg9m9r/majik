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
/// Named-card factory for Village Rites (Throne of Eldraine / reprints, {B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "As an additional cost to cast this spell, sacrifice a creature.
///    Draw two cards."
///
/// ## Why it gets its own factory
/// Village Rites is the mono-black, one-mana cousin of
/// <see cref="CostlyPlunderFactory"/>: pitch an expendable creature, draw two.
/// The only differences from Costly Plunder are the cheaper {B} cost and a
/// narrower additional cost — sacrifice a *creature* (not "an artifact or
/// creature"). It reuses the existing
/// <see cref="SacrificeACreatureAdditionalCost"/> (CR 601.2f) and the draw-two
/// resolve via <see cref="Fx.DrawCards"/>. Every primitive already ships — no
/// new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}, black. Card shape comes from the embedded
///   JSON (<c>village-rites.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Additional cost (CR 601.2f)</b>:
///   <see cref="SacrificeACreatureAdditionalCost"/> — sacrifice a creature the
///   caster controls. The cast flow's pre-check rejects the cast when the
///   caster controls no creature (CR 601.2g — an additional cost that can't be
///   paid makes the cast illegal).
/// - <b>Resolve</b>: the caster draws two cards (CR 121.1) via
///   <see cref="Fx.DrawCards"/> (per-draw replacement bus; an empty library
///   stamps the SBA loss flag — CR 704.5b — without throwing). No targets,
///   no token.
///
/// ## Rules citations
/// - CR 601.2f — "additional cost to cast" (the sacrifice).
/// - CR 121.1 — "Draw two cards."
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-target prompt</b>: the agent doesn't choose WHICH creature
///   to sacrifice at announcement; the cost picks the first eligible creature.
///   Same queue as the sibling sacrifice-picker costs' deferred prompts.
/// </summary>
[CardName("Village Rites")]
public static class VillageRitesFactory
{
    public const string CardName = "Village Rites";
    public const string Slug = "village-rites";
    public const string PrintedManaCost = "{B}";

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
    /// Build the <see cref="SpellDefinition"/> for Village Rites. Declares the
    /// sacrifice-a-creature additional cost (CR 601.2f); no modes, no X, no
    /// target requests — the resolve body draws two cards.
    /// </summary>
    /// <param name="caster">The player who cast Village Rites; pays the
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
                new SacrificeACreatureAdditionalCost(),
            });
    }

    /// <summary>
    /// Build the resolve effect: caster draws two cards (CR 121.1). The
    /// additional cost (sacrifice a creature) is paid at announcement by the
    /// cast flow, NOT here — so a countered Village Rites still consumed its
    /// additional cost, matching the printed "additional cost to cast" wording
    /// (CR 601.2f).
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
