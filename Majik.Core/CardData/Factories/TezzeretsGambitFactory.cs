using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tezzeret's Gambit (New Phyrexia, {3}{U/P}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "({U/P} can be paid with either {U} or 2 life.)
///    Draw two cards, then proliferate. (Choose any number of permanents
///    and/or players, then give each another counter of each kind already
///    there.)"
///
/// ## Shape source
/// Card identity (name, {3}{U/P}, Sorcery) is loaded from
/// <c>Majik.Core/CardData/Cards/tezzerets-gambit.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> and built
/// through <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="WildGuessFactory"/> (draw-two sorcery from JSON). The Phyrexian
/// mana symbol {U/P} (CR 107.4f — payable with {U} or 2 life) parses in the
/// JSON manaCost exactly like Porcelain Legionnaire's {2}{W/P}; it is a
/// cost-payment OPTION, not an additional cost, so it shapes no
/// <see cref="SpellDefinition.AdditionalCosts"/> entry.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {3}{U/P}, blue. No modes, no X, no targets.
/// - <b>Resolve (CR 121.1)</b>: the caster draws two cards via
///   <see cref="Fx.DrawCards"/> (each draw routes through the replacement bus;
///   an empty library stamps the SBA loss flag — CR 704.5b).
/// - <b>then proliferate (CR 701.27)</b>: after the draw, the shared
///   proliferate primitive
///   <see cref="SwordOfTruthAndJusticeFactory.Proliferate"/> walks every known
///   player's battlefield and adds one more counter of an existing kind to each
///   permanent that already has at least one counter. The word "then" sequences
///   the two parts of one resolution (CR 608.2c) — modeled as two ordered
///   effects in <see cref="BuildResolveEffect"/>.
///
/// ## Deferred (v1 gaps — shared with the proliferate primitive)
/// - <b>"Any number" → "all of them"</b>: agent-driven subset selection is
///   deferred; v1 deterministically proliferates every eligible permanent.
/// - <b>Player counters</b> (poison, energy, experience) and opponent
///   battlefields: <see cref="SwordOfTruthAndJusticeFactory.Proliferate"/>
///   walks only the source controller's battlefield until a <c>Game</c>
///   reference is plumbed through — engine-wide proliferate gap, not
///   card-specific.
/// - <b>Replacement-effect draws</b>: covered to the extent
///   <see cref="Fx.DrawCards"/> routes through the per-player replacement bus
///   (same posture as Wild Guess / Concentrate).
///
/// ## Rules citations
/// - CR 107.4f — {U/P} is payable with {U} or 2 life (Phyrexian mana).
/// - CR 121.1 — "Draw two cards."
/// - CR 608.2c — "then" sequences the parts of a single resolution.
/// - CR 701.27 — proliferate.
/// - CR 704.5b — drawing from an empty library flags the SBA loss.
/// </summary>
[CardName("Tezzeret's Gambit")]
public static class TezzeretsGambitFactory
{
    public const string CardName = "Tezzeret's Gambit";
    public const string Slug = "tezzerets-gambit";
    public const string PrintedManaCost = "{3}{U/P}";

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
    /// Build the <see cref="SpellDefinition"/> for Tezzeret's Gambit. No modes,
    /// no X, no target requests, no additional costs — the Phyrexian {U/P}
    /// symbol is a cost-payment option (CR 107.4f), not an additional cost. The
    /// resolve body draws two cards then proliferates (CR 121.1 / CR 701.27).
    /// </summary>
    /// <param name="caster">The player who cast Tezzeret's Gambit; draws the two
    /// cards and is the proliferate source.</param>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster));
    }

    /// <summary>
    /// Build the ordered resolve effects: (1) the caster draws two cards
    /// (CR 121.1), then (2) proliferate (CR 701.27). The "then" wording
    /// (CR 608.2c) sequences the two parts of the single resolution — the draw
    /// happens fully before the proliferate, so freshly-drawn counter-related
    /// state is already in place. Proliferate delegates to the shared primitive
    /// <see cref="SwordOfTruthAndJusticeFactory.Proliferate"/>.
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
            new Effect(
                $"{CardName}: proliferate (CR 701.27).",
                () => SwordOfTruthAndJusticeFactory.Proliferate(caster)),
        };
    }
}
