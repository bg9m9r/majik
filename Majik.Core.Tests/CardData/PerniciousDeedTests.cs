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
/// Unit tests for <see cref="PerniciousDeedFactory"/> (Apocalypse,
/// {1}{B}{G}).
///
/// Covers:
/// - Identity (name, type, mana cost, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Activated ability shape: {X} mana cost + sacrifice cost.
/// - X=2 sweep destroys mv-≤-2 artifacts / creatures / enchantments;
///   lands and mv-3 permanents survive.
/// - X=0 sweep only destroys mv-0 permanents.
/// - Self-sacrifice on activation (CR 701.16).
/// </summary>
public class PerniciousDeedTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PerniciousDeed_Identity()
    {
        var deed = PerniciousDeedFactory.Create(_alice);

        deed.Name.Should().Be("Pernicious Deed");
        deed.ManaCost.Should().Be("{1}{B}{G}");
        deed.HasType(CardType.Enchantment).Should().BeTrue();
        deed.Owner.Should().BeSameAs(_alice);
        deed.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PerniciousDeed_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Pernicious Deed", _alice);

        card.Should().BeOfType<Enchantment>("Pernicious Deed is an Enchantment");
        card.Name.Should().Be("Pernicious Deed");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{X}, Sacrifice: sweep is surfaced for shape");
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PerniciousDeed_ActivatedAbility_HasXManaPlusSacrificeCost()
    {
        var deed = PerniciousDeedFactory.Create(_alice);

        var ability = deed.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the printed activation has a single mana cost component ({X})");

        var sac = ability.Costs.OfType<AdditionalCost>().Single();
        sac.CostType.Should().Be(AdditionalCostType.Sacrifice,
            "the second cost is sacrificing Pernicious Deed itself");
    }

    // -----------------------------------------------------------------------
    // Sweep — X = 2: mv-≤-2 artifacts/creatures/enchantments destroyed on
    // both battlefields; lands + mv-3 survive.
    // -----------------------------------------------------------------------

    [Fact]
    public void PerniciousDeed_Activate_X2_DestroysMv2OrLessAcrossAllBattlefields()
    {
        var deed = PerniciousDeedFactory.Create(
            _alice,
            xValueProvider: () => 2,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(deed);
        deed.SetZone(ZoneType.Battlefield);

        // Alice: mv-2 bear (destroy), mv-1 artifact (destroy), mv-3 giant
        // (survive), Mountain (survive — Land filter).
        var aliceBear = new Creature("Grizzly Bears", "1G", 2, 2);
        aliceBear.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(aliceBear);
        aliceBear.SetZone(ZoneType.Battlefield);

        var aliceArtifact = new Artifact("Mishra's Bauble", "0");
        aliceArtifact.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(aliceArtifact);
        aliceArtifact.SetZone(ZoneType.Battlefield);

        var aliceGiant = new Creature("Hill Giant", "3R", 3, 3);
        aliceGiant.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(aliceGiant);
        aliceGiant.SetZone(ZoneType.Battlefield);

        var mountain = NamedCardFactory.Create("Mountain", _alice);
        _alice.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        // Bob: mv-2 enchantment (destroy), mv-4 enchantment (survive).
        var bobAura = new Enchantment("Some Aura", "1B");
        bobAura.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(bobAura);
        bobAura.SetZone(ZoneType.Battlefield);

        var bobBig = new Enchantment("Big Enchantment", "2BB");
        bobBig.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(bobBig);
        bobBig.SetZone(ZoneType.Battlefield);

        var ability = deed.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        aliceBear.Zone.Should().Be(ZoneType.Graveyard,
            "Alice's mv-2 creature is destroyed (mv ≤ 2)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceBear);

        aliceArtifact.Zone.Should().Be(ZoneType.Graveyard,
            "Alice's mv-0 artifact is destroyed (mv ≤ 2)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceArtifact);

        aliceGiant.Zone.Should().Be(ZoneType.Battlefield,
            "mv-3 creature survives (mv > 2)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(aliceGiant);

        mountain.Zone.Should().Be(ZoneType.Battlefield,
            "Lands are excluded from the artifact/creature/enchantment predicate");
        _alice.Zones.Battlefield.GetCards().Should().Contain(mountain);

        bobAura.Zone.Should().Be(ZoneType.Graveyard,
            "Bob's mv-2 enchantment is destroyed (sweep crosses both battlefields)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobAura);

        bobBig.Zone.Should().Be(ZoneType.Battlefield,
            "mv-4 enchantment survives (mv > 2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBig);
    }

    // -----------------------------------------------------------------------
    // Sweep — X = 0: only mv-0 permanents are destroyed.
    // -----------------------------------------------------------------------

    [Fact]
    public void PerniciousDeed_Activate_X0_OnlyDestroysMv0Permanents()
    {
        var deed = PerniciousDeedFactory.Create(
            _alice,
            xValueProvider: () => 0,
            allPlayersResolver: null);
        _alice.Zones.Battlefield.AddCard(deed);
        deed.SetZone(ZoneType.Battlefield);

        // mv-0 artifact — destroyed.
        var bauble = new Artifact("Mishra's Bauble", "0");
        bauble.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        // mv-1 creature — survives.
        var bird = new Creature("Birds of Paradise", "G", 0, 1);
        bird.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(bird);
        bird.SetZone(ZoneType.Battlefield);

        var ability = deed.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        bauble.Zone.Should().Be(ZoneType.Graveyard,
            "mv-0 artifact destroyed at X = 0");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bauble);

        bird.Zone.Should().Be(ZoneType.Battlefield,
            "mv-1 creature survives at X = 0 (mv > 0)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bird);
    }

    // -----------------------------------------------------------------------
    // Self-sacrifice — CR 701.16.
    // -----------------------------------------------------------------------

    [Fact]
    public void PerniciousDeed_Activate_SacrificesPerniciousDeedItself()
    {
        var deed = PerniciousDeedFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(deed);
        deed.SetZone(ZoneType.Battlefield);

        var ability = deed.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        deed.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrifice cost moves Pernicious Deed to its owner's graveyard (CR 701.16)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(deed);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(deed);
    }
}
