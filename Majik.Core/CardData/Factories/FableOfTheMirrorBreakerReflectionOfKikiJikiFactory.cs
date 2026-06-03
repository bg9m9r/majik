using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Abilities;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Combined transforming-DFC name registration for
/// <b>Fable of the Mirror-Breaker // Reflection of Kiki-Jiki</b>
/// (Kamigawa: Neon Dynasty, {2}{R}).
///
/// Both faces already ship as fully-built factories — the front Saga
/// (<see cref="FableOfTheMirrorBreakerFactory"/>) and the transformed back
/// Enchantment Creature (<see cref="ReflectionOfKikiJikiFactory"/>). This
/// class is the <b>transforming-DFC alias</b>: it carries the
/// <c>[CardName]</c> for the printed combined name that the embedded
/// Scryfall seed keys on, so the card resolves through
/// <see cref="NamedCardFactory"/> dispatch and its <c>IsImplemented</c>
/// flag flips on (the seed stores the card under
/// "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki", not under the
/// bare front-face name). Same precedent as
/// <c>[CardName("Thing in the Ice // Awoken Horror")]</c> on
/// <see cref="ThingInTheIceFactory"/>.
///
/// A transforming DFC enters the battlefield on its FRONT face (CR 712.4),
/// so the combined-name build delegates to
/// <see cref="FableOfTheMirrorBreakerFactory.Create(Player)"/> — the Saga.
/// Chapter III then exiles + returns it transformed into Reflection of
/// Kiki-Jiki via that factory's own chapter wiring (CR 714.4 / 712.4). All
/// chapter / transform / copy-ability behaviour, plus its CR citations, lives
/// in the two underlying factories; this is a thin dispatch alias only.
///
/// The base front-face shape (name, Enchantment — Saga, {2}{R}) is also
/// mirrored as an embedded JSON definition
/// (<c>fable-of-the-mirror-breaker-reflection-of-kiki-jiki.json</c>) for
/// schema parity with the rest of the card pool, matching the analogue's
/// <c>thing-in-the-ice.json</c> base-shape resource.
/// </summary>
[CardName("Fable of the Mirror-Breaker // Reflection of Kiki-Jiki")]
public static class FableOfTheMirrorBreakerReflectionOfKikiJikiFactory
{
    public const string CombinedName =
        "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki";
    public const string FrontName = "Fable of the Mirror-Breaker";
    public const string BackName = "Reflection of Kiki-Jiki";
    public const string Slug = "fable-of-the-mirror-breaker-reflection-of-kiki-jiki";

    /// <summary>
    /// Single-arg dispatcher path — the overload
    /// <see cref="NamedCardFactory"/> resolves the combined printed name to.
    /// Delegates to the front-face Saga factory (a transforming DFC enters on
    /// its front face, CR 712.4) with no live runtime services attached.
    /// </summary>
    public static Enchantment Create(Player owner)
        => FableOfTheMirrorBreakerFactory.Create(owner);

    /// <summary>
    /// Fully-wired overload — forwards the runtime services to the front-face
    /// Saga factory (chapter-I attack trigger, chapter-II rummage, chapter-III
    /// exile/return-transformed). Provided so the combined-name alias can be
    /// driven with live wiring identically to the front-face factory.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers,
        System.Func<int>? rummageChoice = null)
        => FableOfTheMirrorBreakerFactory.Create(
            owner, zoneService, eventBus, triggers, rummageChoice);
}
