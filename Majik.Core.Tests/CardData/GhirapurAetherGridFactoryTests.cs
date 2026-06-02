using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GhirapurAetherGridFactory"/> (Kaladesh).
///
/// Oracle (verified against Scryfall):
///   "Tap two untapped artifacts you control: This enchantment deals 1
///    damage to any target."
///
/// Covers:
/// - Identity ({2}{R}, Enchantment, NOT an artifact).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - "Tap two untapped artifacts" activated ability shape
///   (<see cref="TapTwoUntappedArtifactsCost"/>, single any-target).
/// - Cost can't be paid with fewer than two untapped artifacts.
/// - Resolution deals 1 damage to a player / creature / planeswalker
///   (CR 119 / 306.7), and no-ops on an untargeted resolution (CR 608.2b).
/// </summary>
public class GhirapurAetherGridFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GhirapurAetherGrid_Identity_Enchantment()
    {
        var card = GhirapurAetherGridFactory.Create(_alice);

        card.Name.Should().Be("Ghirapur Aether Grid");
        card.ManaCost.ToString().Should().Be("{2}{R}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeFalse(
            "Ghirapur Aether Grid is an Enchantment, not an artifact — "
            + "it cannot pay its own tap-two-artifacts cost");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GhirapurAetherGrid_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Ghirapur Aether Grid", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Ghirapur Aether Grid");
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GhirapurAetherGrid_HasExactlyOneActivatedAbility()
    {
        var card = GhirapurAetherGridFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the tap-two-artifacts: 1 damage to any target activation");
    }

    [Fact]
    public void GhirapurAetherGrid_ActivatedAbility_HasTapTwoArtifactsCost()
    {
        var card = GhirapurAetherGridFactory.Create(_alice);
        var activated = card.Abilities.OfType<ActivatedAbility>().Single();

        var cost = activated.Costs.OfType<TapTwoUntappedArtifactsCost>().Single();
        cost.Count.Should().Be(2, "Tap two untapped artifacts you control");
    }

    [Fact]
    public void GhirapurAetherGrid_ActivatedAbility_HasSingleAnyTarget()
    {
        var card = GhirapurAetherGridFactory.Create(_alice);
        var activated = card.Abilities.OfType<ActivatedAbility>().Single();

        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Cost payability
    // -----------------------------------------------------------------------

    [Fact]
    public void GhirapurAetherGrid_Cost_CannotPay_WithOneArtifact()
    {
        var artifact = MakeArtifact("Ornithopter", _alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var card = GhirapurAetherGridFactory.Create(_alice);
        var cost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<TapTwoUntappedArtifactsCost>().Single();

        cost.CanPay(_alice).Should().BeFalse(
            "only one untapped artifact — can't pay tap-two");
    }

    [Fact]
    public void GhirapurAetherGrid_Cost_CanPay_WithTwoArtifacts_AndTapsBoth()
    {
        var a1 = MakeArtifact("Ornithopter", _alice);
        var a2 = MakeArtifact("Memnite", _alice);
        foreach (var a in new[] { a1, a2 })
        {
            _alice.Zones.Battlefield.AddCard(a);
            a.SetZone(ZoneType.Battlefield);
        }

        var card = GhirapurAetherGridFactory.Create(_alice);
        var cost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<TapTwoUntappedArtifactsCost>().Single();

        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice);

        a1.IsTapped.Should().BeTrue("paying the cost taps the chosen artifacts");
        a2.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Resolution — 1 damage to any target (CR 119 / 306.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void GhirapurAetherGrid_Resolve_AgainstPlayer_Deals1Damage()
    {
        var card = GhirapurAetherGridFactory.Create(_alice);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        var bobStart = _bob.LifeTotal;
        foreach (var e in ability.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobStart - 1, "1 damage to the targeted player");
    }

    [Fact]
    public void GhirapurAetherGrid_Resolve_AgainstCreature_Deals1Damage()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var card = GhirapurAetherGridFactory.Create(_alice);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        foreach (var e in ability.Effects) e.Execute();

        bear.Damage.Should().Be(1, "1 damage marked on the targeted creature");
    }

    [Fact]
    public void GhirapurAetherGrid_Resolve_AgainstPlaneswalker_RemovesOneLoyalty()
    {
        // CR 306.7 — damage to a planeswalker removes that many loyalty.
        var pw = new Planeswalker(
            "Liliana of the Veil",
            "{1}{B}{B}",
            startingLoyalty: 3,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Liliana });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var card = GhirapurAetherGridFactory.Create(_alice);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        foreach (var e in ability.Effects) e.Execute();

        pw.Loyalty.Should().Be(2, "1 loyalty removed (3 - 1)");
    }

    [Fact]
    public void GhirapurAetherGrid_Resolve_NoTarget_NoOp()
    {
        var card = GhirapurAetherGridFactory.Create(_alice);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        // No SetChosenTargets — ChosenTargets is empty.

        var bobStart = _bob.LifeTotal;
        var resolve = () => { foreach (var e in ability.Effects) e.Execute(); };

        resolve.Should().NotThrow("an untargeted resolution is a silent no-op (CR 608.2b)");
        _bob.LifeTotal.Should().Be(bobStart);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Artifact MakeArtifact(string name, Player owner)
    {
        var a = new Artifact(name, "{0}");
        a.SetOwner(owner);
        a.SetController(owner);
        return a;
    }
}
