using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Production-wiring tests for the context-aware continuous-effect predicate
/// seam (the <c>continuous-effect-predicate-context-aware-opponents</c>
/// deferral).
///
/// <para>The one-shot (resolution-context) halves of Thieves' Guild Enforcer
/// (each-opponent mill) and Scourge of the Skyclaves (each-player half-life
/// loss) already read live players off the <c>ResolutionContext</c>. The
/// remaining gap was the CONTINUOUS-EFFECT half — Thieves' Guild Enforcer's
/// conditional +2/+1 + deathtouch (predicate scans opponents' graveyards) and
/// Scourge's CDA P/T (highest life among ALL players). Both relied on a
/// <c>Func&lt;IReadOnlyList&lt;Player&gt;&gt;</c> resolver captured at
/// factory-build time, which the production source-generated instance-swap
/// dispatch NEVER supplies — it only calls
/// <c>Create(Player, ContinuousEffectsService)</c>. So in real matches the
/// buff / CDA were inert.</para>
///
/// <para>The fix mirrors the group-grant family
/// (<see cref="GroupGrantControlledNotOwnedProdWiringTests"/>): each factory
/// gains a 2-arg <c>Create(Player, ContinuousEffectsService)</c> overload that
/// derives the live player roster from
/// <see cref="ContinuousEffectsService.PlayersProvider"/> (wired by the live
/// game graph — GameFacade / Game) instead of a captured resolver. These tests
/// drive exactly that production entry point —
/// <c>NamedCardFactory.Create(name, owner, effects)</c> with the roster on the
/// service and NO per-factory resolver — and assert the continuous-effect half
/// is live.</para>
/// </summary>
public class ContinuousEffectContextAwareOpponentsProdWiringTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;

    public ContinuousEffectContextAwareOpponentsProdWiringTests()
    {
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
        // Mirror the production game graph (GameFacade.cs sets exactly this):
        // the live player roster is wired onto the effects service so a
        // context-aware continuous-effect predicate can read opponents /
        // all-players WITHOUT a captured resolver.
        _effects.PlayersProvider = () => new[] { _alice, _bob };
    }

    private void PutOnBattlefield(ICard card, Player owner)
    {
        if (card is Permanent p) p.ActiveEffects = _effects; // prod wires this on every battlefield permanent
        owner.Zones.Library.AddCard(card);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
    }

    private static void StockGraveyard(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var card = new Card($"GraveFiller {i}", "");
            card.SetOwner(p);
            card.SetController(p);
            p.Zones.Graveyard.AddCard(card);
            card.SetZone(ZoneType.Graveyard);
        }
    }

    // -----------------------------------------------------------------------
    // Thieves' Guild Enforcer — conditional +2/+1 + deathtouch.
    //   "As long as an opponent has 8+ cards in their graveyard, this gets
    //    +2/+1 and has deathtouch."
    // -----------------------------------------------------------------------

    /// <summary>
    /// RED before the fix: built through the PRODUCTION effects-aware dispatch
    /// (<c>NamedCardFactory.Create(name, owner, effects)</c>) with the roster on
    /// the service but NO per-factory resolver, the conditional buff must read
    /// the OPPONENT's graveyard off the live roster and activate the +2/+1 +
    /// deathtouch (CR 700.2g — "as long as" re-evaluates continuously).
    /// </summary>
    [Fact]
    public void ThievesGuildEnforcer_ProdDispatch_BuffActivatesFromLiveOpponentGraveyard()
    {
        StockGraveyard(_bob, 8); // opponent at threshold.

        var enforcer = (Creature)NamedCardFactory.Create(
            "Thieves' Guild Enforcer", _alice, _effects);
        PutOnBattlefield(enforcer, _alice);

        enforcer.GetPower().Should().Be(3,
            "the opponent has 8+ cards in graveyard → +2/+1 via the live roster " +
            "read from the service PlayersProvider, no captured resolver");
        enforcer.GetToughness().Should().Be(2);
        _effects.Compute(enforcer).Keywords.Should().Contain("Deathtouch",
            "and gains deathtouch under the same context-aware predicate");
    }

    /// <summary>
    /// Below threshold via the production dispatch → base 1/1, no deathtouch.
    /// </summary>
    [Fact]
    public void ThievesGuildEnforcer_ProdDispatch_NoBuffBelowThreshold()
    {
        StockGraveyard(_bob, 7); // one below threshold.

        var enforcer = (Creature)NamedCardFactory.Create(
            "Thieves' Guild Enforcer", _alice, _effects);
        PutOnBattlefield(enforcer, _alice);

        enforcer.GetPower().Should().Be(1, "below the 8-card threshold → base 1/1");
        enforcer.GetToughness().Should().Be(1);
        _effects.Compute(enforcer).Keywords.Should().NotContain("Deathtouch");
    }

    /// <summary>
    /// CR 700.2g — "as long as an OPPONENT has …": the controller's own
    /// graveyard never counts, even through the production dispatch.
    /// </summary>
    [Fact]
    public void ThievesGuildEnforcer_ProdDispatch_IgnoresControllersOwnGraveyard()
    {
        StockGraveyard(_alice, 10); // controller's own graveyard — does NOT count.

        var enforcer = (Creature)NamedCardFactory.Create(
            "Thieves' Guild Enforcer", _alice, _effects);
        PutOnBattlefield(enforcer, _alice);

        enforcer.GetPower().Should().Be(1, "the predicate scans OPPONENTS' graveyards only");
        _effects.Compute(enforcer).Keywords.Should().NotContain("Deathtouch");
    }

    // -----------------------------------------------------------------------
    // Scourge of the Skyclaves — CDA P/T = 20 - highest life among players.
    // -----------------------------------------------------------------------

    /// <summary>
    /// RED before the fix: built through the PRODUCTION effects-aware dispatch
    /// with the roster on the service but NO per-factory resolver, the CDA must
    /// read the highest life among ALL players off the live roster
    /// (CR 604.3 / 613.2 Layer 7a). Both at 20 → 20-20 = 0/0.
    /// </summary>
    [Fact]
    public void Scourge_ProdDispatch_CdaReadsHighestLifeAmongAllPlayers()
    {
        _alice.LifeTotal = 12;
        _bob.LifeTotal = 8;

        var scourge = (Creature)NamedCardFactory.Create(
            "Scourge of the Skyclaves", _alice, _effects);
        PutOnBattlefield(scourge, _alice);

        // Highest life is Alice's 12 → 20-12 = 8/8.
        scourge.GetPower().Should().Be(8,
            "the CDA reads the highest life among all players from the live roster " +
            "(service PlayersProvider), no captured resolver");
        scourge.GetToughness().Should().Be(8);
    }

    /// <summary>
    /// CDA tracks live life-total changes through the production dispatch: an
    /// opponent dropping below the controller flips which life total is highest
    /// (CR 613.2 re-evaluates continuously, via the LifeChangedEvent bump).
    /// </summary>
    [Fact]
    public void Scourge_ProdDispatch_CdaTracksLifeChanges()
    {
        _alice.LifeTotal = 20;
        _bob.LifeTotal = 20;

        var scourge = (Creature)NamedCardFactory.Create(
            "Scourge of the Skyclaves", _alice, _effects);
        PutOnBattlefield(scourge, _alice);

        // Highest 20 → 0/0.
        scourge.GetPower().Should().Be(0);

        // Bob drops to 13, Alice still 20 → highest 20 → still 0/0.
        _bob.LifeTotal = 13;
        _effects.Clear();
        scourge.GetPower().Should().Be(0);

        // Alice drops to 9, Bob 13 → highest now 13 → 20-13 = 7/7.
        _alice.LifeTotal = 9;
        _effects.Clear();
        scourge.GetPower().Should().Be(7);
        scourge.GetToughness().Should().Be(7);
    }
}
