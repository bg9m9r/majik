using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sliver Legion (Future Sight, {W}{U}{B}{R}{G}).
///
/// Legendary Creature — Sliver 7/7. Oracle text (verified against
/// Scryfall):
///   "All Sliver creatures get +1/+1 for each other Sliver on the
///    battlefield."
///
/// The base shape (name, Legendary supertype, Creature, Sliver subtype,
/// five-colour cost, 7/7) is materialised from the embedded JSON
/// definition (<c>sliver-legion.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The dynamic anthem is
/// layered on top here.
///
/// ## Implemented (v1)
///
/// - <b>7/7 Legendary Creature — Sliver</b> at {W}{U}{B}{R}{G}.
/// - <b>Dynamic Sliver anthem (CR 613.7c — Layer 7c)</b>: "All Sliver
///   creatures get +1/+1 for each other Sliver on the battlefield."
///   Wired via <see cref="SliverLegionAnthemEffect"/> (private below).
///   <list type="bullet">
///     <item>"All Sliver creatures" — CR 109.5: the anthem applies to
///       every Sliver creature on the battlefield regardless of
///       controller (NOT controller-scoped, unlike the keyword-granting
///       Sliver lords such as <see cref="StrikingSliverFactory"/>).</item>
///     <item>"for each other Sliver on the battlefield" — the bonus is
///       N/N where N is the total number of Sliver permanents on the
///       battlefield (across ALL players) MINUS one (the "other"
///       exclusion — every recipient is itself a Sliver, so for any
///       single recipient the count of OTHER Slivers is total − 1, and
///       that value is uniform across all recipients).</item>
///   </list>
///   The existing <see cref="LordStaticEffect"/> applies a FIXED ±P/±T;
///   Sliver Legion's pump is a live count of all Slivers on the
///   battlefield, so a tailored variant is shipped here (same posture as
///   <see cref="MasterOfEtheriumLordEffect"/>, whose pump filters on a
///   card-TYPE the generic lord can't express).
///
/// ## All-players count resolver
///
/// Counting Slivers across EVERY battlefield needs the full player list.
/// The engine surfaces that to factory closures via a
/// <c>Func&lt;IReadOnlyList&lt;Player&gt;&gt;</c> resolver (same pattern
/// as Adeline / Ashiok). When the resolver is null the count falls back
/// to the source's controller battlefield only — correct for the common
/// single-controller board and for shape tests, conservative otherwise.
///
/// ## Lifecycle
///
/// The anthem is registered against the supplied
/// <see cref="ContinuousEffectsService"/> when wiring is requested. Its
/// <see cref="SliverLegionAnthemEffect.IsActive"/> gate short-circuits
/// when Sliver Legion is off the battlefield, so the pump lifts when the
/// Legion leaves play (same posture as the keyword Sliver lords). A
/// future Prune pass could drop the stale entry — see the deferred note.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No anthem registered
///   (no continuous-effects service). Suitable for dispatcher / identity
///   tests.
/// - <see cref="Create(Player, ContinuousEffectsService, Func{IReadOnlyList{Player}})"/>
///   — fully wired. The dynamic anthem registers against the layers
///   service; the resolver supplies the all-players list for the count.
///
/// ## Deferred (v1 gaps)
///
/// - <b>LTB unregister</b>: the registered anthem stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="SliverLegionAnthemEffect.IsActive"/> short-circuits when
///   Sliver Legion isn't on the battlefield so the grant lifts correctly,
///   but a future Prune pass could drop the entry. Same shape as
///   <see cref="StrikingSliverFactory"/> / <see cref="BladeSplicerFactory"/>.
/// </summary>
[CardName("Sliver Legion")]
public static class SliverLegionFactory
{
    public const string CardName = "Sliver Legion";
    public const string Slug = "sliver-legion";

    /// <summary>
    /// Construct Sliver Legion with no live wiring. The dynamic anthem is
    /// NOT registered (no continuous-effects service). Suitable for shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, allPlayersResolver: null);

    /// <summary>
    /// Construct a fully-wired Sliver Legion. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="SliverLegionAnthemEffect"/> granting +N/+N (N = other
    /// Slivers on the battlefield) to every Sliver creature is registered
    /// against the layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// dynamic Sliver anthem against. May be null — no live grant.</param>
    /// <param name="allPlayersResolver">Returns the full player list so the
    /// anthem can count Slivers across every battlefield. May be null — the
    /// count then falls back to the source's controller battlefield.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Sliver subtype, {W}{U}{B}{R}{G}, 7/7). The JSON carries
        // no abilities — the dynamic Sliver anthem is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 613.7c — "All Sliver creatures get +1/+1 for each other Sliver
        // on the battlefield." Registered only when a continuous-effects
        // service is supplied (matches StrikingSliver's posture).
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new SliverLegionAnthemEffect(card, allPlayersResolver));
        }

        return card;
    }

    /// <summary>
    /// Count every Sliver permanent on the battlefield across the supplied
    /// players (CR 109.5 — "on the battlefield" is unscoped by controller).
    /// When <paramref name="allPlayersResolver"/> is null, only the source's
    /// controller battlefield is scanned. Pure helper exposed for tests;
    /// mirrors the tally baked into the live anthem effect.
    /// </summary>
    public static int CountSliversOnBattlefield(
        Permanent source,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(source);

        var players = allPlayersResolver?.Invoke();
        if (players == null || players.Count == 0)
        {
            // Fallback: source controller's battlefield only.
            var controller = source.Controller;
            return controller == null ? 0 : CountSliversOn(controller);
        }

        var total = 0;
        foreach (var player in players)
        {
            if (player == null) continue;
            total += CountSliversOn(player);
        }
        return total;
    }

    private static int CountSliversOn(Player player)
    {
        var count = 0;
        foreach (var c in player.Zones.Battlefield.GetCards())
        {
            // CR 711 etc. — a card on the battlefield with the Sliver
            // subtype counts (every printed Sliver is a creature; the
            // anthem's "Sliver" tally is by subtype, not by card type).
            if (c is Permanent p && p.HasSubtype(CardSubtype.Sliver)) count++;
        }
        return count;
    }
}

/// <summary>
/// Sliver Legion's "All Sliver creatures get +1/+1 for each other Sliver
/// on the battlefield" static (CR 613.7c — Layer 7c).
///
/// The generic <see cref="LordStaticEffect"/> applies a FIXED ±P/±T and
/// is controller-scoped by default; Sliver Legion needs (a) an
/// all-players scope ("All Sliver creatures") and (b) a LIVE count of all
/// Slivers on the battlefield. A tailored variant is shipped here (same
/// posture as <see cref="MasterOfEtheriumLordEffect"/>).
///
/// Filter (CR 613.7c — continuous effects apply only to permanents):
///   - Target is on the battlefield.
///   - Target has the <see cref="CardSubtype.Sliver"/> subtype. No
///     controller filter — "All Sliver creatures", every player's.
///   - Sliver Legion itself IS a Sliver creature, so it is included.
///
/// Pump: +N/+N where N = (total Slivers on the battlefield) − 1. Each
/// recipient is itself a Sliver, so the count of OTHER Slivers for any
/// single recipient is the total minus one, a value uniform across every
/// recipient (so the per-recipient subtraction is constant and can be
/// applied without knowing which creature is being computed).
/// </summary>
public sealed class SliverLegionAnthemEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly Func<IReadOnlyList<Player>>? _allPlayersResolver;

    public SliverLegionAnthemEffect(
        Permanent source,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _allPlayersResolver = allPlayersResolver;
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — the lord permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != ZoneType.Battlefield) return false;
        // CR 109.5 — "All Sliver creatures": no controller filter; every
        // player's Slivers (including Sliver Legion itself) are buffed.
        return creature.HasSubtype(CardSubtype.Sliver);
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        // N = other Slivers on the battlefield = total − 1. Every recipient
        // is a Sliver, so "other" is the same total−1 for all of them.
        var total = SliverLegionFactory.CountSliversOnBattlefield(_source, _allPlayersResolver);
        var others = total - 1;
        if (others <= 0) return;
        chars.Power += others;
        chars.Toughness += others;
    }
}
