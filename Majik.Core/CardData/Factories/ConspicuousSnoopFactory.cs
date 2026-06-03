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
/// Named-card factory for Conspicuous Snoop (Jumpstart, {R}{R}).
///
/// Creature — Goblin Rogue 2/2. Oracle text:
///   "Play with the top card of your library revealed.
///    You may cast Goblin spells from the top of your library.
///    As long as the top card of your library is a Goblin card, this creature
///    has all activated abilities of that card."
///
/// ## Implemented
/// - <b>You may cast Goblin spells from the top of your library</b>
///   (CR 601.3e): the bus-aware <see cref="Create(Player, IEventBus)"/> overload
///   attaches a <see cref="LibraryTopPlayStaticEffect"/> registering a
///   <see cref="TopPlayFilter.Creatures"/> + reveal-top grant whose
///   <c>extraPredicate</c> demands the top card be a Goblin
///   (<see cref="IsGoblinCard"/>), while Snoop is on the battlefield (revoked on
///   leave, CR 603.6e). When the controller's top library card is a Goblin
///   creature they may cast it from the library: the card goes onto the stack
///   via <see cref="Majik.Core.Game.SpellCastFlow"/> (which moves a card from
///   whatever zone it occupies onto the stack, stamps the "cast from library"
///   sentinel, and now authorizes the cast against this grant, CR 601.3e).
/// - <b>Card shape</b> with three description-only <see cref="StaticAbility"/>
///   riders carrying their printed text (audit / dispatch / bot surfaces) plus
///   the <see cref="LookAtTopOfLibrary"/> peek helper.
///
/// ## Deferred (v1 gaps — documented)
/// - <b>Top of library revealed</b> as an opponent-facing public reveal:
///   modelled as the registry's reveal-top rider (controller-side); the engine
///   doesn't yet broadcast top-card visibility to opponents.
/// - <b>Has all activated abilities of the top Goblin card</b>: needs the Layer
///   6 dynamic activated-ability-copy primitive (copy the ability list off a
///   non-self card and re-source each to Snoop) — same gap as Vesuvan
///   Doppelganger. Kept as a description-only rider.
/// - <b>Noncreature Goblin spells</b> (e.g. a Goblin instant/sorcery on top):
///   the grant is keyed on <see cref="TopPlayFilter.Creatures"/>, so it
///   currently authorizes only creature Goblin cards (the common case). A
///   Goblin noncreature on top is not yet castable from the top.
/// </summary>
[CardName("Conspicuous Snoop")]
public static class ConspicuousSnoopFactory
{
    public const string CardName = "Conspicuous Snoop";
    public const string PrintedManaCost = "{R}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    public const string PlayRevealedDescription =
        "Play with the top card of your library revealed.";

    public const string MayCastGoblinDescription =
        "You may cast Goblin spells from the top of your library.";

    public const string CopyActivatedAbilitiesDescription =
        "As long as the top card of your library is a Goblin card, this creature has all activated abilities of that card.";

    /// <summary>
    /// Construct Conspicuous Snoop with no live bus wiring. The three oracle
    /// riders are attached as description-only <see cref="StaticAbility"/>
    /// entries (CR 604.1); the live "cast Goblin spells from the top" grant is
    /// NOT registered (use the <see cref="Create(Player, IEventBus)"/> overload).
    /// Suitable for shape / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Effects-aware build — the overload the production
    /// <c>NamedCardFactory.CreateGeneratedWithEffects</c> dispatch invokes.
    /// When <paramref name="continuousEffects"/> carries an event bus, the
    /// cast-Goblin-spells-from-top grant is registered (and revoked) as Snoop
    /// enters / leaves the battlefield. Mirrors Mystic Forge's production-routing
    /// overload so the permission is genuinely live in a real match.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects?.EventBus);

    /// <summary>
    /// Construct Conspicuous Snoop. The three oracle riders are attached as
    /// description-only <see cref="StaticAbility"/> entries (active on the
    /// battlefield only per CR 604.1). When <paramref name="eventBus"/> is
    /// supplied, a <see cref="LibraryTopPlayStaticEffect"/> registers the
    /// "may cast Goblin spells from the top, revealed" grant (CR 601.3e /
    /// CR 715.4) while Snoop is on the battlefield. See class doc for the
    /// deferred copy-abilities clause.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Rogue });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 604.1 — static abilities. Three riders, each with its printed
        // description for audit / bot-surface visibility. IsActiveCheck
        // defaults to "active on the battlefield" via StaticAbility's
        // permanent-on-battlefield convention.
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: PlayRevealedDescription));

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: MayCastGoblinDescription));

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: CopyActivatedAbilitiesDescription));

        // CR 601.3e / CR 715.4 — live "cast Goblin spells from the top,
        // revealed" grant, battlefield-gated. A Creatures-filter grant whose
        // extra predicate also demands the top card be a Goblin.
        if (eventBus != null)
        {
            var lifecycle = new LibraryTopPlayStaticEffect(
                source: card,
                controller: owner,
                filter: TopPlayFilter.Creatures,
                eventBus: eventBus,
                revealsTop: true,
                extraPredicate: IsGoblinCard);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>True if <paramref name="card"/> is a Goblin card — the cast
    /// gate for Snoop's "cast Goblin spells from the top" grant.</summary>
    public static bool IsGoblinCard(ICard card) =>
        card != null && card.HasSubtype(CardSubtype.Goblin);

    /// <summary>
    /// Helper exposing Snoop's "play with the top card of your library
    /// revealed" rider as a controller-side peek. Returns the top card of
    /// <paramref name="controller"/>'s library, or null when the library
    /// is empty. Pure read — no zone mutation, no event publish. Bot /
    /// decision surfaces use this to consult Snoop's revealed top card
    /// when computing "is the top card a Goblin? can I cast it?" lines.
    /// </summary>
    public static ICard? LookAtTopOfLibrary(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Library.GetCards().FirstOrDefault();
    }

    /// <summary>
    /// True when the top card of <paramref name="controller"/>'s library is
    /// a Goblin card. Predicate used by both the "may cast top if Goblin"
    /// rider and the "copy activated abilities if Goblin" rider. Returns
    /// false when the library is empty.
    /// </summary>
    public static bool IsTopOfLibraryGoblin(Player controller)
    {
        var top = LookAtTopOfLibrary(controller);
        return top != null && top.HasSubtype(CardSubtype.Goblin);
    }
}
