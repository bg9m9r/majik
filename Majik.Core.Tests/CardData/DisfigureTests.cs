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
/// Tests for Disfigure (Worldwake, {B}, Instant).
/// "Target creature gets -2/-2 until end of turn."
///
/// Covers:
///   - Card identity (Instant, {B}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve registers a -2/-2 PumpUntilEndOfTurnEffect (CR 514.2).
///   - Target not on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
public class DisfigureTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Disfigure_Identity_InstantAtB()
    {
        var card = DisfigureFactory.Create(_alice);

        card.Name.Should().Be("Disfigure");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Disfigure()
    {
        var card = NamedCardFactory.Create("Disfigure", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Disfigure");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildDefinition_SingleTargetCreatureRequest()
    {
        var def = DisfigureFactory.BuildDefinition();

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    [Fact]
    public void Disfigure_AppliesMinus2Minus2_UntilEndOfTurn()
    {
        // Wire the target with a ContinuousEffectsService so the
        // PumpUntilEndOfTurnEffect can register (mirrors Dismember /
        // Meathook Massacre test pattern).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        Resolve(bear);

        // CR 613 Layer 7c — -2/-2 takes Bears to 0/0.
        bear.Power.Should().Be(0, "Grizzly Bears 2/2 with -2/-2 → 0/0");
        bear.Toughness.Should().Be(0);
    }

    [Fact]
    public void Disfigure_TargetNotOnBattlefield_NoOp()
    {
        // Creature already left the battlefield before Disfigure resolves
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
    public void Disfigure_NoActiveEffectsService_DoesNotThrow()
    {
        // Shape-only path: target on battlefield but no
        // ContinuousEffectsService wired. The factory must silently no-op
        // (Dismember-style guard) so dispatch / build tests don't need to
        // stand up a full effects service.
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
        var def = DisfigureFactory.BuildDefinition();
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
