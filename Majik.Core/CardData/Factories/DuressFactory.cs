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
/// Named-card factory for Duress (various sets, {B}).
///
/// Sorcery. Oracle text:
///   "Target opponent reveals their hand. You choose a noncreature, nonland
///    card from it. That player discards that card."
///
/// <see cref="ThoughtseizeFactory"/>-shape targeted discard with a
/// noncreature + nonland filter and no life cost. Reveal (CR 701.16) → caster
/// picks via <see cref="IPlayerAgent.ChooseFromHandAsync"/> (intent
/// HandHate; deterministic first-legal fallback) → discard Hand → Graveyard.
/// </summary>
[CardName("Duress")]
public static class DuressFactory
{
    public const string CardName = "Duress";
    public const string PrintedManaCost = "{B}";

    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the reveal → pick (noncreature, nonland) → discard
    /// SpellDefinition. Single 1..1 "target opponent" request.
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
                    new Effect("Duress: reveal → caster picks noncreature nonland → discard", () =>
                    {
                        if (raw is not Player victim) return;

                        // CR 701.16 — reveal the hand.
                        RevealHelper.RevealHand(eventBus, victim, CardName);

                        // CR 700.2 — "You choose a noncreature, nonland card."
                        var legal = victim.Zones.Hand.GetCards()
                            .Where(c => !c.HasType(CardType.Land) && !c.HasType(CardType.Creature))
                            .ToList();

                        // Agent pick (intent HandHate) with deterministic
                        // first-legal fallback — same posture as Thoughtseize.
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
                                    || pick.HasType(CardType.Creature)
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
