using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Spatial Contortion (Future Sight, {1}{C}, Instant).
/// "Target creature gets +3/-3 until end of turn."
///
/// Covers:
///   - Card identity (Instant, {1}{C}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve registers a +3/-3 PumpUntilEndOfTurnEffect (CR 514.2 / 613 7c).
///   - Target not on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
public class SpatialContortionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SpatialContortion_Identity_InstantAt1C()
    {
        var card = SpatialContortionFactory.Create(_alice);

        card.Name.Should().Be("Spatial Contortion");
        card.ManaCost.Should().Be("{1}{C}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SpatialContortion()
    {
        var card = NamedCardFactory.Create("Spatial Contortion", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Spatial Contortion");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{C}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildDefinition_SingleTargetCreatureRequest()
    {
        var def = SpatialContortionFactory.BuildDefinition();

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    [Fact]
    public void SpatialContortion_AppliesPlus3Minus3_UntilEndOfTurn()
    {
        // Wire the target with a ContinuousEffectsService so the
        // PumpUntilEndOfTurnEffect can register (mirrors Disfigure pattern).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        Resolve(bear);

        // CR 613 Layer 7c — +3/-3 takes a 2/2 to 5/-1.
        bear.Power.Should().Be(5, "Grizzly Bears 2/2 with +3/-3 → 5/-1");
        bear.Toughness.Should().Be(-1);
    }

    [Fact]
    public void SpatialContortion_TargetNotOnBattlefield_NoOp()
    {
        // Creature already left the battlefield before resolution
        // (CR 608.2b — illegal target → effect does nothing).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        Resolve(bear);

        // Stats unchanged — no pump applied.
        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
    }

    [Fact]
    public void SpatialContortion_NoActiveEffectsService_DoesNotThrow()
    {
        // Shape-only path: target on battlefield but no
        // ContinuousEffectsService wired. The factory must silently no-op
        // (Disfigure-style guard).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var act = () => Resolve(bear);
        act.Should().NotThrow();

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target)
    {
        var def = SpatialContortionFactory.BuildDefinition();
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }
}
