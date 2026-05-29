using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thalia, Heretic Cathar
/// (Eldritch Moon — Legendary Creature — Human Soldier {2}{W} 3/2).
///
/// Oracle text (verified against Scryfall):
///   "First strike
///    Creatures and nonbasic lands your opponents control enter tapped."
///
/// The card's base shape (name, Legendary supertype, Human + Soldier
/// subtypes, {2}{W}, 3/2) is materialised from the embedded JSON definition
/// (<c>thalia-heretic-cathar.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (First strike keyword, the opponent enters-tapped static) are layered on
/// top here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// keyword markers or replacement-effect statics, so they live in the
/// factory (same posture as the other JSON-backed cards whose behaviour
/// outgrows the schema, e.g. <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented
///
/// ### First strike (CR 702.7)
/// Wired as a <see cref="KeywordAbility"/> marker. Combat damage assignment
/// for first-strike creatures reads it in the first-strike damage step.
///
/// ### "Creatures and nonbasic lands your opponents control enter tapped."
/// (CR 614.1c — a static ability generating a one-sided ETB replacement.)
/// Wired via <see cref="ThaliaHereticCatharEntersTappedEffect"/>: while
/// Thalia is on the battlefield, an
/// <see cref="IReplacementEffect{ZoneMoveIntent}"/> is registered on the
/// supplied <see cref="ReplacementBus"/> that sets
/// <see cref="ZoneMoveIntent.EntersTapped"/> = true for any battlefield-entry
/// intent carrying a creature or non-basic land whose controller is an
/// opponent of Thalia's controller (CR 109.5 / CR 305.6). The lifecycle
/// unregisters when Thalia leaves the battlefield, so the effect lifts
/// automatically. Same global-replacement + ETB/LTB-lifecycle shape as
/// <see cref="ContainmentPriestFactory"/>.
///
/// ## Deferred
/// - <b>Ordering with other ETB-tapped replacements</b>: when multiple
///   replacements apply to the same entry the affected player should choose
///   the order (CR 616.1). <see cref="ReplacementBus"/> applies in
///   registration order for now; the observable result (the permanent
///   enters tapped) is unchanged for the enters-tapped case.
/// </summary>
[CardName("Thalia, Heretic Cathar")]
public static class ThaliaHereticCatharFactory
{
    public const string CardName = "Thalia, Heretic Cathar";
    public const string Slug = "thalia-heretic-cathar";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Thalia with no replacement-bus wired. Suitable for card-shape
    /// / dispatcher tests — First strike is present but the opponent
    /// enters-tapped replacement is not registered. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, replacementBus: null, eventBus: null);

    /// <summary>
    /// Construct a fully-wired Thalia, Heretic Cathar with the opponent
    /// enters-tapped replacement lifecycle attached against
    /// <paramref name="replacementBus"/> and <paramref name="eventBus"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to
    /// register the enters-tapped replacement on. May be null — the
    /// replacement simply won't activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be null —
    /// the lifecycle will still sync once on Attach.</param>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacementBus,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Creature, Human + Soldier subtypes, {2}{W}, 3/2). The JSON carries
        // no abilities — First strike + the static are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.7 — First strike. KeywordAbility marker; the combat system
        // reads it when assigning damage in the first-strike damage step.
        card.AddAbility(new KeywordAbility("First strike", card, owner));

        // CR 614.1c — "Creatures and nonbasic lands your opponents control
        // enter tapped." Registered as a one-sided global ETB replacement
        // while Thalia is on the battlefield.
        if (replacementBus != null)
        {
            var lifecycle = new ThaliaHereticCatharEntersTappedEffect(
                source: card,
                replacementBus: replacementBus,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }
}
