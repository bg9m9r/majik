using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Void Winnower (Battle for Zendikar, {9}).
///
/// Creature — Eldrazi, 11/9. Oracle text (verified against Scryfall):
///   "Your opponents can't cast spells with even mana values. (Zero is even.)
///    Your opponents can't block with creatures with even mana values."
///
/// The base shape (name, Creature, Eldrazi subtype, {9}, 11/9) is materialised
/// from the embedded JSON definition (<c>void-winnower.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed statics are
/// layered on here.
///
/// ## Implemented (v1)
/// - <b>"Your opponents can't cast spells with even mana values." (CR 601.3 /
///   202.3 — zero is even)</b>: wired via
///   <see cref="EvenManaValueCastRestrictionEffect"/>. While Void Winnower is
///   on the battlefield, every player returned by <c>opponentResolver</c> is
///   registered into <see cref="Majik.Core.Rules.CastingRestrictions"/>, and
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects any cast — creature
///   or noncreature alike — whose mana value (printed MV + chosen X) is even.
///   The lifecycle detaches as Void Winnower leaves the battlefield via
///   <see cref="CardMovedEvent"/> on the supplied bus.
/// - <b>"Your opponents can't block with creatures with even mana values."
///   (CR 509.1c / 202.3)</b>: a predicate-mode
///   <see cref="CombatRestrictionEffect"/> (<see cref="CombatRestriction.CannotBlock"/>)
///   on the supplied <see cref="ContinuousEffectsService"/>, mirroring
///   <see cref="EnsnaringBridgeFactory"/>. The predicate matches any creature
///   that is (a) controlled by a player OTHER than Void Winnower's controller
///   (CR 109.5 — "your opponents") and (b) has an even mana value. The combat
///   validator (<see cref="Majik.Core.Combat.CombatValidator.CanBlock"/>)
///   rejects such blockers. Gated on Void Winnower staying on the battlefield
///   (CR 603.6e analogue for static restrictions).
///
/// ## Deferred (v1 gaps)
/// - <b>Ambient X-stamping on the cast axis</b>: the cast restriction reads
///   <see cref="Card.PendingCastX"/> for split / X-cost spells. Cast paths that
///   don't stamp X before validation (some alternative-cast flows) treat X as 0
///   on this axis — the same posture as the other mana-value-sensitive rails.
/// - <b>Bot agent surface</b>: the heuristic bot's cast / block planners do not
///   yet pre-filter even-mana-value options; the engine rejects any illegal
///   declaration the validator catches. Same posture as Ensnaring Bridge.
/// </summary>
[CardName("Void Winnower")]
public static class VoidWinnowerFactory
{
    public const string CardName = "Void Winnower";
    public const string Slug = "void-winnower";
    public const string PrintedManaCost = "{9}";
    public const int Power = 11;
    public const int Toughness = 9;

    /// <summary>
    /// Construct Void Winnower with no runtime wiring (the dispatcher / shape
    /// path). Neither printed static is registered — this returns a vanilla
    /// 11/9 Eldrazi with correct identity. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentResolver: null, eventBus: null, effects: null);

    /// <summary>
    /// Construct Void Winnower with both printed statics wired.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="opponentResolver">Returns the set of players treated as
    /// opponents at restriction-sync time. May be null — the cast restriction
    /// simply won't activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking of the cast
    /// restriction. May be null — the lifecycle still syncs once on Attach.</param>
    /// <param name="effects">Game-level continuous-effects service for the
    /// can't-block predicate. May be null — the block restriction is then
    /// skipped.</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        IEventBus? eventBus,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi, {9}, 11/9). No abilities in the JSON — both printed statics
        // are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // --------------------------------------------------------------------
        // "Your opponents can't cast spells with even mana values." (CR 601.3 /
        // 202.3 — zero is even.) Registers each opponent into
        // CastingRestrictions while Void Winnower is on the battlefield; the
        // ActionValidator performs the parity test per candidate spell.
        // --------------------------------------------------------------------
        if (opponentResolver != null)
        {
            var castLifecycle = new EvenManaValueCastRestrictionEffect(
                source: card,
                eventBus: eventBus,
                affectedPlayersResolver: opponentResolver);
            castLifecycle.Attach();
        }

        // --------------------------------------------------------------------
        // "Your opponents can't block with creatures with even mana values."
        // (CR 509.1c / 202.3.) Predicate-mode CombatRestrictionEffect: matches
        // any creature controlled by a player other than Void Winnower's
        // controller whose mana value is even. Recomputed every block-
        // validation pass; gated on Void Winnower staying on the battlefield.
        // --------------------------------------------------------------------
        if (effects != null)
        {
            effects.Register(new CombatRestrictionEffect(
                restriction: CombatRestriction.CannotBlock,
                predicate: c =>
                {
                    var winnowerController = card.Controller;
                    if (winnowerController == null) return false;
                    // "your opponents" — only creatures controlled by a player
                    // other than Void Winnower's controller (CR 109.5).
                    if (c.Controller == null
                        || ReferenceEquals(c.Controller, winnowerController))
                    {
                        return false;
                    }
                    // CR 202.3 — zero is even. Mana value of a permanent on the
                    // battlefield is its printed mana value (no X on the stack).
                    return c.ManaCostValue.TotalValue % 2 == 0;
                },
                isActiveGate: () => card.Zone == ZoneType.Battlefield,
                expiresAtEndOfTurn: false));
        }

        return card;
    }
}
