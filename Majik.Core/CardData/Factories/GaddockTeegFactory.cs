using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gaddock Teeg (Lorwyn, {G}{W}). Legendary Creature —
/// Kithkin Advisor, 2/2. Oracle text (verified against Scryfall):
///   "Noncreature spells with mana value 4 or greater can't be cast.
///    Noncreature spells with {X} in their mana costs can't be cast."
///
/// The card's base shape (name, Legendary supertype, Creature, Kithkin +
/// Advisor subtypes, {G}{W}, 2/2) is materialised from the embedded JSON
/// definition (<c>gaddock-teeg.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed statics are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express a casting-restriction static, so it lives in the factory (same
/// posture as <see cref="SanctumPrelateFactory"/> and
/// <see cref="VoidWinnowerFactory"/>).
///
/// ## Implemented (v1)
/// - Legendary Creature {G}{W}, P/T 2/2, Kithkin + Advisor subtypes,
///   owner/controller wired.
/// - <b>Printed statics (CR 601.3)</b>: two noncreature cast restrictions
///   wired via <see cref="GaddockTeegCastRestrictionEffect"/>. While Gaddock
///   Teeg is on the battlefield it registers into
///   <see cref="Majik.Core.Rules.CastingRestrictions"/>:
///   <list type="bullet">
///     <item><b>"mana value 4 or greater"</b> — the
///           <c>NoncreatureManaValueAtLeastBlocks</c> rail (threshold 4);
///           <see cref="Majik.Core.Rules.ActionValidator"/> rejects any
///           <c>CastSpellAction</c> for a noncreature card whose mana value
///           (printed MV + chosen X, CR 202.3b) is &gt;= 4.</item>
///     <item><b>"{X} in their mana costs"</b> — the
///           <c>NoncreatureXCostBlocks</c> rail; the validator rejects any
///           noncreature card whose printed cost contains the {X} symbol
///           (CR 107.3 — <c>Card.ManaCostValue.HasX</c>), regardless of the
///           chosen X value.</item>
///   </list>
///   Both blocks are symmetric — Gaddock Teeg's printed text is not
///   player-scoped, so it restricts every player's noncreature spells
///   (including its controller's). The effect detaches as Gaddock Teeg leaves
///   the battlefield via <see cref="Majik.Core.Events.CardMovedEvent"/> on the
///   supplied bus.
///
/// ## Deferred (v1 gaps)
/// - <b>Bot agent surface</b>: the heuristic bot's cast planner does not yet
///   pre-filter out noncreature spells these bands forbid; the engine rejects
///   any illegal declaration the validator catches. Same posture as Sanctum
///   Prelate / Void Winnower.
/// </summary>
[CardName("Gaddock Teeg")]
public static class GaddockTeegFactory
{
    public const string CardName = "Gaddock Teeg";
    public const string Slug = "gaddock-teeg";
    public const string PrintedManaCost = "{G}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Gaddock Teeg with no runtime wiring (the dispatcher / shape
    /// path). Neither printed static is registered — this returns a vanilla
    /// 2/2 Legendary Kithkin Advisor with correct identity. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Gaddock Teeg with both printed statics wired.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking of the cast
    /// restrictions. May be null — the lifecycle still syncs once on Attach
    /// (no LTB unregistration).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Kithkin + Advisor, {G}{W}, 2/2). No abilities in the JSON —
        // both printed statics are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 601.3 — "Noncreature spells with mana value 4 or greater can't be
        // cast. Noncreature spells with {X} in their mana costs can't be cast."
        // Both bands register into CastingRestrictions while Gaddock Teeg is on
        // the battlefield; ActionValidator performs the per-candidate MV / {X}
        // tests.
        if (eventBus != null)
        {
            var lifecycle = new GaddockTeegCastRestrictionEffect(
                source: card,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }
}
