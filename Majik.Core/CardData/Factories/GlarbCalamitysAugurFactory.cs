using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glarb, Calamity's Augur (Modern Horizons 3, {B}{G}{U}).
///
/// Legendary Creature — Frog Wizard Noble 2/4. Oracle text (Scryfall-verified):
///   "Deathtouch
///    You may look at the top card of your library any time.
///    You may play lands and cast spells with mana value 4 or greater from the
///    top of your library.
///    {T}: Surveil 2."
///
/// ## Shape source
/// Card identity (name, Legendary supertype, {B}{G}{U}, 2/4, Frog Wizard Noble),
/// the <b>Deathtouch</b> keyword, and the <b>{T}: Surveil 2</b> activated ability
/// are all declared in the embedded JSON definition
/// (<c>Majik.Core/CardData/Cards/glarb-calamitys-augur.json</c>) and materialised
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The "look at the top" / "play-and-cast
/// from the top" riders are attached in code below.
///
/// ## Implemented
/// - <b>Deathtouch</b> (CR 702.2): declared as a JSON keyword.
/// - <b>{T}: Surveil 2</b> (CR 701.20): a JSON <c>activated</c> ability with a
///   <c>tap_self</c> cost and a <c>surveil_self</c> effect (amount 2) — same
///   shape as Sinister Starfish's <c>{T}: Surveil 1</c>.
/// - <b>You may play lands and cast spells with mana value 4 or greater from the
///   top of your library</b> (CR 601.3e / CR 305.6 / CR 715.4): the bus-aware
///   <see cref="Create(Player, ContinuousEffectsService)"/> overload (the
///   production routing overload) attaches a <see cref="LibraryTopPlayStaticEffect"/>
///   registering a single <see cref="TopPlayFilter.Any"/> grant into
///   <see cref="LibraryTopPlayPermissions"/> while Glarb is on the battlefield
///   (revoked on leave, CR 603.6e). The grant carries an
///   <c>extraPredicate</c> restricting it to cards with mana value 4 or greater
///   (<see cref="HasManaValue4OrGreater"/>) — one clause covers both halves:
///   the play-as-a-land side (<see cref="LibraryTopPlayPermissions.MayPlayTopCard"/>)
///   and the cast side (<see cref="LibraryTopPlayPermissions.MayCastTopCard"/>),
///   each of which already ANDs the extra predicate on top of the type filter.
///   A land on top is only playable from the top when ITS mana value is 4+
///   (lands are MV 0, so ordinary lands stay ineligible — matching the printed
///   "lands … with mana value 4 or greater" wording, CR 202.3); a nonland spell
///   on top is castable from the top with its printed cost when MV is 4+.
/// - <b>Card shape</b> with a description-only <see cref="StaticAbility"/> rider
///   for each of the two text riders (audit / dispatch / bot surfaces) plus the
///   controller-side <see cref="LookAtTopOfLibrary"/> peek (CR 401.4 — "look at
///   the top card of your library any time").
///
/// ## Production wiring
/// The live play-and-cast-from-top grant requires the per-game
/// <see cref="ContinuousEffectsService"/>'s event bus so the grant follows Glarb
/// in / out of the battlefield. The effects-aware overload
/// <see cref="Create(Player, ContinuousEffectsService)"/> — the overload the
/// production source-generated dispatch invokes — reads
/// <see cref="ContinuousEffectsService.EventBus"/> and attaches the lifecycle.
/// The single-arg <see cref="Create(Player)"/> path attaches the description
/// markers + JSON shape for dispatch / shape tests without the live grant.
/// Mirrors <see cref="AugurOfAutumnFactory"/> / <see cref="OracleOfMulDayaFactory"/>.
/// </summary>
[CardName("Glarb, Calamity's Augur")]
public static class GlarbCalamitysAugurFactory
{
    public const string CardName = "Glarb, Calamity's Augur";

    /// <summary>CR 202.3 — the printed mana-value floor for the top-play clause.</summary>
    public const int TopPlayManaValueFloor = 4;

    public const string LookAtTopDescription =
        "You may look at the top card of your library any time.";

    public const string PlayAndCastFromTopDescription =
        "You may play lands and cast spells with mana value 4 or greater from the top of your library.";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("glarb-calamitys-augur");

    /// <summary>
    /// Shape-only build (no live play-and-cast-from-top grant). Identity,
    /// Deathtouch, and {T}: Surveil 2 come from the embedded JSON; the two text
    /// riders are attached as description-only <see cref="StaticAbility"/> entries
    /// (CR 604.1). Use <see cref="Create(Player, ContinuousEffectsService)"/> (the
    /// production-routing overload) for the live top-of-library permission.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Effects-aware build — the overload the production
    /// <c>NamedCardFactory.CreateGeneratedWithEffects</c> dispatch invokes. When
    /// <paramref name="continuousEffects"/> carries an event bus, the
    /// "may play lands and cast spells with mana value 4+ from the top" grant is
    /// registered (and revoked) as Glarb enters / leaves the battlefield.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 604.1 — description-only static markers (UI / audit / bot surface).
        // Live behaviour is the LibraryTopPlayPermissions grant wired below.
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: LookAtTopDescription));

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: PlayAndCastFromTopDescription));

        // CR 601.3e / CR 305.6 / CR 715.4 — live "may play lands AND cast spells
        // with mana value 4 or greater from the top" grant, battlefield-gated.
        // A single TopPlayFilter.Any grant covers both halves (play-as-land +
        // cast); the MV>=4 extraPredicate (ANDed by both MayPlayTopCard and
        // MayCastTopCard) enforces the printed mana-value floor. revealsTop:true
        // models "you may look at the top card any time" (the top is public).
        var bus = continuousEffects?.EventBus;
        if (bus != null)
        {
            new LibraryTopPlayStaticEffect(
                source: card,
                controller: owner,
                filter: TopPlayFilter.Any,
                eventBus: bus,
                revealsTop: true,
                extraPredicate: HasManaValue4OrGreater).Attach();
        }

        return card;
    }

    /// <summary>
    /// CR 202.3 — true when <paramref name="card"/> has mana value 4 or greater,
    /// the per-card gate on Glarb's "play lands and cast spells … from the top"
    /// grant. Reads the printed converted mana cost
    /// (<see cref="Card.ManaCostValue"/>); returns false for a null card or one
    /// whose characteristics are unavailable.
    /// </summary>
    public static bool HasManaValue4OrGreater(ICard card) =>
        card is Card c && c.ManaCostValue.TotalValue >= TopPlayManaValueFloor;

    /// <summary>
    /// Glarb's "look at the top card of your library any time" rider as a
    /// controller-side peek (CR 401.4). Returns the top card of
    /// <paramref name="controller"/>'s library, or null when the library is
    /// empty. Pure read — no zone mutation, no event publish.
    /// </summary>
    public static ICard? LookAtTopOfLibrary(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Library.GetCards().FirstOrDefault();
    }
}
