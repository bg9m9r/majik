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
/// Named-card factory for Courser of Kruphix (Born of the Gods, {1}{G}{G}).
///
/// Enchantment Creature — Centaur 2/4. Oracle text:
///   "Play with the top card of your library revealed.
///    You may play lands from the top of your library.
///    Landfall — Whenever a land you control enters, you gain 1 life."
///
/// ## Shape source
/// Card identity (name, {1}{G}{G}, 2/4, Enchantment Creature — Centaur) is
/// loaded from <c>Majik.Core/CardData/Cards/courser-of-kruphix.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The three oracle riders are attached in
/// code below.
///
/// ## Implemented
/// - <b>Play with the top card revealed + play lands from the top</b>
///   (CR 715.4 / CR 601.3e / CR 305.6): a battlefield-gated continuous
///   permission registered into <see cref="LibraryTopPlayPermissions"/> by a
///   <see cref="LibraryTopPlayStaticEffect"/> while Courser is on the
///   battlefield (and revoked on leave, CR 603.6e). The grant is
///   <see cref="TopPlayFilter.Lands"/> + reveal-top. When the controller's top
///   library card is a land they may play it as their land for the turn — it is
///   played from the library (the engine's land-play path already moves a land
///   from whatever zone it occupies), consumes the normal CR 305.2 land drop
///   (still gated by <see cref="Majik.Core.Game.LandDropTracker"/> + any
///   additional-land static), and the next card becomes the new revealed top.
/// - <b>Landfall — you gain 1 life</b> (CR 614 / CR 603.1): a triggered ability
///   on <see cref="Triggers.OnLandEntersUnderControl"/> that gains the
///   controller 1 life on resolution, mirroring the Lotus Cobra / Steppe Lynx
///   landfall shape.
///
/// ## Static-ability marker
/// The reveal + play-from-top clauses also carry a description-only
/// <see cref="StaticAbility"/> so shape / dispatch / UI surfaces see the printed
/// text; the live behaviour is the registry grant above.
/// </summary>
[CardName("Courser of Kruphix")]
public static class CourserOfKruphixFactory
{
    public const string CardName = "Courser of Kruphix";

    public const string RevealTopDescription =
        "Play with the top card of your library revealed.";

    public const string PlayLandsFromTopDescription =
        "You may play lands from the top of your library.";

    public const string LandfallDescription =
        "Landfall — Whenever a land you control enters, you gain 1 life.";

    /// <summary>Life gained on each landfall (CR 614).</summary>
    public const int LifeGainPerLandfall = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("courser-of-kruphix");

    /// <summary>
    /// Construct Courser of Kruphix with no live bus / trigger wiring. The
    /// static-ability markers and the landfall trigger are attached for shape
    /// inspection, but the play-from-top grant is not registered and the
    /// landfall trigger is not bus-registered. Suitable for shape / dispatch
    /// tests. Use <see cref="Create(Player, IEventBus, TriggerManager)"/> for
    /// live play.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Courser of Kruphix with live wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, a
    /// <see cref="LibraryTopPlayStaticEffect"/> is attached so the
    /// "play lands from the top, revealed" grant is registered while Courser is
    /// on the battlefield and revoked when it leaves.</param>
    /// <param name="triggers">When supplied, the landfall lifegain trigger is
    /// registered with the bus so a land entering under the controller's
    /// control gains them 1 life.</param>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 604.1 — description-only static markers for the reveal +
        // play-from-top clauses (UI / shape surface). Live behaviour is the
        // LibraryTopPlayPermissions grant wired below.
        // ----------------------------------------------------------------
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
        // CR 614 — Landfall. "Whenever a land you control enters, you gain 1
        // life." Same landfall predicate as Lotus Cobra / Steppe Lynx
        // (Triggers.OnLandEntersUnderControl, CR 603.6a).
        // ----------------------------------------------------------------
        var landfallEffect = new Effect(
            $"{CardName}: landfall — gain {LifeGainPerLandfall} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                if (!controller.HasLost) controller.GainLife(LifeGainPerLandfall);
            });

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { landfallEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        // ----------------------------------------------------------------
        // CR 601.3e / CR 305.6 / CR 715.4 — live "may play lands from the top,
        // revealed" grant, battlefield-gated. Registered now if Courser is
        // already on the battlefield; the lifecycle re-syncs on every move of
        // the source.
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            var lifecycle = new LibraryTopPlayStaticEffect(
                source: card,
                controller: owner,
                filter: TopPlayFilter.Lands,
                eventBus: eventBus,
                revealsTop: true);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Courser's "play with the top card revealed" rider as a controller-side
    /// peek (CR 715.4). Returns the top card of <paramref name="controller"/>'s
    /// library, or null when the library is empty. Pure read.
    /// </summary>
    public static ICard? RevealedTopCard(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Library.GetCards().FirstOrDefault();
    }
}
