using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for All Is Dust (Rise of the Eldrazi, {7}).
///
/// Tribal Sorcery — Eldrazi. Oracle text:
///   "Each player sacrifices all colored permanents they control."
///
/// ## Implemented (v1)
/// - Tribal Sorcery — Eldrazi at {7}, colourless (no coloured pips in
///   the printed cost so <see cref="CardColors.GetColors"/> returns the
///   empty set — All Is Dust does not sacrifice itself off the stack
///   because the stack is not a "permanent" zone, CR 110.1 / CR 405).
/// - The card carries both <see cref="CardType.Sorcery"/> and
///   <see cref="CardType.Tribal"/> (CR 308 — the legacy Tribal card
///   type is set independently of Sorcery; the Eldrazi subtype piggy-
///   backs on the Tribal type rather than the Sorcery type per
///   CR 308.2).
/// - <b>Resolve effect</b> via <see cref="BuildResolveEffect"/>:
///   each supplied player sacrifices every coloured permanent they
///   control. A permanent counts as "coloured" when
///   <see cref="CardColors.GetColors"/> returns one or more
///   <see cref="ManaColor"/> entries (excluding Generic / Colorless).
///   The Eldrazi titans printed with no coloured pips (Emrakul,
///   Ulamog, Kozilek, Endless One, etc.) survive — that's the whole
///   reason the deck plays All Is Dust. Tokens with an explicit
///   colour override (Wurmcoil Engine's Phyrexian Wurms are colourless;
///   any white / black / red token via Ocelot Pride / Bitterblossom /
///   Goblin Rabblemaster IS coloured) participate via the
///   <see cref="Card.TokenColorsOverride"/> path of
///   <see cref="CardColors.GetColors"/>.
///
/// ## Sacrifice routing (CR 701.16)
/// Each coloured permanent is moved to its owner's graveyard via
/// <see cref="ZoneService.MoveCard"/> with destination
/// <see cref="ZoneType.Graveyard"/>; when no <see cref="ZoneService"/>
/// is supplied the move falls back to raw-zone shuffles. Sacrifice is
/// NOT a destroy effect (CR 701.16 vs CR 701.7), so indestructible
/// (CR 702.12b) does NOT save coloured permanents from All Is Dust —
/// e.g. Avacyn, Angel of Hope (white, indestructible) is sacrificed
/// the same as any other coloured creature. Routing through the zone
/// service rather than <see cref="CardData.OracleSpellBinder.MoveToGraveyard"/>
/// avoids the indestructible / regeneration gate which only applies
/// to the Destroy reasons.
///
/// ## APNAP order (CR 101.4)
/// The resolve effect honours the supplied player order — callers
/// pass <c>[apActive, nap1, nap2, …]</c>. Each player sacrifices
/// independently before the next player begins; within a single
/// player the snapshot-then-sacrifice loop avoids "collection
/// modified" errors. v1 does NOT prompt for sacrifice ordering
/// within a player (CR 701.16b — "the player chooses which of the
/// affected permanents to sacrifice"); All Is Dust says "sacrifices
/// all" so ordering is observationally irrelevant for the final
/// game state, only for trigger ordering across LTB effects.
///
/// ## Stack-cast snapshot (CR 608.2)
/// The resolve effect ignores All Is Dust itself — at resolution
/// time the sorcery is on the stack, not the battlefield, so it's
/// not eligible to be sacrificed. After resolving it moves to its
/// owner's graveyard via the standard sorcery cleanup flow
/// (CR 608.2f), not through this effect.
///
/// ## Cross-card interactions (correct by oracle)
/// - Pithing Needle (artifact, colourless, no coloured pips) —
///   survives.
/// - Karn, the Great Creator (colourless planeswalker) — survives.
/// - Liliana of the Veil (black planeswalker) — sacrificed.
/// - Aether Vial (artifact, colourless) — survives.
/// - Mox Opal (artifact, colourless) — survives.
/// - Birds of Paradise (green creature) — sacrificed.
/// - Mutavault (land, no colour pip — the activated ability that
///   turns it into a creature gives it ALL creature types but its
///   PRINTED colour is none; CardColors.GetColors reads the printed
///   mana cost which is empty) — survives.
/// - Celestial Colonnade (creature-land with W/U mana cost on the
///   ACTIVATED ability but no printed pips on the land itself) —
///   v1 keys off printed mana cost so Colonnade survives. Paper
///   ruling: same — the land's colour is determined by printed
///   characteristics, not the active animation.
///
/// ## Deferred (v1 gaps)
/// - In-player sacrifice ordering prompt — see APNAP note above.
/// - LTB trigger interleaving across the sweep — relies on
///   downstream trigger registration (Bitterblossom etc.); the
///   resolve body emits sacrifices in zone-iteration order per
///   player which is deterministic but not authored by an agent.
/// </summary>
[CardName("All Is Dust")]
public static class AllIsDustFactory
{
    public const string CardName = "All Is Dust";
    public const string PrintedManaCost = "{7}";

    /// <summary>
    /// Build All Is Dust owned and controlled by <paramref name="owner"/>.
    /// Card shape only — wire the resolve effect via
    /// <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Eldrazi });

        // CR 308 — the legacy Tribal card type. Layered on top of
        // Sorcery so the Eldrazi subtype is grammatically grounded
        // (CR 308.2). Idempotent — AddCardType skips duplicates.
        card.AddCardType(CardType.Tribal);

        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build All Is Dust's resolve effect — each supplied player
    /// sacrifices every coloured permanent they control. Each
    /// sacrificed permanent routes to its owner's graveyard via
    /// <paramref name="zoneService"/> when supplied so
    /// <see cref="Events.CardMovedEvent"/> fires for downstream LTB /
    /// dies triggers; absent a service the move falls back to raw-
    /// zone shuffles (suitable for unit tests).
    /// </summary>
    /// <param name="players">All players whose battlefields should be
    /// swept. Typically <c>[apActive, nap1, nap2, …]</c> in APNAP
    /// order (CR 101.4).</param>
    /// <param name="zoneService">Optional zone service. When supplied,
    /// sacrifices route through <see cref="ZoneService.MoveCard"/> so
    /// the event bus + replacement system see the move.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> players,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(players);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: each player sacrifices all coloured permanents they control (CR 701.16).",
                () =>
                {
                    foreach (var pl in players)
                    {
                        // Snapshot the battlefield up front — the zone is
                        // mutated in place by the sacrifice loop below.
                        var coloured = pl.Zones.Battlefield.GetCards()
                            .Where(IsColouredPermanent)
                            .ToList();

                        foreach (var permanent in coloured)
                        {
                            SacrificePermanent(permanent, zoneService);
                        }
                    }
                }),
        };
    }

    /// <summary>
    /// CR 105 / CR 111.4 — a permanent is "coloured" when its colour
    /// set (printed mana cost pips, or explicit token colour override)
    /// contains one or more of W/U/B/R/G. Colourless permanents
    /// (Eldrazi titans, most artifacts, Wastes lands) return an empty
    /// set.
    /// </summary>
    public static bool IsColouredPermanent(ICard card)
    {
        if (card == null) return false;
        var colours = CardColors.GetColors(card);
        if (colours.Count == 0) return false;

        // Defensive: the colour set is built to skip Generic /
        // Colorless, but in case a future override smuggles them in,
        // re-filter here.
        foreach (var c in colours)
        {
            if (c != ManaColor.Generic && c != ManaColor.Colorless) return true;
        }
        return false;
    }

    /// <summary>
    /// CR 701.16 — sacrifice <paramref name="permanent"/> by moving it
    /// from its current controller's battlefield to its owner's
    /// graveyard. Routes through <paramref name="zoneService"/> when
    /// supplied so <see cref="Events.CardMovedEvent"/> fires; absent
    /// the service the move falls back to raw-zone shuffles.
    ///
    /// Sacrifice bypasses indestructible (CR 702.12b — only "destroy"
    /// effects are gated by indestructible) so this routing
    /// intentionally does NOT pass through
    /// <see cref="CardData.OracleSpellBinder.MoveToGraveyard"/>'s
    /// destroy gate.
    /// </summary>
    private static void SacrificePermanent(ICard permanent, ZoneService? zoneService)
    {
        var owner = permanent.Owner;
        if (owner == null) return;

        if (zoneService != null)
        {
            zoneService.MoveCard(
                permanent,
                ZoneType.Battlefield,
                ZoneType.Graveyard,
                owner);
            return;
        }

        var currentController = permanent.Controller ?? owner;
        currentController.Zones.Battlefield.RemoveCard(permanent);
        owner.Zones.Graveyard.AddCard(permanent);
        permanent.SetZone(ZoneType.Graveyard);
    }
}
