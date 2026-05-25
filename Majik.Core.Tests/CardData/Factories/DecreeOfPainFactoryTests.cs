using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DecreeOfPainFactory"/> (Scourge).
///
/// Covers:
/// - Identity ({6}{B}{B} Sorcery).
/// - Cycling activated ability ({3}{B}{B} mana + DiscardSelfCost) shape.
/// - On-cycle trigger shape — subscribes to
///   <see cref="CardCycledEvent"/>, fires on self-cycle only, lives in
///   the graveyard (post-discard zone gate).
/// - <see cref="BuildResolveEffect"/> destroys all creatures + each
///   controller discards one (CR 701.7 + CR 701.15 no-regen rider).
/// - <see cref="BuildCycleEffect"/> applies -2/-2 EOT to every creature
///   on every supplied battlefield (CR 613 / CR 514.2 layer-system
///   pump).
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class DecreeOfPainFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DecreeOfPain_Identity_Sorcery6BB()
    {
        var card = DecreeOfPainFactory.Create(_alice);

        card.Name.Should().Be("Decree of Pain");
        card.ManaCost.Should().Be("{6}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DecreeOfPain_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Decree of Pain", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Decree of Pain");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the cycling activated ability");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the on-cycle -2/-2 trigger");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void DecreeOfPain_HasCyclingActivatedAbility_With3BBAndDiscardSelf()
    {
        var card = DecreeOfPainFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2, "cycling = {3}{B}{B} + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(3);
        mana.Black.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // On-cycle trigger shape — CR 702.32d
    // -----------------------------------------------------------------------

    [Fact]
    public void DecreeOfPain_OnCycleTrigger_SubscribesToCardCycledEvent_FromGraveyard()
    {
        var card = DecreeOfPainFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Should().BeOfType<EventTriggerCondition<CardCycledEvent>>();
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "Decree of Pain lives in the graveyard when its on-cycle trigger fires");
        trigger.TargetRequests.Should().BeEmpty("no targets — sweep is global");
    }

    [Fact]
    public void DecreeOfPain_OnCycleTrigger_Fires_OnSelfCycle()
    {
        var card = DecreeOfPainFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var selfEvent = new CardCycledEvent(card, _alice);
        trigger.Condition.Matches(selfEvent, trigger).Should().BeTrue(
            "self-cycle gate fires when THIS Decree is the cycled card");
    }

    [Fact]
    public void DecreeOfPain_OnCycleTrigger_DoesNotFire_OnOtherCardCycle()
    {
        var card = DecreeOfPainFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var otherCard = new Card("Some Cycler", "");
        var otherEvent = new CardCycledEvent(otherCard, _alice);
        trigger.Condition.Matches(otherEvent, trigger).Should().BeFalse(
            "trigger gates on ReferenceEquals(e.Card, card) — other cards don't fire");
    }

    // -----------------------------------------------------------------------
    // BuildResolveEffect — wrath + per-controller discard
    // -----------------------------------------------------------------------

    [Fact]
    public void DecreeOfPain_Resolve_DestroysAllCreatures_NoRegen_AndDiscardsPerController()
    {
        // Seed Alice and Bob each with two creatures + one card in hand.
        var aliceCreatures = new[]
        {
            SeedCreature(_alice, "Alice-A"),
            SeedCreature(_alice, "Alice-B"),
        };
        var bobCreatures = new[] { SeedCreature(_bob, "Bob-A") };
        var aliceHand = SeedHand(_alice, "Alice-Inquisition");
        var bobHand = SeedHand(_bob, "Bob-Thoughtseize");

        var effects = DecreeOfPainFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // All creatures dead.
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();

        // Each controller discarded one card.
        _alice.Zones.Hand.GetCards().Should().NotContain(aliceHand);
        _bob.Zones.Hand.GetCards().Should().NotContain(bobHand);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceHand);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobHand);

        // All destroyed creatures land in their respective graveyards.
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceCreatures);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobCreatures);
    }

    [Fact]
    public void DecreeOfPain_Resolve_LeavesNonCreaturePermanentsAlone()
    {
        var aliceCreature = SeedCreature(_alice, "Alice-Creature");
        var aliceLand = SeedLand(_alice, "Alice-Swamp");
        var aliceArtifact = SeedArtifact(_alice, "Alice-Mox");

        var effects = DecreeOfPainFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(aliceLand);
        _alice.Zones.Battlefield.GetCards().Should().Contain(aliceArtifact);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceCreature);
    }

    [Fact]
    public void DecreeOfPain_Resolve_EmptyHand_NoCrash_NoDiscard()
    {
        SeedCreature(_alice, "Alice-Lone");
        // Bob has no cards in hand at all.

        var effects = DecreeOfPainFactory.BuildResolveEffect(new[] { _alice, _bob });
        // Alice has nothing in hand either — the per-kill discard is a
        // clean no-op (CR 119.3 / CR 701.12 — discarding zero cards is
        // legal at empty hand).
        var act = () =>
        {
            foreach (var e in effects) e.Execute();
        };
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // BuildCycleEffect — -2/-2 EOT sweep
    // -----------------------------------------------------------------------

    [Fact]
    public void DecreeOfPain_CycleEffect_AppliesMinusTwoMinusTwoEot_ToAllCreatures()
    {
        var continuous = new ContinuousEffectsService();
        var aliceCreature = SeedCreature(_alice, "Bear", power: 5, toughness: 5, effects: continuous);
        var bobCreature = SeedCreature(_bob, "Wolf", power: 4, toughness: 4, effects: continuous);

        var effects = DecreeOfPainFactory.BuildCycleEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Layer 7c modify — Power/Toughness reflect the -2/-2 continuous
        // effect via the shared ContinuousEffectsService.
        aliceCreature.Power.Should().Be(3);
        aliceCreature.Toughness.Should().Be(3);
        bobCreature.Power.Should().Be(2);
        bobCreature.Toughness.Should().Be(2);
    }

    [Fact]
    public void DecreeOfPain_CycleEffect_NoActiveEffects_IsNoOp()
    {
        // Creatures without ActiveEffects wired (shape-only path) — the
        // sweep simply doesn't register against them and they keep their
        // base P/T. Matches the AllCreaturesPumpSpell shape-only posture.
        var aliceCreature = SeedCreature(_alice, "Bear", power: 5, toughness: 5);

        var effects = DecreeOfPainFactory.BuildCycleEffect(new[] { _alice, _bob });
        var act = () =>
        {
            foreach (var e in effects) e.Execute();
        };
        act.Should().NotThrow("missing ActiveEffects is a clean skip");
        aliceCreature.Power.Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature SeedCreature(
        Player owner, string name, int power = 2, int toughness = 2,
        ContinuousEffectsService? effects = null)
    {
        var c = new Creature(name, "", power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        if (effects != null)
        {
            c.ActiveEffects = effects;
        }
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Land SeedLand(Player owner, string name)
    {
        var l = new Land(name);
        l.SetOwner(owner);
        l.SetController(owner);
        owner.Zones.Battlefield.AddCard(l);
        l.SetZone(ZoneType.Battlefield);
        return l;
    }

    private static Artifact SeedArtifact(Player owner, string name)
    {
        var a = new Artifact(name, "");
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }

    private static Card SeedHand(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }
}
