using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ArcaneSignetFactory"/>.
///
/// Arcane Signet (Throne of Eldraine Commander, {2}). Artifact. Oracle text:
///   "{T}: Add one mana of any color in your commander's color identity."
///
/// Majik is a 1v1 / no-Commander engine, so the commander-colour-identity
/// clause degrades to a plain "{T}: Add one mana of any color" — modeled as
/// five <see cref="ManaAbility"/> instances (one per WUBRG), the same shape
/// Ornithopter of Paradise uses, but on a colourless Artifact body with no
/// printed activation cost beyond the {T} self-tap.
/// </summary>
[Trait("Color", "C")]
public class ArcaneSignetFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ArcaneSignet_Identity()
    {
        var c = ArcaneSignetFactory.Create(_alice);

        c.Name.Should().Be("Arcane Signet");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ArcaneSignet_HasFiveManaAbilities_OnePerColor()
    {
        // "{T}: Add one mana of any color" modeled as five ManaAbility
        // instances (one per WUBRG), mirroring Ornithopter of Paradise.
        var c = ArcaneSignetFactory.Create(_alice);
        var mas = c.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");
    }

    [Fact]
    public void ArcaneSignet_ManaAbilitiesCoverEveryColor()
    {
        var c = ArcaneSignetFactory.Create(_alice);

        // ManaCost.ToString() returns bare colour letters — no braces.
        var manaStrings = c.Abilities.OfType<ManaAbility>()
            .Select(a => a.ManaGenerated?.ToString())
            .OrderBy(s => s)
            .ToList();

        manaStrings.Should().BeEquivalentTo(new[] { "B", "G", "R", "U", "W" },
            "Arcane Signet taps for one mana of any color.");
    }

    [Fact]
    public void ArcaneSignet_GreenManaAbility_ProducesGreenAndTaps()
    {
        var c = ArcaneSignetFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var greenAbility = c.Abilities.OfType<ManaAbility>()
            .FirstOrDefault(a => a.ManaGenerated?.ToString() == "G");

        greenAbility.Should().NotBeNull("{T}: Add {G} must be present.");
        greenAbility!.CanActivate().Should().BeTrue("the signet is untapped.");

        var mana = greenAbility.Activate();
        mana.ToString().Should().Be("G");
        c.IsTapped.Should().BeTrue("activating the {T} mana ability taps the signet.");
    }
}
