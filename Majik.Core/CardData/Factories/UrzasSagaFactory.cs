using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Urza's Saga (Modern Horizons 2).
///
/// Type line: <c>Legendary Enchantment Land — Urza's Saga</c>.
/// Oracle text (Scryfall):
///   "(As this Saga enters and after your draw step, add a lore counter.
///     Sacrifice after III.)
///   I, II — Create a 0/0 colorless Construct artifact creature token
///           with 'This creature gets +1/+1 for each artifact you control.'
///   III   — Search your library for an artifact card with mana value 2
///           or less, put it onto the battlefield, then shuffle.
///   {T}: Add {C}."
///
/// ## Implemented (v1)
/// - <b>Dual type — Legendary Enchantment Land</b>. Primary runtime
///   type is <see cref="Land"/> (matches
///   <c>ScryfallCardFactory.PickPrimaryType</c>'s preference order where
///   Land beats Enchantment); <see cref="CardType.Enchantment"/> is
///   added via <see cref="Card.AddCardType"/> so <c>HasType</c> lookups
///   match both. Supertype <c>Legendary</c>, subtypes <c>Urza's</c> +
///   <c>Saga</c> (CR 205.3 / 205.4).
/// - <b>Mana ability — "{T}: Add {C}"</b>. Wired as a stack-free
///   <see cref="ManaAbility"/> (CR 605.1). Listed in the printed oracle
///   text so the production <see cref="OracleManaBinder"/> path attaches
///   the same shape; the named-card factory wires it inline.
/// - <b>Saga state + chapter handlers</b>. Delegates to
///   <see cref="SagaBinder.Bind"/> (the Urza's Saga branch) which
///   attaches a <see cref="Majik.Core.CardData.Sagas.SagaState"/> with
///   the I/II/III chapter callbacks. The state increments a lore
///   counter at the start of each pre-combat main phase
///   (<c>TurnDriver.AdvanceSagas</c>) and the chapter body fires on the
///   matching count (CR 714.2).
/// - <b>I, II — 0/0 Construct artifact creature token</b>. Reuses
///   <see cref="KarnScionOfUrzaFactory.CreateConstructToken"/> — same
///   shape (colourless, Construct subtype, dual Artifact + Creature
///   typing via <c>AddCardType</c>) with the dynamic CDA P/T rider
///   ("+1/+1 per artifact you control") registered on the supplied
///   <see cref="ContinuousEffectsService"/>. Without an effects service
///   the token still spawns but stays at 0/0 (SBA 704.5f sweeps it).
/// - <b>III — tutor artifact mv ≤ 2 to battlefield</b>. Deterministic
///   v1 picker (first matching card in library order); the move goes
///   through <see cref="ZoneService.MoveCard"/> when supplied so ETB
///   triggers on the tutored artifact fire (CR 603.6a). Library
///   shuffles via <see cref="Majik.Core.Zones.LibraryShuffle"/>
///   (CR 701.20a).
/// - <b>Self-sacrifice after III</b>. Handled by the generic Saga SBA
///   (<c>SagaSacrificedCheck</c> — CR 714.5 / 704.5r). No per-card
///   sacrifice plumbing.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven tutor pick</b>: III currently auto-picks the
///   first artifact with mv ≤ 2 by library order. A future cut should
///   route through <see cref="Majik.Core.Players.Agents.AgentRegistry"/>'s
///   <c>ChooseLibraryPickAsync</c> for parity with
///   <see cref="ChordOfCallingFactory"/>.
/// - <b>"Sacrifice after III" sentinel timing</b>: SBA fires immediately
///   when the lore counter hits 3, matching the engine's synchronous
///   chapter resolution. Stack-driven chapter triggers (so III can be
///   responded to before resolution) is a deeper Saga-engine cut.
/// </summary>
[CardName("Urza's Saga")]
public static class UrzasSagaFactory
{
    public const string CardName = "Urza's Saga";

    /// <summary>
    /// Construct Urza's Saga with no live runtime services. Chapter
    /// bodies still fire on <c>SagaState.AdvanceAndChapter</c>; the I/II
    /// Construct token spawns as a 0/0 (no CDA P/T rider — SBA will
    /// sweep it). Suitable for identity / shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, zoneService: null, effects: null);

    /// <summary>
    /// Construct Urza's Saga with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service. Routes the III
    /// tutor's Library → Battlefield move through
    /// <see cref="ZoneService.MoveCard"/> so ETB triggers on the
    /// tutored artifact fire (CR 603.6a). Also forwarded to the I/II
    /// Construct token spawn so token ETB triggers (e.g. Soul Warden)
    /// fire.</param>
    /// <param name="effects">Optional continuous-effects service. Used
    /// to register the I/II Construct token's CDA "+1/+1 per artifact
    /// you control" P/T rider — without it the token is a 0/0 SBA
    /// victim.</param>
    public static Land Create(
        Player owner,
        ZoneService? zoneService,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Primary type Land — matches ScryfallCardFactory.PickPrimaryType
        // ordering (Land > Enchantment). CR 205.4 — subtypes Urza's + Saga.
        var saga = new Land(
            name: CardName,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Urzas, CardSubtype.Saga });

        saga.SetOwner(owner);
        saga.SetController(owner);

        // CR 205.2a — additional card types stack on the primary. Add
        // Enchantment so HasType(CardType.Enchantment) matches (the
        // Land base only stamps CardType.Land).
        saga.AddCardType(CardType.Enchantment);

        // {T}: Add {C}. CR 605.1 — mana ability, never goes on the stack.
        // Listed explicitly in Urza's Saga's printed oracle, so the
        // production Scryfall load path also wires this via OracleManaBinder.
        saga.AddAbility(new ManaAbility(saga, owner, ManaCost.Parse("C")));

        // Chapter wiring lives in SagaBinder — same branch the Scryfall
        // load path takes. Pass the matching oracle text so the binder
        // can parse the final chapter (III). effects + zones forward to
        // the I/II Construct token + III tutor closures.
        var entity = new CardEntity
        {
            ScryfallId = Guid.NewGuid().ToString(),
            Name = CardName,
            TypeLine = "Legendary Enchantment Land — Urza's Saga",
            OracleText = OracleText,
            Colors = "",
            ColorIdentity = "",
            Keywords = "",
            Legalities = "",
        };
        SagaBinder.Bind(saga, entity, effects, zoneService);

        return saga;
    }

    /// <summary>
    /// Printed oracle text used to seed <see cref="SagaBinder"/>'s
    /// chapter parser. Pulled from the
    /// <c>SagaBinder.ChapterMarker</c> regex's "<c>I, II —</c>" / "<c>III —</c>"
    /// markers to determine the final chapter.
    /// </summary>
    private const string OracleText =
        "(As this Saga enters and after your draw step, add a lore counter. Sacrifice after III.)\n" +
        "I, II — Create a 0/0 colorless Construct artifact creature token with \"This creature gets +1/+1 for each artifact you control.\"\n" +
        "III — Search your library for an artifact card with mana value 2 or less, put it onto the battlefield, then shuffle.\n" +
        "{T}: Add {C}.";
}
