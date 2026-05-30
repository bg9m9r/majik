using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="OrnithopterOfParadiseFactory"/>.
///
/// Ornithopter of Paradise (March of the Machine, {2}).
/// Artifact Creature — Thopter 0/2. Oracle text:
///   "Flying
///    {T}: Add one mana of any color."
///
/// Twin of Birds of Paradise (Flying + any-colour mana dork) but on an
/// Artifact Creature — Thopter 0/2 body, mirroring the Ornithopter chassis.
/// </summary>
public class OrnithopterOfParadiseFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void OrnithopterOfParadise_Identity()
    {
        var c = OrnithopterOfParadiseFactory.Create(_alice);

        c.Name.Should().Be("Ornithopter of Paradise");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue("it is an Artifact Creature");
        c.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OrnithopterOfParadise_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Ornithopter of Paradise", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Ornithopter of Paradise");
        ((Creature)c).HasSubtype(CardSubtype.Thopter).Should().BeTrue();
    }

    [Fact]
    public void OrnithopterOfParadise_HasFlying()
    {
        // CR 702.9 — Flying.
        var c = OrnithopterOfParadiseFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue("Ornithopter of Paradise has Flying");
    }

    [Fact]
    public void OrnithopterOfParadise_HasFiveManaAbilities_OnePerColor()
    {
        // "{T}: Add one mana of any color." modeled as five ManaAbility
        // instances (one per WUBRG), mirroring Birds of Paradise.
        var c = OrnithopterOfParadiseFactory.Create(_alice);
        var mas = c.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");
    }

    [Fact]
    public void OrnithopterOfParadise_ManaAbilitiesCoverEveryColor()
    {
        var c = OrnithopterOfParadiseFactory.Create(_alice);

        // ManaCost.ToString() returns bare colour letters — no braces.
        var manaStrings = c.Abilities.OfType<ManaAbility>()
            .Select(a => a.ManaGenerated?.ToString())
            .OrderBy(s => s)
            .ToList();

        manaStrings.Should().BeEquivalentTo(new[] { "B", "G", "R", "U", "W" },
            "Ornithopter of Paradise taps for one mana of any color.");
    }

    [Fact]
    public void OrnithopterOfParadise_GreenManaAbility_ProducesGreenAndTaps()
    {
        var c = OrnithopterOfParadiseFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        // CR 302.6 — clear summoning sickness so we exercise mana production
        // rather than the {T} sickness gate.
        c.ClearSummoningSickness();

        var greenAbility = c.Abilities.OfType<ManaAbility>()
            .FirstOrDefault(a => a.ManaGenerated?.ToString() == "G");

        greenAbility.Should().NotBeNull("{T}: Add {G} must be present.");
        greenAbility!.CanActivate().Should().BeTrue("creature is untapped.");

        var mana = greenAbility.Activate();
        mana.ToString().Should().Be("G");
        c.IsTapped.Should().BeTrue("activating the {T} mana ability taps the Thopter.");
    }
}
