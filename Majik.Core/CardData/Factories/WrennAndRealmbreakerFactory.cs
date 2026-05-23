using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wrenn and Realmbreaker (The Brothers' War,
/// {3}{G}).
///
/// Legendary Planeswalker — Wrenn, starting loyalty 4.
/// Oracle text:
///   "+1: Mill three cards, then you may return a land card from your
///         graveyard to your hand.
///    −2: Put target nonland permanent card from a graveyard onto the
///         battlefield under your control.
///    −7: You get an emblem with 'Whenever a nonland permanent enters
///         under your control, you may search your library for a basic
///         land card, put it onto the battlefield tapped, then shuffle.'"
///
/// ## Implemented (v1)
/// - Legendary Planeswalker, loyalty 4, Wrenn subtype, mana cost {3}{G}.
/// - <b>+1</b>: mills 3 cards from the top of controller's library
///   (<see cref="MillAction"/> — CR 701.13), then if any land card is in
///   controller's graveyard the first one is moved back to hand. v1
///   deterministic auto-pick; "you may" defaults to taking the action
///   when an eligible land card is present.
/// - <b>-2</b>: scans a candidate-graveyards list for the first nonland
///   <see cref="Permanent"/> card and reanimates it under the activator's
///   control. With <paramref name="allPlayersResolver"/> non-null the scan
///   sweeps every player's graveyard; otherwise the scan is limited to
///   the controller's graveyard. Routes through
///   <see cref="ZoneService.MoveCard"/> when supplied so ETB triggers on
///   the reanimated permanent fire (CR 603.6a). v1 auto-pick.
/// - <b>-7 ultimate</b>: mints an emblem in the controller's command
///   zone (CR 114). The emblem's printed triggered rider ("whenever a
///   nonland permanent enters under your control, you may search your
///   library for a basic land …") is structural only at v1 — see
///   "Deferred" below.
///
/// ## Deferred (v1 gaps)
/// - <b>Targeting prompts</b>: <see cref="LoyaltyAbility"/> does not yet
///   declare <see cref="TargetRequest"/>s, so the +1's "may" and the -2's
///   target pick are auto-resolved (first eligible). Same posture as
///   <see cref="WrennAndSixFactory"/>, <see cref="LilianaOfTheVeilFactory"/>,
///   etc.
/// - <b>Mill ordering / library shuffle</b>: mill uses
///   <see cref="MillAction.Apply"/>. The +1's "you may" return defaults
///   to taking the return when an eligible land is present.
/// - <b>-7 emblem's tutor trigger</b>: registering a triggered ability
///   on a permanent <see cref="Emblem"/> source — and driving an
///   on-trigger "search controller's library for a basic land card,
///   put it onto the battlefield tapped, then shuffle" — needs
///   command-zone trigger registration plus a basic-land tutor primitive
///   not yet wired through the search-spell factory. The emblem is
///   minted with no live ability rider so the structure tests can assert
///   the emblem exists and is keyed to the controller; the trigger
///   itself is a future slice.
/// - <b>"Under your control"</b>: the -2 sets the reanimated permanent's
///   controller to the activator. Owner is preserved on the moved card.
/// </summary>
public static class WrennAndRealmbreakerFactory
{
    /// <summary>
    /// Construct Wrenn and Realmbreaker with no live <see cref="ZoneService"/>
    /// wiring (the shape/dispatcher path). +1 mill + return-land effects
    /// operate on the controller's zones directly. -2 scans only the
    /// controller's graveyard. ETB triggers on the reanimated permanent
    /// will NOT fire on this path — use the overload to wire a
    /// <see cref="ZoneService"/>.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, zoneService: null, allPlayersResolver: null);

    /// <summary>
    /// Construct Wrenn and Realmbreaker with optional runtime services.
    /// When <paramref name="zoneService"/> is supplied, the -2's graveyard
    /// → battlefield move routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers / replacements on the reanimated permanent fire
    /// (CR 603.6a). When <paramref name="allPlayersResolver"/> is supplied,
    /// the -2 scans every player's graveyard for an eligible nonland
    /// permanent card; otherwise the scan is limited to
    /// <paramref name="owner"/>'s graveyard.
    /// </summary>
    public static Planeswalker Create(
        Player owner,
        ZoneService? zoneService,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var wrenn = new Planeswalker(
            name: "Wrenn and Realmbreaker",
            manaCost: "{3}{G}",
            startingLoyalty: 4,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Wrenn });

        wrenn.SetOwner(owner);
        wrenn.SetController(owner);

        // -----------------------------------------------------------------
        // +1: Mill three cards, then you may return a land card from
        //     your graveyard to your hand.
        // CR 701.13 — Mill. The mill is mandatory; the return is a
        // "you may" optional action. v1 deterministic: auto-pick the
        // first land card in the controller's graveyard after the mill,
        // mirroring the WrennAndSix +1 pattern.
        // -----------------------------------------------------------------
        wrenn.AddAbility(new LoyaltyAbility(wrenn, +1, () =>
        {
            MillAction.Apply(owner, 3);

            var landPick = owner.Zones.Graveyard.GetCards()
                .FirstOrDefault(c => c.HasType(CardType.Land));
            if (landPick == null) return; // "you may" — no eligible land

            owner.Zones.Graveyard.RemoveCard(landPick);
            owner.Zones.Hand.AddCard(landPick);
            landPick.SetZone(ZoneType.Hand);
        }));

        // -----------------------------------------------------------------
        // -2: Put target nonland permanent card from a graveyard onto
        //     the battlefield under your control.
        // CR 603.6a — the reanimated permanent's ETB triggers fire when
        // movement goes through ZoneService. v1 auto-pick: first nonland
        // permanent card found scanning the configured graveyards.
        // -----------------------------------------------------------------
        wrenn.AddAbility(new LoyaltyAbility(wrenn, -2, () =>
        {
            var candidatePlayers = allPlayersResolver?.Invoke()
                ?? (IReadOnlyList<Player>)new[] { owner };

            foreach (var p in candidatePlayers)
            {
                if (p == null) continue;
                var pick = p.Zones.Graveyard.GetCards()
                    .OfType<Permanent>()
                    .FirstOrDefault(c => !c.HasType(CardType.Land));
                if (pick == null) continue;

                if (zoneService != null)
                {
                    zoneService.MoveCard(pick, ZoneType.Graveyard, ZoneType.Battlefield, owner);
                }
                else
                {
                    p.Zones.Graveyard.RemoveCard(pick);
                    owner.Zones.Battlefield.AddCard(pick);
                    pick.SetZone(ZoneType.Battlefield);
                    pick.SetController(owner);
                }

                return; // CR 700.6 — "target" is a single object
            }
        }));

        // -----------------------------------------------------------------
        // -7 ultimate: emblem with "whenever a nonland permanent enters
        // under your control, you may search your library for a basic
        // land card, put it onto the battlefield tapped, then shuffle."
        //
        // v1: structural emblem only — the tutor rider needs a
        // command-zone trigger registration path and a basic-land tutor
        // primitive that aren't wired yet (see class xmldoc — "Deferred").
        // The emblem is minted with no live ability rider so the
        // structure tests can assert the emblem exists and is keyed to
        // the controller.
        // -----------------------------------------------------------------
        wrenn.AddAbility(new LoyaltyAbility(wrenn, -7, () =>
        {
            var emblem = new Emblem(
                controller: owner,
                sourceName: "Wrenn and Realmbreaker — basic-land tutor emblem",
                abilities: Array.Empty<IAbility>());
            owner.AddEmblem(emblem);
        }));

        return wrenn;
    }
}
