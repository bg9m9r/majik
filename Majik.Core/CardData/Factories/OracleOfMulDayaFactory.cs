using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Oracle of Mul Daya (Zendikar, {3}{G}).
///
/// Creature — Elf Shaman 2/2. Oracle text (verified against Scryfall):
///   "You may play an additional land on each of your turns.
///    Play with the top card of your library revealed.
///    You may play lands from the top of your library."
///
/// ## Implemented
/// - <b>You may play an additional land on each of your turns</b>
///   (CR 305.2 / 603.6e): the integer
///   <see cref="Permanent.AdditionalLandPlaysGranted"/> = 1 stamped on the card.
///   <see cref="Majik.Core.Game.LandDropTracker"/> sums this live over the
///   active player's permanents, so the +1 appears the instant Oracle enters
///   and vanishes when it leaves; stacks additively with Azusa / Exploration
///   (same posture as <see cref="AzusaLostButSeekingFactory"/>).
/// - <b>Play with the top card revealed + play lands from the top</b>
///   (CR 715.4 / CR 601.3e / CR 305.6): a battlefield-gated continuous
///   permission registered into <see cref="LibraryTopPlayPermissions"/> by a
///   <see cref="LibraryTopPlayStaticEffect"/> while Oracle is on the
///   battlefield (revoked on leave, CR 603.6e). The grant is
///   <see cref="TopPlayFilter.Lands"/> + reveal-top — when the controller's top
///   library card is a land they may play it as their land for the turn (still
///   consuming the CR 305.2 land drop). Identical surface to
///   <see cref="CourserOfKruphixFactory"/> / <see cref="AugurOfAutumnFactory"/>.
///
/// ## Production wiring
/// The live play-lands-from-top grant requires the per-game
/// <see cref="ContinuousEffectsService"/>'s event bus, so the grant follows
/// Oracle in / out of the battlefield. The effects-aware overload
/// <see cref="Create(Player, ContinuousEffectsService)"/> — the overload the
/// production source-generated dispatch invokes — reads
/// <see cref="ContinuousEffectsService.EventBus"/> and attaches the lifecycle.
/// The single-arg <see cref="Create(Player)"/> path stamps the additional-land
/// grant (live in real matches via the instance-swap rebuild) + attaches
/// description-only markers for shape / dispatch tests.
/// </summary>
[CardName("Oracle of Mul Daya")]
public static class OracleOfMulDayaFactory
{
    public const string CardName = "Oracle of Mul Daya";
    public const string PrintedManaCost = "{3}{G}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>CR 305.2 — Oracle grants one additional land play each turn.</summary>
    public const int AdditionalLandPlays = 1;

    public const string AdditionalLandDescription =
        "You may play an additional land on each of your turns.";

    public const string RevealTopDescription =
        "Play with the top card of your library revealed.";

    public const string PlayLandsFromTopDescription =
        "You may play lands from the top of your library.";

    /// <summary>
    /// Shape-only build (no live play-from-top grant). The additional-land grant
    /// is stamped (live in real matches via GameFacade's instance-swap rebuild)
    /// and the description markers are attached for shape / dispatch tests. Use
    /// <see cref="Create(Player, ContinuousEffectsService)"/> (the production
    /// routing overload) for the live play-lands-from-top permission.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Effects-aware build — the overload the production
    /// <c>NamedCardFactory.CreateGeneratedWithEffects</c> dispatch invokes.
    /// When <paramref name="continuousEffects"/> carries an event bus, the
    /// play-lands-from-top + reveal-top grant is registered (and revoked) as
    /// Oracle enters / leaves the battlefield.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 305.2 / 603.6e — "You may play an additional land on each of your
        // turns." Battlefield-gated, controller-scoped, summed live by
        // LandDropTracker.AdditionalLandPlaysFromBattlefield.
        card.AdditionalLandPlaysGranted = AdditionalLandPlays;

        // CR 604.1 — description-only static markers (UI / shape surface). Live
        // behaviour is the LibraryTopPlayPermissions grant wired below.
        card.AddAbility(new StaticAbility(
            source: card, controller: owner, description: AdditionalLandDescription));
        card.AddAbility(new StaticAbility(
            source: card, controller: owner,
            description: RevealTopDescription,
            isActiveCheck: () => card.Zone == ZoneType.Battlefield));
        card.AddAbility(new StaticAbility(
            source: card, controller: owner,
            description: PlayLandsFromTopDescription,
            isActiveCheck: () => card.Zone == ZoneType.Battlefield));

        // CR 601.3e / CR 305.6 / CR 715.4 — live "may play lands from the top,
        // revealed" grant, battlefield-gated.
        var bus = continuousEffects?.EventBus;
        if (bus != null)
        {
            new LibraryTopPlayStaticEffect(
                source: card,
                controller: owner,
                filter: TopPlayFilter.Lands,
                eventBus: bus,
                revealsTop: true).Attach();
        }

        return card;
    }

    /// <summary>
    /// Oracle's "play with the top card revealed" rider as a controller-side
    /// peek (CR 715.4). Returns the top card of <paramref name="controller"/>'s
    /// library, or null when the library is empty. Pure read.
    /// </summary>
    public static ICard? RevealedTopCard(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Library.GetCards().FirstOrDefault();
    }
}
