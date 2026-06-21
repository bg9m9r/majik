using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Recross the Paths (Shadowmoor, {2}{G}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Reveal cards from the top of your library until you reveal a land card.
///    Put that card onto the battlefield and the rest on the bottom of your
///    library in any order. Clash with an opponent. If you win, return Recross
///    the Paths to its owner's hand. (Each clashing player reveals the top card
///    of their library, then puts that card on their choice of the top or
///    bottom. A player wins if their card had a greater mana value.)"
///
/// The base shape (name, Sorcery, {2}{G}) is materialised from the embedded
/// JSON definition (<c>recross-the-paths.json</c>); the two-clause resolve
/// body is layered on here.
///
/// ## Implemented (v1)
/// - <b>Clause 1 — reveal-until-land onto the battlefield, rest on bottom
///   (CR 701.18-style reveal):</b> reveals cards off the top of the caster's
///   library in order until a land card is revealed; that land enters the
///   battlefield (routed through <see cref="ZoneServiceRegistry"/> so ETB
///   triggers / enters-tapped replacements fire — same posture as
///   <see cref="WoodElvesFactory"/>); every other revealed card (including a
///   library exhausted with no land found) goes to the BOTTOM of the library.
///   "In any order" is resolved deterministically as reveal order (a lossy v1
///   simplification shared with the rest of the reveal-onto-battlefield
///   family).
/// - <b>Clause 2 — Clash with an opponent (CR 701.32 / 601.2c):</b> routes
///   through the <see cref="ClashAction.ClashAsync"/> engine primitive — the
///   caster and one CHOSEN opponent each reveal the top of their library,
///   choose top-or-bottom, and the greater mana value wins (CR 701.32a). The
///   opponent is picked via the caster's
///   <see cref="Players.Agents.IPlayerAgent.ChoosePlayerAsync"/> over the live
///   <see cref="ContextOpponents"/> enumeration (CR 102.1 / 800.4a): forced in
///   the two-player engine target, a real choice in a 3+ player match.
/// - <b>"If you win, return Recross to its owner's hand" (CR 608.3 override):</b>
///   when the caster wins the clash, the resolve body stamps
///   <see cref="Card.MarkReturnToHandOnResolution"/> on the spell card so
///   <see cref="Majik.Core.Services.StackResolver"/> sends the spell to its
///   owner's hand instead of the graveyard.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal events</b>: the reveal-until-land pile and the clash reveals do
///   not publish a dedicated reveal event — same posture as every other
///   reveal-and-choose factory (Grisly Salvage, Malevolent Rumble).
/// </summary>
[CardName("Recross the Paths")]
public static class RecrossThePathsFactory
{
    public const string CardName = "Recross the Paths";
    public const string Slug = "recross-the-paths";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>
    /// Build the Recross the Paths card shape from the embedded JSON definition
    /// (name, Sorcery, {2}{G}). The resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> (cast through
    /// <see cref="Game.SpellCastFlow"/> with <see cref="BuildSpellDefinition"/>).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Recross the Paths — no modes,
    /// no X, no target requests; the entire effect is the two-clause resolve
    /// body (reveal-until-land onto battlefield, then clash + conditional
    /// return-to-hand).
    /// </summary>
    /// <param name="caster">The player casting Recross — reveals their library,
    /// initiates the clash, and (on a win) returns the spell to their hand.</param>
    /// <param name="card">The Recross spell card whose return-to-hand sentinel
    /// is stamped on a clash win. When null the resolve body still runs both
    /// clauses; only the self-return is skipped (shape-test convenience).</param>
    public static SpellDefinition BuildSpellDefinition(Player caster, ICard? card = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, card));
    }

    /// <summary>
    /// Build Recross the Paths' resolve effect (both clauses). Exposed for
    /// tests / integrations that splice the effect directly.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster, ICard? card = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: reveal until land onto battlefield (rest on bottom), " +
                "then clash with an opponent; if you win, return to hand.",
                async ctx =>
                {
                    // CLAUSE 1 — reveal until a land, put it onto the
                    // battlefield, the rest on the bottom of the library.
                    RevealUntilLandToBattlefield(caster);

                    // CLAUSE 2 — Clash with an opponent (CR 701.32 / 601.2c).
                    // The caster CHOOSES which opponent to clash with — routed
                    // through the agent's ChoosePlayerAsync over the live
                    // ContextOpponents enumeration (CR 102.1 / 800.4a). In the
                    // two-player matches the engine ships there is exactly one
                    // opponent, so the pick is forced; in a 3+ player game the
                    // agent picks a specific opponent instead of the first.
                    var casterAgent = ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(caster);
                    var opponent = await ChooseClashOpponentAsync(caster, casterAgent, ctx).ConfigureAwait(false);
                    if (opponent is null) return; // No opponent — no clash.

                    var opponentAgent = Majik.Core.Players.Agents.AgentRegistry.Get(opponent);

                    var result = await ClashAction.ClashAsync(
                        initiator: caster,
                        other: opponent,
                        initiatorAgent: casterAgent,
                        otherAgent: opponentAgent,
                        game: ctx.Game,
                        ct: ctx.Ct).ConfigureAwait(false);

                    // "If you win, return Recross the Paths to its owner's hand."
                    // CR 608.3 override via the resolution sentinel; the move is
                    // applied by StackResolver after the body resolves.
                    if (result.InitiatorWon && card is Card concrete)
                    {
                        concrete.MarkReturnToHandOnResolution();
                    }
                }),
        };
    }

    /// <summary>
    /// CR (reveal-until-land) — reveal cards off the top of
    /// <paramref name="caster"/>'s library in order until a land card is
    /// revealed; put that land onto the battlefield and every other revealed
    /// card on the BOTTOM of the library (reveal order — the deterministic v1
    /// reading of "in any order"). If the library is exhausted without a land,
    /// every revealed card is on the bottom and nothing enters.
    /// </summary>
    private static void RevealUntilLandToBattlefield(Player caster)
    {
        var library = caster.Zones.Library;
        var bottomed = new List<ICard>();
        ICard? land = null;

        // Snapshot the top-down order; consume until a land is found.
        foreach (var revealed in library.GetCards())
        {
            if (revealed.HasType(CardType.Land))
            {
                land = revealed;
                break;
            }
            bottomed.Add(revealed);
        }

        // Put the land onto the battlefield (CR 614 — through ZoneService so
        // ETB triggers / enters-tapped replacements fire; raw-zone fallback on
        // the shape-test path). Same posture as Wood Elves' tutored Forest.
        if (land is not null)
        {
            var zones = ZoneServiceRegistry.Get(caster);
            if (zones != null)
            {
                zones.MoveCard(land, ZoneType.Library, ZoneType.Battlefield, caster);
            }
            else
            {
                library.RemoveCard(land);
                caster.Zones.Battlefield.AddCard(land);
                land.SetZone(ZoneType.Battlefield);
                land.SetController(caster);
                if (land is Permanent perm) perm.MarkEnteredBattlefield();
            }
        }

        // The rest go on the bottom of the library, in reveal order (Zone.AddCard
        // appends == bottom; remove-then-add re-seats each at the end).
        foreach (var c in bottomed)
        {
            library.RemoveCard(c);
            library.AddCard(c);
        }
    }

    /// <summary>
    /// CR 601.2c / CR 701.32 — choose the opponent for "clash with an
    /// opponent". The caster's agent picks one opponent from the live
    /// <see cref="ContextOpponents"/> enumeration (CR 102.1 / 800.4a — every
    /// in-game opponent of the caster, read off the resolution context). In the
    /// two-player engine target there is exactly one opponent, so the pick is
    /// forced; the surface lets a 3+ player match pick a specific opponent
    /// instead of the historical first-opponent shortcut. Returns
    /// <see langword="null"/> only when no opponent exists (no game context, or
    /// every opponent has left the game) — then the clash clause no-ops.
    /// </summary>
    private static async Task<Player?> ChooseClashOpponentAsync(
        Player caster, Players.Agents.IPlayerAgent? agent, ResolutionContext ctx)
    {
        if (ctx.Game is null) return null;
        var opponents = ContextOpponents.Of(ctx, caster).ToList();
        if (opponents.Count == 0) return null;
        if (agent is null) return opponents[0];
        return await agent.ChoosePlayerAsync(
            ctx.Game, opponents, $"{CardName}: choose an opponent to clash with",
            Cards.BotIntent.None, ctx.Ct).ConfigureAwait(false);
    }
}
