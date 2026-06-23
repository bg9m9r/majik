using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="EmeriaTheSkyRuinFactory"/> — Land (Worldwake) with an
/// upkeep intervening-if trigger (CR 603.4) that reanimates a target creature
/// card from the controller's graveyard when the controller controls seven or
/// more Plains.
///
/// Covers only the card's UNIQUE behaviour (the upkeep reanimation trigger,
/// its 7+ Plains intervening-if, and the {T}: Add {W} mana ability). Card
/// dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so this suite does not re-test those.
/// </summary>
[Trait("Color", "W")]
public class EmeriaTheSkyRuinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // {T}: Add {W} + ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Emeria_IsLand_WithWhiteManaAbility_AndUpkeepTrigger()
    {
        var land = EmeriaTheSkyRuinFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Emeria, the Sky Ruin is not a basic land");
        land.Name.Should().Be("Emeria, the Sky Ruin");

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "{T}: Add {W}");
        manaAbilities[0].ManaGenerated.White.Should().Be(1);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1, "the upkeep reanimation trigger");
    }

    [Fact]
    public void Emeria_UpkeepTrigger_HasCreatureCardTargetRequest()
    {
        var land = EmeriaTheSkyRuinFactory.Create(_alice);

        var upkeep = land.Abilities.OfType<TriggeredAbility>().Single();
        upkeep.TargetRequests.Should().HaveCount(1);

        var req = upkeep.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature card");
        req.Description.Should().Contain("graveyard");
        upkeep.ActiveZones.Should().Contain(ZoneType.Battlefield);
        upkeep.OptionalPrompt.Should().NotBeNull("\"you may\" is a first-class optional-trigger prompt (CR 603.5)");
    }

    // -----------------------------------------------------------------------
    // Intervening-if: 7+ Plains
    // -----------------------------------------------------------------------

    [Fact]
    public void Emeria_InterveningIf_TrueWithSevenPlains_FalseWithSix()
    {
        var land = EmeriaTheSkyRuinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var upkeep = land.Abilities.OfType<TriggeredAbility>().Single();

        // 6 Plains — one short of the threshold.
        for (int i = 0; i < 6; i++) AddPlains($"Plains {i}");

        upkeep.CanBePutOnStack().Should().BeFalse(
            "only 6 Plains; the 7+ Plains threshold is not met (Emeria itself has no Plains subtype)");

        // 7th Plains → condition satisfied.
        AddPlains("Plains 6");

        upkeep.CanBePutOnStack().Should().BeTrue(
            "7 Plains satisfies the intervening-if (CR 603.4)");
    }

    // -----------------------------------------------------------------------
    // Resolution: reanimate a creature card from graveyard to battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolution_ReturnsTargetCreatureCard_FromGraveyard_ToBattlefield()
    {
        var (zones, _, triggers) = BuildEngine();
        ZoneServiceRegistry.Set(_alice, zones);
        try
        {
            var land = EmeriaTheSkyRuinFactory.Create(_alice, replacements: null, triggers: triggers);
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);

            // Creature card in the graveyard — the reanimation target.
            var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
            bear.SetOwner(_alice);
            bear.SetController(_alice);
            bear.SetZone(ZoneType.Graveyard);
            _alice.Zones.Graveyard.AddCard(bear);

            var upkeep = land.Abilities.OfType<TriggeredAbility>().Single();
            upkeep.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

            foreach (var effect in upkeep.Effects)
                effect.Execute();

            bear.Zone.Should().Be(ZoneType.Battlefield,
                "the chosen creature card should be reanimated to the controller's battlefield");
            _alice.Zones.Graveyard.ContainsCard(bear).Should().BeFalse();
            _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
            bear.Controller.Should().BeSameAs(_alice);
        }
        finally
        {
            ZoneServiceRegistry.Remove(_alice);
        }
    }

    [Fact]
    public void Resolution_NonCreatureCard_IsNotReturned()
    {
        var land = EmeriaTheSkyRuinFactory.Create(_alice);
        var upkeep = land.Abilities.OfType<TriggeredAbility>().Single();

        // An instant card in the graveyard is NOT a creature card — must no-op.
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        upkeep.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bolt } });

        foreach (var effect in upkeep.Effects)
            effect.Execute();

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "an instant card is not a creature card; CR 608.2b recheck makes the effect a no-op");
        _alice.Zones.Graveyard.ContainsCard(bolt).Should().BeTrue();
    }

    [Fact]
    public void Resolution_NoTargetChosen_NoOps_Cleanly()
    {
        var land = EmeriaTheSkyRuinFactory.Create(_alice);
        var upkeep = land.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in upkeep.Effects)
                effect.Execute();
        };

        act.Should().NotThrow("a trigger with no chosen target should no-op without exception");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void AddPlains(string name)
    {
        var plains = new Land(name, supertypes: null, subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(plains);
        plains.SetZone(ZoneType.Battlefield);
    }

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
