using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Rattlechains (Shadows over Innistrad, {1}{W}).
///
/// Covers:
///   - Card shape: name, type, Spirit subtype, P/T 2/2, mana cost.
///   - Flash + Flying keyword markers.
///   - ETB trigger structure (1..1 target Spirit, BotIntent.Protection).
///   - Resolve: grants Hexproof until EOT to the chosen target Spirit.
///   - Resolve guards: illegal target → no-op (off-battlefield, non-Spirit,
///     opponent-controlled).
///   - Spirit-flash printed static: while on battlefield, Spirit cards in
///     hand pass instant-speed cast check; lifted on LTB.
///   - Spirit-flash does NOT cover opponent's Spirit cards or non-Spirits.
///   - NamedCardFactory dispatch.
/// </summary>
public class RattlechainsFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public RattlechainsFactoryTests()
    {
        // Defensive — same posture as SigardasAidTests. Other named-card
        // tests share FlashGrantRegistry global state.
        FlashGrantRegistry.Clear();
    }

    public void Dispose()
    {
        FlashGrantRegistry.Clear();
    }

    [Fact]
    public void Rattlechains_IsCreature_Spirit_2_2_AtCost1W()
    {
        var c = RattlechainsFactory.Create(_alice);

        c.Name.Should().Be("Rattlechains");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Rattlechains_HasFlashAndFlying()
    {
        var c = RattlechainsFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Flying");
    }

    [Fact]
    public void Rattlechains_EtbTrigger_DeclaresTargetSpiritYouControl()
    {
        var c = RattlechainsFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("Spirit");
        req.Intent.Should().Be(BotIntent.Protection);

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void Rattlechains_Etb_GrantsHexproofToTargetSpiritEOT()
    {
        var svc = new ContinuousEffectsService();
        var c = RattlechainsFactory.Create(_alice, eventBus: null, triggers: null, continuousEffects: svc);

        var ally = new Creature("Mausoleum Wanderer", "{U}", 1, 1,
            subtypes: new[] { CardSubtype.Spirit });
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ally },
        });

        foreach (var e in etb.Effects) e.Execute();

        svc.Compute(ally).Keywords.Should().Contain("Hexproof");
    }

    [Fact]
    public void Rattlechains_Etb_NonSpiritTarget_NoOp()
    {
        var svc = new ContinuousEffectsService();
        var c = RattlechainsFactory.Create(_alice, eventBus: null, triggers: null, continuousEffects: svc);

        var human = new Creature("Doomed Traveler", "{W}", 1, 1,
            subtypes: new[] { CardSubtype.Human });
        human.SetOwner(_alice);
        human.SetController(_alice);
        human.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(human);
        human.ActiveEffects = svc;

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { human },
        });

        foreach (var e in etb.Effects) e.Execute();

        svc.Compute(human).Keywords.Should().NotContain("Hexproof");
    }

    [Fact]
    public void Rattlechains_Etb_OpponentControlledSpirit_NoOp()
    {
        var svc = new ContinuousEffectsService();
        var c = RattlechainsFactory.Create(_alice, eventBus: null, triggers: null, continuousEffects: svc);

        var bobSpirit = new Creature("Bob's Spirit", "{1}{U}", 1, 1,
            subtypes: new[] { CardSubtype.Spirit });
        bobSpirit.SetOwner(_bob);
        bobSpirit.SetController(_bob);
        bobSpirit.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobSpirit);
        bobSpirit.ActiveEffects = svc;

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobSpirit },
        });

        foreach (var e in etb.Effects) e.Execute();

        // "Target Spirit you control" — bob's spirit fails the controller
        // legality re-check at resolution (CR 608.2b).
        svc.Compute(bobSpirit).Keywords.Should().NotContain("Hexproof");
    }

    [Fact]
    public void Rattlechains_Etb_TargetLeftBattlefield_NoOp()
    {
        var svc = new ContinuousEffectsService();
        var c = RattlechainsFactory.Create(_alice, eventBus: null, triggers: null, continuousEffects: svc);

        var ally = new Creature("Selfless Spirit", "{1}{W}", 2, 1,
            subtypes: new[] { CardSubtype.Spirit });
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Graveyard); // moved off bf before resolve
        _alice.Zones.Graveyard.AddCard(ally);
        ally.ActiveEffects = svc;

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ally },
        });

        foreach (var e in etb.Effects) e.Execute();

        svc.Compute(ally).Keywords.Should().NotContain("Hexproof");
    }

    [Fact]
    public void Rattlechains_OnBattlefield_GrantsFlashToOwnersSpiritInHand()
    {
        var (bus, zones, _, _) = BuildEngine();

        var rc = RattlechainsFactory.Create(_alice, bus, triggers: null, continuousEffects: null);
        _alice.Zones.Hand.AddCard(rc);
        rc.SetZone(ZoneType.Hand);
        zones.MoveCardTo(rc, ZoneType.Battlefield, controller: _alice);

        // Spirit card in Alice's hand — Rattlechains' static should grant it
        // as-though-it-had-flash.
        var spirit = new Creature("Mausoleum Wanderer", "{U}", 1, 1,
            subtypes: new[] { CardSubtype.Spirit });
        spirit.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(spirit);
        spirit.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(spirit).Should().BeTrue();
    }

    [Fact]
    public void Rattlechains_DoesNotGrantFlashToOpponentsSpirit()
    {
        var (bus, zones, _, _) = BuildEngine();

        var rc = RattlechainsFactory.Create(_alice, bus, triggers: null, continuousEffects: null);
        _alice.Zones.Hand.AddCard(rc);
        rc.SetZone(ZoneType.Hand);
        zones.MoveCardTo(rc, ZoneType.Battlefield, controller: _alice);

        var bobSpirit = new Creature("Bob's Spirit", "{1}{U}", 1, 1,
            subtypes: new[] { CardSubtype.Spirit });
        bobSpirit.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobSpirit);
        bobSpirit.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(bobSpirit).Should().BeFalse(
            "predicate keys on owner per CR 108.4 — opponent-owned Spirits don't get the grant");
    }

    [Fact]
    public void Rattlechains_DoesNotGrantFlashToNonSpirit()
    {
        var (bus, zones, _, _) = BuildEngine();

        var rc = RattlechainsFactory.Create(_alice, bus, triggers: null, continuousEffects: null);
        _alice.Zones.Hand.AddCard(rc);
        rc.SetZone(ZoneType.Hand);
        zones.MoveCardTo(rc, ZoneType.Battlefield, controller: _alice);

        var human = new Creature("Doomed Traveler", "{W}", 1, 1,
            subtypes: new[] { CardSubtype.Human });
        human.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(human);
        human.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(human).Should().BeFalse();
    }

    [Fact]
    public void Rattlechains_LeavesBattlefield_FlashGrantLifted()
    {
        var (bus, zones, _, _) = BuildEngine();

        var rc = RattlechainsFactory.Create(_alice, bus, triggers: null, continuousEffects: null);
        _alice.Zones.Hand.AddCard(rc);
        rc.SetZone(ZoneType.Hand);
        zones.MoveCardTo(rc, ZoneType.Battlefield, controller: _alice);

        var spirit = new Creature("Selfless Spirit", "{1}{W}", 2, 1,
            subtypes: new[] { CardSubtype.Spirit });
        spirit.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(spirit);
        spirit.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(spirit).Should().BeTrue();

        // Rattlechains dies — grant lifts.
        zones.MoveCardTo(rc, ZoneType.Graveyard, controller: _alice);

        TimingRules.CanCastAtInstantSpeed(spirit).Should().BeFalse(
            "FlashGrantStaticEffect unregisters on LTB");
    }

    [Fact]
    public void Rattlechains_NamedCardFactory_Dispatch()
    {
        var card = NamedCardFactory.Create("Rattlechains", _alice);

        card.Should().NotBeNull();
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Rattlechains");
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(2);
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
    }

    // ─── BuildEngine helper (mirrors SigardasAidTests) ──────────────────────

    private static (EventBus bus, ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new Majik.Core.Effects.ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (bus, zones, stack, triggers);
    }
}
