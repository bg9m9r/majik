using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
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
/// Creature — Elf Shaman 2/2. Oracle text (verified against Scryfall 2026-06-03):
///   "You may play an additional land on each of your turns.
///    Play with the top card of your library revealed.
///    You may play lands from the top of your library."
///
/// ## Shape source
/// Card identity (name, {3}{G}, 2/2, Creature — Elf Shaman) is loaded from
/// <c>Majik.Core/CardData/Cards/oracle-of-mul-daya.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The three oracle riders are attached in
/// code below.
///
/// ## Implemented
/// - <b>Play an additional land on each of your turns</b> (CR 305.2 / 720): the
///   integer <see cref="Permanent.AdditionalLandPlaysGranted"/> stamped on the
///   card. <see cref="Majik.Core.Game.LandDropTracker"/> sums this value live
///   over the battlefield permanents the active player controls, so the +1
///   appears the instant Oracle enters, vanishes the instant she leaves
///   (CR 603.6e), is correct every turn (CR 505.5b — the land-play permission
///   resets each turn, independent of the static), and stacks additively with
///   other sources (Oracle + Azusa = +3). Same surface as
///   <see cref="AzusaLostButSeekingFactory"/>; stamped on the single-arg
///   <see cref="Create(Player)"/> path so the grant is live in real matches
///   (GameFacade's instance-swap rebuild calls that overload for non-land
///   permanents).
/// - <b>Play with the top card revealed + play lands from the top</b>
///   (CR 715.4 / CR 601.3e / CR 305.6): a battlefield-gated continuous
///   permission registered into <see cref="LibraryTopPlayPermissions"/> by a
///   <see cref="LibraryTopPlayStaticEffect"/> while Oracle is on the
///   battlefield (revoked on leave, CR 603.6e). The grant is
///   <see cref="TopPlayFilter.Lands"/> + reveal-top — identical to Courser of
///   Kruphix / Augur of Autumn. When the controller's top library card is a
///   land they may play it (from the library) as a land for the turn, still
///   consuming a CR 305.2 land drop — and with Oracle's own +1 additional land
///   play, they can play a land from hand AND a land from the top in the same
///   turn.
///
/// ## Production wiring
/// The live play-from-top grant requires the per-game
/// <see cref="ContinuousEffectsService"/>'s event bus (so the grant follows
/// Oracle in/out of the battlefield). The effects-aware overload
/// <see cref="Create(Player, ContinuousEffectsService)"/> — the overload the
/// production source-generated dispatch invokes — reads
/// <see cref="ContinuousEffectsService.EventBus"/> and attaches the lifecycle,
/// so the permission is genuinely live in a real match (mirrors Mystic Forge /
/// Augur of Autumn's production-routing overload). The additional-land static
/// is stamped on the single-arg path so it is always live regardless of
/// effects wiring.
///
/// ## Static-ability markers
/// All three clauses also carry a description-only <see cref="StaticAbility"/>
/// so shape / dispatch / UI surfaces see the printed text; the live behaviour
/// is the additional-land stamp + the registry grant above.
/// </summary>
[CardName("Oracle of Mul Daya")]
public static class OracleOfMulDayaFactory
{
    public const string CardName = "Oracle of Mul Daya";

    public const string AdditionalLandDescription =
        "You may play an additional land on each of your turns.";

    public const string RevealTopDescription =
        "Play with the top card of your library revealed.";

    public const string PlayLandsFromTopDescription =
        "You may play lands from the top of your library.";

    /// <summary>CR 720 — Oracle grants one additional land play each turn.</summary>
    public const int AdditionalLandPlays = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("oracle-of-mul-daya");

    /// <summary>
    /// Shape build (no live play-from-top grant). The additional-land static is
    /// stamped (it lives outside the bus, summed by the land-drop tracker) and
    /// the three description markers are attached for shape / dispatch tests.
    /// Use <see cref="Create(Player, ContinuousEffectsService)"/> (the production
    /// routing overload) for the live play-lands-from-top permission.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Effects-aware build — the overload the production
    /// <c>NamedCardFactory.CreateGeneratedWithEffects</c> dispatch invokes.
    /// When <paramref name="continuousEffects"/> carries an event bus, the
    /// play-lands-from-top + reveal grant is registered (and revoked) as Oracle
    /// enters / leaves the battlefield. The additional-land static is stamped
    /// regardless (it is bus-independent).
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects?.EventBus);

    /// <summary>
    /// Construct Oracle of Mul Daya. Identity comes from the embedded JSON
    /// definition; the three oracle riders are attached as description-only
    /// <see cref="StaticAbility"/> entries (CR 604.1) plus the live
    /// additional-land stamp. When <paramref name="eventBus"/> is supplied, a
    /// <see cref="LibraryTopPlayStaticEffect"/> registers the
    /// "may play lands from the top, revealed" grant (CR 601.3e / CR 305.6 /
    /// CR 715.4) while Oracle is on the battlefield.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 305.2 / 720 — "You may play an additional land on each of your
        // turns." Battlefield-gated, controller-scoped, summed live by
        // LandDropTracker.AdditionalLandPlaysFromBattlefield. Bus-independent —
        // stamped on every build path so it is live in real matches.
        // ----------------------------------------------------------------
        card.AdditionalLandPlaysGranted = AdditionalLandPlays;

        // ----------------------------------------------------------------
        // CR 604.1 — description-only static markers (UI / shape surface).
        // Live behaviour is the additional-land stamp above + the
        // LibraryTopPlayPermissions grant wired below.
        // ----------------------------------------------------------------
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: AdditionalLandDescription,
            isActiveCheck: () => card.Zone == ZoneType.Battlefield));

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: RevealTopDescription,
            isActiveCheck: () => card.Zone == ZoneType.Battlefield));

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: PlayLandsFromTopDescription,
            isActiveCheck: () => card.Zone == ZoneType.Battlefield));

        // ----------------------------------------------------------------
        // CR 601.3e / CR 305.6 / CR 715.4 — live "may play lands from the top,
        // revealed" grant, battlefield-gated. Registered now if Oracle is
        // already on the battlefield; the lifecycle re-syncs on every move of
        // the source. Needs the event bus from the per-game
        // ContinuousEffectsService.
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            new LibraryTopPlayStaticEffect(
                source: card,
                controller: owner,
                filter: TopPlayFilter.Lands,
                eventBus: eventBus,
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
