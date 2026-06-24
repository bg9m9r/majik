using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Highway Robbery (Outlaws of Thunder Junction,
/// {1}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-24):
///   "You may discard a card or sacrifice a land. If you do, draw two cards.
///    Plot {1}{R} (You may pay {1}{R} and exile this card from your hand.
///    Cast it as a sorcery on a later turn without paying its mana cost.
///    Plot only as a sorcery.)"
///
/// ## Why it gets its own factory
/// Highway Robbery is a "pay an optional self-cost, then draw two" looter in
/// the family of <see cref="TormentingVoiceFactory"/> /
/// <see cref="ThrillOfPossibilityFactory"/> — discard-then-draw — but with two
/// twists: (1) the cost is an OPTIONAL "may" rather than a mandatory discard,
/// and (2) the cost is a CHOICE between discarding a card OR sacrificing a
/// land (CR 701.16 discard / CR 701.17 sacrifice). The draw is conditional —
/// "If you do" (CR 608.2j) — so it only happens when a card was discarded or a
/// land sacrificed. The base card shape (name / Sorcery type / {1}{R}) is
/// materialised from the embedded JSON definition
/// (<c>highway-robbery.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="ThrillOfPossibilityFactory"/>); the resolve effect is built on
/// demand via <see cref="BuildResolveEffect"/> because the choose-cost +
/// conditional-draw body is not expressible in the data-only JSON
/// <c>AbilityDefinition</c> schema.
///
/// ## Implemented (v1)
///
/// - Sorcery shape, mana cost {1}{R}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>): the controller
///   MAY discard a card or sacrifice a land; if a payment is made, draw two
///   cards (CR 608.2j "If you do" — the draw is conditional on the optional
///   action actually happening).
///   - Deterministic v1 policy (null agent): prefer discarding a card (last
///     card in hand) when the hand is non-empty; otherwise sacrifice a land
///     (last land on the battlefield) when one exists; otherwise do nothing
///     (no payment → no draw). Discard pick mirrors
///     <see cref="TormentingVoiceFactory"/>'s last-in-hand fallback.
///   - Agent policy: the agent's
///     <see cref="IPlayerAgent.ChooseFromHandAsync"/> with
///     <see cref="BotIntent.Discard"/> chooses the discard target when the
///     hand is non-empty; a null pick falls through to the land-sacrifice arm.
/// - Empty library: draws what's available, sets the
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> flag (CR 704.5b SBA
///   loss), and continues — same handling as
///   <see cref="TormentingVoiceFactory"/>.
///
/// ## Deviation from printed text (documented)
///
/// The printed "may" is a free choice between three options (discard /
/// sacrifice / decline). v1 ships a deterministic preference order
/// (discard > sacrifice land > decline) for the null-agent path because there
/// is no cast-time "choose one of {discard a card, sacrifice a land, decline}"
/// prompt primitive yet — same queue as the analogues' deferred discard pick.
/// A future PR can promote the choice to a real agent-driven mode prompt.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Plot (CR 718)</b>: the printed "Plot {1}{R}" rider is NOT yet wired —
///   Plot is not yet an engine primitive (cast-from-exile-on-a-later-turn
///   alt-cost cluster). Same deferral as
///   <see cref="SlickshotShowOffFactory"/>: ship the printed body, defer Plot
///   until its primitive lands.
/// - Agent-driven sacrifice-land pick prompt (currently last land on the
///   battlefield).
/// </summary>
[CardName("Highway Robbery")]
public static class HighwayRobberyFactory
{
    public const string CardName = "Highway Robbery";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "highway-robbery";

    public const string PrintedManaCost = "{1}{R}";
    public const int DrawCount = 2;

    /// <summary>
    /// Build the Highway Robbery sorcery shape from the embedded JSON
    /// definition. Card shape only — the resolve effect is built on demand
    /// via <see cref="BuildResolveEffect"/> (same split as
    /// <see cref="ThrillOfPossibilityFactory"/>).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build Highway Robbery's resolve effect — the controller may discard a
    /// card or sacrifice a land; if they do, draw two cards.
    /// </summary>
    /// <param name="caster">The player paying + drawing.</param>
    /// <param name="agent">Optional agent for the discard target selection.
    /// When null, the deterministic v1 policy (prefer last card in hand, then
    /// last land on battlefield) is used.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Highway Robbery: you may discard a card or sacrifice a land. If you do, draw two cards.", async ctx =>
            {
                // ----------------------------------------------------------
                // CR 608.2j — "If you do, draw two cards." The draw is gated
                // on actually paying the optional cost. `paid` tracks whether
                // a card was discarded (CR 701.16) or a land sacrificed
                // (CR 701.17).
                //
                // v1 deterministic preference: discard a card first (the
                // looter intent), else sacrifice a land, else decline (no
                // draw). Raw zone manipulation mirrors TormentingVoice /
                // ThrillOfPossibility; in production (wired ZoneService) the
                // Hand → Graveyard / Battlefield → Graveyard moves funnel
                // through CardMovedEvent.
                // ----------------------------------------------------------
                var paid = false;

                var hand = caster.Zones.Hand.GetCards().ToList();
                if (hand.Count > 0)
                {
                    // CR 701.16 — discard a card.
                    ICard? pick;
                    if (agent != null)
                    {
                        pick = (await agent.ChooseFromHandAsync(caster, hand, BotIntent.Discard).ConfigureAwait(false));
                        if (pick == null || pick.Zone != ZoneType.Hand)
                            pick = hand[^1];
                    }
                    else
                    {
                        pick = hand[^1];
                    }
                    caster.Zones.Hand.RemoveCard(pick);
                    caster.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                    paid = true;
                }
                else
                {
                    // CR 701.17 — sacrifice a land. Only reached when the hand
                    // is empty (deterministic v1 prefers the discard arm).
                    var land = caster.Zones.Battlefield.GetCards()
                        .LastOrDefault(c => c.HasType(CardType.Land));
                    if (land != null)
                    {
                        caster.Zones.Battlefield.RemoveCard(land);
                        caster.Zones.Graveyard.AddCard(land);
                        land.SetZone(ZoneType.Graveyard);
                        paid = true;
                    }
                }

                // ----------------------------------------------------------
                // CR 121.1 — "draw two cards", conditional on `paid`. Empty
                // library mid-draw flags the player for the SBA loss
                // (CR 704.5b) and short-circuits the remaining draws — same
                // handling as Tormenting Voice.
                // ----------------------------------------------------------
                if (!paid) return;

                for (var i = 0; i < DrawCount; i++)
                {
                    var top = caster.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        caster.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    caster.Zones.Library.RemoveCard(top);
                    caster.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            }),
        };
    }
}
