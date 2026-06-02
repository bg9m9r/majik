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
/// Named-card factory for Wild Guess (Time Spiral / reprints, {R}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "As an additional cost to cast this spell, discard a card.
///    Draw two cards."
///
/// ## Why it gets its own factory
/// Wild Guess is the double-red, sorcery-speed sibling of the "discard a
/// card as an additional cost, then draw two" loot cycle. It shares
/// Demand Answers' shape — an "additional cost to cast" discard plus a
/// plain "draw two cards" resolve — but with the simpler, non-disjunctive
/// "discard a card" cost (CR 601.2f) rather than Demand Answers'
/// "sacrifice an artifact or discard a card". Both primitives already
/// ship: the additional cost is the new thin
/// <see cref="DiscardACardAdditionalCost"/> (composing the existing
/// <see cref="IAdditionalCost"/> contract — the spell-cast analogue of
/// <see cref="DiscardACardCost"/>), and the draw uses
/// <see cref="Fx.DrawCards"/>. No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {R}{R}, red. Card shape comes from the
///   embedded JSON (<c>wild-guess.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Additional cost (CR 601.2f)</b>:
///   <see cref="DiscardACardAdditionalCost"/> — discard one card from hand
///   at announcement. The cast flow's pre-check
///   (<see cref="SpellCastFlow"/>) rejects the cast when the caster's hand
///   is empty (CR 601.2g — a mandatory additional cost that can't be paid
///   makes the cast illegal). Unlike the printed-cost looters
///   (Tormenting Voice et al.), Wild Guess pays its discard at
///   announcement, so a countered Wild Guess still consumed the discard.
/// - <b>Resolve (CR 121.1)</b>: the caster draws two cards via
///   <see cref="Fx.DrawCards"/>. Each draw routes through the replacement
///   bus (Dredge etc.) and an empty library stamps the SBA loss flag
///   (CR 704.5b) without throwing. No targets — the spell resolves entirely
///   on the caster.
///
/// ## Rules citations
/// - CR 601.2f — "As an additional cost to cast this spell, discard a card."
/// - CR 601.2g — empty hand → mandatory additional cost unpayable → cast illegal.
/// - CR 701.16a — discard moves a card from hand to graveyard.
/// - CR 121.1 — "Draw two cards."
/// - CR 704.5b — drawing from an empty library flags the SBA loss.
///
/// ## Deferred (v1 gaps)
/// - <b>Discard-target prompt</b>: the agent doesn't choose WHICH card to
///   discard at announcement; the cost picks the first card in hand. Same
///   queue as the sibling discard costs' deferred prompts.
/// </summary>
[CardName("Wild Guess")]
public static class WildGuessFactory
{
    public const string CardName = "Wild Guess";
    public const string Slug = "wild-guess";
    public const string PrintedManaCost = "{R}{R}";

    /// <summary>CR 121.1 — "Draw two cards."</summary>
    public const int DrawAmount = 2;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Wild Guess. Declares the
    /// discard-a-card additional cost (CR 601.2f); no modes, no X, no target
    /// requests — the resolve body draws two cards for the caster (CR 121.1).
    /// </summary>
    /// <param name="caster">The player who cast Wild Guess; pays the
    /// additional cost (discards a card) and draws the two cards.</param>
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
                new DiscardACardAdditionalCost(),
            });
    }

    /// <summary>
    /// Build the resolve effect: caster draws two cards (CR 121.1). The
    /// additional cost (discard a card) is paid at announcement by the cast
    /// flow, NOT here — so a countered Wild Guess still consumed its discard,
    /// matching the printed "additional cost to cast" wording (CR 601.2f).
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
