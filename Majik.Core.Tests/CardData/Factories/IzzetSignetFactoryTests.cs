using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="IzzetSignetFactory"/> (Ravnica signet cycle).
///
/// Artifact mana rock. Oracle text (verified against Scryfall):
///   "{1}, {T}: Add {U}{R}."
///
/// Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Identity (name, Artifact type, {2} cost, owner/controller, nonbasic
///   / non-legendary, no creature type).
/// - Exactly one mana ability producing {U}{R} together (CR 605.1a).
/// - The {1} additional cost gates activation: no mana in pool => cannot
///   activate; one generic in pool => can activate.
/// - Activation pays {1} from the pool, taps the signet, and adds {U}{R}.
/// - Tap-as-cost: a tapped signet cannot activate (CR 605.1 / {T} cost).
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
public class IzzetSignetFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void IzzetSignet_IsArtifact_WithCorrectName()
    {
        var signet = (Artifact)NamedCardFactory.Create("Izzet Signet", _alice);

        signet.Should().BeOfType<Artifact>();
        signet.Name.Should().Be("Izzet Signet");
        signet.HasType(CardType.Artifact).Should().BeTrue();
        signet.HasType(CardType.Creature).Should().BeFalse();
        signet.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        signet.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        signet.Owner.Should().BeSameAs(_alice);
        signet.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_IzzetSignet()
    {
        var card = NamedCardFactory.Create("Izzet Signet", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Izzet Signet");
    }

    // -----------------------------------------------------------------------
    // Mana ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void IzzetSignet_HasExactlyOneManaAbility_ProducingUR()
    {
        var signet = (Artifact)NamedCardFactory.Create("Izzet Signet", _alice);

        var mana = signet.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "{1}, {T}: Add {U}{R} is a single mana ability");

        var produced = mana[0].ManaGenerated;
        produced.Blue.Should().Be(1);
        produced.Red.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Green.Should().Be(0);
        produced.Generic.Should().Be(0, "{U}{R} adds two coloured pips, no generic");
    }

    [Fact]
    public void IzzetSignet_HasNoActivatedOrTriggeredAbilities()
    {
        var signet = (Artifact)NamedCardFactory.Create("Izzet Signet", _alice);

        signet.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the signet's only ability is a mana ability");
        signet.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {1} cost gate (CR 605.1 — extra mana is part of activation)
    // -----------------------------------------------------------------------

    [Fact]
    public void IzzetSignet_CannotActivate_WithEmptyPool()
    {
        var signet = (Artifact)NamedCardFactory.Create("Izzet Signet", _alice);
        var mana = signet.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeFalse("the {1} additional cost cannot be paid from an empty pool");
    }

    [Fact]
    public void IzzetSignet_CanActivate_WithOneGenericInPool()
    {
        var signet = (Artifact)NamedCardFactory.Create("Izzet Signet", _alice);
        _alice.AddManaToPool(ManaCost.Parse("1"));
        var mana = signet.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
    }

    [Fact]
    public void IzzetSignet_Activation_PaysOneGeneric_TapsSelf_AndAddsUR()
    {
        var signet = (Artifact)NamedCardFactory.Create("Izzet Signet", _alice);
        // Seed pool with {1} (the signet's additional cost).
        _alice.AddManaToPool(ManaCost.Parse("1"));
        var mana = signet.Abilities.OfType<ManaAbility>().Single();
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(mana, _alice);

        // The seeded {1} was spent on the additional cost; {U}{R} added.
        _alice.ManaPool.Blue.Should().Be(1);
        _alice.ManaPool.Red.Should().Be(1);
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        _alice.ManaPool.Generic.Should().Be(0, "the seed {1} was spent on the signet's {1} cost");
        signet.IsTapped.Should().BeTrue("{T} is part of the activation cost");
    }

    // -----------------------------------------------------------------------
    // Tap-as-cost
    // -----------------------------------------------------------------------

    [Fact]
    public void IzzetSignet_CannotActivate_WhenTapped()
    {
        var signet = (Artifact)NamedCardFactory.Create("Izzet Signet", _alice);
        _alice.AddManaToPool(ManaCost.Parse("1"));
        var mana = signet.Abilities.OfType<ManaAbility>().Single();
        var activator = new ManaAbilityActivator();

        // First activation taps the signet.
        activator.ActivateManaAbility(mana, _alice);
        signet.IsTapped.Should().BeTrue();

        // Refill the pool so the rejection below is solely from the tap state.
        _alice.AddManaToPool(ManaCost.Parse("1"));
        mana.CanActivate().Should().BeFalse("a tapped permanent cannot pay the {T} cost");
    }
}
