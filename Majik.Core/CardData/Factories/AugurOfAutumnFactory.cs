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
/// Named-card factory for Augur of Autumn (Innistrad: Midnight Hunt, {1}{G}{G}).
///
/// Creature — Human Druid 2/3. Oracle text:
///   "You may look at the top card of your library any time.
///    You may play lands from the top of your library.
///    Coven — As long as you control three or more creatures with different
///    powers, you may cast creature spells from the top of your library."
///
/// ## Shape source
/// Card identity (name, {1}{G}{G}, 2/3, Creature — Human Druid) is loaded from
/// <c>Majik.Core/CardData/Cards/augur-of-autumn.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The three oracle riders are attached in
/// code below as description-only <see cref="StaticAbility"/> entries.
///
/// ## Implemented
/// - <b>Play lands from the top of your library</b> (CR 601.3e / CR 305.6): the
///   bus-aware <see cref="Create(Player, IEventBus)"/> overload attaches a
///   <see cref="LibraryTopPlayStaticEffect"/> that registers a
///   <see cref="TopPlayFilter.Lands"/> + reveal-top grant into
///   <see cref="LibraryTopPlayPermissions"/> while Augur is on the battlefield
///   (revoked on leave, CR 603.6e). Same surface as Courser of Kruphix — when
///   the controller's top library card is a land they may play it as their land
///   for the turn (still consuming the CR 305.2 land drop).
/// - <b>Card shape</b> with three <see cref="StaticAbility"/> riders carrying
///   their printed descriptions (audit / dispatch / bot surfaces) plus
///   controller-side helpers:
///   - <see cref="LookAtTopOfLibrary"/> — the "look at the top card any time"
///     peek (CR 401.4). Returns the top card or null when the library is empty.
///   - <see cref="HasCoven"/> — the Coven condition (control three or more
///     creatures with different powers). Gates the "cast creature spells from
///     the top" rider once cast-from-zone permission exists.
///
/// ## Deferred (v1 gaps — documented)
/// - <b>Cast creature spells from the top (Coven-gated)</b>: lands are PLAYED
///   (the land-play path already moves a land from any zone), but casting a
///   nonland spell from the library needs a continuous "permission to cast from
///   a zone other than hand" primitive that the spell-cast flow consults. That
///   cast-from-top wiring is not yet built (same gap as Bolas's Citadel, Vivien
///   (Champion of the Wilds), Conspicuous Snoop), so the creature clause is
///   description-only for now. The <see cref="TopPlayFilter.Creatures"/> filter
///   exists in the registry ready for that surface to consult.
/// - <b>Coven as a live conditional grant</b>: <see cref="HasCoven"/> evaluates
///   the condition on demand, but it is not wired as a continuous static
///   ability that toggles the cast-from-top permission as the board changes.
/// </summary>
[CardName("Augur of Autumn")]
public static class AugurOfAutumnFactory
{
    public const string CardName = "Augur of Autumn";

    public const string LookAtTopDescription =
        "You may look at the top card of your library any time.";

    public const string PlayLandsFromTopDescription =
        "You may play lands from the top of your library.";

    public const string CovenCastFromTopDescription =
        "Coven — As long as you control three or more creatures with different powers, you may cast creature spells from the top of your library.";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("augur-of-autumn");

    /// <summary>
    /// Construct Augur of Autumn with no live bus wiring. The three oracle
    /// riders are attached as description-only <see cref="StaticAbility"/>
    /// entries (CR 604.1); the live "play lands from the top" grant is NOT
    /// registered (use the <see cref="Create(Player, IEventBus)"/> overload).
    /// Suitable for shape / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Augur of Autumn. Identity comes from the embedded JSON
    /// definition; the three oracle riders are attached as description-only
    /// <see cref="StaticAbility"/> entries (CR 604.1). When
    /// <paramref name="eventBus"/> is supplied, a
    /// <see cref="LibraryTopPlayStaticEffect"/> registers the
    /// "may play lands from the top, revealed" grant (CR 601.3e / CR 305.6 /
    /// CR 715.4) while Augur is on the battlefield. See class doc for the
    /// deferred Coven cast-from-top clause.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 604.1 — static abilities. Three riders, each carrying its printed
        // description for audit / bot-surface visibility.
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: LookAtTopDescription));

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: PlayLandsFromTopDescription));

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: CovenCastFromTopDescription));

        // CR 601.3e / CR 305.6 / CR 715.4 — live "may play lands from the top,
        // revealed" grant, battlefield-gated. The Coven creature clause stays
        // description-only pending cast-from-top wiring (see class doc).
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
    /// Augur's "look at the top card of your library any time" rider as a
    /// controller-side peek (CR 401.4). Returns the top card of
    /// <paramref name="controller"/>'s library, or null when the library is
    /// empty. Pure read — no zone mutation, no event publish.
    /// </summary>
    public static ICard? LookAtTopOfLibrary(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Library.GetCards().FirstOrDefault();
    }

    /// <summary>
    /// The Coven condition: <paramref name="controller"/> controls three or
    /// more creatures with different powers (the printed Coven definition).
    /// Counts the distinct effective <see cref="Creature.Power"/> values among
    /// creatures the player controls on the battlefield; Coven is active when
    /// that count is three or more. Returns false when fewer than three
    /// distinct powers are present.
    /// </summary>
    public static bool HasCoven(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var distinctPowers = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => ReferenceEquals(c.Controller, controller))
            .Select(c => c.Power)
            .Distinct()
            .Count();

        return distinctPowers >= 3;
    }
}
