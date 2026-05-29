using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sanctum Prelate (Conspiracy: Take the Crown,
/// {1}{W}{W}). Creature — Human Cleric, 2/2. Oracle text (verified against
/// Scryfall):
///   "As this creature enters, choose a number.
///    Noncreature spells with mana value equal to the chosen number can't
///    be cast."
///
/// The card's base shape (name, Creature, Human + Cleric subtypes,
/// {1}{W}{W}, 2/2) is materialised from the embedded JSON definition
/// (<c>sanctum-prelate.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed static
/// (ETB-choose-number + mana-value cast block) is layered on top here — the
/// JSON <c>AbilityDefinition</c> schema doesn't express an "as it enters,
/// choose a number" replacement nor a casting-restriction static, so it
/// lives in the factory (same posture as <see cref="MeddlingMageFactory"/>
/// and the other JSON-backed cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - Creature {1}{W}{W}, P/T 2/2, Human + Cleric subtypes, owner/controller
///   wired.
/// - <b>ETB number choice (CR 614.1c)</b>: accepted as an optional
///   <paramref name="chosenNumber"/> parameter on
///   <see cref="Create(Player,int,IEventBus)"/>. The single-arg path passes
///   <c>-1</c> ("no number chosen" → no restriction) for dispatcher / shape
///   tests, mirroring Meddling Mage's empty-name default.
/// - <b>Printed static (CR 601.3)</b>: mana-value-targeted noncreature cast
///   restriction. Wired via
///   <see cref="SanctumPrelateCastRestrictionEffect"/>: while the Prelate is
///   on the battlefield, the chosen number is registered into
///   <see cref="Majik.Core.Rules.CastingRestrictions"/> via
///   <c>AddNoncreatureManaValueBlock</c>, and
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects any
///   <c>CastSpellAction</c> for a noncreature card whose mana value (printed
///   MV + chosen X, CR 202.3b) matches. The block is symmetric — it applies
///   to both players' noncreature spells (the printed text isn't player-
///   scoped). The effect detaches as the Prelate leaves the battlefield via
///   <see cref="Majik.Core.Events.CardMovedEvent"/> on the supplied bus.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-prompt integration</b>:
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> doesn't yet
///   declare a ChooseNumber prompt for the "as this creature enters, choose
///   a number" replacement. Until that lands, callers supply the chosen
///   number directly to the factory overload — identical deferral to
///   Meddling Mage's ChooseCardName prompt.
/// </summary>
[CardName("Sanctum Prelate")]
public static class SanctumPrelateFactory
{
    public const string CardName = "Sanctum Prelate";
    public const string Slug = "sanctum-prelate";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct a Sanctum Prelate with no chosen number. Suitable for
    /// card-shape / dispatcher tests — the printed static will not block any
    /// casts (a negative number means "no number chosen"). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, chosenNumber: -1, eventBus: null);

    /// <summary>
    /// Construct a Sanctum Prelate with <paramref name="chosenNumber"/> as
    /// the ETB-declared number. When <paramref name="eventBus"/> is supplied,
    /// the printed static lifecycle is fully wired (the mana value is
    /// registered into <see cref="Majik.Core.Rules.CastingRestrictions"/>
    /// while the Prelate is on the battlefield; removed on LTB).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenNumber">The number chosen as the Prelate enters
    /// (CR 614.1c). Noncreature spells with this mana value can't be cast. A
    /// negative number means no restriction (shape tests). Zero is a valid
    /// choice.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be null —
    /// the lifecycle will still sync once on Attach (no LTB
    /// unregistration).</param>
    public static Creature Create(
        Player owner,
        int chosenNumber,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human + Cleric subtypes, {1}{W}{W}, 2/2). The JSON carries no
        // abilities — the printed static is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var prelate = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (chosenNumber >= 0)
        {
            var lifecycle = new SanctumPrelateCastRestrictionEffect(
                source: prelate,
                chosenNumber: chosenNumber,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return prelate;
    }
}
