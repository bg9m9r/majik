using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hibernation (Visions, {1}{U}).
///
/// Instant. Oracle text:
///   "Return all green creatures to their owners' hands."
///
/// ## Implemented (v1)
/// - Instant {1}{U}, owner/controller wired.
/// - <see cref="BuildResolveEffect"/> wires the resolve body: snapshot every
///   <see cref="Creature"/> on every supplied player's battlefield whose
///   <see cref="CardColors.GetColors"/> contains <see cref="ManaColor.Green"/>,
///   then bounce each to its owner's hand via <see cref="Fx.BounceToHand"/>
///   (routes through <see cref="ZoneService"/> when supplied so LTB / ETB
///   replacement effects fire correctly).
/// - Symmetric — Hibernation hits every green creature regardless of
///   controller (printed text is unqualified). Snapshotting up front
///   avoids "collection modified" on in-place zone mutation, same pattern
///   as <see cref="WrathOfGodFactory.BuildResolveEffect"/>.
///
/// ## CR alignment
/// - <b>CR 105 / CardColors.GetColors</b>: colour is computed from the
///   printed mana cost (W/U/B/R/G pips). Hybrid pips contribute both
///   listed colours; Phyrexian pips contribute the named colour. Tokens
///   without a printed cost surface their colour via the explicit
///   <c>TokenColorsOverride</c> on the card (Llanowar Elf token, Sproutback
///   Trudge token, etc.) which <see cref="CardColors.GetColors"/> reads
///   first. A green creature whose only colour comes from a "becomes the
///   chosen colour" continuous effect (Conspiracy etc.) is not caught at
///   v1 — those effects aren't yet surfaced into <see cref="CardColors"/>
///   and the broader colour-by-effect pipeline is a separate retrofit.
/// - <b>CR 701.20</b>: each bounce is a "return to owner's hand" — owner
///   resolves at the moment of the return, mirroring
///   <see cref="VaporSnagFactory"/>'s single-target shape.
///
/// ## Dispatcher posture
/// Shape-only on the dispatcher path (<see cref="Create(Player)"/> builds
/// the instant card). The resolve effect is built on demand by
/// <see cref="BuildResolveEffect"/> — matches Wrath of God / Boil /
/// Meltdown convention where sweep bodies are caller-wired against a
/// concrete list of players (<c>Game.Players</c> in production).
/// </summary>
[CardName("Hibernation")]
public static class HibernationFactory
{
    public const string CardName = "Hibernation";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>
    /// Build Hibernation as an Instant card with owner/controller wired.
    /// Card shape only — wire the resolve effect via
    /// <see cref="BuildResolveEffect"/>.
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
    /// Build Hibernation's resolve effect — return every green
    /// <see cref="Creature"/> on every supplied player's battlefield to
    /// its owner's hand. Each return is routed via
    /// <see cref="Fx.BounceToHand"/>; when <paramref name="zoneService"/>
    /// is supplied the move flows through <see cref="ZoneService.MoveCard"/>
    /// so LTB / ETB events + replacement effects fire.
    /// </summary>
    /// <param name="allPlayers">Players whose battlefields are scanned.
    /// Typically <c>Game.Players</c>; pass <c>new[] { caster }</c> for
    /// controller-only resolution (off-oracle).</param>
    /// <param name="zoneService">Optional ZoneService for bus-aware zone
    /// moves. When null, raw-zone fallback inside
    /// <see cref="Fx.BounceToHand"/> is used.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"{CardName}: return all green creatures to their owners' hands.", () =>
            {
                // Snapshot every battlefield up front — BounceToHand mutates
                // the source zone in place, and we want a deterministic
                // "all currently-green creatures" set per CR 608.2 (effects
                // observe game state at resolution, then act on that snapshot).
                foreach (var pl in allPlayers)
                {
                    var greens = pl.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Where(c => CardColors.GetColors(c).Contains(ManaColor.Green))
                        .ToList();
                    foreach (var creature in greens)
                    {
                        // CR 701.20 — return to owner's hand. Fx routes
                        // through ZoneService when supplied so the move
                        // fires LTB / zone-change events and replacement
                        // effects can rewrite it.
                        Fx.BounceToHand(creature, zoneService);
                    }
                }
            }),
        };
    }
}
