using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blasphemous Edict (Modern Horizons 3, {3}{B}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "You may pay {B} rather than pay this spell's mana cost if there are
///    thirteen or more creatures on the battlefield.
///    Each player sacrifices thirteen creatures of their choice."
///
/// ## Why it gets its own factory
/// Blasphemous Edict combines two already-supported shapes:
/// - <b>Each-player forced sacrifice</b>: the symmetric edict body of
///   <see cref="SmallpoxFactory"/> / <see cref="SheoldredsEdictFactory"/>,
///   scaled from "a creature" to thirteen creatures per player. For every
///   player, in order (CR 608.2), that player sacrifices up to thirteen
///   creatures OF THEIR CHOICE (CR 701.16) — the affected player's agent
///   drives each pick (<see cref="BotIntent.Removal"/>) with a deterministic
///   first-creature fallback. A player with fewer than thirteen creatures
///   sacrifices all of them (CR 608.2 — do as much as possible).
/// - <b>Conditional fixed-mana alternative cost (CR 118.9)</b>: pay {B} rather
///   than the printed {3}{B}{B} if there are thirteen or more creatures on the
///   battlefield, via <see cref="PayManaIfThirteenCreaturesAlternativeCost"/>
///   (the head-count analogue of <see cref="SpectacleAlternativeCost"/>). The
///   alt-cost's <c>CanCastFor</c> counts creatures across every battlefield
///   (CR 109.4) reading the live player set off
///   <see cref="GamePlayersRegistry"/>.
///
/// All primitives already ship — no new engine mechanic is required:
/// <see cref="Fx.Sacrifice"/> (CR 701.16) and the fixed-mana alt-cost plumbing
/// (<see cref="SpectacleAlternativeCost"/>) both pre-exist.
///
/// ## Rules citations
/// - CR 118.9 — alternative cost ("pay {B} rather than ... mana cost").
/// - CR 109.4 — "thirteen or more creatures on the battlefield" counts every
///   player's creatures (no controller qualifier).
/// - CR 701.16 — "sacrifices ... creatures of their choice."
/// - CR 608.2 — sequential each-player resolution; do as much as possible when
///   a player controls fewer than thirteen creatures.
/// - CR 109.5 / 800.4 — "each player" includes the spell's controller.
///
/// ## Deferred (v1 gaps)
/// - <b>Forced-sacrifice prompt UI</b>: each affected player's agent receives
///   the full eligible creature list; surfacing the choice to the portal
///   decision panel is deferred — same queue as <see cref="SmallpoxFactory"/> /
///   <see cref="SheoldredsEdictFactory"/>.
/// </summary>
[CardName("Blasphemous Edict")]
public static class BlasphemousEdictFactory
{
    public const string CardName = "Blasphemous Edict";
    public const string Slug = "blasphemous-edict";
    public const string PrintedManaCost = "{3}{B}{B}";

    /// <summary>CR 701.16 — each player sacrifices up to this many creatures.</summary>
    public const int SacrificeCount = 13;

    /// <summary>CR 118.9 — the alternative mana cost ({B}).</summary>
    public static ManaCost AlternativeManaCost => ManaCost.Parse("B");

    /// <summary>Build the card shape from the embedded JSON definition
    /// (Sorcery, {3}{B}{B}). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "pay {B} rather than pay this spell's mana cost if there are
    /// thirteen or more creatures on the battlefield" alternative cost
    /// (CR 118.9). Callers supply it to the spell-cast flow; the alt-cost's
    /// <c>CanCastFor</c> gates on the global creature count.
    /// </summary>
    /// <param name="players">Optional explicit player list whose battlefields
    /// are counted (unit tests). Null reads the live player set off
    /// <see cref="GamePlayersRegistry"/> (production path).</param>
    public static PayManaIfThirteenCreaturesAlternativeCost BuildAlternativeCost(
        IReadOnlyList<Player>? players = null)
        => new(AlternativeManaCost, SacrificeCount, players);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Blasphemous Edict
    /// resolves. No modes, no X, no target requests (CR 115.1a — "each player"
    /// is not a chosen target). The resolve body iterates every player from the
    /// LIVE resolution context (<c>ctx.Game.AllPlayers</c>) and, for each, makes
    /// that player sacrifice up to <see cref="SacrificeCount"/> creatures of
    /// their choice (CR 701.16). With no live game context the body no-ops
    /// (shape-only paths). Each affected player's "of their choice" picks read
    /// THAT player's agent from <see cref="AgentRegistry"/> at resolution (the
    /// optional <paramref name="agentSelector"/> overrides it for tests).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Func<Player, IPlayerAgent?>? agentSelector = null)
    {
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: each player sacrifices {SacrificeCount} creatures of their choice",
                    ctx =>
                    {
                        Resolve(ctx.Game?.AllPlayers, agentSelector);
                        return ValueTask.CompletedTask;
                    }),
            });
    }

    // -----------------------------------------------------------------------
    // Resolution body — CR 608.2 / CR 701.16. For each player, in order, that
    // player sacrifices up to SacrificeCount creatures of their choice.
    // -----------------------------------------------------------------------
    private static void Resolve(
        IReadOnlyList<Player>? players,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        if (players == null) return; // shape path — no live game.

        foreach (var pl in players)
        {
            if (pl == null) continue;
            var agent = agentSelector?.Invoke(pl) ?? AgentRegistry.Get(pl);

            // CR 701.16 — sacrifice up to thirteen creatures of their choice.
            // CR 608.2 — fewer than thirteen → sacrifice all of them.
            for (int i = 0; i < SacrificeCount; i++)
            {
                if (!SacrificeOneCreatureOfTheirChoice(pl, agent)) break;
            }
        }
    }

    /// <summary>
    /// CR 701.16 — <paramref name="player"/> sacrifices one creature of their
    /// choice from the creatures they control. Returns false (and does nothing)
    /// when the player controls no creature. Agent-driven pick
    /// (<see cref="BotIntent.Removal"/>) with a deterministic first-creature
    /// fallback.
    /// </summary>
    private static bool SacrificeOneCreatureOfTheirChoice(Player player, IPlayerAgent? agent)
    {
        var eligible = player.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(CardType.Creature))
            .ToList();
        if (eligible.Count == 0) return false;

        ICard pick;
        if (agent != null)
        {
            var chosen = agent
                .ChooseFromBattlefieldAsync(player, eligible, BotIntent.Removal)
                .GetAwaiter().GetResult();
            pick = (chosen != null
                    && chosen.Zone == ZoneType.Battlefield
                    && ReferenceEquals(chosen.Controller, player)
                    && chosen.HasType(CardType.Creature))
                ? chosen
                : eligible[0];
        }
        else
        {
            pick = eligible[0];
        }

        // CR 701.16 — sacrifice bypasses Indestructible / regeneration.
        Fx.Sacrifice(pick);
        return true;
    }
}
