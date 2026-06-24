using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RoxanneStarfallSavantFactory"/>
/// (Outlaws of Thunder Junction, {3}{R}{G}). Legendary Creature — Cat Druid
/// 4/3:
///   "Whenever Roxanne enters or attacks, create a tapped colorless artifact
///    token named Meteorite with 'When this token enters, it deals 2 damage to
///    any target' and '{T}: Add one mana of any color.'
///    Whenever you tap an artifact token for mana, add one mana of any type
///    that artifact token produced."
///
/// Covers (unique behaviour only — CardFactoryContractTests asserts dispatch +
/// well-formedness):
/// - Identity (Legendary Cat Druid 4/3 at {3}{R}{G}).
/// - The enters trigger AND the attacks trigger each mint one TAPPED colorless
///   Meteorite artifact token (CR 111.8 / 603.6a / 508.1f).
/// - The Meteorite carries five "{T}: Add one mana of any color" mana abilities
///   and a "When this token enters, deal 2 damage to any target" ETB trigger.
/// - The Meteorite ETB deals 2 damage to a chosen target (CR 119).
/// - The "tap an artifact token for mana" mana-doubler (CR 605.1b) adds one
///   mana of the type the token produced, and does NOT fire for a non-token
///   artifact.
/// </summary>
[Trait("Color", "M")]
public class RoxanneStarfallSavantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers,
        ManaAbilityActivator activator, EventBus bus) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var activator = new ManaAbilityActivator(bus);
        return (zones, stack, triggers, activator, bus);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Roxanne_Identity()
    {
        var c = RoxanneStarfallSavantFactory.Create(_alice, zoneService: null, triggers: null);

        c.Name.Should().Be("Roxanne, Starfall Savant");
        c.ManaCost.Should().Be("{3}{R}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue("CR 205.4a — Legendary.");
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Enters / attacks → tapped Meteorite token
    // -----------------------------------------------------------------------

    [Fact]
    public void RoxanneEnters_CreatesTappedColorlessMeteoriteToken()
    {
        var (zones, stack, triggers, _, _) = BuildEngine();

        var roxanne = RoxanneStarfallSavantFactory.Create(_alice, zones, triggers);
        roxanne.SetZone(ZoneType.Library); // sentinel for the ETB zone move.
        _alice.Zones.Library.AddCard(roxanne);

        // Roxanne enters the battlefield → her enters trigger fires.
        zones.MoveCardTo(roxanne, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1, "Roxanne's enters trigger should queue once.");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var meteorites = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.Name == "Meteorite" && a.IsToken)
            .ToList();
        meteorites.Should().HaveCount(1, "CR 603.6a — one Meteorite per enters trigger.");

        var meteorite = meteorites[0];
        meteorite.HasType(CardType.Artifact).Should().BeTrue();
        meteorite.IsTapped.Should().BeTrue("CR 111.8 — 'create a tapped … token'.");
        CardColors.GetColors(meteorite).Should().BeEmpty("a colorless artifact token.");
    }

    [Fact]
    public void RoxanneAttacks_CreatesTappedMeteoriteToken()
    {
        var (zones, stack, triggers, _, _) = BuildEngine();

        var roxanne = RoxanneStarfallSavantFactory.Create(_alice, zones, triggers);
        roxanne.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(roxanne);

        // Roxanne attacks → her attacks trigger fires (CR 508.1f).
        triggers.EvaluateTriggers(new CreatureAttacksEvent(roxanne, _bob));

        triggers.PendingCount.Should().Be(1, "Roxanne's attacks trigger should queue once.");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var meteorites = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.Name == "Meteorite" && a.IsToken)
            .ToList();
        meteorites.Should().HaveCount(1, "CR 508.1f — one Meteorite per attacks trigger.");
        meteorites[0].IsTapped.Should().BeTrue("CR 111.8 — the Meteorite enters tapped.");
    }

    // -----------------------------------------------------------------------
    // Meteorite shape: five any-color mana abilities + ETB damage trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void Meteorite_HasFiveAnyColorManaAbilitiesAndEtbDamageTrigger()
    {
        var meteorite = RoxanneStarfallSavantFactory.CreateMeteorite(_alice);

        meteorite.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "'{T}: Add one mana of any color' is five WUBRG mana options.");

        meteorite.Abilities.OfType<TriggeredAbility>()
            .Count(t => t.Condition is EventTriggerCondition<CardMovedEvent>)
            .Should().Be(1, "the Meteorite has its 'when this token enters' ETB damage trigger.");
    }

    [Fact]
    public void Meteorite_EtbTrigger_DealsTwoDamageToChosenTarget()
    {
        var meteorite = RoxanneStarfallSavantFactory.CreateMeteorite(_alice);

        var trigger = meteorite.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        trigger.SetChosenTargets(new System.Collections.Generic.IReadOnlyList<object>[]
            { new object[] { _bob } });
        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(18,
            "CR 119 — the Meteorite ETB deals 2 damage to the chosen target (a player).");
    }

    // -----------------------------------------------------------------------
    // "Whenever you tap an artifact token for mana" doubler (CR 605.1b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TappingYourArtifactTokenForMana_AddsAdditionalManaOfThatType()
    {
        var (zones, stack, triggers, activator, _) = BuildEngine();

        var roxanne = RoxanneStarfallSavantFactory.Create(_alice, zones, triggers);
        roxanne.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(roxanne);

        // An artifact TOKEN Alice controls with a mana ability. Build the
        // Meteorite directly with no zone service (its ETB CardMovedEvent +
        // damage trigger are irrelevant here) and untap it so its {R} option
        // can be activated.
        var meteorite = RoxanneStarfallSavantFactory.CreateMeteorite(_alice);
        if (meteorite.IsTapped) meteorite.Untap();

        // Activate the Meteorite's {R} option.
        var redOption = meteorite.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Equals(ManaCost.Parse("R")));
        activator.ActivateManaAbility(redOption, _alice);

        _alice.ManaPool.Red.Should().Be(1, "the Meteorite's own {R}.");
        triggers.PendingCount.Should().Be(1,
            "tapping an artifact token for mana triggers Roxanne's doubler.");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.ManaPool.Red.Should().Be(2,
            "CR 605.1b — Roxanne adds one additional {R} of the type the token produced.");
    }

    [Fact]
    public void TappingYourNonTokenArtifactForMana_DoesNotTrigger()
    {
        var (zones, stack, triggers, activator, _) = BuildEngine();

        var roxanne = RoxanneStarfallSavantFactory.Create(_alice, zones, triggers);
        roxanne.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(roxanne);

        // A real (non-token) mana rock — "an artifact TOKEN" gates it out.
        var rock = new Artifact("Mind Stone", "2");
        rock.SetController(_alice);
        rock.SetZone(ZoneType.Battlefield);
        rock.AddAbility(new ManaAbility(rock, _alice, ManaCost.Parse("C")));
        _alice.Zones.Battlefield.AddCard(rock);

        activator.ActivateManaAbility(rock.Abilities.OfType<ManaAbility>().Single(), _alice);

        triggers.PendingCount.Should().Be(0, "a non-token artifact isn't an 'artifact token'.");
    }
}
