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
/// Named-card factory for Burnished Hart (Theros, {3}).
///
/// Artifact Creature — Elk 2/2. Oracle text:
///   "{3}, Sacrifice this creature: Search your library for up to two
///    basic land cards, put them onto the battlefield tapped, then
///    shuffle."
///
/// ## Implemented (v1)
/// - 2/2 Elk shape with both Artifact AND Creature card types, mana cost
///   {3}. The Card constructor exposes only one primary type; Burnished
///   Hart is constructed as a <see cref="Creature"/> (so combat / power /
///   toughness / subtype plumbing works) and <see cref="CardType.Artifact"/>
///   is added explicitly so artifact-matters effects (Mox Opal metalcraft,
///   Affinity, Cranial Plating) see it.
/// - <b>{3}, Sacrifice ~: tutor up to two basics -> battlefield tapped</b>
///   — single <see cref="ActivatedAbility"/> with two costs:
///   <see cref="ManaCostCost"/>("{3}") + <see cref="AdditionalCost.Sacrifice"/>
///   on the hart itself. Note there is NO {T} cost — the printed line is
///   just "{3}, Sacrifice this creature:" (a creature can activate this
///   the turn it enters the battlefield since summoning sickness only gates
///   <see cref="AdditionalCost.Tap"/> per CR 302.1).
/// - Resolution closure follows the up-to-two-basics tutor pattern:
///   sacrifice the hart (battlefield → owner's graveyard, CR 701.16),
///   consult the controller's agent twice via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> for two basic land
///   picks (CR 701.19a — agent may decline either / both, "up to two"
///   permits 0..2 picks; deterministic first-two-basics fallback when no
///   agent registered — same posture as
///   <see cref="SearchSpellFactory.SearchUpToTwoBasicsBattlefieldAndHandSpell"/>),
///   move each pick to the battlefield with the printed "tapped" rider
///   applied AFTER the move (so ETB-tapped replacements like snow basics
///   have already applied), then shuffle ONCE via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a — a single
///   search effect performs one shuffle even when finding multiple cards).
/// - "Basic land" predicate matches by CR 305.6 — restricts to the Basic
///   supertype + Land card type.
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/>
///   so ETB-tapped replacements and <c>CardMovedEvent</c> subscribers
///   (Amulet of Vigor, Lotus Cobra) fire on each tutored basic.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move directly so behaviour
///   is observable — same posture as Expedition Map / Mind Stone /
///   Caustic Caterpillar.
/// - <b>Reveal event</b>: the tutored basics move Library → Battlefield
///   without publishing a reveal event. Same gap as every tutor factory.
/// </summary>
[CardName("Burnished Hart")]
public static class BurnishedHartFactory
{
    public const string CardName = "Burnished Hart";
    public const string PrintedManaCost = "{3}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Burnished Hart owned and controlled by
    /// <paramref name="owner"/>. The single "{3}, Sac: tutor up to two
    /// basics to battlefield tapped" activated ability is attached
    /// structurally.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elk });

        // Burnished Hart is BOTH Artifact and Creature (CR 205.2a — a
        // permanent can have multiple card types). The Creature ctor
        // sets the primary type; add Artifact so artifact-matters
        // effects (Affinity, metalcraft) see it.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {3}, Sacrifice this creature: Search library for up to two basic
        // land cards, put them onto the battlefield tapped, then shuffle.
        // CR 602 — activated ability with two costs (mana + sac).
        // CR 701.19a — search consults the agent (null = decline; legal).
        // CR 701.20a — shuffle ONCE after the search via LibraryShuffle.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: sac self + tutor up to two basics -> battlefield tapped",
            () =>
            {
                var controller = card.Controller ?? owner;
                SacrificeSelf(card, owner, controller);
                TutorUpToTwoBasicsToBattlefieldTapped(controller);
            });

        var tutorAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{3}"),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { tutorEffect });

        card.AddAbility(tutorAbility);

        return card;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent.
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner, Player controller)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        controller.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for up to two basic land
    /// cards (CR 305.6 — Basic supertype + Land card type), consult the
    /// agent twice (each pick may decline, "up to two" permits 0..2 picks;
    /// deterministic first-two-basics fallback when no agent), move each
    /// pick to the battlefield with the printed "tapped" rider applied
    /// after the move, then shuffle once (CR 701.20a — one shuffle per
    /// search effect even when multiple cards are found).
    /// </summary>
    private static void TutorUpToTwoBasicsToBattlefieldTapped(Player player)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var agent = AgentRegistry.Get(player);
        var picks = new List<ICard>(capacity: 2);

        // First pick.
        var firstCandidates = player.Zones.Library.GetCards()
            .Where(IsBasicLand).ToList();
        if (firstCandidates.Count > 0)
        {
            ICard? first = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, firstCandidates,
                        "basic land card to put onto the battlefield tapped")
                    .GetAwaiter().GetResult()
                : firstCandidates[0];
            if (first != null) picks.Add(first);
        }

        // Second pick (excluding the first).
        var secondCandidates = player.Zones.Library.GetCards()
            .Where(c => IsBasicLand(c) && (picks.Count == 0 || !ReferenceEquals(c, picks[0])))
            .ToList();
        if (secondCandidates.Count > 0)
        {
            ICard? second = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, secondCandidates,
                        "basic land card to put onto the battlefield tapped")
                    .GetAwaiter().GetResult()
                : secondCandidates[0];
            if (second != null) picks.Add(second);
        }

        var zones = ZoneServiceRegistry.Get(player);
        foreach (var pick in picks)
        {
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
                if (pick is Permanent perm) perm.Tap();
            }
        }

        // CR 701.20a — shuffle once after the search, even when zero
        // cards were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, "burnished-hart");
    }
}
