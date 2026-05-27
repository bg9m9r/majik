using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Coercion (various sets, {2}{B}).
///
/// Sorcery. Oracle text:
///   "Target opponent reveals their hand. You choose a card from it.
///    That player discards that card."
///
/// Identical to Duress / Thoughtseize-shape discard but with NO filter
/// (any card — creature, land, or spell is a legal pick) and NO life loss.
/// Reveal (CR 701.16) → caster picks via
/// <see cref="IPlayerAgent.ChooseFromHandAsync"/> (intent HandHate;
/// deterministic first-card fallback) → discard Hand → Graveyard.
/// </summary>
[CardName("Coercion")]
public static class CoercionFactory
{
    public const string CardName = "Coercion";
    public const string PrintedManaCost = "{2}{B}";

    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the reveal → pick (any card) → discard SpellDefinition.
    /// Single 1..1 "target opponent" request.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        IPlayerAgent? agent,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target opponent", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Coercion: reveal → caster picks any card → discard", () =>
                    {
                        if (raw is not Player victim) return;

                        // CR 701.16 — reveal the hand.
                        RevealHelper.RevealHand(eventBus, victim, CardName);

                        // CR 700.2 — "You choose a card." No filter:
                        // creatures, lands, and spells are all legal picks.
                        var candidates = victim.Zones.Hand.GetCards().ToList();

                        ICard? pick = null;
                        if (candidates.Count > 0)
                        {
                            if (agent != null)
                            {
                                pick = agent
                                    .ChooseFromHandAsync(victim, candidates, BotIntent.HandHate)
                                    .GetAwaiter().GetResult();
                                // Validate the agent's pick: must be in hand,
                                // owned by victim; fall back to first if invalid.
                                if (pick == null
                                    || pick.Zone != ZoneType.Hand
                                    || !ReferenceEquals(pick.Owner, victim))
                                {
                                    pick = candidates[0];
                                }
                            }
                            else
                            {
                                pick = candidates[0];
                            }
                        }

                        // CR 701.16 — "That player discards that card."
                        if (pick != null)
                        {
                            victim.Zones.Hand.RemoveCard(pick);
                            victim.Zones.Graveyard.AddCard(pick);
                            pick.SetZone(ZoneType.Graveyard);
                        }
                        // No life loss — unlike Thoughtseize, Coercion has
                        // no printed life-loss clause.
                    }),
                };
            });
    }
}
