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
/// Tests for Last Gasp (Ravnica: City of Guilds, {1}{B}, Instant).
/// "Target creature gets -3/-3 until end of turn."
///
/// Covers:
///   - Card identity (Instant, {1}{B}, owner/controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition single-target creature request.
///   - Resolve registers a -3/-3 PumpUntilEndOfTurnEffect (CR 514.2).
///   - Target not on battlefield at resolution → no-op (CR 608.2b).
///   - No ActiveEffectsService wired → no-op, no throw.
/// </summary>
public class LastGaspTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void LastGasp_Identity_InstantAt1B()
    {
        var card = LastGaspFactory.Create(_alice);

        card.Name.Should().Be("Last Gasp");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LastGasp()
    {
        var card = NamedCardFactory.Create("Last Gasp", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Last Gasp");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildDefinition_SingleTargetCreatureRequest()
    {
        var def = LastGaspFactory.BuildDefinition();

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    [Fact]
    public void LastGasp_AppliesMinus3Minus3_UntilEndOfTurn_3x3Creature()
    {
        // 3/3 creature → 0/0 after -3/-3 (CR 613 Layer 7c, CR 514.2).
        var creature = new Creature("Trained Armodon", "{1}{G}{G}", 3, 3)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        creature.SetOwner(_bob);
        creature.SetController(_bob);
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        Resolve(creature);

        creature.Power.Should().Be(0, "Trained Armodon 3/3 with -3/-3 → 0/0");
        creature.Toughness.Should().Be(0);
    }

    [Fact]
    public void LastGasp_AppliesMinus3Minus3_UntilEndOfTurn_4x4Creature()
    {
        // 4/4 creature → 1/1 after -3/-3 (CR 613 Layer 7c, CR 514.2).
        var creature = new Creature("Serra Angel", "{3}{W}{W}", 4, 4)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        creature.SetOwner(_bob);
        creature.SetController(_bob);
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        Resolve(creature);

        creature.Power.Should().Be(1, "Serra Angel 4/4 with -3/-3 → 1/1");
        creature.Toughness.Should().Be(1);
    }

    [Fact]
    public void LastGasp_TargetNotOnBattlefield_NoOp()
    {
        // Creature already left the battlefield before Last Gasp resolves
        // (CR 608.2b — illegal target → effect does nothing).
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        creature.SetOwner(_bob);
        creature.SetController(_bob);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        creature.Power.Should().Be(2);
        creature.Toughness.Should().Be(2);
    }

    [Fact]
    public void LastGasp_NoActiveEffectsService_DoesNotThrow()
    {
        // Shape-only path: target on battlefield but no
        // ContinuousEffectsService wired. Must silently no-op.
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_bob);
        creature.SetController(_bob);
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var act = () => Resolve(creature);
        act.Should().NotThrow();

        creature.Power.Should().Be(2);
        creature.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target)
    {
        var def = LastGaspFactory.BuildDefinition();
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
