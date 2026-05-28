using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tainted Indulgence (Streets of New Capenna,
/// {U}{B}).
///
/// Instant. Oracle text:
///   "Draw two cards. Then discard a card unless there are five or more
///    mana values among cards in your graveyard."
///
/// ## Rules
/// - CR 121.1 — Draw two cards (draw-2 body, instant timing; analogous to
///   <see cref="DivinationFactory"/> as an instant-speed draw-2).
/// - CR 701.16 — "Then discard a card" is the DEFAULT outcome. The unless-
///   clause (CR 702.* — "unless" conditional skip) skips the discard when
///   the controller's graveyard contains cards spanning five or more
///   distinct mana values at resolution time.
/// - "Mana values among cards in your graveyard" counts DISTINCT mana
///   values across all cards in the controller's graveyard at resolution
///   (CR 202.3 — mana value of a card with no mana cost is 0; lands
///   and zero-mana cards both contribute MV 0, the same as other cards
///   with MV 0, and count only once toward the distinct-count).
/// - "Five or more" (inclusive) → discard is skipped (net +2 hand).
/// - Fewer than 5 distinct MVs (including empty graveyard) → discard one
///   (net +1 hand).
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}{B} (blue + black, MV 2).
/// - <see cref="BuildSpellDefinition"/>: no modes, no X, no targets.
/// - <see cref="BuildResolveEffect"/>: draws 2 via <see cref="Fx.DrawCards"/>,
///   then evaluates the unless-condition; if fewer than
///   <see cref="UnlessThreshold"/> distinct mana values are in the
///   controller's graveyard, discards one card.
/// - Agent-or-last-card discard fallback: same policy as
///   <see cref="TormentingVoiceFactory"/>.
/// - Empty-library handling: draws what is available and stamps
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> (CR 704.5b);
///   the discard check still runs afterward regardless.
///
/// ## Deferred (v1 gaps)
/// - Agent-driven discard target prompt (currently last-card-in-hand pick).
/// - Replacement-bus interactions on individual draws (e.g. Dredge) route
///   through <see cref="Fx.DrawCards"/> so they fire automatically.
/// </summary>
[CardName("Tainted Indulgence")]
public static class TaintedIndulgenceFactory
{
    public const string CardName = "Tainted Indulgence";
    public const string PrintedManaCost = "{U}{B}";

    /// <summary>
    /// Distinct-mana-value threshold (inclusive) at which the discard is
    /// skipped. Printed oracle text: "five or more mana values".
    /// </summary>
    public const int UnlessThreshold = 5;

    /// <summary>
    /// Construct Tainted Indulgence as an Instant owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve effect is
    /// built on demand via <see cref="BuildResolveEffect"/>.
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
    /// Build the <see cref="SpellDefinition"/> for Tainted Indulgence.
    /// No modes, no X, no target requests.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster));
    }

    /// <summary>
    /// Build the resolve effect:
    /// 1. Draw two cards (CR 121.1, via <see cref="Fx.DrawCards"/>).
    /// 2. Unless the controller's graveyard contains ≥5 distinct mana
    ///    values, discard one card (CR 701.16).
    /// </summary>
    /// <param name="caster">The player drawing and potentially discarding.</param>
    /// <param name="agent">Optional agent for discard target selection.
    /// When null, the deterministic last-card-in-hand fallback is used.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect(
                $"{CardName}: draw two cards, then discard unless ≥5 distinct MV in graveyard.",
                () =>
                {
                    // -------------------------------------------------------
                    // CR 121.1 — draw 2. Routes through Fx.DrawCards so the
                    // replacement bus (Dredge, etc.) fires per draw; empty
                    // library stamps the SBA loss flag (CR 704.5b) and stops
                    // drawing without throwing.
                    // -------------------------------------------------------
                    Fx.DrawCards(caster, 2);

                    // -------------------------------------------------------
                    // Unless-clause check (CR 202.3 / printed oracle text):
                    // count distinct mana values among cards in the controller's
                    // graveyard AT resolution. MV of a card with no printed
                    // mana cost (e.g. lands, suspend castings) is 0, which
                    // counts as a distinct value if any such card is present
                    // (same as the comprehensive rules' MV definition).
                    // -------------------------------------------------------
                    if (GraveyardDistinctMvCount(caster) >= UnlessThreshold)
                        return; // discard is skipped.

                    // -------------------------------------------------------
                    // CR 701.16 — discard a card. Empty hand → no-op
                    // (the unless already skipped when applicable; if hand
                    // is now empty after drawing into nothing the discard
                    // clause has no effect). Agent-or-last-card policy mirrors
                    // TormentingVoiceFactory.
                    // -------------------------------------------------------
                    var hand = caster.Zones.Hand.GetCards().ToList();
                    if (hand.Count == 0) return;

                    ICard pick;
                    if (agent != null)
                    {
                        var chosen = agent.ChooseFromHandAsync(caster, hand, BotIntent.Discard)
                            .GetAwaiter().GetResult();
                        pick = (chosen != null && chosen.Zone == ZoneType.Hand)
                            ? chosen
                            : hand[^1];
                    }
                    else
                    {
                        pick = hand[^1];
                    }

                    caster.Zones.Hand.RemoveCard(pick);
                    caster.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                }),
        };
    }

    /// <summary>
    /// Returns the count of distinct mana values among all cards currently
    /// in <paramref name="controller"/>'s graveyard. Used to evaluate the
    /// "five or more mana values" unless-clause at resolution time.
    ///
    /// CR 202.3: the mana value of a card with no mana cost is 0. Lands and
    /// other zero-cost / no-cost permanents contribute MV 0 and are counted
    /// as one distinct value regardless of how many such cards are present.
    /// </summary>
    public static int GraveyardDistinctMvCount(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Graveyard
            .GetCards()
            .Select(c => ManaCost.Parse(c.ManaCost).TotalValue)
            .Distinct()
            .Count();
    }
}
