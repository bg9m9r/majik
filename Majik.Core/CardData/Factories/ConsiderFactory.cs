using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Consider (Innistrad: Midnight Hunt, {U}).
///
/// Instant. Oracle text:
///   "Look at the top card of your library. You may put that card into your
///    graveyard. Then draw a card."
///
/// Effectively Surveil 1 (CR 701.42) followed by drawing a card.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) runs the standard
///   <see cref="SurveilAction"/> path for N=1 — when an
///   <see cref="IPlayerAgent"/> is registered via <see cref="AgentRegistry"/>
///   the controller decides whether to mill the peeked card; otherwise the
///   pre-agent default sends the peeked card to the graveyard. Then the
///   caster draws one card.
/// - Empty library: surveil short-circuits (peek returns an empty list) and
///   the subsequent draw flags the player for the standard
///   draw-from-empty-library penalty via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
///
/// ## Deferred (v1 gaps)
/// - Bot-side surveil decision quality lives in the agent implementations
///   (<see cref="HeuristicBotAgent"/> / <see cref="DeterministicBotAgent"/>);
///   this factory just consults whichever agent is registered.
/// </summary>
public static class ConsiderFactory
{
    public const string CardName = "Consider";
    public const string PrintedManaCost = "{U}";

    /// <summary>
    /// Build a Consider instant owned by <paramref name="owner"/>. Card shape
    /// only — the resolve effect is built on-demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can plug it
    /// into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
    /// directly to a <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Consider's resolve effect — surveil 1, then draw a card. Returns
    /// a single <see cref="IEffect"/> entry so callers can splice it into a
    /// <c>SpellDefinition.EffectFactory</c> result or a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Consider: surveil 1, then draw a card.", () =>
            {
                // CR 701.42 — Surveil 1. Look at the top card; the controller
                // chooses whether to send it to the graveyard or leave it on
                // top. Decision is sourced from the registered agent when
                // available, falling back to the pre-agent default
                // (all-to-graveyard) when none is registered. This is the
                // same flow LibrarySpellFactory.SurveilSelfSpell uses.
                var peeked = SurveilAction.Peek(caster, 1);
                if (peeked.Count > 0)
                {
                    var agent = AgentRegistry.Get(caster);
                    SurveilAction.SurveilDecision decision;
                    if (agent != null)
                    {
                        // TODO: drop sync-over-async once IEffect.Execute becomes async.
                        decision = agent.ChooseSurveilDecisionAsync(null, peeked)
                            .GetAwaiter().GetResult();
                    }
                    else
                    {
                        decision = new SurveilAction.SurveilDecision(
                            ToGraveyard: peeked.ToList(),
                            TopOrder: Array.Empty<ICard>());
                    }
                    SurveilAction.Apply(caster, 1, decision);
                }

                // CR 121.1 — "Then draw a card." Simple top-of-library draw;
                // empty library flags the player for the SBA-driven loss
                // (CR 704.5b) via MarkTriedToDrawFromEmptyLibrary.
                var top = caster.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    caster.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                caster.Zones.Library.RemoveCard(top);
                caster.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }),
        };
    }
}
