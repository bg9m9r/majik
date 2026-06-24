using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Plumecreed Escort (Bloomburrow, {1}{U}).
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "Flash
///    Flying
///    When this creature enters, target creature you control gains hexproof
///    until end of turn."
///
/// Covers:
///   - Identity: name, {1}{U}, Creature — Bird Scout, 2/1, blue.
///   - Flash + Flying keyword markers.
///   - ETB trigger structure (1..1 target creature you control,
///     BotIntent.Protection).
///   - Resolve: grants Hexproof until EOT to the chosen target creature.
///   - Resolve guards: illegal target → no-op (off-battlefield,
///     opponent-controlled).
/// </summary>
[Trait("Color", "U")]
public class PlumecreedEscortFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void PlumecreedEscort_Identity()
    {
        var c = PlumecreedEscortFactory.Create(_alice);

        c.Name.Should().Be("Plumecreed Escort");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PlumecreedEscort_HasFlashAndFlying()
    {
        var c = PlumecreedEscortFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Flying");
    }

    [Fact]
    public void PlumecreedEscort_EtbTrigger_DeclaresTargetCreatureYouControl()
    {
        var c = PlumecreedEscortFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature you control");
        req.Intent.Should().Be(BotIntent.Protection);

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void PlumecreedEscort_Etb_GrantsHexproofToTargetCreatureEOT()
    {
        var svc = new ContinuousEffectsService();
        var c = PlumecreedEscortFactory.Create(_alice, triggers: null, continuousEffects: svc);

        var ally = new Creature("Bird Token", "{U}", 1, 1,
            subtypes: new[] { CardSubtype.Bird });
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
    public void PlumecreedEscort_Etb_OpponentControlledCreature_NoOp()
    {
        var svc = new ContinuousEffectsService();
        var c = PlumecreedEscortFactory.Create(_alice, triggers: null, continuousEffects: svc);

        var bobCreature = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        bobCreature.SetOwner(_bob);
        bobCreature.SetController(_bob);
        bobCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobCreature);
        bobCreature.ActiveEffects = svc;

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobCreature },
        });

        foreach (var e in etb.Effects) e.Execute();

        // "Target creature you control" — bob's creature fails the controller
        // legality re-check at resolution (CR 608.2b).
        svc.Compute(bobCreature).Keywords.Should().NotContain("Hexproof");
    }

    [Fact]
    public void PlumecreedEscort_Etb_TargetLeftBattlefield_NoOp()
    {
        var svc = new ContinuousEffectsService();
        var c = PlumecreedEscortFactory.Create(_alice, triggers: null, continuousEffects: svc);

        var ally = new Creature("Departed Bird", "{U}", 1, 1,
            subtypes: new[] { CardSubtype.Bird });
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
}
