using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seize the Spoils (Kaldheim, {2}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "As an additional cost to cast this spell, discard a card.
///    Draw two cards and create a Treasure token. (It's an artifact with
///    "{T}, Sacrifice this token: Add one mana of any color.")"
///
/// ## Why it gets its own factory
/// Seize the Spoils is the red loot-plus-ramp sorcery: pitch a card, draw
/// two, and mint a Treasure. It is the sorcery-speed sibling of
/// <see cref="DeadlyDisputeFactory"/> — same "draw two + create a Treasure"
/// resolve, but the additional cost is the plain single-card discard of
/// <see cref="DemandAnswersFactory"/>. It composes three already-shipped
/// primitives:
/// <list type="bullet">
///   <item><see cref="DiscardACardAdditionalCost"/> — the
///     "As an additional cost to cast this spell, discard a card."
///     additional cost (CR 601.2f).</item>
///   <item><see cref="Fx.DrawCards"/> — "Draw two cards." (CR 121.1).</item>
///   <item><see cref="TokenFactory.CreateTreasure"/> — "create a Treasure
///     token." (CR 111.10).</item>
/// </list>
/// No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{R}, red. Card shape comes from the embedded
///   JSON (<c>seize-the-spoils.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Additional cost (CR 601.2f)</b>: <see cref="DiscardACardAdditionalCost"/>
///   — discard one card from the caster's hand. The cast flow's pre-check
///   (<see cref="SpellCastFlow"/>) rejects the cast when the hand is empty
///   (CR 601.2g — an additional cost that can't be paid makes the cast
///   illegal). Same posture as <see cref="DemandAnswersFactory"/>.
/// - <b>Resolve</b>: the caster draws two cards (CR 121.1) via
///   <see cref="Fx.DrawCards"/> (per-draw replacement bus; empty library
///   stamps the SBA loss flag — CR 704.5b — without throwing), then creates
///   one Treasure token under their control (CR 111.10) via
///   <see cref="TokenFactory.CreateTreasure"/> — a colourless artifact with
///   the five-option any-colour sac mana ability. No targets.
///
/// ## Rules citations
/// - CR 601.2f — "additional cost to cast" (the discard).
/// - CR 121.1 — "Draw two cards."
/// - CR 111.10 — Treasure token (colourless artifact, any-colour sac mana).
///
/// ## Deferred (v1 gaps)
/// - <b>Discard-target prompt</b>: the agent doesn't choose WHICH card to
///   discard at announcement; the cost discards the first card in hand. Same
///   queue as <see cref="DiscardACardAdditionalCost"/>'s deferred prompt.
/// - <b>Treasure tap-to-sac colour prompt</b>: uses the five-option
///   ManaAbility model shared by all Treasure tokens; the agent picks the
///   colour at mana-pick time.
/// </summary>
[CardName("Seize the Spoils")]
public static class SeizeTheSpoilsFactory
{
    public const string CardName = "Seize the Spoils";
    public const string Slug = "seize-the-spoils";
    public const string PrintedManaCost = "{2}{R}";

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
    /// Build the <see cref="SpellDefinition"/> for Seize the Spoils. Declares
    /// the discard-a-card additional cost (CR 601.2f); no modes, no X, no
    /// target requests — the resolve body draws two cards and creates a
    /// Treasure token for the caster.
    /// </summary>
    /// <param name="caster">The player who cast Seize the Spoils; pays the
    /// additional cost, draws the two cards, and receives the Treasure.</param>
    /// <param name="zoneService">Optional zone service — routes the Treasure
    /// ETB through <see cref="ZoneService"/> so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes (enabling
    /// downstream triggers). Null → direct zone move, suitable for unit-test /
    /// shape-only paths.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, zoneService),
            AdditionalCosts: new IAdditionalCost[]
            {
                new DiscardACardAdditionalCost(),
            });
    }

    /// <summary>
    /// Build the resolve effect: caster draws two cards (CR 121.1), then
    /// creates one Treasure token (CR 111.10). The additional cost (discard a
    /// card) is paid at announcement by the cast flow, NOT here — so a
    /// countered Seize the Spoils still consumed its additional cost, matching
    /// the printed "additional cost to cast" wording (CR 601.2f).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: draw two cards and create a Treasure token.",
                () =>
                {
                    // CR 121.1 — draw 2. Replacement bus per-draw; empty
                    // library stamps the SBA loss flag (CR 704.5b).
                    Fx.DrawCards(caster, DrawAmount);

                    // CR 111.10 — create one Treasure token: a colourless
                    // artifact with the five-option any-colour sac mana
                    // ability. TokenFactory.CreateTreasure handles the full
                    // spec + the battlefield ETB move.
                    TokenFactory.CreateTreasure(caster, zoneService);
                }),
        };
    }
}
