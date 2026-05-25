using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Defile (Modern Horizons 2, {B}).
/// Exercises:
///   * Card shape + dispatch.
///   * Swamp-counting on the controller's battlefield.
///   * N damage marked on target creature + -N/-N until EOT applied.
///   * No Swamps → clean no-op.
///   * Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class DefileFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Create_HasInstantShape_Black()
    {
        var defile = DefileFactory.Create(_alice);
        defile.Name.Should().Be("Defile");
        defile.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(defile).Should().Contain(ManaColor.Black);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsDefileShape()
    {
        var dispatched = NamedCardFactory.Create("Defile", _alice);
        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Defile");
    }

    [Fact]
    public void CountSwamps_CountsOnlyControllerSwamps()
    {
        // Alice: 2 Swamps. Bob: 1 Swamp (irrelevant — count is controller-scoped).
        for (var i = 0; i < 2; i++)
        {
            var s = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp })
                { Owner = _alice, Controller = _alice };
            s.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(s);
        }
        var bobSwamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp })
            { Owner = _bob, Controller = _bob };
        bobSwamp.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobSwamp);

        // Alice also controls a non-Swamp land — must not count.
        var aliceForest = new Land("Forest", subtypes: new[] { CardSubtype.Forest })
            { Owner = _alice, Controller = _alice };
        aliceForest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceForest);

        DefileFactory.CountSwamps(_alice).Should().Be(2);
        DefileFactory.CountSwamps(_bob).Should().Be(1);
    }

    [Fact]
    public void CountSwamps_NullController_IsZero()
    {
        DefileFactory.CountSwamps(null!).Should().Be(0);
    }

    [Fact]
    public void Resolve_NoSwamps_IsNoOp()
    {
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.ActiveEffects = new ContinuousEffectsService();

        var def = DefileFactory.BuildSpellDefinition(_alice, o => o);
        var picks = new ChosenSpellParams(
            null, null,
            new[] { (IReadOnlyList<object>)new object[] { bobBear } },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(picks)) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Battlefield);
        bobBear.Damage.Should().Be(0,
            because: "0 Swamps → 0 damage, no pump");
    }

    [Fact]
    public void Resolve_WithSwamps_DealsNDamageAndAppliesMinusNMinusN()
    {
        // Alice controls 3 Swamps.
        for (var i = 0; i < 3; i++)
        {
            var s = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp })
                { Owner = _alice, Controller = _alice };
            s.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(s);
        }

        // 4/4 Bob creature — 3 damage + -3/-3 pump = lethal (toughness goes
        // to 1, marked 3 damage ≥ effective toughness 1 → CR 704.5g).
        var bobBeast = new Creature("Tarmogoyf", "{1}{G}", 4, 4)
            { Owner = _bob, Controller = _bob };
        bobBeast.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBeast);
        var ce = new ContinuousEffectsService();
        bobBeast.ActiveEffects = ce;

        var def = DefileFactory.BuildSpellDefinition(_alice, o => o);
        var picks = new ChosenSpellParams(
            null, null,
            new[] { (IReadOnlyList<object>)new object[] { bobBeast } },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(picks)) e.Execute();

        bobBeast.Damage.Should().Be(3,
            because: "Defile deals N damage where N = Swamps Alice controls (3)");

        // Confirm -3/-3 pump applied via Compute: 4/4 base → 1/1 effective.
        var chars = ce.Compute(bobBeast);
        chars.Power.Should().Be(1);
        chars.Toughness.Should().Be(1);
    }

    [Fact]
    public void Resolve_TargetOffBattlefield_IsNoOp()
    {
        for (var i = 0; i < 2; i++)
        {
            var s = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp })
                { Owner = _alice, Controller = _alice };
            s.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(s);
        }

        var deadBear = new Creature("Bear", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        deadBear.SetZone(ZoneType.Graveyard); // moved off-battlefield before resolution

        var def = DefileFactory.BuildSpellDefinition(_alice, o => o);
        var picks = new ChosenSpellParams(
            null, null,
            new[] { (IReadOnlyList<object>)new object[] { deadBear } },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(picks)) e.Execute();

        deadBear.Damage.Should().Be(0,
            because: "CR 608.2b — illegal target at resolution → no damage marked");
    }
}
