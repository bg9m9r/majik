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
/// Named-card factory for Deadly Dispute (Adventures in the Forgotten Realms
/// / reprints, {1}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "As an additional cost to cast this spell, sacrifice an artifact or
///    creature.
///    Draw two cards and create a Treasure token. (It's an artifact with
///    "{T}, Sacrifice this token: Add one mana of any color.")"
///
/// ## Why it gets its own factory
/// Deadly Dispute is the black aristocrats cantrip-plus-ramp staple: pitch a
/// spent artifact (Treasure / Clue / Blood) or an expendable creature, draw
/// two, and mint a fresh Treasure. It combines the real
/// "additional cost to cast" sacrifice shape of
/// <see cref="DemandAnswersFactory"/> (CR 601.2f — re-pointed at an
/// artifact-OR-creature disjunction via
/// <see cref="SacrificeAnArtifactOrCreatureAdditionalCost"/>) with the
/// draw-two + Treasure-mint resolve of <see cref="BigScoreFactory"/>
/// (<see cref="Fx.DrawCards"/> + <see cref="TokenFactory.CreateTreasure"/>).
/// All three primitives already ship — no new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}, black. Card shape comes from the
///   embedded JSON (<c>deadly-dispute.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Additional cost (CR 601.2f)</b>:
///   <see cref="SacrificeAnArtifactOrCreatureAdditionalCost"/> — sacrifice an
///   artifact or a creature the caster controls. The cast flow's pre-check
///   (<see cref="SpellCastFlow"/>) rejects the cast when the caster controls
///   no artifact and no creature (CR 601.2g — additional cost that can't be
///   paid → cast is illegal). Same posture as <see cref="DemandAnswersFactory"/>.
/// - <b>Resolve</b>: the caster draws two cards (CR 121.1) via
///   <see cref="Fx.DrawCards"/> (per-draw replacement bus; empty library
///   stamps the SBA loss flag — CR 704.5b — without throwing), then creates
///   one Treasure token under their control (CR 111.10) via
///   <see cref="TokenFactory.CreateTreasure"/> — a colourless artifact with
///   the five-option any-colour sac mana ability. No targets.
///
/// ## Rules citations
/// - CR 601.2f — "additional cost to cast" (the sacrifice).
/// - CR 121.1 — "Draw two cards."
/// - CR 111.10 — Treasure token (colourless artifact, any-colour sac mana).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-target prompt</b>: the agent doesn't choose WHICH
///   artifact-or-creature to sacrifice at announcement; the cost picks the
///   first eligible permanent. Same queue as the sibling sacrifice-picker
///   costs' deferred prompts.
/// - <b>Treasure tap-to-sac colour prompt</b>: uses the five-option
///   ManaAbility model shared by all Treasure tokens; the agent picks the
///   colour at mana-pick time.
/// </summary>
[CardName("Deadly Dispute")]
public static class DeadlyDisputeFactory
{
    public const string CardName = "Deadly Dispute";
    public const string Slug = "deadly-dispute";
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
    /// Build the <see cref="SpellDefinition"/> for Deadly Dispute. Declares
    /// the sacrifice-an-artifact-or-creature additional cost (CR 601.2f); no
    /// modes, no X, no target requests — the resolve body draws two cards and
    /// creates a Treasure token for the caster.
    /// </summary>
    /// <param name="caster">The player who cast Deadly Dispute; pays the
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
                new SacrificeAnArtifactOrCreatureAdditionalCost(),
            });
    }

    /// <summary>
    /// Build the resolve effect: caster draws two cards (CR 121.1), then
    /// creates one Treasure token (CR 111.10). The additional cost (sacrifice
    /// an artifact or creature) is paid at announcement by the cast flow, NOT
    /// here — so a countered Deadly Dispute still consumed its additional
    /// cost, matching the printed "additional cost to cast" wording
    /// (CR 601.2f).
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
