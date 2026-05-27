using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Mausoleum Wanderer (Shadows over Innistrad, {U}).
///
/// Covers:
///   - Identity (name, type, subtype, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch.
///   - Flying keyword presence.
///   - Spirit-ETB pump: another Spirit you control entering pumps +1/+1
///     EOT; non-Spirit creatures + opponent Spirits don't pump; self
///     doesn't pump (CR 109.5 + "another" qualifier).
///   - Activated ability: sac self + counter target instant/sorcery
///     unless controller pays {X = power}. Both auto-pay and counter
///     paths covered; verifies post-pump power scaling.
/// </summary>
public class MausoleumWandererTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeSpirit(Player owner, string name = "Topplegeist")
    {
        var c = new Creature(name, "W", 1, 1, subtypes: new[] { CardSubtype.Spirit });
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static Creature MakeNonSpirit(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2, subtypes: new[] { CardSubtype.Bear });
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MausoleumWanderer_Identity()
    {
        var w = MausoleumWandererFactory.Create(_alice);

        w.Name.Should().Be("Mausoleum Wanderer");
        w.ManaCost.Should().Be("{U}");
        w.HasType(CardType.Creature).Should().BeTrue();
        w.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        w.BasePower.Should().Be(1);
        w.BaseToughness.Should().Be(1);
        w.Owner.Should().BeSameAs(_alice);
        w.Controller.Should().BeSameAs(_alice);

        // Flying marker.
        w.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Flying");
    }

    [Fact]
    public void MausoleumWanderer_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Mausoleum Wanderer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Mausoleum Wanderer");
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Flying");
    }

    [Fact]
    public void MausoleumWanderer_ShapeOnly_HasOneTriggeredAndOneActivated()
    {
        var w = MausoleumWandererFactory.Create(_alice);
        w.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the 'another Spirit enters' pump trigger");
        w.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the sac-self counter-unless-pay-{X} activated ability");
    }

    // -----------------------------------------------------------------------
    // Spirit-ETB pump trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void AnotherSpiritYouControlEnters_PumpsPlusOnePlusOne()
    {
        var effects = new ContinuousEffectsService();
        var w = MausoleumWandererFactory.Create(
            _alice, stack: null, triggers: null, continuousEffects: effects);
        w.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(w);

        var spirit = MakeSpirit(_alice);

        var trigger = w.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CardMovedEvent(
            spirit, ZoneType.Stack, ZoneType.Battlefield)).Should().BeTrue();

        // Execute the pump.
        foreach (var e in trigger.Effects) e.Execute();

        w.GetPower().Should().Be(2, "+1/+1 EOT — CR 613.1f Layer 7c");
        w.GetToughness().Should().Be(2);
    }

    [Fact]
    public void NonSpiritEntering_DoesNotTrigger()
    {
        var w = MausoleumWandererFactory.Create(_alice);
        w.SetZone(ZoneType.Battlefield);

        var bear = MakeNonSpirit(_alice);

        var trigger = w.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CardMovedEvent(
            bear, ZoneType.Stack, ZoneType.Battlefield)).Should().BeFalse(
            "non-Spirit creatures don't satisfy the 'another Spirit' predicate");
    }

    [Fact]
    public void OpponentSpiritEntering_DoesNotTrigger()
    {
        var w = MausoleumWandererFactory.Create(_alice);
        w.SetZone(ZoneType.Battlefield);

        var oppSpirit = MakeSpirit(_bob, "Bob's Spirit");

        var trigger = w.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CardMovedEvent(
            oppSpirit, ZoneType.Stack, ZoneType.Battlefield)).Should().BeFalse(
            "CR 109.5 — 'you control' restricts to controller's own Spirits");
    }

    [Fact]
    public void WandererItselfEntering_DoesNotTrigger()
    {
        var w = MausoleumWandererFactory.Create(_alice);
        w.SetZone(ZoneType.Battlefield);

        var trigger = w.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CardMovedEvent(
            w, ZoneType.Stack, ZoneType.Battlefield)).Should().BeFalse(
            "'another' qualifier excludes Wanderer itself");
    }

    // -----------------------------------------------------------------------
    // Activated ability — sac self + counter unless pay {X = power}
    // -----------------------------------------------------------------------

    [Fact]
    public void Activated_CountersTargetSpell_WhenControllerCannotPayX()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var w = MausoleumWandererFactory.Create(
            _alice, stack: stack, triggers: null, continuousEffects: null);
        _alice.Zones.Battlefield.AddCard(w);
        w.SetZone(ZoneType.Battlefield);

        // Bob casts an instant; he has NO mana available to pay {X}.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);
        bolt.SetZone(ZoneType.Stack);
        var boltSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        stack.Push(boltSpell);

        var ability = w.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { boltSpell },
        });

        foreach (var e in ability.Effects) e.Execute();

        // Wanderer was sacrificed — graveyard, not battlefield.
        w.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.16 — sacrifice moves the permanent to its owner's graveyard");
        _alice.Zones.Graveyard.GetCards().Should().Contain(w);

        // Bolt was countered — off the stack, in Bob's graveyard.
        stack.GetAll().Should().NotContain(boltSpell);
        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.5 — countered spell goes to its owner's graveyard");
    }

    [Fact]
    public void Activated_DoesNotCounter_WhenControllerPaysX()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var w = MausoleumWandererFactory.Create(
            _alice, stack: stack, triggers: null, continuousEffects: null);
        _alice.Zones.Battlefield.AddCard(w);
        w.SetZone(ZoneType.Battlefield);

        // Bob has {1} available and can pay {X = 1} = Wanderer's base power.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var spell = new Sorcery("Bob's Sorcery", "{1}{B}");
        spell.SetOwner(_bob);
        spell.SetController(_bob);
        spell.SetZone(ZoneType.Stack);
        var stackSpell = new Majik.Core.Spells.Spell(spell, _bob);
        stack.Push(stackSpell);

        var ability = w.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { stackSpell },
        });

        foreach (var e in ability.Effects) e.Execute();

        // Wanderer still sacrificed (cost paid regardless).
        w.Zone.Should().Be(ZoneType.Graveyard);

        // But the target spell stays on the stack — Bob paid {X}.
        stack.GetAll().Should().Contain(stackSpell);
        spell.Zone.Should().Be(ZoneType.Stack,
            "CR 118.4 — when controller pays the alternative cost, the counter does not happen");
    }

    [Fact]
    public void Activated_XScalesWithPumpedPower()
    {
        // After a Spirit-ETB pump, Wanderer's power is 2 — so {X} = {2}.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var effects = new ContinuousEffectsService();

        var w = MausoleumWandererFactory.Create(
            _alice, stack: stack, triggers: null, continuousEffects: effects);
        _alice.Zones.Battlefield.AddCard(w);
        w.SetZone(ZoneType.Battlefield);

        // Pump Wanderer to 2/2 (simulate one prior Spirit-ETB pump).
        var spirit = MakeSpirit(_alice);
        var pumpTrigger = w.Abilities.OfType<TriggeredAbility>().Single();
        // Confirm condition matches before executing the effect.
        pumpTrigger.IsTriggered(new CardMovedEvent(
            spirit, ZoneType.Stack, ZoneType.Battlefield)).Should().BeTrue();
        foreach (var e in pumpTrigger.Effects) e.Execute();
        w.GetPower().Should().Be(2);

        // Bob has only {1} — not enough to pay {X = 2}.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var spell = new Sorcery("Bob's Sorcery 2", "{B}");
        spell.SetOwner(_bob);
        spell.SetController(_bob);
        spell.SetZone(ZoneType.Stack);
        var stackSpell = new Majik.Core.Spells.Spell(spell, _bob);
        stack.Push(stackSpell);

        var ability = w.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { stackSpell },
        });

        foreach (var e in ability.Effects) e.Execute();

        // Bob couldn't pay {2} (only had {1}) — spell countered.
        spell.Zone.Should().Be(ZoneType.Graveyard,
            "X scaled to Wanderer's pumped power (2) — Bob's {1} couldn't satisfy {2}");
    }

    [Fact]
    public void Activated_TargetRequest_FiltersInstantsAndSorceries()
    {
        var w = MausoleumWandererFactory.Create(_alice);

        var ability = w.Abilities.OfType<ActivatedAbility>().Single();
        ability.TargetRequests.Should().HaveCount(1);

        var req = ability.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("instant or sorcery");
    }
}
