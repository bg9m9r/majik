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
/// Tests for Stab (Tarkir: Dragonstorm, {B}, Instant).
/// "Target creature gets -2/-2 until end of turn."
///
/// Covers only Stab's UNIQUE behaviour (the -2/-2 EOT pump) plus a single
/// Identity assert for the exact mana cost / type. NamedCardFactory dispatch +
/// well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so they are not re-tested here.
/// </summary>
[Trait("Color", "B")]
public class StabTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Stab_Identity_InstantAtB()
    {
        var card = StabFactory.Create(_alice);

        card.Name.Should().Be("Stab");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildDefinition_SingleTargetCreatureRequest()
    {
        var def = StabFactory.BuildDefinition();

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    [Fact]
    public void Stab_AppliesMinus2Minus2_UntilEndOfTurn()
    {
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
    public void Stab_TargetNotOnBattlefield_NoOp()
    {
        // Creature already left the battlefield before Stab resolves
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

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
    }

    [Fact]
    public void Stab_NoActiveEffectsService_DoesNotThrow()
    {
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

    private static void Resolve(Creature target)
    {
        var def = StabFactory.BuildDefinition();
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
