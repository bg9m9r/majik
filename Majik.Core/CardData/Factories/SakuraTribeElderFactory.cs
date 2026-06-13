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
/// ## Sacrifice-event bus (class-(b) pay-down)
/// The effects-aware <see cref="Create(Player, Effects.ContinuousEffectsService)"/>
/// overload (source-gen-routed on the prod GameFacade build, Festival-Crasher
/// pattern) threads <c>effects.EventBus</c> into BOTH the sac
/// <see cref="AdditionalCost"/> (live activation publishes
/// <see cref="Majik.Core.Events.PermanentSacrificedEvent"/> via
/// <see cref="AdditionalCost.Pay"/>) AND the bus-aware <c>SacrificeSelf</c>
/// fallback (resolve-only dispatcher/test path). CR 701.16a — one publish per
/// path. Aristocrat "whenever a/an [opponent] sacrifices…" payoffs now fire.
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
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Effects-aware overload the source-gen routes on the production
    /// <see cref="Majik.Core.Api.GameFacade"/> build (Festival-Crasher
    /// pattern). The only thing it adds over <see cref="Create(Player)"/> is
    /// threading <c>effects.EventBus</c> into the self-sacrifice so paying
    /// the cost publishes a <see cref="Majik.Core.Events.PermanentSacrificedEvent"/>
    /// (CR 701.16a) — the seam aristocrat "whenever a/an opponent sacrifices…"
    /// payoffs read. A null <paramref name="effects"/> (or a service with no
    /// wired bus) preserves the legacy publish-nothing posture.
    /// </summary>
    public static Creature Create(Player owner, Effects.ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var eventBus = effects?.EventBus;

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
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                SacrificeSelf(card, owner, controller, eventBus);
                await TutorBasicLandToBattlefieldTappedAsync(controller, ctx)
                    .ConfigureAwait(false);
            });

        var tutorAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                // CR 701.16a — thread the bus into the SAC COST so the LIVE
                // activation path (AbilityActivator → CostPayment → cost.Pay)
                // publishes PermanentSacrificedEvent. The closure's
                // SacrificeSelf is a bus-aware fallback for the resolve-only
                // dispatcher/test path where the cost was never pre-paid.
                AdditionalCost.Sacrifice(card, eventBus),
            },
            effects: new IEffect[] { tutorEffect });

        card.AddAbility(tutorAbility);

        return card;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent. When <paramref name="eventBus"/> is
    /// supplied (the prod effects-aware build) the move routes through
    /// <see cref="Primitives.Fx.Sacrifice(Cards.ICard, Player, Events.IEventBus)"/>,
    /// which publishes a <see cref="Majik.Core.Events.PermanentSacrificedEvent"/>
    /// (CR 701.16a) crediting <paramref name="controller"/> as the sacrificing
    /// player — the seam aristocrat payoffs read. With no bus it falls back to
    /// the bare owner-routed zone move (publish-nothing, dispatcher / shape
    /// test path).
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner, Player controller, Events.IEventBus? eventBus)
    {
        if (card.Zone != ZoneType.Battlefield) return;

        if (eventBus != null)
        {
            // CR 701.16a — the sacrificing player is the permanent's
            // controller; Fx.Sacrifice snapshots token-ness, routes the move
            // (Sacrifice reason — bypasses Indestructible / regeneration), and
            // publishes PermanentSacrificedEvent after the move.
            Primitives.Fx.Sacrifice(card, controller, eventBus);
            return;
        }

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
    private static async ValueTask TutorBasicLandToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            .ToList();

        // CR 701.19a — prompt agent even on zero candidates so the human
        // searcher sees the failed search (see LibrarySearch xmldoc).
        var pick = await LibrarySearch.PromptOnlyAsync(ctx, player, candidates, "basic land card")
            .ConfigureAwait(false);

        if (pick != null)
        {
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
        }

        // CR 701.20a — shuffle whether or not a card was found.
        LibraryShuffle.ShuffleLibrary(player, "sakura-tribe-elder");
    }
}
