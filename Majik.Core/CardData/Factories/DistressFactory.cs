using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Distress (Mirrodin / various, {B}{B}).
///
/// Sorcery. Oracle text:
///   "Target player reveals their hand. You choose a nonland card from it.
///    That player discards that card."
///
/// <see cref="DuressFactory"/>-shape targeted discard, widened in two ways:
/// the filter excludes only lands (creatures ARE fair game, unlike Duress),
/// and the legal target is "target player" (any player, not just opponents).
/// Reveal (CR 701.16) → caster picks via
/// <see cref="IPlayerAgent.ChooseFromHandAsync"/> (intent HandHate;
/// deterministic first-legal fallback) → discard Hand → Graveyard.
/// </summary>
[CardName("Distress")]
public static class DistressFactory
{
    public const string CardName = "Distress";
    public const string PrintedManaCost = "{B}{B}";

    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the reveal → pick (nonland) → discard SpellDefinition.
    /// Single 1..1 "target player" request.
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
                new TargetRequest("target player", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Distress: reveal → caster picks nonland → discard", () =>
                    {
                        if (raw is not Player victim) return;

                        // CR 701.16 — reveal the hand.
                        RevealHelper.RevealHand(eventBus, victim, CardName);

                        // CR 700.2 — "You choose a nonland card." Only lands
                        // are excluded; creatures and other nonland cards are
                        // legal picks (wider than Duress's noncreature filter).
                        var legal = victim.Zones.Hand.GetCards()
                            .Where(c => !c.HasType(CardType.Land))
                            .ToList();

                        // Agent pick (intent HandHate) with deterministic
                        // first-legal fallback — same posture as Duress.
                        ICard? pick = null;
                        if (legal.Count > 0)
                        {
                            if (agent != null)
                            {
                                pick = agent
                                    .ChooseFromHandAsync(victim, legal, BotIntent.HandHate)
                                    .GetAwaiter().GetResult();
                                if (pick == null
                                    || pick.Zone != ZoneType.Hand
                                    || pick.HasType(CardType.Land)
                                    || !ReferenceEquals(pick.Owner, victim))
                                {
                                    pick = legal[0];
                                }
                            }
                            else
                            {
                                pick = legal[0];
                            }
                        }

                        // CR 701.16 — "That player discards that card."
                        if (pick != null)
                        {
                            victim.Zones.Hand.RemoveCard(pick);
                            victim.Zones.Graveyard.AddCard(pick);
                            pick.SetZone(ZoneType.Graveyard);
                        }
                    }),
                };
            });
    }
}
