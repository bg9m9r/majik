using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Show and Tell (Urza's Saga, {2}{U}).
///
/// Sorcery. Oracle text:
///   "Each player may put an artifact, creature, enchantment, or land card
///    from their hand onto the battlefield."
///
/// ## Implementation
///
/// Resolves as one effect: iterate through every player in the supplied
/// order (typically turn-order starting with the controller) and, for
/// each player, allow them to optionally put one permanent card
/// (<see cref="Permanent"/> — covers artifact / creature / enchantment /
/// land per the card hierarchy under <c>Majik.Core/Cards/</c>) from hand
/// directly onto the battlefield.
///
/// CR 113.6c / 117.1a — putting a permanent directly onto the battlefield
/// is NOT casting; the move funnels through <see cref="ZoneService.MoveCard"/>
/// when a service is supplied so ETB triggers / replacements on the
/// permanent fire (CR 603.6a, CR 614). When no <c>ZoneService</c> is
/// supplied, the move falls back to raw zone manipulation suitable for
/// shape / unit tests (mirrors <see cref="StoneforgeMysticFactory"/>'s
/// activated-ability resolve).
///
/// CR 117 — the per-player "which permanent card" decision is a
/// resolution-time choice. The default selector (used when the caller
/// supplies <c>null</c>) is the deterministic first-eligible-permanent
/// pick used elsewhere in the engine (Stoneforge Mystic, Sun Titan).
/// Callers wanting a real agent prompt + decline path supply their own
/// per-player selector; returning <c>null</c> from the selector models
/// the "may" decline clause (CR 605.1 / 117.x — a may-effect that
/// produces no valid execution simply does nothing for that player).
///
/// ## v1 simplifications
/// - Player order is taken from <see cref="BuildResolveEffect"/>'s
///   <c>allPlayers</c> argument verbatim. CR 101.4 turn-order
///   resolution is the caller's responsibility (matches Wheel of Fortune
///   / other multiplayer-iteration factories).
/// - Eligibility is "card in hand that is a <see cref="Permanent"/>" —
///   <see cref="Sorcery"/> and <see cref="Instant"/> cards are filtered
///   out because they are NOT <see cref="Permanent"/> subclasses
///   (CR 110.4 — only artifact, creature, enchantment, land, planeswalker,
///   and battle are permanents; Show and Tell's text restricts to
///   artifact / creature / enchantment / land — a strict subset of
///   <see cref="Permanent"/> for the card pool we currently model).
/// - The default selector takes the first permanent card in the player's
///   hand. Real "any of N choices + opt-out" prompt deferred (same
///   queue as Stoneforge Mystic / Sun Titan).
/// </summary>
public static class ShowAndTellFactory
{
    public const string CardName = "Show and Tell";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>
    /// Build a Show and Tell sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so callers can splice it into a
    /// <see cref="SpellDefinition"/> or a
    /// <see cref="Majik.Core.Spells.Spell"/>.
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
    /// Build Show and Tell's resolve effect — for each player, optionally
    /// put one permanent card from that player's hand onto the battlefield.
    ///
    /// The default per-player selector (used when <paramref name="picker"/>
    /// is <c>null</c>) is the deterministic first-permanent-card-in-hand
    /// pick. Custom selectors can return <c>null</c> to model the "may"
    /// decline clause for a given player.
    /// </summary>
    /// <param name="allPlayers">All players in the game, in the order the
    /// effect should iterate (typically turn order starting with the
    /// controller per CR 101.4).</param>
    /// <param name="zoneService">Optional zone service. When supplied the
    /// hand → battlefield move funnels through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers / replacements on the put-in permanent fire
    /// (CR 603.6a / CR 614).</param>
    /// <param name="picker">Optional per-player picker. Called once per
    /// player at resolution time with that player's hand-permanents
    /// snapshot; returning <c>null</c> declines for that player.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers,
        ZoneService? zoneService = null,
        Func<Player, IReadOnlyList<Permanent>, Permanent?>? picker = null)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        picker ??= DefaultPicker;

        return new IEffect[]
        {
            new Effect(
                "Show and Tell: each player may put a permanent card from their hand onto the battlefield.",
                () =>
                {
                    foreach (var pl in allPlayers)
                    {
                        // CR 117.1a — choose the card to put in at resolution
                        // time. Snapshot the eligible hand-permanents so the
                        // selector sees a stable view. CR 110.4 — Permanent
                        // covers artifact / creature / enchantment / land
                        // (+ planeswalker / battle); Sorcery / Instant in
                        // hand are filtered out by the OfType<Permanent>().
                        var candidates = pl.Zones.Hand.GetCards()
                            .OfType<Permanent>()
                            .ToList();

                        var pick = picker(pl, candidates);

                        // CR 605.1 / 117.x — "may" decline path. No eligible
                        // permanent in hand, or the selector opts out, is a
                        // legal no-op for that player.
                        if (pick == null) continue;

                        // Sanity: a custom selector could return a card that
                        // is no longer in the player's hand. Skip silently
                        // rather than mis-route the move.
                        if (pick.Zone != ZoneType.Hand) continue;

                        // Move hand → battlefield. Prefer ZoneService so ETB
                        // triggers / replacements on the permanent fire
                        // (CR 603.6a, CR 614). Fall back to raw zone
                        // manipulation when no service is wired (test path).
                        if (zoneService != null)
                        {
                            zoneService.MoveCard(pick, ZoneType.Hand, ZoneType.Battlefield, pl);
                        }
                        else
                        {
                            pl.Zones.Hand.RemoveCard(pick);
                            pl.Zones.Battlefield.AddCard(pick);
                            pick.SetZone(ZoneType.Battlefield);
                            pick.SetController(pl);
                        }
                    }
                }),
        };
    }

    /// <summary>
    /// Default deterministic picker — first permanent card in the player's
    /// hand snapshot. Returns <c>null</c> if the snapshot is empty
    /// (a hand with only sorceries / instants resolves as a decline for
    /// that player).
    /// </summary>
    private static Permanent? DefaultPicker(Player _, IReadOnlyList<Permanent> candidates) =>
        candidates.FirstOrDefault();
}
