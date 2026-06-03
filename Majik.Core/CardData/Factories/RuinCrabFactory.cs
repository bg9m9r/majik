using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ruin Crab (Zendikar Rising, {U}).
///
/// Creature — Crab 0/3. Oracle text:
///   "Landfall — Whenever a land you control enters, each opponent mills
///    three cards. (To mill a card, a player puts the top card of their
///    library into their graveyard.)"
///
/// The "Ruin Crab is to Hedron Crab" of Zendikar Rising — same {U} landfall
/// mill-3 one-drop, but UNTARGETED: it hits <i>each opponent</i> rather than
/// a single chosen target player. This factory therefore reuses
/// <see cref="HedronCrabFactory"/>'s landfall trigger predicate
/// (<see cref="Triggers.OnLandEntersUnderControl"/>) but resolves like
/// <see cref="ThievesGuildEnforcerFactory"/>'s "each opponent mills N" body
/// (iterate the player list via an <c>allPlayersResolver</c>, skip the
/// controller, <see cref="MillAction.Apply"/> per opponent).
///
/// ## Implemented (v1)
/// - 0/3 Creature — Crab, mana cost {U}, owner / controller wired.
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 614 / CR 702.142)
///   — fires on <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to
///   "land entering the battlefield under controller's control" via the
///   shared <see cref="Triggers.OnLandEntersUnderControl"/> predicate.
/// - <b>NO TargetRequest</b> (CR 115.1a) — "each opponent" is not a target.
/// - <b>Resolve — each opponent mills 3</b>: enumerates the players supplied
///   by <c>allPlayersResolver</c>, skips the controller (CR 102.1), and mills
///   <see cref="MillCount"/> per opponent via <see cref="MillAction.Apply"/>
///   (CR 701.13b). A library shorter than 3 mills all remaining cards without
///   that player losing the game (CR 701.13a).
///
/// ## Deferred (v1 gaps)
/// - <b>Player-list resolver</b>: the no-resolver <see cref="Create(Player)"/>
///   path builds the trigger for shape inspection but mills nobody at
///   resolution (there is no engine handle to the opponent list from the
///   card alone). Live games supply the resolver via the
///   <see cref="Create(Player, TriggerManager, Func{IReadOnlyList{Player}})"/>
///   overload. Same convention as
///   <see cref="ThievesGuildEnforcerFactory"/> / <see cref="SheoldredsEdictFactory"/>.
/// - <b>Trigger registration</b>: the shape-only path attaches the trigger to
///   the card but does not register it with a bus; pass a
///   <see cref="TriggerManager"/> for live firing.
/// </summary>
[CardName("Ruin Crab")]
public static class RuinCrabFactory
{
    public const string CardName = "Ruin Crab";
    public const string PrintedManaCost = "{U}";
    public const int Power = 0;
    public const int Toughness = 3;
    public const int MillCount = 3;

    /// <summary>
    /// Construct Ruin Crab with no live <see cref="TriggerManager"/> wiring
    /// and no player-list resolver. The landfall trigger is attached for
    /// shape inspection but not registered with a bus; with no resolver the
    /// resolve body mills nobody. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null, allPlayersResolver: null);

    /// <summary>
    /// Construct Ruin Crab. When <paramref name="triggers"/> is supplied the
    /// landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering
    /// under the controller's control automatically queues the ability. When
    /// <paramref name="allPlayersResolver"/> is supplied, resolving the
    /// ability mills <see cref="MillCount"/> from each opponent's library
    /// (every player in the list except the controller, CR 102.1).
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? allPlayersResolver = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Crab });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Landfall trigger — CR 603.1 / 603.6a / CR 614 / CR 702.142.
        //   "Whenever a land you control enters, each opponent mills three
        //    cards."
        // Predicate is shared with Hedron Crab / Steppe Lynx / Plated
        // Geopede. Untargeted (CR 115.1a) — no TargetRequest; the resolve
        // body iterates opponents directly (same shape as Thieves' Guild
        // Enforcer's "each opponent mills two").
        // ----------------------------------------------------------------
        var millEffect = new Effect(
            $"{CardName}: each opponent mills {MillCount} cards (landfall)",
            () =>
            {
                if (allPlayersResolver == null) return;
                var players = allPlayersResolver();
                if (players == null) return;

                var controller = card.Controller ?? owner;
                foreach (var p in players)
                {
                    // "Each OPPONENT" — never the controller (CR 102.1).
                    if (ReferenceEquals(p, controller)) continue;

                    // CR 701.13b — mill 3 per opponent. Empty / short
                    // libraries handled inside MillAction.Apply (CR 701.13a).
                    MillAction.Apply(p, MillCount);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { millEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
