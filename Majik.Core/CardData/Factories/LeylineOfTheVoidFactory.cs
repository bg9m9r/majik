using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline of the Void (Guildpact / Modern
/// Horizons / many reprints, {2}{B}{B}).
///
/// Enchantment. Oracle text:
///   "If this card is in your opening hand, you may begin the game
///    with it on the battlefield."
///   "If a card would be put into an opponent's graveyard from
///    anywhere, exile it instead."
///
/// ## Implementation
///
/// Two halves:
/// 1. <b>Opening-hand alt-cost</b> (CR 702.95 — Leyline keyword) via the
///    shared <see cref="OpeningHandLeylineAlternativeCost"/> subscriber:
///    the factory stamps a <see cref="KeywordAbility"/> with marker
///    <c>"OpeningHandLeyline"</c> (
///    <see cref="OpeningHandLeylineAlternativeCost.LeylineKeyword"/>),
///    which the subscriber scans for on the
///    <see cref="Majik.Core.Events.OpeningHandCheckEvent"/> fired by
///    <see cref="GameDriver"/> AFTER the mulligan loop resolves.
/// 2. <b>Static replacement</b> (CR 614) — opponent-graveyard rewrites
///    to exile while Leyline is on the battlefield.
///
/// The replacement is scoped to opponents of Leyline's controller:
/// any <see cref="ZoneMoveIntent"/> headed to
/// <see cref="ZoneType.Graveyard"/> whose owner-side graveyard belongs
/// to a non-controller player is rewritten to
/// <see cref="ZoneType.Exile"/>. Cards heading to Leyline's
/// controller's own graveyard pass through unchanged — distinct from
/// <see cref="RestInPeaceFactory"/>'s symmetric rewrite.
///
/// "Opponent's graveyard" is determined by the moving card's
/// <see cref="ICard.Owner"/> (CR 109.5 — a card always goes to its
/// owner's graveyard, never the controller's). The check happens on
/// the moving card's owner, not the intent's
/// <see cref="ZoneMoveIntent.Controller"/> field — that field tracks
/// the new battlefield controller for ETB moves and is null for
/// graveyard-bound intents.
///
/// ## Lifecycle
///
/// The replacement is registered up-front in <see cref="Create"/> and
/// gates internally on Leyline being on the battlefield via
/// <see cref="LeylineOfTheVoidRewrite.Applies"/>. Blink / bounce /
/// destroy stop the rewrite immediately. The static is NOT
/// EOT-expirable (CR 614.6).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Replacement-ordering prompt</b> (CR 616.1): bus applies in
///   registration order — overlapping with Rest in Peace, Anafenza,
///   or another Leyline of the Void picks the registration order
///   today. Affected-player choice deferred.
/// </summary>
[CardName("Leyline of the Void")]
public static class LeylineOfTheVoidFactory
{
    public const string CardName = "Leyline of the Void";
    public const string PrintedManaCost = "{2}{B}{B}";

    /// <summary>
    /// Constructs a Leyline of the Void with card identity only.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Constructs a Leyline of the Void. When
    /// <paramref name="replacements"/> is supplied, the static
    /// opponent-graveyard rewrite is registered. The replacement
    /// internally gates on Leyline being on the battlefield, so
    /// registration is idempotent across the card's lifecycle.
    /// </summary>
    public static Enchantment Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.95 — Leyline keyword marker. Scanned by
        // OpeningHandLeylineAlternativeCost on OpeningHandCheckEvent.
        card.AddAbility(new KeywordAbility(
            OpeningHandLeylineAlternativeCost.LeylineKeyword, card, owner));

        if (replacements != null)
        {
            replacements.Register<ZoneMoveIntent>(new LeylineOfTheVoidRewrite(card));
        }

        return card;
    }
}

/// <summary>
/// CR 614 replacement effect: while Leyline of the Void is on the
/// battlefield, every <see cref="ZoneMoveIntent"/> whose moving card
/// is owned by a non-controller (i.e. an opponent of Leyline's
/// controller) and whose destination is
/// <see cref="ZoneType.Graveyard"/> is rewritten to
/// <see cref="ZoneType.Exile"/>. Not EOT-expirable.
/// </summary>
public sealed class LeylineOfTheVoidRewrite : IReplacementEffect<ZoneMoveIntent>
{
    private readonly Enchantment _source;

    public LeylineOfTheVoidRewrite(Enchantment source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.ToZone != ZoneType.Graveyard) return false;

        // CR 109.5 — the card's owner determines whose graveyard it
        // would land in. Leyline scopes to "opponent's graveyard",
        // i.e. any owner that is not Leyline's controller. When the
        // owner is null (raw test-only card without an owner stamp)
        // the replacement no-ops conservatively.
        var owner = intent.Card.Owner;
        if (owner == null) return false;
        return !ReferenceEquals(owner, _source.Controller);
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { ToZone = ZoneType.Exile };
}
