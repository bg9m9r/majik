using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Preordain (Magic 2011 / Modern Horizons 3, {U}).
///
/// Sorcery. Oracle text:
///   "Scry 2, then draw a card."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {U}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) runs the standard
///   <see cref="ScryAction"/> for N=2 — when an <see cref="IPlayerAgent"/>
///   is registered via <see cref="AgentRegistry"/> the controller decides
///   the bottom/top partition; otherwise the pre-agent default sends all
///   peeked cards to the bottom. Then the caster draws one card.
/// - Empty library: scry short-circuits (peek returns an empty list) and the
///   subsequent draw flags the player for the standard
///   draw-from-empty-library penalty via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
///
/// ## Notes
/// - <c>LibrarySpellFactory.ScryNSpell</c> already covers "scry N, then
///   draw" via the data-driven oracle binder's tail detection. This named
///   factory exists for direct construction in tests / dispatch parity with
///   other shipped cantrips (Consider, Ponder), and so the
///   <see cref="NamedCardFactory"/> path produces an identical card shape.
/// </summary>
public static class PreordainFactory
{
    public const string CardName = "Preordain";
    public const string PrintedManaCost = "{U}";
    private const int ScryAmount = 2;

    /// <summary>
    /// Build a Preordain sorcery owned by <paramref name="owner"/>. Card
    /// shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.
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
    /// Build Preordain's resolve effect — scry 2, then draw a card.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Preordain: scry 2, then draw a card.", () =>
            {
                // CR 701.20 — Scry 2. Look at the top two cards; the
                // controller chooses which (if any) to put on the bottom of
                // the library. Sourced from the registered agent when
                // available; the pre-agent default sends everything to the
                // bottom (same fallback as LibrarySpellFactory.ScryNSpell).
                var peeked = ScryAction.Peek(caster, ScryAmount);
                if (peeked.Count > 0)
                {
                    var agent = AgentRegistry.Get(caster);
                    ScryAction.ScryDecision decision;
                    if (agent != null)
                    {
                        // TODO: drop sync-over-async once IEffect.Execute becomes async.
                        decision = agent.ChooseScryDecisionAsync(null, peeked)
                            .GetAwaiter().GetResult();
                    }
                    else
                    {
                        decision = new ScryAction.ScryDecision(
                            ToBottom: peeked.ToList(),
                            TopOrder: Array.Empty<ICard>());
                    }
                    ScryAction.Apply(caster, peeked.Count, decision);
                }

                // "Then draw a card." Simple top-of-library draw; empty
                // library flags the player for the SBA-driven loss
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
