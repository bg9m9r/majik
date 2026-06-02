using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Touch the Spirit Realm (Kamigawa: Neon Dynasty, {2}{W}).
///
/// Oracle (Scryfall, verified 2026-06-02):
///   "When this enchantment enters, exile up to one target artifact or
///    creature until this enchantment leaves the battlefield.
///    Channel — {1}{W}, Discard this card: Exile target artifact or creature.
///    Return it to the battlefield under its owner's control at the beginning
///    of the next end step."
///
/// Covers: Enchantment identity + dispatch; ETB O-Ring exile (any controller,
/// artifact-or-creature, "up to one" optional); ETB land rejection; LTB return
/// under owner's control; Channel cost shape + exile.
/// </summary>
[Trait("Color", "W")]
public class TouchTheSpiritRealmTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility Etb(Enchantment c) =>
        c.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 1);

    private static TriggeredAbility Ltb(Enchantment c) =>
        c.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 0);

    [Fact]
    public void Identity_IsEnchantment_At2W()
    {
        var c = TouchTheSpiritRealmFactory.Create(_alice);

        c.Name.Should().Be("Touch the Spirit Realm");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2, "ETB exile + LTB return");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1, "Channel");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AsEnchantment()
    {
        var card = NamedCardFactory.Create("Touch the Spirit Realm", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Touch the Spirit Realm");
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void Etb_ExilesTargetCreature_AnyController()
    {
        var touch = TouchTheSpiritRealmFactory.Create(_alice);
        touch.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(touch);

        // No "an opponent controls" restriction — Bob's creature is fine.
        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob); bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = Etb(touch);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsCreature } });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(bobsCreature);
    }

    [Fact]
    public void Etb_UpToOne_EmptyChoice_IsNoOp()
    {
        var touch = TouchTheSpiritRealmFactory.Create(_alice);
        touch.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(touch);

        var etb = Etb(touch);
        etb.TargetRequests[0].MinTargets.Should().Be(0, "'up to one' is optional");
        // Empty choice — clean no-op (CR 115.1b).
        etb.SetChosenTargets(new IReadOnlyList<object>[] { Array.Empty<object>() });
        var act = () => { foreach (var e in etb.Effects) e.Execute(); };
        act.Should().NotThrow();
    }

    [Fact]
    public void Etb_RejectsLandTarget()
    {
        var touch = TouchTheSpiritRealmFactory.Create(_alice);
        touch.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(touch);

        var bobsLand = new Land("Forest");
        bobsLand.SetOwner(_bob); bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var etb = Etb(touch);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsLand } });
        foreach (var e in etb.Effects) e.Execute();

        bobsLand.Zone.Should().Be(ZoneType.Battlefield,
            "a plain land is neither artifact nor creature — skipped");
    }

    [Fact]
    public void Ltb_ReturnsExiledCard_UnderOwnersControl()
    {
        var touch = TouchTheSpiritRealmFactory.Create(_alice);
        touch.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(touch);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob); bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = Etb(touch);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsCreature } });
        foreach (var e in etb.Effects) e.Execute();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        // Touch leaves the battlefield → exiled card returns.
        foreach (var e in Ltb(touch).Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield);
        bobsCreature.Controller.Should().BeSameAs(_bob, "returns under its owner's control (CR 110.2)");
    }

    [Fact]
    public void Channel_CostShape_IsOneWPlusDiscardSelf()
    {
        var touch = TouchTheSpiritRealmFactory.Create(_alice);
        var channel = touch.Abilities.OfType<ActivatedAbility>().Single();

        channel.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Cost.ToString().Should().Be("1W", "Channel costs {1}{W}");
        channel.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();
    }

    [Fact]
    public void Channel_ExilesTargetArtifactOrCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var touch = TouchTheSpiritRealmFactory.Create(_alice, triggers, zones);

        var bobsArtifact = new Artifact("Worn Powerstone", "{3}");
        bobsArtifact.SetOwner(_bob); bobsArtifact.SetController(_bob);
        bobsArtifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsArtifact);

        var channel = touch.Abilities.OfType<ActivatedAbility>().Single();
        channel.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsArtifact } });
        foreach (var e in channel.Effects) e.Execute();

        bobsArtifact.Zone.Should().Be(ZoneType.Exile, "Channel exiles the target (CR 701.21)");
    }
}
