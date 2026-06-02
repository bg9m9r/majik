using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grumgully, the Generous (Modern Horizons 2 —
/// Legendary Creature — Goblin Shaman {1}{R}{G} 3/3).
///
/// Oracle text (verified against Scryfall):
///   "Each other non-Human creature you control enters with an additional
///    +1/+1 counter on it."
///
/// The base shape (name, Legendary Creature — Goblin Shaman, {1}{R}{G}, 3/3)
/// is materialised from the embedded JSON definition
/// (<c>grumgully-the-generous.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single printed static
/// replacement is layered on top here — the JSON <c>AbilityDefinition</c>
/// schema doesn't express a global ETB-counter replacement, so it lives in
/// the factory (same posture as <see cref="MetallicMimicFactory"/>).
///
/// ## Implemented
///
/// ### "Each other non-Human creature you control enters with an additional
/// +1/+1 counter on it." (CR 614.1d)
/// Wired via <see cref="GrumgullyEntersWithCounterEffect"/>: while Grumgully
/// is on the battlefield, an
/// <see cref="IReplacementEffect{ZoneMoveIntent}"/> is registered on the
/// supplied <see cref="ReplacementBus"/> that increments
/// <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> for any
/// battlefield-entry intent carrying a non-Human creature that Grumgully's
/// controller will control — excluding Grumgully itself ("each OTHER
/// creature", CR 109.5). The lifecycle unregisters when Grumgully leaves the
/// battlefield. Same global-replacement + ETB/LTB-lifecycle shape as
/// <see cref="MetallicMimicFactory"/>.
/// </summary>
[CardName("Grumgully, the Generous")]
public static class GrumgullyTheGenerousFactory
{
    public const string CardName = "Grumgully, the Generous";
    public const string Slug = "grumgully-the-generous";

    /// <summary>
    /// Construct Grumgully with no live wiring — the ETB-counter replacement
    /// is not registered. Suitable for card-shape / dispatcher tests. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, replacementBus: null, eventBus: null);

    /// <summary>
    /// Construct a fully-wired Grumgully. The "other non-Human creatures you
    /// control enter with a +1/+1 counter" replacement is registered when
    /// <paramref name="replacementBus"/> is supplied; its ETB/LTB lifecycle
    /// re-syncs off <paramref name="eventBus"/>. The card shape is always
    /// wired regardless of which services are present.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to
    /// register the ETB-counter replacement on. May be null.</param>
    /// <param name="eventBus">Event bus for the replacement's ETB/LTB
    /// lifecycle. May be null — the lifecycle still syncs once on Attach.</param>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacementBus,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Legendary Creature —
        // Goblin Shaman, {1}{R}{G}, 3/3).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 614.1d — "Each other non-Human creature you control enters with
        // an additional +1/+1 counter on it." Global ETB replacement
        // registered while Grumgully is on the battlefield.
        if (replacementBus != null)
        {
            var lifecycle = new GrumgullyEntersWithCounterEffect(
                source: card,
                replacementBus: replacementBus,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }
}
