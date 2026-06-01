using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ZealousPersecutionFactory"/>
/// (Alara Reborn / Modern reprints, {W}{B}).
///
/// Instant. Oracle text:
///   "Until end of turn, creatures you control get +1/+1 and creatures your
///    opponents control get -1/-1."
///
/// Covers:
///   - Identity ({W}{B} Instant, white+black, owner/controller, dispatch).
///   - <see cref="ZealousPersecutionFactory.BuildResolveEffect"/>: the
///     controller's creatures get +1/+1; every opponent's creatures get
///     -1/-1; both riders expire at end of turn (CR 514.2). Snapshot at
///     resolution (CR 608.2).
///   - No-creature board is a clean no-op.
/// </summary>
public class ZealousPersecutionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity()
    {
        var c = ZealousPersecutionFactory.Create(_alice);

        c.Name.Should().Be("Zealous Persecution");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.ManaCost.Should().Be("{W}{B}");
        CardColors.GetColors(c).Should().Contain(ManaColor.White);
        CardColors.GetColors(c).Should().Contain(ManaColor.Black);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Zealous Persecution", _alice);

        c.Should().BeOfType<Instant>();
        c.Name.Should().Be("Zealous Persecution");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.ManaCost.Should().Be("{W}{B}");
    }

    // -----------------------------------------------------------------------
    // Resolve — symmetric team buff / opponent debuff
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_PumpsYourCreatures_AndShrinksOpponents()
    {
        var effects = new ContinuousEffectsService();

        var myBear = NewCreature(_alice, "Grizzly Bears", "{1}{G}", 2, 2, effects);
        var myGiant = NewCreature(_alice, "Hill Giant", "{3}{R}", 3, 3, effects);
        var foeBear = NewCreature(_bob, "Runeclaw Bear", "{1}{G}", 2, 2, effects);
        var foeWurm = NewCreature(_bob, "Craw Wurm", "{4}{G}{G}", 6, 4, effects);

        var resolve = ZealousPersecutionFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob });
        foreach (var e in resolve) e.Execute();

        // Your creatures: +1/+1.
        myBear.GetPower().Should().Be(3);
        myBear.GetToughness().Should().Be(3);
        myGiant.GetPower().Should().Be(4);
        myGiant.GetToughness().Should().Be(4);

        // Opponent creatures: -1/-1.
        foeBear.GetPower().Should().Be(1);
        foeBear.GetToughness().Should().Be(1);
        foeWurm.GetPower().Should().Be(5);
        foeWurm.GetToughness().Should().Be(3);
    }

    [Fact]
    public void Resolve_MinusOneMinusOne_CanBeLethalToOneToughnessCreatures()
    {
        var effects = new ContinuousEffectsService();

        // 1/1 opponent goblin: -1/-1 drops it to 0 toughness → SBA-lethal.
        var goblin = NewCreature(_bob, "Goblin", "{R}", 1, 1, effects);

        var resolve = ZealousPersecutionFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob });
        foreach (var e in resolve) e.Execute();

        goblin.GetToughness().Should().Be(0, "-1/-1 on a 1/1 leaves 0 toughness");
    }

    [Fact]
    public void Resolve_RidersExpireAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();

        var myBear = NewCreature(_alice, "Grizzly Bears", "{1}{G}", 2, 2, effects);
        var foeBear = NewCreature(_bob, "Runeclaw Bear", "{1}{G}", 2, 2, effects);

        var resolve = ZealousPersecutionFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob });
        foreach (var e in resolve) e.Execute();

        myBear.GetPower().Should().Be(3);
        foeBear.GetPower().Should().Be(1);

        // CR 514.2 — cleanup step expiry clears both riders.
        effects.ExpireEndOfTurn();

        myBear.GetPower().Should().Be(2, "pump expires at end of turn");
        myBear.GetToughness().Should().Be(2);
        foeBear.GetPower().Should().Be(2, "debuff expires at end of turn");
        foeBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void Resolve_NoCreatures_IsCleanNoOp()
    {
        var resolve = ZealousPersecutionFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob });
        var act = () => { foreach (var e in resolve) e.Execute(); };

        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreature(
        Player owner, string name, string manaCost, int power, int toughness,
        ContinuousEffectsService effects)
    {
        var c = new Creature(name, manaCost, power, toughness)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
