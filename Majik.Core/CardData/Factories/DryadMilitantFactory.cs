using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dryad Militant (Return to Ravnica promo / reprints,
/// {G/W}).
///
/// Creature — Dryad Soldier 2/1. Oracle text (verified against Scryfall):
///   "({G/W} can be paid with either {G} or {W}.)"
///   "If an instant or sorcery card would be put into a graveyard from
///    anywhere, exile it instead."
///
/// Structurally the static-replacement half of <see cref="RestInPeaceFactory"/>
/// / <see cref="SanctifierEnVecFactory"/> (a CR 614 graveyard→exile rewrite,
/// gated on the source being on the battlefield, not EOT-expirable), but
/// FILTERED to instant-or-sorcery <i>cards</i> instead of colour. There is no
/// ETB sweep — Dryad Militant has no enters trigger (it only ever affects
/// future graveyard moves).
///
/// The base shape (name, Creature, Dryad/Soldier, {G/W}, 2/1) is materialised
/// from the embedded JSON definition (<c>dryad-militant.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the hybrid {G/W} pip parses
/// through <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> (CR 107.4e),
/// same as Kitchen Finks {G/W}. The replacement behaviour is layered on here
/// because the JSON <c>AbilityDefinition</c> schema doesn't express replacement
/// effects (same posture as <see cref="SanctifierEnVecFactory"/>).
///
/// ## Implemented (v1)
/// - 2/1 <see cref="Creature"/> at {G/W} with Dryad + Soldier subtypes.
/// - <b>Static replacement (CR 614)</b>: while Dryad Militant is on the
///   battlefield, any <see cref="ZoneMoveIntent"/> headed to
///   <see cref="ZoneType.Graveyard"/> whose moving card is an instant or
///   sorcery <i>card</i> is rewritten to <see cref="ZoneType.Exile"/>. "From
///   anywhere" → no source-zone gate (a spell resolving off the stack, a
///   discard from hand, a mill from library all qualify). The rewrite keys on
///   the card type only — CR 614.13 / CR 111 tokens are not "cards", so an
///   instant/sorcery copy (a token copy of a spell) is excluded by the
///   <c>card</c> wording; the common case (real spell cards) is what reaches a
///   graveyard. Not EOT-expirable (CR 614.6); gates internally on the creature
///   being on the battlefield so blink / bounce / destroy stop the rewrite
///   immediately without explicit deregistration.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-ordering prompt</b> (CR 616.1): the bus applies in
///   registration order — overlapping with Rest in Peace / Leyline of the Void
///   / another graveyard replacement picks the registration order today.
///   Affected-player choice deferred (same gap as every other graveyard
///   replacement factory).
/// </summary>
[CardName("Dryad Militant")]
public static class DryadMilitantFactory
{
    public const string CardName = "Dryad Militant";
    public const string Slug = "dryad-militant";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Dryad Militant with card identity only (no replacement
    /// wiring). This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to — suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Dryad Militant.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">Bus on which the static instant-or-sorcery
    /// graveyard→exile replacement is registered. Null → static half is
    /// skipped (card identity only).</param>
    public static Creature Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Dryad/Soldier, {G/W}, 2/1). The JSON carries no abilities — the
        // replacement is layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Static replacement — register up-front. The replacement's Applies
        // check gates on Card.Zone == Battlefield, so it's inert until the
        // creature lands. CR 614.6 — static effects from on-battlefield
        // permanents are not EOT-expirable.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<ZoneMoveIntent>(new DryadMilitantGraveyardRewrite(card));
        }

        return card;
    }

    /// <summary>
    /// CR 305 / CR 304 — a card is an instant or sorcery card iff its card
    /// types include <see cref="CardType.Instant"/> or
    /// <see cref="CardType.Sorcery"/>.
    /// </summary>
    internal static bool IsInstantOrSorcery(ICard card)
    {
        if (card == null) return false;
        return card.HasType(CardType.Instant) || card.HasType(CardType.Sorcery);
    }
}

/// <summary>
/// CR 614 replacement effect: while Dryad Militant is on the battlefield,
/// every <see cref="ZoneMoveIntent"/> headed to <see cref="ZoneType.Graveyard"/>
/// whose moving card is an instant or sorcery card (from any source zone, any
/// controller — "from anywhere") is rewritten to <see cref="ZoneType.Exile"/>.
/// Not EOT-expirable — the static stays live as long as the creature is on the
/// battlefield (CR 614.6).
/// </summary>
public sealed class DryadMilitantGraveyardRewrite : IReplacementEffect<ZoneMoveIntent>
{
    private readonly Creature _source;

    public DryadMilitantGraveyardRewrite(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.ToZone != ZoneType.Graveyard) return false;
        return DryadMilitantFactory.IsInstantOrSorcery(intent.Card);
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { ToZone = ZoneType.Exile };
}
