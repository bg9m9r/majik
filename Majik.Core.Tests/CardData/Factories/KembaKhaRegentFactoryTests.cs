using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KembaKhaRegentFactory"/> (Scars of Mirrodin /
/// Commander, {1}{W}{W}).
///
/// Oracle text (Scryfall, verified 2026-06-02):
///   "At the beginning of your upkeep, create a 2/2 white Cat creature
///    token for each Equipment attached to Kemba."
///
/// Covers:
/// - Identity (name, type Creature, supertype Legendary, subtypes Cat +
///   Cleric, P/T 2/4, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Your-upkeep trigger (CR 603.1 / CR 500.4): fires only on the
///   controller's own Upkeep step, not an opponent's, not another step.
/// - Token count = number of Equipment attached to Kemba (CR 301.5 /
///   CR 111.4): 0 Equipment → 0 tokens; N Equipment → N 2/2 white Cat
///   tokens. Non-Equipment attachments (Auras) are not counted.
/// - Trigger is battlefield-only (CR 113.6).
/// </summary>
[Trait("Color", "W")]
public class KembaKhaRegentFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility UpkeepTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single();

    private Artifact MakeEquipment(string name = "Bonesplitter")
    {
        var eq = new Artifact(name, "{1}", subtypes: new[] { CardSubtype.Equipment });
        eq.SetOwner(_alice);
        eq.SetController(_alice);
        return eq;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Kemba_Identity()
    {
        var c = KembaKhaRegentFactory.Create(_alice);

        c.Name.Should().Be("Kemba, Kha Regent");
        c.ManaCost.Should().Be("{1}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Kemba is Legendary");
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Kemba_DispatchesThroughNamedCardFactory()
    {
        var built = Majik.Core.CardData.NamedCardFactory.Create("Kemba, Kha Regent", _alice);
        built.Should().NotBeNull();
        built.Name.Should().Be("Kemba, Kha Regent");
        built.HasType(CardType.Creature).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Your-upkeep trigger gating (CR 603.1 / 500.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void Kemba_UpkeepTrigger_FiresOnlyOnControllersUpkeep()
    {
        var kemba = KembaKhaRegentFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(kemba);
        kemba.SetZone(ZoneType.Battlefield);

        var upkeep = UpkeepTrigger(kemba);

        upkeep.IsTriggered(new StepStartedEvent(StepStateType.Upkeep, _alice))
            .Should().BeTrue("At the beginning of your upkeep — CR 603.1");
        upkeep.IsTriggered(new StepStartedEvent(StepStateType.Upkeep, _bob))
            .Should().BeFalse("\"your upkeep\" fires for the controller only — CR 603.1");
        upkeep.IsTriggered(new StepStartedEvent(StepStateType.Draw, _alice))
            .Should().BeFalse("only the Upkeep step fires it");
    }

    [Fact]
    public void Kemba_UpkeepTrigger_OnlyActiveOnBattlefield()
    {
        var kemba = KembaKhaRegentFactory.Create(_alice);
        var upkeep = UpkeepTrigger(kemba);

        upkeep.ActiveZones.Should().Contain(ZoneType.Battlefield);
        upkeep.ActiveZones.Should().NotContain(ZoneType.Hand,
            "upkeep triggers are battlefield-only abilities — CR 113.6");
        upkeep.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Token count = Equipment attached to Kemba (CR 301.5 / 111.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void Kemba_CountEquipment_CountsOnlyAttachedEquipment()
    {
        var kemba = KembaKhaRegentFactory.Create(_alice);

        KembaKhaRegentFactory.CountEquipmentAttachedTo(kemba).Should().Be(0,
            "no Equipment attached → 0");

        var sword = MakeEquipment("Sword of Fire and Ice");
        sword.AttachTo(kemba);
        var splitter = MakeEquipment("Bonesplitter");
        splitter.AttachTo(kemba);

        // A non-Equipment artifact attached as an Aura-like permanent must
        // NOT be counted (CR 301.5 — only Equipment).
        var aura = new Enchantment("Pacifism", "{1}{W}", subtypes: new[] { CardSubtype.Aura });
        aura.SetOwner(_alice);
        aura.SetController(_alice);
        aura.AttachTo(kemba);

        KembaKhaRegentFactory.CountEquipmentAttachedTo(kemba).Should().Be(2,
            "two Equipment attached; the Aura does not count");
    }

    [Fact]
    public void Kemba_UpkeepResolution_CreatesOneCatTokenPerAttachedEquipment()
    {
        var kemba = KembaKhaRegentFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(kemba);
        kemba.SetZone(ZoneType.Battlefield);

        var sword = MakeEquipment("Sword of Fire and Ice");
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(kemba);

        var splitter = MakeEquipment("Bonesplitter");
        _alice.Zones.Battlefield.AddCard(splitter);
        splitter.SetZone(ZoneType.Battlefield);
        splitter.AttachTo(kemba);

        var beforeTokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count(c => c.IsToken);

        var upkeep = UpkeepTrigger(kemba);
        upkeep.IsTriggered(new StepStartedEvent(StepStateType.Upkeep, _alice)).Should().BeTrue();
        foreach (var e in upkeep.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => c.IsToken).ToList();

        tokens.Should().HaveCount(beforeTokens + 2,
            "one 2/2 white Cat token per Equipment attached to Kemba");
        tokens.Should().OnlyContain(t =>
            t.BasePower == 2 && t.BaseToughness == 2
            && t.HasSubtype(CardSubtype.Cat),
            "tokens are 2/2 Cats — CR 111.4");
        tokens.Should().OnlyContain(t => CardColors.GetColors(t).Contains(ManaColor.White),
            "tokens are white — CR 111.4");
    }

    [Fact]
    public void Kemba_UpkeepResolution_NoEquipment_CreatesNoTokens()
    {
        var kemba = KembaKhaRegentFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(kemba);
        kemba.SetZone(ZoneType.Battlefield);

        var upkeep = UpkeepTrigger(kemba);
        upkeep.IsTriggered(new StepStartedEvent(StepStateType.Upkeep, _alice)).Should().BeTrue();

        var act = () => { foreach (var e in upkeep.Effects) e.Execute(); };
        act.Should().NotThrow();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.IsToken).Should().Be(0,
            "no Equipment attached → no tokens (CR 111.4)");
    }
}
