using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// STAGE 1 (re-sourceable abilities) — groundwork for Agatha's Soul Cauldron
/// granting an imprinted creature's activated abilities re-homed to a bearer.
///
/// <para>
/// Two additive seams are exercised here: (1) <see cref="ResolutionContext.Source"/>
/// is populated from the resolving ability's own source, so an effect can read
/// "its source" generically (CR 113.7); and (2)
/// <see cref="ActivatedAbility.RebindTo"/> now re-homes source-capturing COSTS
/// ({T} / sacrifice) onto the new source, so a re-sourced ability taps /
/// sacrifices the NEW permanent rather than the original (CR 707.2). Effects
/// are NOT migrated this stage.
/// </para>
/// </summary>
public class ActivatedAbilityRebindSourceTests
{
    private readonly Player _alice = new("Alice", 20);

    /// <summary>Test effect that records the <see cref="ResolutionContext.Source"/>
    /// it was resolved with, proving the ability threads its own source.</summary>
    private sealed class SourceCapturingEffect : IEffect
    {
        public Permanent? SeenSource { get; private set; }
        public bool Ran { get; private set; }
        public string Description => "capture source";

        public ValueTask ExecuteAsync(ResolutionContext ctx)
        {
            Ran = true;
            SeenSource = ctx.Source;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task ResolveAsync_ExposesAbilitySource_InContext()
    {
        // Arrange — an activated ability whose source is permanent A.
        var source = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = _alice };
        source.SetZone(ZoneType.Battlefield);
        var effect = new SourceCapturingEffect();
        var ability = new ActivatedAbility(
            source: source,
            controller: _alice,
            effects: new[] { effect });

        // Act
        await ability.ResolveAsync(agent: null, game: null);

        // Assert — the effect saw the ability's own source on the context.
        effect.Ran.Should().BeTrue();
        effect.SeenSource.Should().BeSameAs(source);
    }

    [Fact]
    public void RebindTo_TapCost_PaysWithNewSource_NotOriginal()
    {
        // Arrange — ability on A with a {T} cost capturing A.
        var a = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = _alice };
        a.SetZone(ZoneType.Battlefield);
        a.ClearSummoningSickness();

        var b = new Creature("Llanowar Elves", "G", 1, 1) { Controller = _alice };
        b.SetZone(ZoneType.Battlefield);
        b.ClearSummoningSickness();

        var ability = new ActivatedAbility(
            source: a,
            controller: _alice,
            costs: new[] { AdditionalCost.Tap(a) });

        // Act — re-source the ability onto B, then pay the rebound costs.
        var rebound = ability.RebindTo(b, _alice);
        foreach (var cost in rebound.Costs)
        {
            cost.Pay(_alice);
        }

        // Assert — the rebound {T} cost taps B (the new source), not A.
        rebound.Source.Should().BeSameAs(b);
        b.IsTapped.Should().BeTrue();
        a.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void RebindTo_SacrificeCost_PaysWithNewSource_NotOriginal()
    {
        // Arrange — ability on A with a sacrifice cost capturing A.
        var a = new Creature("Grizzly Bears", "1G", 2, 2);
        a.SetOwner(_alice);
        a.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);

        var b = new Creature("Llanowar Elves", "G", 1, 1);
        b.SetOwner(_alice);
        b.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(b);
        b.SetZone(ZoneType.Battlefield);

        var ability = new ActivatedAbility(
            source: a,
            controller: _alice,
            costs: new[] { AdditionalCost.Sacrifice(a) });

        // Act
        var rebound = ability.RebindTo(b, _alice);
        foreach (var cost in rebound.Costs)
        {
            cost.Pay(_alice);
        }

        // Assert — the rebound sacrifice cost sacrifices B, not A.
        b.Zone.Should().Be(ZoneType.Graveyard);
        a.Zone.Should().Be(ZoneType.Battlefield);
    }
}
