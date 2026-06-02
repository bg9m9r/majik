using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Liliana's Triumph (War of the Spark, {1}{B}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Each opponent sacrifices a creature of their choice. If you control a
///    Liliana planeswalker, each opponent also discards a card."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}, black.
/// - No targets (the card affects "each opponent", not a chosen target), so the
///   <see cref="SpellDefinition.TargetRequests"/> list is empty and there is no
///   modal choice. The opponent set is read from
///   <see cref="ChosenSpellParams.AllPlayers"/> (mirroring
///   <see cref="SheoldredsEdictFactory"/>).
/// - <b>Sacrifice half</b> — each opponent of the caster (CR 102.1) sacrifices
///   one creature "of their choice". This mirrors
///   <see cref="DiabolicEdictFactory"/> / <see cref="SheoldredsEdictFactory"/>:
///     1. Pre-filter the opponent's battlefield to creatures.
///     2. The affected player's agent drives the pick
///        (<see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>, intent
///        <see cref="BotIntent.Removal"/>); a null / illegal pick falls back
///        deterministically to the first creature in battlefield order.
///     3. CR 701.16 — "sacrifice" moves the permanent battlefield → owner's
///        graveyard, bypassing Indestructible / regeneration. An opponent with
///        no creature sacrifices nothing (no-op); the spell still resolves.
/// - <b>Discard rider</b> — "If you control a Liliana planeswalker, each
///   opponent also discards a card." CR 608.2 — the condition is checked as the
///   spell resolves (it is part of the resolution, not an intervening-if
///   trigger). "Liliana planeswalker" = a permanent the caster controls with
///   <see cref="CardType.Planeswalker"/> and the
///   <see cref="CardSubtype.Liliana"/> subtype (CR 205.3j). When the condition
///   holds, each opponent discards one card of their own choice (CR 701.8a —
///   the discarding player chooses), mirroring <see cref="MindRotFactory"/>:
///     - The affected player's agent drives the pick
///       (<see cref="IPlayerAgent.ChooseFromHandAsync"/>, intent
///       <see cref="BotIntent.Discard"/>); a null / illegal pick falls back to
///       the first card in hand order.
///     - An opponent with an empty hand discards nothing (CR 701.8b — can't
///       discard what you don't have).
///
/// ## Why a named factory
/// The "each opponent sacrifices a creature of their choice" body is the
/// <see cref="SheoldredsEdictFactory"/> edict shape (agent-driven pick over the
/// "each opponent" iteration); the conditional discard rider reuses the
/// <see cref="MindRotFactory"/> agent-driven discard. No new engine mechanic is
/// introduced — planeswalker subtype detection
/// (<see cref="Card.HasSubtype"/>), <see cref="OracleSpellBinder.MoveToGraveyard"/>
/// with <see cref="ZoneMoveReason.Sacrifice"/>, and Hand → Graveyard discard all
/// pre-exist.
///
/// ## Deferred (v1 gaps)
/// - <b>Forced sacrifice / discard prompt UI</b>: the affected player's agent
///   receives the full candidate list; surfacing the choice to the portal
///   decision panel is deferred (same queue as Diabolic Edict / Mind Rot).
/// </summary>
[CardName("Liliana's Triumph")]
public static class LilianasTriumphFactory
{
    public const string CardName = "Liliana's Triumph";
    public const string Slug = "lilianas-triumph";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>
    /// Build the Liliana's Triumph instant from the embedded JSON definition
    /// (<c>lilianas-triumph.json</c>) via
    /// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
    /// <see cref="CardDefinitionFactory"/> (matching
    /// <see cref="SuddenEdictFactory"/>). Card shape only — the resolve-time
    /// sacrifice/discard body is built on demand via
    /// <see cref="BuildDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Liliana's Triumph is
    /// cast. No target requests and no modal choice (the card is "each
    /// opponent" / "of their choice"); the opponent set comes from
    /// <see cref="ChosenSpellParams.AllPlayers"/>.
    /// </summary>
    /// <param name="caster">The spell's controller — excluded from the "each
    /// opponent" iteration, and the player whose battlefield is checked for a
    /// Liliana planeswalker (the discard-rider condition).</param>
    /// <param name="allPlayers">All players in turn order (used to find the
    /// caster's opponents at resolution). Falls back to
    /// <see cref="ChosenSpellParams.AllPlayers"/> when the runtime supplies a
    /// fresher list at resolution time.</param>
    /// <param name="agent">Optional agent used to drive each affected player's
    /// "of their choice" sacrifice and discard picks. When null, the pick falls
    /// back deterministically to the first matching card in zone order
    /// (matches <see cref="SheoldredsEdictFactory"/> / <see cref="MindRotFactory"/>).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        IReadOnlyList<Player> allPlayers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p => new IEffect[]
            {
                new Effect(
                    $"{CardName}: each opponent sacrifices a creature; if you control a Liliana planeswalker, each also discards a card",
                    () =>
                    {
                        // Prefer the resolution-time player list when the runtime
                        // supplies it; otherwise use the cast-time list.
                        var players = p.AllPlayers is { Count: > 0 } fresh ? fresh : allPlayers;
                        if (players == null) return;

                        var opponents = players.Where(pl => !ReferenceEquals(pl, caster)).ToList();

                        // --- Sacrifice half: each opponent sacrifices a creature
                        // of their choice (CR 701.16). ---
                        foreach (var pl in opponents)
                        {
                            var creatures = pl.Zones.Battlefield.GetCards()
                                .OfType<Creature>()
                                .Cast<ICard>()
                                .ToList();

                            if (creatures.Count == 0) continue; // no creature → no-op

                            ICard pick;
                            if (agent != null)
                            {
                                var chosen = agent
                                    .ChooseFromBattlefieldAsync(pl, creatures, BotIntent.Removal)
                                    .GetAwaiter().GetResult();

                                // Validate the agent pick — still on this player's
                                // battlefield, still a creature, still theirs.
                                pick = (chosen != null
                                        && chosen.Zone == ZoneType.Battlefield
                                        && chosen.HasType(CardType.Creature)
                                        && ReferenceEquals(chosen.Controller, pl))
                                    ? chosen
                                    : creatures[0];
                            }
                            else
                            {
                                pick = creatures[0];
                            }

                            // CR 701.16 — sacrifice (bypasses Indestructible / regen).
                            OracleSpellBinder.MoveToGraveyard(pick, ZoneMoveReason.Sacrifice);
                        }

                        // --- Discard rider: "If you control a Liliana
                        // planeswalker, each opponent also discards a card."
                        // CR 608.2 — checked as the spell resolves. ---
                        var controlsLiliana = caster.Zones.Battlefield.GetCards()
                            .Any(c => c.HasType(CardType.Planeswalker)
                                      && c.HasSubtype(CardSubtype.Liliana));
                        if (!controlsLiliana) return;

                        foreach (var pl in opponents)
                        {
                            var hand = pl.Zones.Hand.GetCards().ToList();
                            if (hand.Count == 0) continue; // CR 701.8b — nothing to discard

                            // CR 701.8a — the discarding player chooses.
                            ICard discardPick;
                            if (agent != null)
                            {
                                var chosen = agent
                                    .ChooseFromHandAsync(pl, hand, BotIntent.Discard)
                                    .GetAwaiter().GetResult();
                                discardPick = (chosen != null
                                               && chosen.Zone == ZoneType.Hand
                                               && ReferenceEquals(chosen.Owner, pl))
                                    ? chosen
                                    : hand[0];
                            }
                            else
                            {
                                discardPick = hand[0];
                            }

                            pl.Zones.Hand.RemoveCard(discardPick);
                            pl.Zones.Graveyard.AddCard(discardPick);
                            discardPick.SetZone(ZoneType.Graveyard);
                        }
                    }),
            });
    }
}
