using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wayfarer's Bauble (Time Spiral, {1}).
///
/// Artifact. Oracle text:
///   "{2}, {T}, Sacrifice Wayfarer's Bauble: Search your library for a
///    basic land card, put that card onto the battlefield tapped, then
///    shuffle."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>{2}, {T}, Sacrifice ~: tutor a basic land -> battlefield tapped</b>
///   — single <see cref="ActivatedAbility"/> with three costs:
///   <see cref="ManaCostCost"/>("{2}") + <see cref="AdditionalCost.Tap"/> +
///   <see cref="AdditionalCost.Sacrifice"/> on the bauble itself.
///   Mirrors <see cref="ExpeditionMapFactory"/>'s cost shape and
///   <see cref="PrismaticVistaFactory"/>'s tutor-to-battlefield resolution.
/// - Resolution sacrifices the bauble (battlefield → owner's graveyard),
///   consults the controller's agent via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> for the basic land
///   pick (CR 701.19a; deterministic first-basic fallback when no agent
///   registered), moves the pick to the battlefield, taps it (printed
///   rider), then shuffles via <see cref="LibraryShuffle.ShuffleLibrary"/>
///   (CR 701.20a — publishes <c>LibraryShuffledEvent</c> when a bus is
///   registered).
/// - "Basic land" predicate matches by CR 305.6 — restricts to the Basic
///   supertype + Land card type.
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/>
///   so ETB-tapped replacements (snow basics) and <c>CardMovedEvent</c>
///   subscribers (Amulet of Vigor untap, Lotus Cobra) fire on the tutored
///   basic.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move directly so behaviour
///   is observable — same posture as Expedition Map / Mind Stone /
///   Pyrite Spellbomb.
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield
///   without publishing a reveal event. Same gap as every tutor factory.
/// </summary>
[CardName("Wayfarer's Bauble")]
public static class WayfarersBaubleFactory
{
    public const string CardName = "Wayfarer's Bauble";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Wayfarer's Bauble owned and controlled by
    /// <paramref name="owner"/>. The single "{2}, {T}, Sac: tutor basic
    /// land to battlefield tapped" activated ability is attached
    /// structurally.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var bauble = new Artifact(CardName, PrintedManaCost);
        bauble.SetOwner(owner);
        bauble.SetController(owner);

        // ----------------------------------------------------------------
        // {2}, {T}, Sacrifice ~: Search library for a basic land, put it
        // onto the battlefield tapped, then shuffle.
        // CR 602 — activated ability with three costs (mana + tap + sac).
        // CR 701.19a — search consults the agent (null = decline; legal).
        // CR 701.20a — shuffle after the search via LibraryShuffle.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: sac self + tutor basic land -> battlefield tapped",
            async ctx =>
            {
                var controller = bauble.Controller ?? owner;
                SacrificeSelf(bauble, owner, controller);
                await TutorBasicLandToBattlefieldTappedAsync(controller, ctx).ConfigureAwait(false);
            });

        var tutorAbility = new ActivatedAbility(
            source: bauble,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(bauble),
                AdditionalCost.Sacrifice(bauble),
            },
            effects: new IEffect[] { tutorEffect });

        bauble.AddAbility(tutorAbility);

        return bauble;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="bauble"/> from the battlefield to
    /// its owner's graveyard. Idempotent. Mirrors the closure used by
    /// Expedition Map / Mind Stone / Pyrite Spellbomb.
    /// </summary>
    private static void SacrificeSelf(Artifact bauble, Player owner, Player controller)
    {
        if (bauble.Zone != ZoneType.Battlefield) return;
        controller.Zones.Battlefield.RemoveCard(bauble);
        owner.Zones.Graveyard.AddCard(bauble);
        bauble.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for a basic land card
    /// (CR 305.6 / 205.4a — Basic supertype + Land card type), consult the
    /// agent to pick among candidates (falls back to the first deterministic
    /// match), move the chosen card to the battlefield, apply the printed
    /// "tapped" rider, then shuffle (CR 701.20a).
    /// </summary>
    private static async ValueTask TutorBasicLandToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            .ToList();
        if (candidates.Count == 0)
        {
            LibraryShuffle.ShuffleLibrary(player, "wayfarers-bauble");
            return;
        }

        var agent = ctx.Agent ?? AgentRegistry.Get(player);
        ICard? pick = agent != null
            ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates, "basic land card")
                .ConfigureAwait(false)
            : candidates[0];
        if (pick == null)
        {
            LibraryShuffle.ShuffleLibrary(player, "wayfarers-bauble");
            return;
        }

        var zones = ZoneServiceRegistry.Get(player);
        if (zones != null)
        {
            zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
            if (pick is Permanent permTapped && !permTapped.IsTapped)
            {
                permTapped.Tap();
            }
        }
        else
        {
            player.Zones.Library.RemoveCard(pick);
            player.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(player);
            if (pick is Permanent perm)
                perm.Tap();
        }

        LibraryShuffle.ShuffleLibrary(player, "wayfarers-bauble");
    }
}
