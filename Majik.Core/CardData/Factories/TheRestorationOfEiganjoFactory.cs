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
/// Named-card factory for The Restoration of Eiganjo — front face of the
/// transforming Saga The Restoration of Eiganjo // Architect of Restoration
/// (Kamigawa: Neon Dynasty, {2}{W}).
///
/// Enchantment — Saga. Oracle text:
///   "(As this Saga enters and after your draw step, add a lore counter.)
///   I   — Search your library for a basic Plains card, reveal it, put it
///         into your hand, then shuffle.
///   II  — You may discard a card. When you do, return target permanent card
///         with mana value 2 or less from your graveyard to the battlefield
///         tapped.
///   III — Exile this Saga, then return it to the battlefield transformed."
///
/// ## Implemented
/// - {2}{W} <see cref="Enchantment"/> — Saga, white.
/// - <see cref="MdfcState"/> attached (front = "The Restoration of Eiganjo",
///   back = "Architect of Restoration") so the transform target is observable
///   (CR 712).
/// - Chapter handlers bound through <see cref="SagaBinder.Bind"/> (the
///   Eiganjo branch) — the same path the Scryfall production load takes:
///     * <b>I</b> — search the controller's library for a basic Plains card
///       (CR 701.19a), put it into hand, then shuffle (CR 701.20a).
///     * <b>II</b> — "you may discard a card; when you do, return target
///       permanent card with mana value 2 or less from your graveyard to the
///       battlefield tapped" (CR 701.7 discard + CR 701.x reanimation). The
///       optional discard is agent-driven (declining honours the "you may" and
///       the reflexive "when you do" never fires).
///     * <b>III</b> — exile this Saga, return it transformed into Architect of
///       Restoration (CR 714.4 / 712.4) via
///       <see cref="ArchitectOfRestorationFactory"/>; the
///       <see cref="Majik.Core.CardData.Sagas.SagaState"/> is cleared so the
///       generic Saga-sacrifice SBA (CR 704.5r) does not fire on the
///       transformed creature.
///
/// ## Chapter abilities on the stack (CR 714.2b)
/// When a <see cref="TriggerManager"/> is supplied (the production path), the
/// chapter ability is enqueued as a triggered ability and resolves off the
/// stack, so an opponent gets a priority window to respond before it resolves —
/// chapter III's transform is responded-to-able. See
/// <see cref="Majik.Core.CardData.Sagas.SagaState"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Chapter I "you may"/reveal</b>: the search consults the agent (which
///   may decline) and moves the basic Plains to hand without a public reveal
///   event — same gap as every tutor factory (Borderland Ranger, Solemn
///   Simulacrum).
/// - <b>Chapter II targeting</b>: the reanimation target (a mv-2-or-less
///   permanent card in the controller's graveyard) is picked deterministically
///   (first eligible by graveyard order) rather than via a real
///   <see cref="Majik.Core.Targeting.TargetRequest"/> — same posture as Urza's
///   Saga's chapter-III tutor pick. The reflexive trigger's "when you do" is
///   modelled inline: it fires only when a card was actually discarded.
/// </summary>
[CardName("The Restoration of Eiganjo")]
public static class TheRestorationOfEiganjoFactory
{
    public const string CardName = "The Restoration of Eiganjo";
    public const string BackName = "Architect of Restoration";
    public const string PrintedManaCost = "{2}{W}";

    /// <summary>Construct The Restoration of Eiganjo with no live runtime
    /// services. Chapter bodies still fire on
    /// <c>SagaState.AdvanceAndChapter</c>; the chapter-III transform uses raw
    /// zone moves. Suitable for identity / shape / dispatcher tests.</summary>
    public static Enchantment Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>Construct The Restoration of Eiganjo with optional runtime
    /// services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service — routes the chapter-I
    /// tutor, chapter-II reanimation, and chapter-III exile/return through
    /// <see cref="ZoneService"/> so <see cref="CardMovedEvent"/> publishes.</param>
    /// <param name="eventBus">Optional event bus, forwarded to the Saga binder
    /// for downstream subscribers.</param>
    /// <param name="triggers">Optional trigger manager — routes the chapter
    /// abilities through the stack (CR 714.2b) and registers the transformed
    /// Architect's attacks-or-blocks Spirit triggers.</param>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var saga = new Enchantment(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Saga });

        saga.SetOwner(owner);
        saga.SetController(owner);

        // CR 712 — attach the DFC face tracker so the transform target is
        // observable. Starts on the front face (The Restoration of Eiganjo).
        saga.MdfcState = new MdfcState(CardName, BackName);

        var entity = new CardEntity
        {
            // PLAN 08 — deterministic (cosmetic) id; reproducible on replay.
            ScryfallId = Majik.Core.Game.DeterministicIdScope.NewId().ToString(),
            Name = CardName,
            TypeLine = "Enchantment — Saga",
            OracleText = OracleText,
            Colors = "W",
            ColorIdentity = "W",
            Keywords = "",
            Legalities = "",
        };
        SagaBinder.Bind(saga, entity, effects: null, zones: zoneService,
            triggers: triggers, eventBus: eventBus);

        return saga;
    }

    /// <summary>Printed oracle text used to seed <see cref="SagaBinder"/>'s
    /// chapter parser (final chapter III).</summary>
    private const string OracleText =
        "(As this Saga enters and after your draw step, add a lore counter.)\n" +
        "I — Search your library for a basic Plains card, reveal it, put it into your hand, then shuffle.\n" +
        "II — You may discard a card. When you do, return target permanent card with mana value 2 or less from your graveyard to the battlefield tapped.\n" +
        "III — Exile this Saga, then return it to the battlefield transformed.";
}
