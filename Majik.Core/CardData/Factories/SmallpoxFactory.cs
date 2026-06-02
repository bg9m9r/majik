using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Smallpox (Planar Chaos, {B}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Each player loses 1 life, discards a card, sacrifices a creature of
///    their choice, then sacrifices a land of their choice."
///
/// ## Why it gets its own factory
/// Smallpox is the symmetric black "everyone gives up everything" sorcery:
/// it stacks four sub-effects over EACH player (the caster included —
/// CR 109.5 / 800.4 "each player"), composing primitives that already ship.
/// It mirrors the each-player edict-plus-discard resolution of
/// <see cref="PlaguecrafterFactory"/> (which iterates "each player",
/// sacrifices a permanent of their choice, and discards), but as a sorcery
/// resolve rather than an ETB trigger, and with the fixed four-step
/// sequence (loseLife → discard → sac creature → sac land) instead of the
/// sacrifice-or-discard branch. No new engine mechanic is required:
/// <see cref="Fx.LoseLife"/>, the discard move (CR 701.8), and
/// <see cref="Fx.Sacrifice"/> (CR 701.16) all exist.
///
/// ## Implemented (v1)
/// - <b>Sorcery</b> shape, mana cost {B}{B}, black. Card shape comes from the
///   embedded JSON (<c>smallpox.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build(CardDefinition, Player)"/> —
///   same JSON-backed posture as <see cref="DeadlyDisputeFactory"/> /
///   <see cref="PlaguecrafterFactory"/>.
/// - <b>Resolve</b> (no targets — CR 115.1a; the spell affects "each player").
///   For every player in the resolver's list, IN ORDER (CR 608.2):
///     1. <b>Loses 1 life</b> (CR 119 — life loss, not damage; routed through
///        <see cref="Fx.LoseLife"/> so damage-prevention / lifelink never
///        engage). Always applies.
///     2. <b>Discards a card of their choice</b> (CR 701.8). Agent-driven pick
///        (intent <see cref="BotIntent.Discard"/>) with a deterministic
///        first-card fallback. Empty hand → nothing to discard.
///     3. <b>Sacrifices a creature of their choice</b> (CR 701.16). Agent-driven
///        (intent <see cref="BotIntent.Removal"/>), deterministic
///        first-creature fallback. No creature → nothing sacrificed.
///     4. <b>Then sacrifices a land of their choice</b> (CR 701.16). The
///        "then" (CR 608.2) sequences this AFTER the creature sacrifice — a
///        creature-land (e.g. a manland) sacrificed in step 3 is no longer
///        available in step 4, matching the printed ordering. No land →
///        nothing sacrificed.
///   Sacrifice bypasses Indestructible / regeneration
///   (<see cref="Fx.Sacrifice"/>).
///
/// ## Rules citations
/// - CR 119 — "loses 1 life" (life loss, not damage).
/// - CR 701.8 — "discards a card."
/// - CR 701.16 — "sacrifices a creature / land of their choice."
/// - CR 608.2 — single-instruction sequential resolution ("then"); do as
///   much as possible when a piece is missing.
/// - CR 109.5 / 800.4 — "each player" includes the spell's controller.
///
/// ## Deferred (v1 gaps)
/// - <b>Discard / sacrifice prompt UI</b>: each affected player's agent
///   receives the full hand / eligible list; surfacing the choice to the
///   portal decision panel is deferred — same queue as
///   <see cref="PlaguecrafterFactory"/> / <see cref="DeadlyDisputeFactory"/>.
/// - <b>APNAP ordering of choices</b>: the resolver is expected to yield
///   players in APNAP order (CR 101.4) when supplied by the runtime; the
///   sub-effects are independent across players (no shared objects) so the
///   per-player order is not observable here.
/// </summary>
[CardName("Smallpox")]
public static class SmallpoxFactory
{
    public const string CardName = "Smallpox";
    public const string Slug = "smallpox";
    public const string PrintedManaCost = "{B}{B}";

    /// <summary>CR 119 — each player loses 1 life.</summary>
    public const int LifeLoss = 1;

    /// <summary>
    /// Build the card shape from the embedded JSON definition (Sorcery,
    /// {B}{B}). The four-step each-player resolve is supplied at cast time
    /// via <see cref="BuildSpellDefinition"/> (the runtime threads the live
    /// player list + agents from the <see cref="GameContext"/>). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Smallpox resolves.
    /// No modes, no X, no target requests (CR 115.1a — "each player" is not a
    /// chosen target). The resolve body iterates
    /// <paramref name="playerResolver"/> and, for each player in order,
    /// applies the four sub-effects (CR 608.2).
    /// </summary>
    /// <param name="playerResolver">Returns the live player list at resolution
    /// time, ideally in APNAP order (CR 101.4). Null → the resolve body
    /// no-ops (shape path).</param>
    /// <param name="agentSelector">Optional per-player agent selector driving
    /// the "of their choice" discard / creature / land picks. Null falls back
    /// to deterministic first-eligible picks.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<IReadOnlyList<Player>>? playerResolver,
        Func<Player, IPlayerAgent?>? agentSelector = null)
    {
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: each player loses 1 life, discards a card, sacrifices a creature, then sacrifices a land",
                    () => Resolve(playerResolver, agentSelector)),
            });
    }

    // -----------------------------------------------------------------------
    // Resolution body — CR 608.2. For each player, in order:
    //   loseLife → discard → sacrifice a creature → then sacrifice a land.
    // -----------------------------------------------------------------------
    private static void Resolve(
        Func<IReadOnlyList<Player>>? playerResolver,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        var players = playerResolver?.Invoke();
        if (players == null) return; // shape path — no players wired.

        foreach (var pl in players)
        {
            if (pl == null) continue;
            var agent = agentSelector?.Invoke(pl);

            // 1. CR 119 — loses 1 life (life loss, not damage).
            Fx.LoseLife(pl, LifeLoss);

            // 2. CR 701.8 — discards a card of their choice.
            DiscardOfTheirChoice(pl, agent);

            // 3. CR 701.16 — sacrifices a creature of their choice.
            SacrificeOfTheirChoice(pl, CardType.Creature, agent);

            // 4. CR 608.2 "then" — sacrifices a land of their choice, AFTER
            //    the creature sacrifice.
            SacrificeOfTheirChoice(pl, CardType.Land, agent);
        }
    }

    /// <summary>
    /// CR 701.8 — <paramref name="player"/> discards a card of their choice.
    /// Empty hand → no discard. Agent-driven pick
    /// (<see cref="BotIntent.Discard"/>) with a deterministic first-card
    /// fallback.
    /// </summary>
    private static void DiscardOfTheirChoice(Player player, IPlayerAgent? agent)
    {
        var hand = player.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return;

        ICard pick;
        if (agent != null)
        {
            var chosen = agent
                .ChooseFromHandAsync(player, hand.Cast<ICard>().ToList(), BotIntent.Discard)
                .GetAwaiter().GetResult();
            pick = (chosen != null && chosen.Zone == ZoneType.Hand) ? chosen : hand[0];
        }
        else
        {
            pick = hand[0];
        }

        player.Zones.Hand.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// CR 701.16 — <paramref name="player"/> sacrifices a permanent of
    /// <paramref name="type"/> (creature or land) of their choice from the
    /// permanents they control. No eligible permanent → nothing sacrificed
    /// (CR 608.2 — do as much as possible). Agent-driven pick
    /// (<see cref="BotIntent.Removal"/>) with a deterministic first-eligible
    /// fallback.
    /// </summary>
    private static void SacrificeOfTheirChoice(
        Player player, CardType type, IPlayerAgent? agent)
    {
        var eligible = player.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(type))
            .ToList();
        if (eligible.Count == 0) return;

        ICard pick;
        if (agent != null)
        {
            var chosen = agent
                .ChooseFromBattlefieldAsync(player, eligible, BotIntent.Removal)
                .GetAwaiter().GetResult();
            pick = (chosen != null
                    && chosen.Zone == ZoneType.Battlefield
                    && ReferenceEquals(chosen.Controller, player)
                    && chosen.HasType(type))
                ? chosen
                : eligible[0];
        }
        else
        {
            pick = eligible[0];
        }

        // CR 701.16 — sacrifice bypasses Indestructible / regeneration.
        Fx.Sacrifice(pick);
    }
}
