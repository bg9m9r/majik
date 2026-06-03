using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Combined transforming-DFC name registration for
/// <b>The Restoration of Eiganjo // Architect of Restoration</b>
/// (Kamigawa: Neon Dynasty, {2}{W}).
///
/// Both faces ship as fully-built factories — the front Saga
/// (<see cref="TheRestorationOfEiganjoFactory"/>) and the transformed back
/// Enchantment Creature (<see cref="ArchitectOfRestorationFactory"/>). This
/// class is the <b>transforming-DFC alias</b>: it carries the
/// <c>[CardName]</c> for the printed combined name that the embedded Scryfall
/// seed keys on, so the card resolves through <see cref="NamedCardFactory"/>
/// dispatch and its <c>IsImplemented</c> flag flips on. Same precedent as
/// <see cref="FableOfTheMirrorBreakerReflectionOfKikiJikiFactory"/>.
///
/// A transforming DFC enters the battlefield on its FRONT face (CR 712.4), so
/// the combined-name build delegates to
/// <see cref="TheRestorationOfEiganjoFactory.Create(Player)"/> — the Saga.
/// Chapter III then exiles + returns it transformed into Architect of
/// Restoration via that factory's own chapter wiring (CR 714.4 / 712.4). All
/// chapter / transform behaviour, plus its CR citations, lives in the two
/// underlying factories; this is a thin dispatch alias only.
///
/// The base front-face shape (name, Enchantment — Saga, {2}{W}) is also
/// mirrored as an embedded JSON definition
/// (<c>the-restoration-of-eiganjo-architect-of-restoration.json</c>) for schema
/// parity with the rest of the card pool, matching
/// <c>fable-of-the-mirror-breaker-reflection-of-kiki-jiki.json</c>.
/// </summary>
[CardName("The Restoration of Eiganjo // Architect of Restoration")]
public static class TheRestorationOfEiganjoArchitectOfRestorationFactory
{
    public const string CombinedName =
        "The Restoration of Eiganjo // Architect of Restoration";
    public const string FrontName = "The Restoration of Eiganjo";
    public const string BackName = "Architect of Restoration";
    public const string Slug = "the-restoration-of-eiganjo-architect-of-restoration";

    /// <summary>
    /// Single-arg dispatcher path — the overload <see cref="NamedCardFactory"/>
    /// resolves the combined printed name to. Delegates to the front-face Saga
    /// factory (a transforming DFC enters on its front face, CR 712.4) with no
    /// live runtime services attached.
    /// </summary>
    public static Enchantment Create(Player owner)
        => TheRestorationOfEiganjoFactory.Create(owner);

    /// <summary>
    /// Fully-wired overload — forwards the runtime services to the front-face
    /// Saga factory (chapter-I tutor, chapter-II discard + reanimate,
    /// chapter-III exile/return-transformed).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
        => TheRestorationOfEiganjoFactory.Create(
            owner, zoneService, eventBus, triggers);
}
