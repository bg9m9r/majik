using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for the modal double-faced card
/// Kazandu Mammoth // Kazandu Valley (Zendikar Rising).
///
/// Oracle text (verified against Scryfall):
///   Front — Kazandu Mammoth, Creature — Elephant, {1}{G}{G}, 3/3:
///     "Landfall — Whenever a land you control enters, this creature gets
///      +2/+2 until end of turn."
///   Back — Kazandu Valley, Land:
///     "This land enters tapped."
///     "{T}: Add {G}."
///
/// The front face is the landfall-pump Creature analogue of
/// <see cref="PlatedGeopedeFactory"/> (no First strike). The back face is an
/// unconditional enters-tapped mana land — the simpler sibling of
/// <see cref="SoporificSpringsFactory"/> (plain "enters tapped" rather than
/// "pay 3 life or enters tapped"). MDFC cast-either-face wiring mirrors
/// <see cref="TurntimberSymbiosisFactory"/> (front carries a castable
/// <see cref="MdfcFace.Land"/> back-face descriptor).
/// </summary>
[Trait("Color", "G")]
public class KazanduMammothFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Front face — Kazandu Mammoth identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KazanduMammoth_Identity_CreatureElephant_3_3_Green1GG()
    {
        var mammoth = KazanduMammothFactory.Create(_alice);

        mammoth.Name.Should().Be("Kazandu Mammoth");
        mammoth.HasType(CardType.Creature).Should().BeTrue();
        mammoth.ManaCost.Should().Be("{1}{G}{G}");
        mammoth.ManaCostValue.TotalValue.Should().Be(3);
        CardColors.GetColors(mammoth).Should().Contain(ManaColor.Green);
        mammoth.Power.Should().Be(3);
        mammoth.Toughness.Should().Be(3);
        mammoth.Subtypes.Should().Contain(CardSubtype.Elephant);
        mammoth.Owner.Should().BeSameAs(_alice);
        mammoth.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KazanduMammoth_NamedCardFactory_Dispatch_ProducesCreature()
    {
        var card = NamedCardFactory.Create("Kazandu Mammoth", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Kazandu Mammoth");
    }

    [Fact]
    public void KazanduMammoth_HasMdfcState_WithCastableLandBackFace()
    {
        var mammoth = KazanduMammothFactory.Create(_alice);

        // CR 712.3 — front-face card carries the castable back-face descriptor.
        mammoth.MdfcState.Should().NotBeNull();
        mammoth.MdfcState!.FrontFaceName.Should().Be("Kazandu Mammoth");
        mammoth.MdfcState.BackFaceName.Should().Be("Kazandu Valley");
        mammoth.MdfcState.IsBackFace.Should().BeFalse("the creature is the front face");
        mammoth.MdfcState.CastableBackFace.Should().NotBeNull();
        mammoth.MdfcState.CastableBackFace!.IsLand.Should().BeTrue();
        mammoth.MdfcState.CastableBackFace.Name.Should().Be("Kazandu Valley");
    }

    // -----------------------------------------------------------------------
    // Front face — Landfall trigger (CR 603.6a / CR 702.142)
    // -----------------------------------------------------------------------

    [Fact]
    public void KazanduMammoth_LandfallTrigger_IsSelfAffecting_NoTargets()
    {
        var mammoth = KazanduMammothFactory.Create(_alice);

        var trigger = mammoth.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(mammoth);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "landfall pump affects the Mammoth itself — no target is chosen");
    }

    [Fact]
    public void KazanduMammoth_OwnersLandEnters_QueuesTrigger_PumpsPlusTwoPlusTwo()
    {
        var (zones, stack, triggers) = BuildEngine();

        var mammoth = KazanduMammothFactory.Create(_alice, triggers);
        mammoth.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(mammoth);
        mammoth.SetZone(ZoneType.Battlefield);
        triggers.BindCard(mammoth);

        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(forest);

        zones.MoveCardTo(forest, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "landfall trigger must queue when a land enters under controller's control");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        mammoth.GetPower().Should().Be(
            KazanduMammothFactory.Power + KazanduMammothFactory.PumpAmount);
        mammoth.GetToughness().Should().Be(
            KazanduMammothFactory.Toughness + KazanduMammothFactory.PumpAmount);
    }

    [Fact]
    public void KazanduMammoth_Pump_ExpiresAtEndOfTurn()
    {
        var (zones, stack, triggers) = BuildEngine();

        var mammoth = KazanduMammothFactory.Create(_alice, triggers);
        var svc = new ContinuousEffectsService();
        mammoth.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(mammoth);
        mammoth.SetZone(ZoneType.Battlefield);
        triggers.BindCard(mammoth);

        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(forest);

        zones.MoveCardTo(forest, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        mammoth.GetPower().Should().Be(5);

        // CR 514.2 — "until end of turn" effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        mammoth.GetPower().Should().Be(KazanduMammothFactory.Power);
        mammoth.GetToughness().Should().Be(KazanduMammothFactory.Toughness);
    }

    [Fact]
    public void KazanduMammoth_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var mammoth = KazanduMammothFactory.Create(_alice, triggers);
        mammoth.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(mammoth);
        mammoth.SetZone(ZoneType.Battlefield);
        triggers.BindCard(mammoth);

        var swamp = new Land("Swamp");
        swamp.SetOwner(_bob);
        swamp.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(swamp);

        zones.MoveCardTo(swamp, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "landfall only triggers on a land entering under YOUR control");
    }

    // -----------------------------------------------------------------------
    // Back face — Kazandu Valley identity + mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void KazanduValley_Identity_Land_TapsForGreen_BackFace()
    {
        var valley = KazanduValleyFactory.Create(_alice);

        valley.Name.Should().Be("Kazandu Valley");
        valley.HasType(CardType.Land).Should().BeTrue();
        valley.Owner.Should().BeSameAs(_alice);
        valley.Controller.Should().BeSameAs(_alice);

        // Pre-flipped to the back face — the land is the back face that exists.
        valley.MdfcState.Should().NotBeNull();
        valley.MdfcState!.IsBackFace.Should().BeTrue();
        valley.MdfcState.ActiveFaceName.Should().Be("Kazandu Valley");

        // {T}: Add {G} — single mana ability producing one green.
        var mana = valley.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.Should().NotBeNull();
    }

    [Fact]
    public void KazanduValley_NamedCardFactory_Dispatch_ProducesLand()
    {
        var card = NamedCardFactory.Create("Kazandu Valley", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Kazandu Valley");
    }

    [Fact]
    public void KazanduValley_EntersTapped_ViaReplacementBus()
    {
        var bus = new ReplacementBus();
        var valley = KazanduValleyFactory.Create(_alice, bus);

        // CR 614.1c — unconditional "this land enters tapped" replacement is
        // registered on the bus. Drive the ETB intent through it and confirm
        // EntersTapped is set.
        var intent = new ZoneMoveIntent(
            Card: valley,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var replaced = bus.Apply(intent);
        replaced.Should().NotBeNull();
        replaced!.EntersTapped.Should().BeTrue(
            "Kazandu Valley always enters tapped (CR 614.1c)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
