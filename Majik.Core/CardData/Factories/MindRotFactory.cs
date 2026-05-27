using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mind Rot (Core Sets, {2}{B}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Target player discards two cards."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}, black.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target player"
///   request. On resolution the target player discards two cards of their
///   own choice (CR 701.7a — the discarding player chooses what to
///   discard). If the hand contains fewer than two cards the player
///   discards as many as possible (CR 701.7c — can't discard what you
///   don't have).
///
/// ## Why a named factory when DiscardTemplate already matches
///
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Resource.DiscardTemplate"/>
/// covers the oracle-text pattern with a deterministic first-card pick
/// (<see cref="Majik.Core.CardData.SpellTemplates.Templates.Resource.ResourceSpellFactory.DiscardNSpell"/>).
/// The named factory upgrades the pick to agent-driven
/// (<see cref="IPlayerAgent.ChooseFromHandAsync"/>) so the target player
/// actively chooses both discards. Source-generated dispatch
/// (<see cref="NamedCardFactory"/>) prefers the factory; the template
/// remains as fallback for oracle-text matches not in the named registry.
///
/// ## Deferred (v1 gaps)
/// - Targeting prompts are supplied by the caster's agent; the target
///   restriction is "any player" (no filter beyond "is a Player").
/// </summary>
[CardName("Mind Rot")]
public static class MindRotFactory
{
    public const string CardName = "Mind Rot";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>
    /// Build a Mind Rot sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve-time
    /// target request + discard body is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Mind Rot is cast.
    /// Single 1..1 "target player" request; on resolution that player
    /// discards two cards of their own choice (agent-driven when
    /// <paramref name="targetAgent"/> is non-null; deterministic
    /// first-card fallback otherwise).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="Game.GameContext"/> (chosen target → live game object).
    /// </param>
    /// <param name="targetAgent">Optional agent for the TARGET player's
    /// discard picks. When non-null, each discard calls
    /// <see cref="IPlayerAgent.ChooseFromHandAsync"/>
    /// (<see cref="BotIntent.Discard"/>) with the remaining hand as the
    /// candidate list. Null falls back to the deterministic first-card
    /// pick (CR 701.7a — player chooses; test fixtures that don't wire
    /// an agent still produce deterministic output).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        IPlayerAgent? targetAgent)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Mind Rot: target player discards two cards", () =>
                    {
                        // CR 608.2b — illegal-target check.
                        if (raw is not Player victim) return;

                        // Discard up to 2 cards. Each pick is made by the
                        // target player's agent (CR 701.7a — "that player
                        // discards … of their choice"). Null/no-agent →
                        // deterministic first-card pick (matches Liliana
                        // of the Veil +1 v1 fallback).
                        for (var i = 0; i < 2; i++)
                        {
                            var hand = victim.Zones.Hand.GetCards().ToList();
                            if (hand.Count == 0) break; // CR 701.7c

                            ICard? pick;
                            if (targetAgent != null)
                            {
                                pick = targetAgent
                                    .ChooseFromHandAsync(victim, hand, BotIntent.Discard)
                                    .GetAwaiter().GetResult();
                                if (pick == null || pick.Zone != ZoneType.Hand)
                                    pick = hand[0];
                            }
                            else
                            {
                                pick = hand[0];
                            }

                            victim.Zones.Hand.RemoveCard(pick);
                            victim.Zones.Graveyard.AddCard(pick);
                            pick.SetZone(ZoneType.Graveyard);
                        }
                    }),
                };
            });
    }
}
