using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PhlageFactory"/>.
///
/// Covers:
/// - Identity: name, type Creature, P/T 4/4, Legendary, Elemental + Incarnation
///   subtypes, mana cost.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB trigger: 3 damage to chosen target + 3 life to controller
///   (CR 119 / CR 119.3) — exercised against Player and Creature targets,
///   plus a no-target fizzle case.
///
/// Escape (CR 702.143) is deferred — same gap as <see cref="UroTitanFactory"/>.
/// </summary>
public class PhlageTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ─────────────────────────────────────────────────────────────────────
    // Identity + dispatch
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Phlage_Identity()
    {
        var c = PhlageFactory.Create(_alice);

        c.Name.Should().Be("Phlage, Titan of Fire's Fury");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "CR 205.4 — Phlage is a Legendary creature");
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{R}{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Phlage_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Phlage, Titan of Fire's Fury", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Phlage, Titan of Fire's Fury");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{R}{W}");
    }

    [Fact]
    public void Phlage_HasSingleEtbTriggeredAbility_WithAnyTargetRequest()
    {
        var c = PhlageFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "only the ETB damage+life trigger is wired in v1");

        var etb = triggers.Single();
        etb.TargetRequests.Should().HaveCount(1);
        etb.TargetRequests[0].MinTargets.Should().Be(1);
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────
    // ETB resolution — CR 119 (damage) + CR 119.3 (life gain)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Phlage_Etb_DealsThreeDamageToPlayer_AndControllerGainsThreeLife()
    {
        var phlage = PhlageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(phlage);
        phlage.SetZone(ZoneType.Battlefield);

        var bobStart = _bob.LifeTotal;
        var aliceStart = _alice.LifeTotal;

        var etb = phlage.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(bobStart - PhlageFactory.DamageAmount,
            "CR 119 — 3 damage to the chosen player");
        _alice.LifeTotal.Should().Be(aliceStart + PhlageFactory.LifeGainAmount,
            "CR 119.3 — controller gains 3 life as part of the same resolution");
    }

    [Fact]
    public void Phlage_Etb_DealsThreeDamageToCreature_AndControllerGainsThreeLife()
    {
        var phlage = PhlageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(phlage);
        phlage.SetZone(ZoneType.Battlefield);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Battlefield);

        var aliceStart = _alice.LifeTotal;

        var etb = phlage.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobBear },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        // 3 damage on a 2-toughness creature — damage exceeds toughness;
        // lethal-damage SBA (CR 704.5g) is checked elsewhere — here we
        // assert the damage was applied to the creature.
        bobBear.Damage.Should().Be(PhlageFactory.DamageAmount,
            "CR 119 — 3 damage marked on the creature");
        _alice.LifeTotal.Should().Be(aliceStart + PhlageFactory.LifeGainAmount,
            "CR 119.3 — controller gains 3 life as part of the same resolution");
    }

    [Fact]
    public void Phlage_Etb_NoTargetChosen_FullSpellFizzles_NoLifeGained()
    {
        // CR 608.2b — Phlage's ETB has exactly one target (the damage
        // target). If no target is chosen / the target is illegal at
        // resolution, the whole ability does nothing — neither damage
        // nor lifegain happens.
        var phlage = PhlageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(phlage);
        phlage.SetZone(ZoneType.Battlefield);

        var aliceStart = _alice.LifeTotal;
        var bobStart = _bob.LifeTotal;

        var etb = phlage.Abilities.OfType<TriggeredAbility>().Single();
        // No SetChosenTargets call — ChosenTargets stays empty.

        foreach (var effect in etb.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(aliceStart, "no target → full fizzle, no life gained");
        _bob.LifeTotal.Should().Be(bobStart, "no target → no damage to anyone");
    }

    // -----------------------------------------------------------------
    // BuildAlternativeCost — CR 702.138 Escape factory exposure
    // -----------------------------------------------------------------

    [Fact]
    public void Phlage_BuildAlternativeCost_ReturnsEscapeAltCost_WithPrintedShape()
    {
        var cost = PhlageFactory.BuildAlternativeCost();

        cost.ExileFromGraveyardCount.Should().Be(5,
            "Phlage's printed Escape rider exiles 5 OTHER graveyard cards");
        // {R}{R}{W}{W} = 2 red + 2 white.
        cost.AlternativeManaCost.Generic.Should().Be(0);
        cost.AlternativeManaCost.Red.Should().Be(2);
        cost.AlternativeManaCost.White.Should().Be(2);
    }
}
