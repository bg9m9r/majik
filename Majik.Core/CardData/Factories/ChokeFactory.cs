using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Choke (Stronghold, {2}{G}).
///
/// Enchantment. Oracle text:
///   "Islands don't untap during their controllers' untap steps."
///
/// ## Implemented (v1)
/// - Enchantment shape with correct printed name, type, and mana cost
///   <see cref="PrintedManaCost"/>.
/// - Dispatches via the source-generated <see cref="NamedCardFactory"/>
///   table.
/// - <b>Printed static (CR 502.1)</b>: wired via
///   <see cref="SubtypeDoesNotUntapStaticEffect"/> targeting
///   <see cref="CardSubtype.Island"/>. While Choke is on the battlefield,
///   an entry is registered with <see cref="UntapStepRestrictions"/>;
///   TurnDriver's UntapStep consults the registry and skips every
///   Permanent that has the Island subtype regardless of which player's
///   untap step it is — matches the symmetric phrasing of the printed
///   oracle text ("their controllers' untap steps", not "your"). On LTB
///   the registration lifts. Pass an <see cref="IEventBus"/> to
///   <see cref="Create(Player, IEventBus?)"/> to activate the lifecycle.
///   The no-arg overload still builds shape without auto-attaching so
///   structural / coverage callers keep working unchanged.
/// </summary>
[CardName("Choke")]
public static class ChokeFactory
{
    public const string CardName = "Choke";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>
    /// Shape-only constructor — builds Choke with correct identity. The
    /// untap-skip lifecycle is NOT attached; pass an event bus to the
    /// overload to activate the printed static.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Choke with optional event-bus wiring. When
    /// <paramref name="eventBus"/> is supplied, the
    /// <see cref="SubtypeDoesNotUntapStaticEffect"/> lifecycle is attached
    /// so the printed "Islands don't untap during their controllers' untap
    /// steps" clause activates on ETB and lifts on LTB.
    /// </summary>
    public static Enchantment Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        if (eventBus != null)
        {
            new SubtypeDoesNotUntapStaticEffect(card, CardSubtype.Island, eventBus).Attach();
        }

        return card;
    }
}
