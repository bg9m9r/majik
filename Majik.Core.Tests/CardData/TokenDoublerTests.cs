using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for the <see cref="TokenCreationIntent"/> /
/// <see cref="TokenDoublerReplacement"/> primitive pair and the three
/// named-card doublers wired on top of it (Anointed Procession,
/// Parallel Lives, Doubling Season).
///
/// Covers:
///   - Intent shape — record with mutable-on-Apply Count via <c>with { }</c>.
///   - <see cref="TokenDoublerReplacement"/> doubles count when predicate fires.
///   - <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, int, ZoneService?, ReplacementBus?)"/>
///     mints the post-replacement count.
///   - Anointed Procession: ship-2 → mint-4.
///   - Parallel Lives + Anointed Procession stack multiplicatively
///     (ship-1 → mint-4).
///   - Doubling Season's token half doubles tokens AND counter half
///     doubles +1/+1 counters routed through <see cref="CountersService.Add"/>.
///   - Dispatcher wiring under each printed name.
///   - Opponent-controlled token creation is NOT doubled (one-sided gate).
/// </summary>
public class TokenDoublerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Intent shape
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenCreationIntent_ShapeFields()
    {
        var spec = new TokenFactory.TokenSpec("Soldier", 1, 1);
        var intent = new TokenCreationIntent(_alice, spec, 2);

        intent.Controller.Should().BeSameAs(_alice);
        intent.Spec.Should().BeSameAs(spec);
        intent.Count.Should().Be(2);
    }

    [Fact]
    public void TokenCreationIntent_WithExpressionRewritesCount()
    {
        var spec = new TokenFactory.TokenSpec("Soldier", 1, 1);
        var intent = new TokenCreationIntent(_alice, spec, 2);

        var rewritten = intent with { Count = intent.Count * 2 };

        rewritten.Count.Should().Be(4);
        intent.Count.Should().Be(2, "record is immutable; `with` returns a new instance");
    }

    // -----------------------------------------------------------------------
    // TokenDoublerReplacement
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenDoubler_PredicateTrue_DoublesCount()
    {
        var bus = new ReplacementBus();
        bus.Register<TokenCreationIntent>(new TokenDoublerReplacement(_ => true));

        var intent = new TokenCreationIntent(
            _alice, new TokenFactory.TokenSpec("Soldier", 1, 1), Count: 3);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.Count.Should().Be(6);
    }

    [Fact]
    public void TokenDoubler_PredicateFalse_LeavesCountUnchanged()
    {
        var bus = new ReplacementBus();
        bus.Register<TokenCreationIntent>(new TokenDoublerReplacement(_ => false));

        var intent = new TokenCreationIntent(
            _alice, new TokenFactory.TokenSpec("Soldier", 1, 1), Count: 3);

        var result = bus.Apply(intent);

        result!.Count.Should().Be(3);
    }

    [Fact]
    public void TokenDoubler_TwoIndependentRegistrations_StackMultiplicatively()
    {
        var bus = new ReplacementBus();
        bus.Register<TokenCreationIntent>(new TokenDoublerReplacement(_ => true));
        bus.Register<TokenCreationIntent>(new TokenDoublerReplacement(_ => true));

        var intent = new TokenCreationIntent(
            _alice, new TokenFactory.TokenSpec("Soldier", 1, 1), Count: 1);

        var result = bus.Apply(intent);

        result!.Count.Should().Be(4,
            "CR 616.1c — each replacement fires once per intent: 1 → 2 → 4");
    }

    [Fact]
    public void TokenDoubler_ZeroCount_DoesNotApply()
    {
        var bus = new ReplacementBus();
        bus.Register<TokenCreationIntent>(new TokenDoublerReplacement(_ => true));

        var intent = new TokenCreationIntent(
            _alice, new TokenFactory.TokenSpec("Soldier", 1, 1), Count: 0);

        var result = bus.Apply(intent);

        result!.Count.Should().Be(0,
            "printed 'one or more' floor — Count ≤ 0 short-circuits Applies");
    }

    // -----------------------------------------------------------------------
    // TokenFactory bus-aware overload
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateOnBattlefield_NoBus_MintsExactCount()
    {
        var spec = new TokenFactory.TokenSpec(
            "Soldier", 1, 1,
            Subtypes: new[] { CardSubtype.Soldier },
            Colors: new[] { ManaColor.White });

        var minted = TokenFactory.CreateOnBattlefield(
            spec, _alice, count: 3, zones: null, replacements: null);

        minted.Should().HaveCount(3);
        minted.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Soldier");
            t.IsToken.Should().BeTrue();
            t.Controller.Should().BeSameAs(_alice);
            t.Zone.Should().Be(ZoneType.Battlefield);
        });
    }

    [Fact]
    public void CreateOnBattlefield_WithDoubler_MintsTwiceCount()
    {
        var bus = new ReplacementBus();
        bus.Register<TokenCreationIntent>(new TokenDoublerReplacement(_ => true));

        var spec = new TokenFactory.TokenSpec("Soldier", 1, 1);
        var minted = TokenFactory.CreateOnBattlefield(
            spec, _alice, count: 1, zones: null, replacements: bus);

        minted.Should().HaveCount(2);
    }

    [Fact]
    public void CreateOnBattlefield_ZeroCount_MintsNothing()
    {
        var minted = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Soldier", 1, 1),
            _alice, count: 0, zones: null, replacements: null);

        minted.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Anointed Procession
    // -----------------------------------------------------------------------

    [Fact]
    public void AnointedProcession_Identity()
    {
        var c = AnointedProcessionFactory.Create(_alice);

        c.Name.Should().Be("Anointed Procession");
        c.ManaCost.Should().Be("{3}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AnointedProcession()
    {
        var card = NamedCardFactory.Create("Anointed Procession", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Anointed Procession");
        card.ManaCost.Should().Be("{3}{W}");
    }

    [Fact]
    public void AnointedProcession_ShippedTwo_MintsFour()
    {
        var bus = new ReplacementBus();
        var ap = AnointedProcessionFactory.Create(_alice, bus);
        PlaceOnBattlefield(ap, _alice);

        var minted = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Soldier", 1, 1),
            _alice, count: 2, zones: null, replacements: bus);

        minted.Should().HaveCount(4, "Anointed Procession doubles: 2 → 4");
    }

    [Fact]
    public void AnointedProcession_NotOnBattlefield_DoesNotDouble()
    {
        var bus = new ReplacementBus();
        var ap = AnointedProcessionFactory.Create(_alice, bus);
        // Intentionally not placed on battlefield — sits in Hand by default.

        var minted = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Soldier", 1, 1),
            _alice, count: 1, zones: null, replacements: bus);

        minted.Should().HaveCount(1,
            "doubler gates on source.Zone == Battlefield");
    }

    [Fact]
    public void AnointedProcession_OpponentTokens_NotDoubled()
    {
        var bus = new ReplacementBus();
        var ap = AnointedProcessionFactory.Create(_alice, bus);
        PlaceOnBattlefield(ap, _alice);

        var minted = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Soldier", 1, 1),
            _bob, count: 2, zones: null, replacements: bus);

        minted.Should().HaveCount(2,
            "Anointed Procession is one-sided ('tokens under your control')");
    }

    // -----------------------------------------------------------------------
    // Parallel Lives
    // -----------------------------------------------------------------------

    [Fact]
    public void ParallelLives_Identity()
    {
        var c = ParallelLivesFactory.Create(_alice);

        c.Name.Should().Be("Parallel Lives");
        c.ManaCost.Should().Be("{3}{G}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ParallelLives()
    {
        var card = NamedCardFactory.Create("Parallel Lives", _alice);
        card.Name.Should().Be("Parallel Lives");
        card.ManaCost.Should().Be("{3}{G}");
    }

    [Fact]
    public void ParallelLives_PlusAnointedProcession_StackMultiplicatively()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(ParallelLivesFactory.Create(_alice, bus), _alice);
        PlaceOnBattlefield(AnointedProcessionFactory.Create(_alice, bus), _alice);

        var minted = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Soldier", 1, 1),
            _alice, count: 1, zones: null, replacements: bus);

        minted.Should().HaveCount(4,
            "CR 616.1c — each doubler fires once: 1 → 2 → 4");
    }

    [Fact]
    public void TwoParallelLives_ShippedOne_MintsFour()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(ParallelLivesFactory.Create(_alice, bus), _alice);
        PlaceOnBattlefield(ParallelLivesFactory.Create(_alice, bus), _alice);

        var minted = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Soldier", 1, 1),
            _alice, count: 1, zones: null, replacements: bus);

        minted.Should().HaveCount(4, "1 → 2 → 4 (two independent doublers)");
    }

    // -----------------------------------------------------------------------
    // Doubling Season
    // -----------------------------------------------------------------------

    [Fact]
    public void DoublingSeason_Identity()
    {
        var c = DoublingSeasonFactory.Create(_alice);

        c.Name.Should().Be("Doubling Season");
        c.ManaCost.Should().Be("{4}{G}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DoublingSeason()
    {
        var card = NamedCardFactory.Create("Doubling Season", _alice);
        card.Name.Should().Be("Doubling Season");
        card.ManaCost.Should().Be("{4}{G}");
    }

    [Fact]
    public void DoublingSeason_TokenHalf_ShippedOne_MintsTwo()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(DoublingSeasonFactory.Create(_alice, bus), _alice);

        var minted = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Soldier", 1, 1),
            _alice, count: 1, zones: null, replacements: bus);

        minted.Should().HaveCount(2);
    }

    [Fact]
    public void DoublingSeason_CounterHalf_PlacingOnePlusOnePlusOne_LandsTwo()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(DoublingSeasonFactory.Create(_alice, bus), _alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(2, "Doubling Season doubles counters: 1 → 2");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void DoublingSeason_TokenHalf_PlusAnointedProcession_ShippedOne_MintsFour()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(DoublingSeasonFactory.Create(_alice, bus), _alice);
        PlaceOnBattlefield(AnointedProcessionFactory.Create(_alice, bus), _alice);

        var minted = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Soldier", 1, 1),
            _alice, count: 1, zones: null, replacements: bus);

        minted.Should().HaveCount(4);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PlaceOnBattlefield(Enchantment card, Player owner)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
