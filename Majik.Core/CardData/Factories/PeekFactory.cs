using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Peek (Onslaught / Mystery Booster / many reprints, {U}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Look at target player's hand.
///    Draw a card."
///
/// A cheap blue cantrip with a hidden-information rider. The "look at target
/// player's hand" half inspects the target's hand without moving any card or
/// changing its visibility to the rest of the table — same posture as
/// <see cref="UrzasBaubleFactory"/>'s look-at-hand. It is deliberately NOT a
/// public reveal (CR 701.16 / <see cref="Majik.Core.Events.CardRevealedEvent"/>):
/// "look at" is private to the spell's controller, so we do not publish a
/// reveal event. The cantrip then draws one card for the caster.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target player"
///   request. On resolution:
///     1. <b>Look at target player's hand</b> — information-only inspection;
///        the hand snapshot is materialised (the caster "sees" it) but no
///        card leaves the target's hand and no zone change occurs. Mirrors
///        <see cref="UrzasBaubleFactory"/>'s deferred look-at-hand posture.
///     2. <b>Draw a card</b> (CR 121.1) — simple top-of-library draw for the
///        caster. Empty library flags the controller for the SBA-driven
///        loss (CR 704.5b) via
///        <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> — same posture
///        as <see cref="OptFactory"/> / <see cref="BrainstormFactory"/>.
/// - <b>Illegal target</b> (CR 608.2b): a single illegal target makes the
///   whole spell do nothing — including the cantrip draw. Parity with
///   <see cref="ThoughtseizeFactory"/>'s fizzle guard.
/// - <b>Self-target</b>: "target player" may be the caster (CR 115.3); the
///   look-at-own-hand is legal and the cantrip still draws.
///
/// ## Deferred (v1 gaps)
/// - <b>Hidden-information surfacing to the controller's UI</b>: the look-at
///   half does not publish a private "you see the target's hand" delta —
///   the engine has no audience-scoped reveal channel yet
///   (<see cref="Majik.Core.Events.CardRevealedEvent"/> is a PUBLIC reveal,
///   wrong for "look at"). When a controller-scoped inspection event lands
///   this factory can opt in without changing the rules-visible outcome.
/// </summary>
[CardName("Peek")]
public static class PeekFactory
{
    public const string CardName = "Peek";
    public const string PrintedManaCost = "{U}";

    /// <summary>
    /// Build a Peek instant owned and controlled by <paramref name="owner"/>.
    /// Card shape only — the resolve-time target request + look/draw body is
    /// built on demand via <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Peek is cast. Single
    /// 1..1 "target player" request; on resolution the caster looks at the
    /// target's hand (information-only, no zone change) and then draws a card.
    /// </summary>
    /// <param name="caster">Cast-time controller — looks at the target's hand
    /// and draws the cantrip card.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.HandHate),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Peek: look at target player's hand, then draw a card.", () =>
                    {
                        // CR 608.2b — single illegal target (target player
                        // left the game, etc.): the spell does nothing,
                        // including the cantrip draw. Parity with
                        // Thoughtseize's fizzle guard.
                        if (raw is not Player target) return;

                        // "Look at target player's hand." Hidden-information
                        // inspection only — the caster sees the hand, but no
                        // card leaves it and no zone change occurs. NOT a
                        // public reveal (CR 701.16): "look at" is private to
                        // the controller, so no CardRevealedEvent is
                        // published. Same information-only posture as
                        // Urza's Bauble's look-at-hand. The snapshot
                        // materialises the "look" without mutation.
                        _ = target.Zones.Hand.GetCards().ToList();

                        // "Draw a card." (CR 121.1) Simple top-of-library
                        // draw for the caster. Empty library flags the
                        // SBA-driven loss (CR 704.5b) via
                        // MarkTriedToDrawFromEmptyLibrary — same posture as
                        // Opt / Brainstorm.
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
            });
    }
}
