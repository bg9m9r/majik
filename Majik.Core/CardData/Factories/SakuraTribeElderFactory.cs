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
/// Named-card factory for Sakura-Tribe Elder (Champions of Kamigawa, {1}{G}).
///
/// Creature — Snake Shaman 1/1. Oracle text:
///   "Sacrifice this creature: Search your library for a basic land card,
///    put that card onto the battlefield tapped, then shuffle."
///
/// ## Implemented (v1)
/// - 1/1 Snake Shaman shape, mana cost {1}{G}.
/// - Single <see cref="ActivatedAbility"/> whose sole cost is
///   <see cref="AdditionalCost.Sacrifice"/> on the elder itself (no mana
///   component — STE is a pure "sacrifice: tutor" activated ability, NOT a
///   mana ability under CR 605.1 because the resolution effect doesn't add
///   mana to a pool).
/// - Resolution closure mirrors <see cref="PrismaticVistaFactory"/>'s
///   tutor: sacrifice the elder to its owner's graveyard (CR 701.16),
///   consult the controller's agent via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> for the basic land
///   pick (CR 701.19a; deterministic first-basic fallback when no agent
///   registered — same posture as Expedition Map / Prismatic Vista), move
///   the chosen land onto the battlefield, tap it (printed rider), then
///   shuffle via <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a —
///   publishes <c>LibraryShuffledEvent</c> when a bus is registered).
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/>
///   so ETB-tapped replacements (snow basics) and <c>CardMovedEvent</c>
///   subscribers (Amulet of Vigor untap, Lotus Cobra) fire on the tutored
///   basic. Raw-zone fallback when no live service is wired (shape /
///   dispatcher-test path).
/// - "Basic land" predicate matches by CR 305.6 — restricts to the Basic
///   supertype + Land card type, so Forest / Island / Plains / Mountain /
///   Swamp / Wastes are all legal targets but a dual or fetch is not.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move directly so behaviour
///   is observable — same posture as Caustic Caterpillar / Expedition Map /
///   Pyrite Spellbomb. Remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// - <b>Sorcery-speed-only flag</b>: Sakura-Tribe Elder's sacrifice ability
///   has no sorcery-speed restriction printed (CR 307 — STE is a creature,
///   not a Saga); the activation timing follows ActionValidator's standard
///   activated-ability gate. Summoning-sickness does NOT block the sac
///   ability — that gate only applies to <see cref="AdditionalCost.Tap"/>
///   (CR 302.1).
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield
///   without publishing a reveal event. Same gap as every tutor factory.
/// </summary>
[CardName("Sakura-Tribe Elder")]
public static class SakuraTribeElderFactory
{
    public const string CardName = "Sakura-Tribe Elder";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Sakura-Tribe Elder owned and controlled by
    /// <paramref name="owner"/>. The single "sacrifice: tutor a basic land
    /// to battlefield tapped" activated ability is attached structurally.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Snake, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Sacrifice this creature: Search your library for a basic land
        // card, put that card onto the battlefield tapped, then shuffle.
        // CR 602 — activated ability with a single sacrifice cost.
        // CR 605.1 — NOT a mana ability (effect doesn't add mana to a
        // pool), so it uses the stack like a normal activated ability.
        // CR 701.19a — search consults the agent (null = decline; legal).
        // CR 701.20a — shuffle after the search via LibraryShuffle.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: sac self + tutor basic land -> battlefield tapped",
            () =>
            {
                var controller = card.Controller ?? owner;
                SacrificeSelf(card, owner, controller);
                TutorBasicLandToBattlefieldTapped(controller);
            });

        var tutorAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { tutorEffect });

        card.AddAbility(tutorAbility);

        return card;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors the closure used by
    /// <see cref="CausticCaterpillarFactory"/> / Expedition Map / Mind Stone
    /// — the generic <see cref="AdditionalCost.Pay"/> sacrifice path is a
    /// no-op stub.
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner, Player controller)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        var holder = controller;
        holder.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for a basic land card
    /// (CR 305.6 / 205.4a — Basic supertype + Land card type), consult the
    /// agent to pick among candidates (falls back to the first deterministic
    /// match), move the chosen card to the battlefield, apply the printed
    /// "tapped" rider, then shuffle (CR 701.20a).
    /// </summary>
    private static void TutorBasicLandToBattlefieldTapped(Player player)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            .ToList();
        if (candidates.Count == 0)
        {
            // CR 701.20a — still shuffle even when search finds nothing.
            LibraryShuffle.ShuffleLibrary(player, "sakura-tribe-elder");
            return;
        }

        var agent = AgentRegistry.Get(player);
        ICard? pick = agent != null
            ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "basic land card")
                .GetAwaiter().GetResult()
            : candidates[0];
        if (pick == null)
        {
            LibraryShuffle.ShuffleLibrary(player, "sakura-tribe-elder");
            return;
        }

        // CR 603.6a / CR 614 — route through ZoneService so ETB triggers
        // (Amulet of Vigor untap, bounce-land bounce, Lotus Cobra) and
        // enters-tapped replacements (snow basics) fire on the tutored
        // basic. The printed "tapped" rider is applied AFTER the move so
        // any ETB-tapped replacement has already applied; double-tapping
        // a tapped permanent is a no-op (CR 701.20).
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

        LibraryShuffle.ShuffleLibrary(player, "sakura-tribe-elder");
    }
}
