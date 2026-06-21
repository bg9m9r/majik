using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end test for Conversion — Enchantment {2}{W}{W}, "All Mountains
/// are Plains." Layer 4 type-changing effect via the shared
/// <see cref="RetypeLandsStaticEffect"/> binder (CR 305.6 / 613.1d).
///
/// Note: Conversion's scope is unusual — it ignores basic/nonbasic and
/// keys on whether the land has the Mountain subtype. The original card's
/// upkeep "sacrifice unless you pay {W}{W}" clause is deferred.
/// </summary>
public class ConversionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public ConversionTests()
    {
        _zones = new ZoneService(_bus);
    }

    [Fact]
    public void Conversion_IsEnchantment_AtCost2WW()
    {
        var conv = ConversionFactory.Create(_alice);

        conv.Name.Should().Be("Conversion");
        conv.HasType(CardType.Enchantment).Should().BeTrue();
        conv.ManaCost.Should().Be("{2}{W}{W}");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Conversion()
    {
        var conv = NamedCardFactory.Create("Conversion", _alice);

        conv.Should().BeOfType<Enchantment>();
        conv.Name.Should().Be("Conversion");
        conv.ManaCost.Should().Be("{2}{W}{W}");
    }

    /// <summary>
    /// Headline lifecycle test: a Land whose subtype set includes Mountain
    /// (e.g. Stomping Ground — Mountain Forest dual) should, while
    /// Conversion is on the battlefield, lose its printed mana abilities
    /// and tap for {W} per CR 305.6 (its land-subtype slot has been
    /// rewritten to {Plains}).
    /// </summary>
    [Fact]
    public void MountainSubtypeLand_UnderConversion_TapsForWhite()
    {
        var stompingGround = new Land(
            "Stomping Ground",
            supertypes: null,
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest });
        stompingGround.SetOwner(_alice);
        stompingGround.SetController(_alice);
        stompingGround.AddAbility(new ManaAbility(stompingGround, _alice, ManaCost.Parse("R")));
        stompingGround.AddAbility(new ManaAbility(stompingGround, _alice, ManaCost.Parse("G")));
        _zones.MoveCard(stompingGround, ZoneType.Library, ZoneType.Battlefield, _alice);

        var conv = ConversionFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(conv, ZoneType.Library, ZoneType.Battlefield, _alice);

        var abilities = EffectiveManaAbilities.For(stompingGround, _effects, _alice);

        abilities.Should().HaveCount(1, "CR 305.6 strips printed abilities and adds {W}");
        abilities[0].ManaGenerated.White.Should().Be(1);
        abilities[0].ManaGenerated.Red.Should().Be(0);
        abilities[0].ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Upkeep "sacrifice unless you pay {W}{W}" (CR 603.1 / CR 117.1)
    // -----------------------------------------------------------------------

    private static TriggeredAbility GetUpkeepTrigger(Enchantment c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);

    [Fact]
    public void Conversion_HasUpkeepTrigger_FiringOnControllersUpkeep()
    {
        var conv = ConversionFactory.Create(_alice);
        conv.SetZone(ZoneType.Battlefield);

        var upkeep = GetUpkeepTrigger(conv);
        upkeep.IsTriggered(new StepStartedEvent(StepStateType.Upkeep, _alice))
            .Should().BeTrue("Conversion taxes at its controller's upkeep (CR 603.1).");
    }

    [Fact]
    public void Conversion_Upkeep_Sacrifices_WhenControllerCannotPay()
    {
        var conv = ConversionFactory.Create(_alice);
        conv.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(conv);

        // Empty pool — Alice cannot pay {W}{W}; Conversion is sacrificed.
        var upkeep = GetUpkeepTrigger(conv);
        foreach (var e in upkeep.Effects) e.Execute();

        conv.Zone.Should().Be(ZoneType.Graveyard,
            "unpaid {W}{W} sacrifices Conversion (Battlefield -> Graveyard).");
        _alice.Zones.Graveyard.GetCards().Should().Contain(conv);
    }

    [Fact]
    public void Conversion_Upkeep_Keeps_WhenControllerPaysWW()
    {
        var conv = ConversionFactory.Create(_alice);
        conv.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(conv);

        _alice.AddManaToPool(ManaCost.Parse("{W}{W}"));

        var upkeep = GetUpkeepTrigger(conv);
        foreach (var e in upkeep.Effects) e.Execute();

        conv.Zone.Should().Be(ZoneType.Battlefield,
            "paying {W}{W} keeps Conversion on the battlefield.");
    }

    [Fact]
    public void Conversion_Upkeep_IsRecurring_FiresEveryUpkeep()
    {
        var conv = ConversionFactory.Create(_alice);
        conv.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(conv);

        var upkeep = GetUpkeepTrigger(conv);

        // Unlike Echo's one-shot gate, Conversion's tax recurs: pay one turn,
        // it must still fire (and be payable) the next.
        _alice.AddManaToPool(ManaCost.Parse("{W}{W}"));
        foreach (var e in upkeep.Effects) e.Execute();
        conv.Zone.Should().Be(ZoneType.Battlefield);

        upkeep.CanBePutOnStack().Should().BeTrue(
            "Conversion's upkeep tax is recurring (no one-shot lift, unlike Echo).");

        _alice.AddManaToPool(ManaCost.Parse("{W}{W}"));
        foreach (var e in upkeep.Effects) e.Execute();
        conv.Zone.Should().Be(ZoneType.Battlefield,
            "paying again the next upkeep keeps it.");
    }
}
