using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Inquisition of Kozilek (Rise of the Eldrazi, {B}).
///
/// Sorcery. Oracle text:
///   "Target player reveals their hand. You choose a nonland card from it
///    with mana value 3 or less. That player discards that card."
///
/// <see cref="ThoughtseizeFactory"/>-shape targeted discard with a nonland +
/// mana-value≤3 filter and no life cost. Reveal (CR 701.16) → caster picks
/// via <see cref="IPlayerAgent.ChooseFromHandAsync"/> (intent HandHate;
/// deterministic first-legal fallback) → discard Hand → Graveyard.
/// </summary>
[CardName("Inquisition of Kozilek")]
public static class InquisitionOfKozilekFactory
{
    public const string CardName = "Inquisition of Kozilek";
    public const string PrintedManaCost = "{B}";

    /// <summary>Printed mana-value cap on the discard pick (CR 700.2).</summary>
    public const int ManaValueCap = 3;

    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the reveal → pick (nonland, mana value ≤ 3) → discard
    /// SpellDefinition. Single 1..1 "target player" request.
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
                    new Effect("Inquisition of Kozilek: reveal → caster picks nonland mv≤3 → discard", () =>
                    {
                        if (raw is not Player victim) return;

                        // CR 701.16 — reveal the hand.
                        RevealHelper.RevealHand(eventBus, victim, CardName);

                        // CR 700.2 — "a nonland card ... with mana value 3 or less."
                        var legal = victim.Zones.Hand.GetCards()
                            .Where(c => !c.HasType(CardType.Land)
                                        && ManaCost.Parse(c.ManaCost).TotalValue <= ManaValueCap)
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
                                    || ManaCost.Parse(pick.ManaCost).TotalValue > ManaValueCap
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
