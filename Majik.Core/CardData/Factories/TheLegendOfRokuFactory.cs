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
/// Named-card factory for The Legend of Roku — front face of the transforming
/// Saga The Legend of Roku // Avatar Roku (Avatar: The Last Airbender,
/// {2}{R}{R}).
///
/// Enchantment — Saga. Oracle text:
///   "(As this Saga enters and after your draw step, add a lore counter.)
///   I   — Exile the top three cards of your library. Until the end of your
///         next turn, you may play those cards.
///   II  — Add one mana of any color.
///   III — Exile this Saga, then return it to the battlefield transformed."
///
/// ## Implemented
/// - <see cref="Enchantment"/> — Saga at {2}{R}{R}, red (red comes from the
///   {R} pips; CardColors reads them off the mana cost).
/// - <see cref="MdfcState"/> attached (front = "The Legend of Roku",
///   back = "Avatar Roku") so the transform target is observable (CR 712).
/// - Chapter handlers bound through <see cref="SagaBinder.Bind"/> (the Roku
///   branch) — the same path the Scryfall production load takes:
///     * <b>I</b> — exile the top three cards of the controller's library
///       (CR 701.20) and stamp a runtime exile-cast grant
///       (<see cref="Card.GrantRuntimeExileCast"/>) so the controller may
///       play them; the grant clears at the end of the controller's NEXT turn
///       (CR 514.2) when an <see cref="IEventBus"/> is supplied — same
///       Cleanup-counting shape as <see cref="LightUpTheStageFactory"/>.
///     * <b>II</b> — add one mana of any color to the controller's pool
///       (CR 106.1). The color is supplied by the chapter-II color chooser
///       (default {R} — matches the deck's red theme).
///     * <b>III</b> — exile this Saga, return it transformed into Avatar Roku
///       (CR 714.4 / 712.4) via <see cref="AvatarRokuFactory"/>; the
///       <see cref="Majik.Core.CardData.Sagas.SagaState"/> is cleared so the
///       generic Saga-sacrifice SBA (CR 704.5r) does not fire on the
///       transformed creature.
///
/// ## Deferred (v1 gaps)
/// - <b>Stack-driven chapter triggers</b>: chapters resolve synchronously on
///   <c>SagaState.AdvanceAndChapter</c> (same posture as Fable / Urza's Saga).
/// - <b>Agent-driven color pick</b>: chapter II's "any color" choice is taken
///   from the color chooser; the default policy adds {R}. The chooser is the
///   same shape as Fable's rummage-count chooser.
/// - <b>"May play" includes lands</b>: the runtime exile-cast grant
///   authorises casting only (same corner-case as Light Up the Stage).
/// </summary>
[CardName("The Legend of Roku")]
public static class TheLegendOfRokuFactory
{
    public const string CardName = "The Legend of Roku";
    public const string BackName = "Avatar Roku";
    public const string PrintedManaCost = "{2}{R}{R}";

    /// <summary>Construct The Legend of Roku with no live runtime services.
    /// Chapter bodies still fire on <c>SagaState.AdvanceAndChapter</c>; the
    /// chapter-I exile-cast grant is stamped but not auto-cleared (no bus),
    /// and the chapter-III transform uses raw zone moves. Suitable for
    /// identity / shape / dispatcher tests.</summary>
    public static Enchantment Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>Construct The Legend of Roku with optional runtime services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service — routes the chapter-III
    /// exile/return through <see cref="ZoneService"/> so
    /// <see cref="CardMovedEvent"/> publishes.</param>
    /// <param name="eventBus">Optional event bus — drives the chapter-I
    /// "until end of your next turn" cleanup and the back face's
    /// firebending end-of-combat mana expiry.</param>
    /// <param name="triggers">Optional trigger manager — registers the
    /// transformed Avatar Roku's firebending attack trigger.</param>
    /// <param name="colorChoice">Optional chapter-II color chooser ("add one
    /// mana of any color"). Null defaults to red.</param>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<ManaColor>? colorChoice = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var saga = new Enchantment(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Saga });

        saga.SetOwner(owner);
        saga.SetController(owner);

        // CR 712 — attach the DFC face tracker so the transform target is
        // observable. Starts on the front face (The Legend of Roku).
        saga.MdfcState = new MdfcState(CardName, BackName);

        // Chapter wiring lives in SagaBinder (the Roku branch) — the same
        // branch the Scryfall load path takes. Pass the matching oracle text
        // so the binder parses the final chapter (III), plus the runtime
        // services for the chapter-I cleanup window and chapter-III transform.
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
            triggers: triggers, eventBus: eventBus, rokuColorChoice: colorChoice);

        return saga;
    }

    /// <summary>Printed oracle text used to seed <see cref="SagaBinder"/>'s
    /// chapter parser (final chapter III).</summary>
    private const string OracleText =
        "(As this Saga enters and after your draw step, add a lore counter.)\n" +
        "I — Exile the top three cards of your library. Until the end of your next turn, you may play those cards.\n" +
        "II — Add one mana of any color.\n" +
        "III — Exile this Saga, then return it to the battlefield transformed.";
}
