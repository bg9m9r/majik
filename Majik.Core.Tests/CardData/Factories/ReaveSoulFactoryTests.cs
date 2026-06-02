using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ReaveSoulFactory"/> (Magic Origins, {1}{B}).
/// "Destroy target creature with power 3 or less." (Sorcery.)
/// </summary>
[Trait("Color", "B")]
public class ReaveSoulFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    private Creature CreatureOnBattlefield(Player owner, int power, int tough)
    {
        var c = new Creature("Test Creature", "{G}", power, tough);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_SorceryAt1B()
    {
        var card = ReaveSoulFactory.Create(_alice);

        card.Name.Should().Be("Reave Soul");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Identity_IsBlackCard()
    {
        var card = ReaveSoulFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Spell definition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_HasSingleTargetCreatureRequest()
    {
        var def = ReaveSoulFactory.BuildDefinition(o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
    }

    // -----------------------------------------------------------------------
    // Effect — destroys creatures with power ≤ 3
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysCreatureWithPower2()
    {
        var target = CreatureOnBattlefield(_bob, power: 2, tough: 2);
        var def     = ReaveSoulFactory.BuildDefinition(o => o);
        var chosen  = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        target.Zone.Should().Be(ZoneType.Graveyard,
            "Reave Soul destroys creatures with power 2 (≤ 3)");
    }

    [Fact]
    public void Resolve_DestroysCreatureWithPower3()
    {
        var target = CreatureOnBattlefield(_bob, power: 3, tough: 3);
        var def     = ReaveSoulFactory.BuildDefinition(o => o);
        var chosen  = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        target.Zone.Should().Be(ZoneType.Graveyard,
            "Reave Soul destroys creatures with power 3 (exactly at the threshold)");
    }

    // -----------------------------------------------------------------------
    // Effect — no-op on power > 3 (CR 608.2b illegal-target at resolution)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_NoOp_OnCreatureWithPower4()
    {
        var target = CreatureOnBattlefield(_bob, power: 4, tough: 4);
        var def     = ReaveSoulFactory.BuildDefinition(o => o);
        var chosen  = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        target.Zone.Should().Be(ZoneType.Battlefield,
            "power-4 creature is not a legal target; effect is a no-op (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Effect — no-op on illegal target (not a Creature / off Battlefield)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_NoOp_OnNonCreatureTarget()
    {
        var def    = ReaveSoulFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana: ManaPayment.Empty);

        var act = () => { foreach (var e in def.EffectFactory(chosen)) e.Execute(); };

        act.Should().NotThrow("non-creature targets are silently ignored (CR 608.2b)");
        _bob.LifeTotal.Should().Be(20);
    }
}
