using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rest in Peace (Avacyn Restored, {1}{W}).
///
/// Enchantment. Oracle text:
///   "When this enchantment enters, exile all graveyards."
///   "If a card or token would be put into a graveyard from anywhere,
///    exile it instead."
///
/// ## Implementation
///
/// Two halves:
///
/// 1. <b>ETB exile-all-graveyards</b> (CR 603.6a / CR 701.21). A
///    <see cref="TriggeredAbility"/> watching the enchantment's own
///    <see cref="CardMovedEvent"/> ETB; on resolve walks every player
///    supplied via <paramref name="allPlayersResolver"/> and moves every
///    card out of <see cref="ZoneType.Graveyard"/> to
///    <see cref="ZoneType.Exile"/>. Routes through
///    <see cref="ZoneService"/> when supplied so the move publishes
///    <see cref="CardMovedEvent"/> (Bridge from Below / Bloodghast
///    graveyard-resident triggers see the moves and react). When the
///    zone service is null, the move is mutated directly on the
///    player's <see cref="Zones.Graveyard"/>.
/// 2. <b>Static replacement</b> (CR 614). While Rest in Peace is on
///    the battlefield, any <see cref="ZoneMoveIntent"/> headed to a
///    graveyard is rewritten to <see cref="ZoneType.Exile"/>. The
///    rewrite is unconditional (no source-zone gate, no controller
///    scope — matches "from anywhere"). The
///    <see cref="RestInPeaceGraveyardRewrite"/> gates its
///    <c>Applies</c> on Rest in Peace being on the battlefield, so
///    blink / bounce / destroy stop the rewrite immediately without
///    explicit deregistration. The replacement is NOT EOT-expirable —
///    static effects from on-battlefield permanents do not sweep at
///    end of turn (CR 614.6).
///
/// ## Cross-references
///
/// Mirror of <see cref="AngerOfTheGodsExileInsteadReplacement"/> shape
/// (graveyard rewrite via <see cref="ZoneMoveIntent"/>) and
/// <see cref="GraveyardToExileReplacement"/> (Ashiok's similar
/// unconditional rewrite — RIP differs by being battlefield-resident
/// rather than EOT-expirable).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Token "put into a graveyard" branch</b>: the printed "card or
///   token" oracle includes tokens, but tokens that "die" cease to
///   exist via SBA (CR 704.5d → token destroyed → CR 704.5e moves it
///   to graveyard then it ceases to exist) — they never settle in a
///   graveyard. v1 rewrites their pre-cease destination to exile
///   identically; the token's eventual cease-to-exist still happens
///   from exile (CR 704.5d operates on the "no longer on the
///   battlefield" predicate, not zone-specific).
/// - <b>Replacement-ordering prompt</b>: if multiple graveyard
///   replacements overlap, the affected player chooses ordering
///   (CR 616.1). Bus applies in registration order today — same gap
///   as Anger of the Gods / Leyline of the Void / every other
///   replacement.
/// </summary>
[CardName("Rest in Peace")]
public static class RestInPeaceFactory
{
    public const string CardName = "Rest in Peace";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>
    /// Constructs a Rest in Peace with card identity only (no ETB or
    /// replacement wiring). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, allPlayersResolver: null, replacements: null, zoneService: null, triggers: null);

    /// <summary>
    /// Constructs a Rest in Peace.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="allPlayersResolver">Returns the full player list
    /// at ETB resolution. v1 walks every player's graveyard. Null →
    /// the ETB exile half no-ops.</param>
    /// <param name="replacements">Bus on which the static
    /// graveyard→exile replacement is registered. Null → static half
    /// is skipped.</param>
    /// <param name="zoneService">Used to route the ETB graveyard-to-exile
    /// moves so <see cref="CardMovedEvent"/> publishes (graveyard-resident
    /// triggers see the moves). Null → falls back to direct zone mutation.</param>
    /// <param name="triggers">When supplied, the ETB triggered ability is
    /// registered against the manager so it lands on the stack on
    /// battlefield arrival.</param>
    public static Enchantment Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        ReplacementBus? replacements,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Static replacement — register up-front. The replacement's
        // Applies check gates on Card.Zone == Battlefield, so it's
        // inert until the enchantment lands. CR 614.6 — static effects
        // from on-battlefield permanents are not EOT-expirable.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<ZoneMoveIntent>(new RestInPeaceGraveyardRewrite(card));
        }

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a. "When ~ enters, exile all
        // graveyards." Walks every player's graveyard at resolve time.
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName} — exile all graveyards (CR 701.21)",
            () => ResolveEtbExile(allPlayersResolver, zoneService));

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Resolve helper: exile every card sitting in every supplied
    /// player's graveyard. Exposed so tests can invoke directly without
    /// driving the full TriggerManager loop.
    /// </summary>
    public static void ResolveEtbExile(
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        ZoneService? zoneService)
    {
        var players = allPlayersResolver?.Invoke();
        if (players == null) return;

        foreach (var player in players)
        {
            if (player == null) continue;
            // Snapshot to a list — moving cards mutates the underlying
            // collection while we iterate.
            var graveyardCards = player.Zones.Graveyard.GetCards().ToList();
            foreach (var graveCard in graveyardCards)
            {
                if (zoneService != null)
                {
                    zoneService.MoveCard(graveCard, ZoneType.Graveyard, ZoneType.Exile);
                }
                else
                {
                    player.Zones.Graveyard.RemoveCard(graveCard);
                    player.Zones.Exile.AddCard(graveCard);
                    graveCard.SetZone(ZoneType.Exile);
                }
            }
        }
    }
}

/// <summary>
/// CR 614 replacement effect: while Rest in Peace is on the
/// battlefield, every <see cref="ZoneMoveIntent"/> headed to
/// <see cref="ZoneType.Graveyard"/> (from any source zone, any
/// controller) is rewritten to <see cref="ZoneType.Exile"/>. Not
/// EOT-expirable — the static stays live as long as the enchantment
/// is on the battlefield (CR 614.6).
/// </summary>
public sealed class RestInPeaceGraveyardRewrite : IReplacementEffect<ZoneMoveIntent>
{
    private readonly Enchantment _source;

    public RestInPeaceGraveyardRewrite(Enchantment source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        return intent.ToZone == ZoneType.Graveyard;
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { ToZone = ZoneType.Exile };
}
