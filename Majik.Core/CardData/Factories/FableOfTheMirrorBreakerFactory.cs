using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fable of the Mirror-Breaker — front face of the
/// transforming Saga Fable of the Mirror-Breaker // Reflection of
/// Kiki-Jiki (Kamigawa: Neon Dynasty, {2}{R}).
///
/// Enchantment — Saga. Oracle text:
///   "(As this Saga enters and after your draw step, add a lore counter.
///     Sacrifice after III.)
///   I   — Create a 2/2 red Goblin Shaman creature token with 'Whenever
///         this creature attacks, create a Treasure token.'
///   II  — You may discard up to two cards, then draw that many cards.
///   III — Exile this Saga, then return it to the battlefield transformed."
///
/// ## Implemented
/// - 2/2-less <see cref="Enchantment"/> — Saga at {2}{R}, red. (Red colour
///   comes from the {R} pip; CardColors reads it off the mana cost.)
/// - <see cref="MdfcState"/> attached (front = "Fable of the
///   Mirror-Breaker", back = "Reflection of Kiki-Jiki") so the transform
///   target is observable (CR 712).
/// - Chapter handlers bound through <see cref="SagaBinder.Bind"/> (the
///   Fable branch) — the same path the Scryfall production load takes:
///     * <b>I</b> — create a 2/2 red Goblin Shaman token whose attack trigger
///       (CR 508.1f) creates a Treasure (CR 111.10) via
///       <see cref="Majik.Core.Tokens.TokenFactory"/>; the trigger is
///       wired live when a <see cref="TriggerManager"/> is supplied.
///     * <b>II</b> — "you may discard up to two, then draw that many"
///       (CR 701.7); the choice is supplied by
///       <paramref name="rummageChoice"/> (default: rummage maximally).
///     * <b>III</b> — exile this Saga, return it transformed into
///       Reflection of Kiki-Jiki (CR 714.4 / 712.4) via
///       <see cref="ReflectionOfKikiJikiFactory"/>; the
///       <see cref="Majik.Core.CardData.Sagas.SagaState"/> is cleared so
///       the generic Saga-sacrifice SBA (CR 704.5r) does not fire on the
///       transformed creature.
///
/// ## Chapter abilities on the stack (CR 714.2b — closed deferral #5)
/// When a <see cref="TriggerManager"/> is supplied (the production path), the
/// chapter ability is enqueued as a triggered ability and resolves OFF THE
/// STACK, so an opponent gets a priority window to respond before it resolves
/// — chapter III's transform is responded-to-able. See
/// <see cref="Majik.Core.CardData.Sagas.SagaState"/>. Without a trigger manager
/// (shape tests) the chapter still resolves synchronously.
///
/// ## Rummage choice (CR 701.7 — closed deferral #6)
/// Chapter II is modelled verbatim as "you MAY discard up to two cards. If you
/// do, draw that many." With no explicit <paramref name="rummageChoice"/> the
/// controller's <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> (resolved
/// via <see cref="Majik.Core.Players.Agents.AgentRegistry"/>) is prompted to
/// pick up to two cards to discard — declining honours the "you may" (discard
/// 0 → draw 0). The legacy <paramref name="rummageChoice"/> count override is
/// retained for bespoke wiring / tests.
/// </summary>
[CardName("Fable of the Mirror-Breaker")]
public static class FableOfTheMirrorBreakerFactory
{
    public const string CardName = "Fable of the Mirror-Breaker";
    public const string BackName = "Reflection of Kiki-Jiki";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>Construct Fable of the Mirror-Breaker with no live runtime
    /// services. Chapter bodies still fire on
    /// <c>SagaState.AdvanceAndChapter</c>; the chapter-I Goblin's attack
    /// trigger is attached but not registered (no trigger manager), and
    /// the chapter-III transform uses raw zone moves. Suitable for
    /// identity / shape / dispatcher tests.</summary>
    public static Enchantment Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>Construct Fable of the Mirror-Breaker with optional runtime
    /// services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service — routes token ETB
    /// + the chapter-III exile/return through <see cref="ZoneService"/> so
    /// <see cref="CardMovedEvent"/> publishes.</param>
    /// <param name="eventBus">Optional event bus, forwarded to the Saga
    /// binder for downstream subscribers.</param>
    /// <param name="triggers">Optional trigger manager — registers the
    /// chapter-I Goblin's attack→Treasure trigger and the transformed
    /// Reflection's delayed end-step token sacrifice.</param>
    /// <param name="rummageChoice">Optional chapter-II rummage count override
    /// ("you may discard up to two"). Clamped to [0, 2] and the hand size. When
    /// null, the controller's agent is prompted to pick which cards to discard
    /// (CR 701.7), declining → discard 0 / draw 0.</param>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<int>? rummageChoice = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var saga = new Enchantment(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Saga });

        saga.SetOwner(owner);
        saga.SetController(owner);

        // CR 712 — attach the DFC face tracker so the transform target is
        // observable. Starts on the front face (Fable).
        saga.MdfcState = new MdfcState(CardName, BackName);

        // Chapter wiring lives in SagaBinder — the same branch the Scryfall
        // load path takes. Pass the matching oracle text so the binder can
        // parse the final chapter (III), plus the runtime services for the
        // chapter-I attack trigger, chapter-II rummage, and chapter-III
        // transform.
        var entity = new CardEntity
        {
            // PLAN 08 — deterministic (cosmetic) id; reproducible on replay.
            ScryfallId = Majik.Core.Game.DeterministicIdScope.NewId().ToString(),
            Name = CardName,
            TypeLine = "Enchantment — Saga",
            OracleText = OracleText,
            Colors = "R",
            ColorIdentity = "R",
            Keywords = "",
            Legalities = "",
        };
        SagaBinder.Bind(saga, entity, effects: null, zones: zoneService,
            triggers: triggers, eventBus: eventBus, fableRummageChoice: rummageChoice);

        return saga;
    }

    /// <summary>Printed oracle text used to seed <see cref="SagaBinder"/>'s
    /// chapter parser (final chapter III).</summary>
    private const string OracleText =
        "(As this Saga enters and after your draw step, add a lore counter. Sacrifice after III.)\n" +
        "I — Create a 2/2 red Goblin Shaman creature token with \"Whenever this creature attacks, create a Treasure token.\"\n" +
        "II — You may discard up to two cards, then draw that many cards.\n" +
        "III — Exile this Saga, then return it to the battlefield transformed.";
}
