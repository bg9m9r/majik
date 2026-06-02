using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Dauntless Bodyguard (Dominaria — Creature — Human Knight {W} 2/1).
///
/// Oracle (verified against Scryfall):
///   "As this creature enters, choose another creature you control.
///    Sacrifice this creature: The chosen creature gains indestructible until
///    end of turn."
///
/// Coverage:
///   * Identity: Creature — Human Knight, {W}, 2/1.
///   * NamedCardFactory dispatch.
///   * Unwired single-arg path: no chosen creature, sac ability still attached.
///   * As-enters creature choice stored + exposed via GetChosenCreature.
///   * Single sacrifice activated ability.
///   * Activating sacrifices the Bodyguard (zone move to graveyard).
///   * Resolution grants Indestructible to the CHOSEN creature only.
///   * A non-chosen creature you control is NOT granted indestructible.
///   * No-service path still sacrifices, grants nothing.
/// </summary>
[Trait("Color", "W")]
public class DauntlessBodyguardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature OwnCreature(string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DauntlessBodyguard_IsCreature_HumanKnight_2_1_AtCostW()
    {
        var c = DauntlessBodyguardFactory.Create(_alice);

        c.Name.Should().Be("Dauntless Bodyguard");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DauntlessBodyguard_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Dauntless Bodyguard", _alice);

        c.Should().NotBeNull();
        c.Name.Should().Be("Dauntless Bodyguard");
        c.Should().BeOfType<Creature>();
    }

    [Fact]
    public void DauntlessBodyguard_HasOneActivatedAbility()
    {
        var c = DauntlessBodyguardFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Unwired single-arg path
    // -----------------------------------------------------------------------

    [Fact]
    public void DauntlessBodyguard_SingleArgPath_NoChoiceResolved()
    {
        var c = DauntlessBodyguardFactory.Create(_alice);

        DauntlessBodyguardFactory.GetChosenCreature(c).Should().BeNull(
            "the single-arg path resolves no creature choice");
    }

    // -----------------------------------------------------------------------
    // As-enters choice (CR 614.12)
    // -----------------------------------------------------------------------

    [Fact]
    public void DauntlessBodyguard_StoresChosenCreature()
    {
        var ally = OwnCreature("Grizzly Bears");

        var c = DauntlessBodyguardFactory.Create(_alice, continuousEffects: null,
            creatureChooser: _ => ally);

        DauntlessBodyguardFactory.GetChosenCreature(c).Should().BeSameAs(ally);
    }

    [Fact]
    public void DauntlessBodyguard_Chooser_ReceivesBodyguardForAnotherRestriction()
    {
        // The chooser is handed the Bodyguard itself so a real chooser can
        // enforce the "another creature" restriction (CR 614.12).
        Creature? handed = null;
        var ally = OwnCreature("Grizzly Bears");

        var c = DauntlessBodyguardFactory.Create(_alice, continuousEffects: null,
            creatureChooser: self => { handed = self; return ally; });

        handed.Should().BeSameAs(c, "the chooser must be able to exclude the Bodyguard itself");
    }

    // -----------------------------------------------------------------------
    // Sacrifice ability (CR 602 / CR 702.12)
    // -----------------------------------------------------------------------

    [Fact]
    public void DauntlessBodyguard_Activate_SacrificesSelf()
    {
        var c = DauntlessBodyguardFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        c.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(c);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(c);
    }

    [Fact]
    public void DauntlessBodyguard_Activate_GrantsIndestructibleToChosenCreatureOnly()
    {
        var svc = new ContinuousEffectsService();

        var chosen = OwnCreature("Serra Angel");
        chosen.ActiveEffects = svc;

        // Another creature you control that was NOT chosen.
        var other = OwnCreature("Grizzly Bears");
        other.ActiveEffects = svc;

        var c = DauntlessBodyguardFactory.Create(_alice, svc, creatureChooser: _ => chosen);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Bodyguard sacrificed.
        c.Zone.Should().Be(ZoneType.Graveyard);

        // Chosen creature gains Indestructible (CR 702.12) until cleanup.
        svc.Compute(chosen).Keywords.Should().Contain("Indestructible");

        // The non-chosen creature is untouched — the grant is single-target.
        svc.Compute(other).Keywords.Should().NotContain("Indestructible");
    }

    [Fact]
    public void DauntlessBodyguard_Activate_NoServiceSupplied_StillSacrificesNoGrant()
    {
        var ally = OwnCreature("Grizzly Bears");

        // Wired chooser but no continuous-effects service.
        var c = DauntlessBodyguardFactory.Create(_alice, continuousEffects: null,
            creatureChooser: _ => ally);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Sacrifice still happens (closure performs the zone move directly).
        c.Zone.Should().Be(ZoneType.Graveyard);
        // No grant registers (no layers service) — chosen creature keeps no
        // keyword from a base ability.
        ally.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().NotContain("Indestructible");
    }
}
