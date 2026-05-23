using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Veil of Summer (Core Set 2020, {G}).
///
/// Oracle: "Draw a card if an opponent has cast a blue or black spell this
/// turn. Spells you control can't be countered this turn, and you and
/// permanents you control gain hexproof from blue and from black until
/// end of turn."
///
/// Coverage:
///   * Card shape + dispatch by name.
///   * Draws on opponent blue cast this turn (Counterspell-like).
///   * Draws on opponent black cast this turn.
///   * No draw when opponent has cast nothing of UB this turn.
///   * Uncounterable flag registered on the caster post-resolve.
///   * Hexproof-from-Blue / -Black granted (structural) to controller's
///     creatures via ContinuousEffectsService.
/// </summary>
public class VeilOfSummerTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public VeilOfSummerTests()
    {
        // Process-level registry — clean slate per test.
        CastingRestrictions.Clear();
    }

    public void Dispose()
    {
        CastingRestrictions.Clear();
    }

    [Fact]
    public void Create_HasInstantShape_Green()
    {
        var veil = VeilOfSummerFactory.Create(_alice);

        veil.Name.Should().Be("Veil of Summer");
        veil.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(veil).Should().Contain(ManaColor.Green);
        veil.ManaCostValue.TotalValue.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsVeilOfSummerShape()
    {
        var dispatched = NamedCardFactory.Create("Veil of Summer", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Veil of Summer");
    }

    [Fact]
    public void Resolve_OpponentCastBlue_DrawsOne()
    {
        // Seed Alice's library so a draw can land.
        var top = new Instant("Top", "{R}") { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(top);

        var ts = new TurnState();
        // Bob cast Counterspell {U}{U} this turn — TurnDriver would normally
        // record this on SpellCastEvent; here we drive it directly.
        ts.RecordSpellCast(_bob, new HashSet<ManaColor> { ManaColor.Blue });

        VeilOfSummerFactory.Resolve(_alice, ts, continuousEffects: null);

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_OpponentCastBlack_DrawsOne()
    {
        var top = new Instant("Top", "{R}") { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(top);

        var ts = new TurnState();
        ts.RecordSpellCast(_bob, new HashSet<ManaColor> { ManaColor.Black });

        VeilOfSummerFactory.Resolve(_alice, ts, continuousEffects: null);

        _alice.Zones.Hand.GetCards().Should().Contain(top);
    }

    [Fact]
    public void Resolve_NoOpponentUBCast_NoDraw()
    {
        var top = new Instant("Top", "{R}") { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(top);

        var ts = new TurnState();
        // Bob only cast a red spell — neither blue nor black.
        ts.RecordSpellCast(_bob, new HashSet<ManaColor> { ManaColor.Red });

        VeilOfSummerFactory.Resolve(_alice, ts, continuousEffects: null);

        _alice.Zones.Hand.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Resolve_AlicesOwnBlueCast_DoesNotTrigger_Draw()
    {
        // Self-casts don't count — "an opponent has cast" filters out the
        // caster's own spells.
        var top = new Instant("Top", "{R}") { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(top);

        var ts = new TurnState();
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Blue });

        VeilOfSummerFactory.Resolve(_alice, ts, continuousEffects: null);

        _alice.Zones.Hand.GetCards().Should().NotContain(top);
    }

    [Fact]
    public void Resolve_AlwaysRegistersUncounterableForController()
    {
        CastingRestrictions.SpellsCannotBeCountered(_alice).Should().BeFalse();

        VeilOfSummerFactory.Resolve(_alice, turnState: null, continuousEffects: null);

        CastingRestrictions.SpellsCannotBeCountered(_alice).Should().BeTrue();
        CastingRestrictions.SpellsCannotBeCountered(_bob).Should().BeFalse(
            because: "the rider is keyed to the caster, not opponents");
    }

    [Fact]
    public void Resolve_GrantsHexproofFromBlueAndBlack_ToControllerCreatures()
    {
        var continuous = new ContinuousEffectsService();
        var bear = new Creature("Bear", "{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        VeilOfSummerFactory.Resolve(_alice, turnState: null, continuous);

        var chars = continuous.Compute(bear);
        chars.Keywords.Should().Contain("Hexproof from Blue");
        chars.Keywords.Should().Contain("Hexproof from Black");
    }

    [Fact]
    public void Resolve_DoesNotGrantHexproof_ToOpponentCreatures()
    {
        var continuous = new ContinuousEffectsService();
        var enemy = new Creature("Enemy Bear", "{G}", 2, 2) { Owner = _bob, Controller = _bob };
        enemy.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enemy);

        VeilOfSummerFactory.Resolve(_alice, turnState: null, continuous);

        var chars = continuous.Compute(enemy);
        chars.Keywords.Should().NotContain("Hexproof from Blue");
        chars.Keywords.Should().NotContain("Hexproof from Black");
    }

    [Fact]
    public void BuildDefinition_ShapeHasNoTargets_NoModes()
    {
        var def = VeilOfSummerFactory.BuildDefinition(_alice, turnState: null, continuousEffects: null);

        def.TargetRequests.Should().BeEmpty();
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
    }
}
