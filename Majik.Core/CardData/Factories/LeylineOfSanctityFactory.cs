using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline of Sanctity (Magic 2011, {2}{W}{W}).
///
/// Enchantment. Oracle text:
///   "If this card is in your opening hand, you may begin the game with
///    it on the battlefield."
///   "You have hexproof. (You can't be the target of spells or abilities
///    your opponents control.)"
///
/// ## Implemented (v1 — full)
/// - Enchantment shape with mana cost {2}{W}{W}, owner / controller
///   wired.
/// - <b>Opening-hand alt-cost</b> (CR 702.95) — marker
///   <see cref="KeywordAbility"/>
///   (<see cref="OpeningHandLeylineAlternativeCost.LeylineKeyword"/>)
///   so the shared subscriber picks Sanctity up from
///   <see cref="Majik.Core.Events.OpeningHandCheckEvent"/>.
/// - <b>"You have hexproof"</b> (CR 702.11) — wired via
///   <see cref="PlayerHexproofEffect"/>. While Sanctity is on the
///   battlefield, its controller (resolved at sync time per CR 605.1c
///   so post-Stealing-effect ownership shifts are picked up next time
///   the source flickers) is registered into
///   <see cref="Majik.Core.Rules.PlayerStaticAbilities"/>.
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects opponent-
///   controlled casts and activations naming the controller as a
///   target; <see cref="Majik.Core.Targeting.TargetLegality"/> also
///   rejects the player at resolution-time legality recheck
///   (CR 608.2b). Self-targeting (your own Healing Salve, your own
///   draw-7 effect) is untouched — hexproof only blocks
///   opponent-controlled targeted effects.
/// </summary>
[CardName("Leyline of Sanctity")]
public static class LeylineOfSanctityFactory
{
    public const string CardName = "Leyline of Sanctity";
    public const string PrintedManaCost = "{2}{W}{W}";

    /// <summary>
    /// Construct Leyline of Sanctity with no event bus wired. Suitable
    /// for card-shape / dispatcher tests — the printed hexproof static
    /// will still register on Attach (no event bus only means LTB sync
    /// won't fire automatically on a zone-move event).
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Leyline of Sanctity with the printed-static
    /// player-hexproof lifecycle wired against
    /// <paramref name="eventBus"/>. The hexproof grant tracks the
    /// current controller via <see cref="ICard.Controller"/>, so
    /// control-change effects (Threads of Disloyalty etc.) shift the
    /// hexproof grant to the new controller next time the lifecycle
    /// re-syncs.
    /// </summary>
    public static Enchantment Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.95 — Leyline keyword marker. The shared
        // OpeningHandLeylineAlternativeCost subscriber scans hands for
        // this keyword on OpeningHandCheckEvent and prompts the agent.
        card.AddAbility(new KeywordAbility(
            OpeningHandLeylineAlternativeCost.LeylineKeyword, card, owner));

        // CR 702.11 / CR 605.1c — "You have hexproof". The grant is
        // registered against the current controller at sync time so a
        // control-change effect lights up hexproof for the new
        // controller on the next sync (typical ETB/LTB flicker; emblem
        // / always-on usage routes through the no-source overload).
        var hexproofLifecycle = new PlayerHexproofEffect(
            source: card,
            eventBus: eventBus,
            affectedPlayersResolver: () =>
            {
                var controller = card.Controller;
                return controller != null
                    ? new[] { controller }
                    : Array.Empty<Player>();
            });
        hexproofLifecycle.Attach();

        return card;
    }
}
