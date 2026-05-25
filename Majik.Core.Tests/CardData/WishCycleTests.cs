using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Wish cycle (Judgment + Future Sight): Burning Wish, Cunning Wish,
/// Glittering Wish, Living Wish, Death Wish. Every factory threads through
/// the shared <see cref="Majik.Core.Effects.WishTutorEffect"/> primitive
/// with a single type / colour predicate (plus a half-life-loss rider for
/// Death Wish).
///
/// Each card covers four checks:
///   1. Identity — name, cost, type, controller.
///   2. <see cref="NamedCardFactory"/> dispatch (source-gen via
///      <see cref="CardNameAttribute"/>).
///   3. <c>BuildDefinition</c> shape — no modes, no target requests.
///   4. Resolve produces the expected wishboard pick (and, for Death Wish,
///      the expected life loss).
///
/// AgentRegistry is process-global; tests Clear() on dispose so they
/// don't leak agents into neighbouring suites.
/// </summary>
public class WishCycleTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    public WishCycleTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static ChosenSpellParams Empty(Player[] all) => new(
        ModeIndex: null,
        X: null,
        Targets: Array.Empty<IReadOnlyList<object>>(),
        Mana: ManaPayment.Empty,
        AllPlayers: all);

    // -----------------------------------------------------------------------
    // Burning Wish — {1}{R} sorcery; sorcery-card tutor.
    // -----------------------------------------------------------------------

    [Fact]
    public void BurningWish_HasSorceryShape_At1R()
    {
        var card = BurningWishFactory.Create(_alice);

        card.Name.Should().Be("Burning Wish");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(2);
        Majik.Core.Cards.CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BurningWish_NamedCardFactory_Dispatches()
    {
        var dispatched = NamedCardFactory.Create("Burning Wish", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Burning Wish");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void BurningWish_BuildDefinition_Shape()
    {
        var def = BurningWishFactory.BuildDefinition(_alice);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty(
            "wish-tutor resolves via the wishboard pile, not a cast-time target");
    }

    [Fact]
    public void BurningWish_ResolvesSorceryPick_FromWishboard()
    {
        // Wishboard contains an instant + a sorcery. Predicate gates to
        // the sorcery only; deterministic first-(and-only)-pick.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var bigRed = new Sorcery("Mind's Desire", "{4}{U}{U}") { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);
        _alice.Wishboard.AddCard(bigRed);

        var def = BurningWishFactory.BuildDefinition(_alice);
        var effects = def.EffectFactory(Empty(new[] { _alice, _bob }));
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bigRed,
            "sorcery predicate picks the only sorcery candidate");
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);
        _alice.Wishboard.GetCards().Should().Contain(bolt);
        _alice.Wishboard.GetCards().Should().NotContain(bigRed);
    }

    // -----------------------------------------------------------------------
    // Cunning Wish — {2}{U} instant; instant-card tutor.
    // -----------------------------------------------------------------------

    [Fact]
    public void CunningWish_HasInstantShape_At2U()
    {
        var card = CunningWishFactory.Create(_alice);

        card.Name.Should().Be("Cunning Wish");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(3);
        Majik.Core.Cards.CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void CunningWish_NamedCardFactory_Dispatches()
    {
        var dispatched = NamedCardFactory.Create("Cunning Wish", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void CunningWish_BuildDefinition_Shape()
    {
        var def = CunningWishFactory.BuildDefinition(_alice);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void CunningWish_ResolvesInstantPick_FromWishboard()
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        _alice.Wishboard.AddCard(bears);
        _alice.Wishboard.AddCard(bolt);

        var def = CunningWishFactory.BuildDefinition(_alice);
        foreach (var e in def.EffectFactory(Empty(new[] { _alice, _bob }))) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bolt,
            "instant predicate picks the only instant candidate");
        _alice.Zones.Hand.GetCards().Should().NotContain(bears);
        _alice.Wishboard.GetCards().Should().Contain(bears);
    }

    // -----------------------------------------------------------------------
    // Glittering Wish — {G}{W} sorcery; multicolored-card tutor.
    // -----------------------------------------------------------------------

    [Fact]
    public void GlitteringWish_HasSorceryShape_AtGW()
    {
        var card = GlitteringWishFactory.Create(_alice);

        card.Name.Should().Be("Glittering Wish");
        card.ManaCost.Should().Be("{G}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(2);
        var colors = Majik.Core.Cards.CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().Contain(ManaColor.White);
    }

    [Fact]
    public void GlitteringWish_NamedCardFactory_Dispatches()
    {
        var dispatched = NamedCardFactory.Create("Glittering Wish", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void GlitteringWish_BuildDefinition_Shape()
    {
        var def = GlitteringWishFactory.BuildDefinition(_alice);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void GlitteringWish_ResolvesMulticoloredPick_FromWishboard()
    {
        // Wishboard mixes mono-colour, mono-colour, and a multicolored
        // sorcery. Multicolored predicate (CR 105.1c — ≥2 distinct colours)
        // gates to the gold card.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var doom = new Instant("Doom Blade", "{1}{B}") { Owner = _alice };
        var goldSorc = new Sorcery("Fire / Ice", "{1}{U}{R}") { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);
        _alice.Wishboard.AddCard(doom);
        _alice.Wishboard.AddCard(goldSorc);

        var def = GlitteringWishFactory.BuildDefinition(_alice);
        foreach (var e in def.EffectFactory(Empty(new[] { _alice, _bob }))) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(goldSorc,
            "multicolored predicate picks the only ≥2-colour candidate");
        _alice.Wishboard.GetCards().Should().Contain(bolt);
        _alice.Wishboard.GetCards().Should().Contain(doom);
    }

    // -----------------------------------------------------------------------
    // Living Wish — {1}{G} sorcery; creature-or-land tutor.
    // -----------------------------------------------------------------------

    [Fact]
    public void LivingWish_HasSorceryShape_At1G()
    {
        var card = LivingWishFactory.Create(_alice);

        card.Name.Should().Be("Living Wish");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(2);
        Majik.Core.Cards.CardColors.GetColors(card).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void LivingWish_NamedCardFactory_Dispatches()
    {
        var dispatched = NamedCardFactory.Create("Living Wish", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void LivingWish_BuildDefinition_Shape()
    {
        var def = LivingWishFactory.BuildDefinition(_alice);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void LivingWish_ResolvesCreaturePick_FromWishboard()
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);
        _alice.Wishboard.AddCard(bears);

        var def = LivingWishFactory.BuildDefinition(_alice);
        foreach (var e in def.EffectFactory(Empty(new[] { _alice, _bob }))) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bears,
            "creature-or-land predicate picks the creature");
        _alice.Wishboard.GetCards().Should().Contain(bolt);
    }

    // -----------------------------------------------------------------------
    // Death Wish — {1}{B}{B} sorcery; any-card tutor + lose half life.
    // -----------------------------------------------------------------------

    [Fact]
    public void DeathWish_HasSorceryShape_At1BB()
    {
        var card = DeathWishFactory.Create(_alice);

        card.Name.Should().Be("Death Wish");
        card.ManaCost.Should().Be("{1}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(3);
        Majik.Core.Cards.CardColors.GetColors(card).Should().Contain(ManaColor.Black);
    }

    [Fact]
    public void DeathWish_NamedCardFactory_Dispatches()
    {
        var dispatched = NamedCardFactory.Create("Death Wish", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void DeathWish_BuildDefinition_Shape_TwoEffects()
    {
        var def = DeathWishFactory.BuildDefinition(_alice);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty();
        // Tutor + half-life-loss = 2 IEffect entries.
        def.EffectFactory(Empty(new[] { _alice, _bob })).Should().HaveCount(2);
    }

    [Fact]
    public void DeathWish_ResolvesAnyPick_AndLosesHalfLife_RoundedUp()
    {
        // Alice at 21 → ceil(21/2) = 11 life lost; ends at 10.
        _alice.GainLife(1); // 20 → 21 so the rounding-up branch is exercised.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);

        var def = DeathWishFactory.BuildDefinition(_alice);
        foreach (var e in def.EffectFactory(Empty(new[] { _alice, _bob }))) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bolt,
            "any-card predicate picks the only wishboard candidate");
        _alice.LifeTotal.Should().Be(10,
            "21 → lose ceil(21/2) = 11 → 10 (CR 119.3 rounding-up)");
    }

    [Fact]
    public void DeathWish_EmptyWishboard_StillLosesHalfLife()
    {
        // Tutor no-op (no candidates) does NOT prevent the unconditional
        // life-loss rider — same posture v1 documents.
        var def = DeathWishFactory.BuildDefinition(_alice);
        foreach (var e in def.EffectFactory(Empty(new[] { _alice, _bob }))) e.Execute();

        _alice.LifeTotal.Should().Be(10,
            "20 → lose ceil(20/2) = 10 → 10");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
