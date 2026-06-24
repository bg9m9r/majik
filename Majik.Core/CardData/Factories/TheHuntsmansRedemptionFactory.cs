using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for The Huntsman's Redemption (Tarkir: Dragonstorm,
/// {2}{G}). Enchantment — Saga (a non-transforming, self-sacrificing Saga).
///
/// Oracle text (Scryfall-verified 2026-06-24):
///   "(As this Saga enters and after your draw step, add a lore counter.
///    Sacrifice after III.)
///   I   — Create a 3/3 green Beast creature token.
///   II  — You may sacrifice a creature. If you do, search your library for a
///         creature or basic land card, reveal it, put it into your hand, then
///         shuffle.
///   III — Up to two target creatures each get +2/+2 and gain trample until
///         end of turn."
///
/// ## Implemented
/// - {2}{G} green <see cref="Enchantment"/> — Saga. Base shape built from the
///   embedded JSON definition (<c>the-huntsmans-redemption.json</c>) through
///   <see cref="CardDefinitionFactory"/>, matching the rest of the JSON-driven
///   card pool.
/// - Chapter handlers bound through <see cref="SagaBinder.Bind"/> (the
///   Huntsman's Redemption branch) — the same path the Scryfall production load
///   takes:
///     * <b>I</b> — create a 3/3 green Beast creature token (CR 111 / 111.4).
///     * <b>II</b> — "you may sacrifice a creature. If you do, search your
///       library for a creature or basic land card, reveal it, put it into your
///       hand, then shuffle" (CR 701.16 sacrifice + reflexive "if you do" +
///       CR 701.19a tutor / CR 701.20a shuffle). The optional sacrifice is
///       agent-driven; declining honours the "you may" and the reflexive tutor
///       never fires.
///     * <b>III</b> — "up to two target creatures each get +2/+2 and gain
///       trample until end of turn" (CR 613 layered until-EOT pump + keyword
///       grant). Requires a <see cref="ContinuousEffectsService"/> to register
///       the until-EOT effects.
/// - After chapter III resolves the Saga self-sacrifices via the generic
///   Saga-sacrifice SBA (CR 714.5 / 704.5r) — this Saga does NOT transform, so
///   <see cref="Cards.Permanent.MdfcState"/> is intentionally left null and
///   <see cref="Cards.Sagas"/>… the <see cref="Majik.Core.CardData.Sagas.SagaState"/>
///   is NOT cleared (unlike the transforming Eiganjo / Fable / Roku Sagas).
///
/// ## Deferred (v1 gaps)
/// - <b>Chapter II reveal</b>: the tutored creature-or-basic-land moves
///   Library → hand without publishing a public reveal event — same gap as
///   every tutor factory (Cultivate, Borderland Ranger).
/// - <b>Chapter III "target"</b>: the up-to-two creatures are picked
///   deterministically (the controller's own creatures, highest power first)
///   rather than via a real <see cref="Majik.Core.Targeting.TargetRequest"/> —
///   same documented v1 Saga posture as The Restoration of Eiganjo's chapter-II
///   reanimation target.
/// </summary>
[CardName("The Huntsman's Redemption")]
public static class TheHuntsmansRedemptionFactory
{
    public const string CardName = "The Huntsman's Redemption";
    public const string Slug = "the-huntsmans-redemption";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>Printed oracle text used to seed <see cref="SagaBinder"/>'s
    /// chapter parser (final chapter III).</summary>
    public const string OracleText =
        "(As this Saga enters and after your draw step, add a lore counter. Sacrifice after III.)\n" +
        "I — Create a 3/3 green Beast creature token.\n" +
        "II — You may sacrifice a creature. If you do, search your library for a creature or basic land card, reveal it, put it into your hand, then shuffle.\n" +
        "III — Up to two target creatures each get +2/+2 and gain trample until end of turn.";

    /// <summary>Construct The Huntsman's Redemption with no live runtime
    /// services. Chapter bodies still fire on
    /// <c>SagaState.AdvanceAndChapter</c> (the chapter-III pump is a no-op
    /// without a <see cref="ContinuousEffectsService"/>). Suitable for identity
    /// / shape / dispatcher tests.</summary>
    public static Enchantment Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null, effects: null);

    /// <summary>Construct The Huntsman's Redemption with optional runtime
    /// services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service — routes the chapter-I
    /// token, chapter-II sacrifice + tutor through <see cref="ZoneService"/> so
    /// <see cref="CardMovedEvent"/> publishes.</param>
    /// <param name="eventBus">Optional event bus, forwarded to the Saga binder
    /// for downstream subscribers.</param>
    /// <param name="triggers">Optional trigger manager — routes the chapter
    /// abilities through the stack (CR 714.2b).</param>
    /// <param name="effects">Optional continuous-effects service — required for
    /// chapter III's +2/+2 + trample until-EOT effects (CR 613).</param>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var saga = (Enchantment)CardDefinitionFactory.Build(definition, owner);
        saga.SetController(owner);

        var entity = new CardEntity
        {
            // PLAN 08 — deterministic (cosmetic) id; reproducible on replay.
            ScryfallId = Majik.Core.Game.DeterministicIdScope.NewId().ToString(),
            Name = CardName,
            TypeLine = "Enchantment — Saga",
            OracleText = OracleText,
            Colors = "G",
            ColorIdentity = "G",
            Keywords = "",
            Legalities = "",
        };
        SagaBinder.Bind(saga, entity, effects: effects, zones: zoneService,
            triggers: triggers, eventBus: eventBus);

        return saga;
    }
}
