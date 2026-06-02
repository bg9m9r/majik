using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PillarOfOriginsFactory"/> (Ixalan).
///
/// Covers:
/// - Identity (name, Artifact type, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - {T}: Add one mana of any color — 5 mana abilities (one per WUBRG),
///   mirroring Delighted Halfling / Cavern of Souls' "any color" shape.
/// - Mana abilities activatable when untapped, not when tapped.
///
/// The "as this artifact enters, choose a creature type" choice and the
/// "spend only to cast a creature spell of the chosen type" rider are v1
/// deferrals (see <see cref="PillarOfOriginsFactory"/> xmldoc) — same
/// posture as Delighted Halfling / Cavern of Souls.
/// </summary>
[Trait("Color", "C")]
public class PillarOfOriginsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PillarOfOrigins_Identity()
    {
        var pillar = (Artifact)NamedCardFactory.Create("Pillar of Origins", _alice);

        pillar.Name.Should().Be("Pillar of Origins");
        pillar.HasType(CardType.Artifact).Should().BeTrue();
        pillar.Owner.Should().BeSameAs(_alice);
        pillar.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PillarOfOrigins_ManaCostIsTwoGeneric()
    {
        var pillar = (Artifact)NamedCardFactory.Create("Pillar of Origins", _alice);

        pillar.ManaCost.Should().Be("{2}");
    }

    [Fact]
    public void PillarOfOrigins_IsNotLegendary()
    {
        var pillar = (Artifact)NamedCardFactory.Create("Pillar of Origins", _alice);

        pillar.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }
    // -----------------------------------------------------------------------
    // Mana abilities — {T}: Add one mana of any color
    // -----------------------------------------------------------------------

    [Fact]
    public void PillarOfOrigins_HasFiveManaAbilities_OnePerColor()
    {
        var pillar = (Artifact)NamedCardFactory.Create("Pillar of Origins", _alice);
        var coloured = pillar.Abilities.OfType<ManaAbility>()
            .Where(m =>
                m.ManaGenerated.White == 1 ||
                m.ManaGenerated.Blue == 1 ||
                m.ManaGenerated.Black == 1 ||
                m.ManaGenerated.Red == 1 ||
                m.ManaGenerated.Green == 1)
            .ToList();

        coloured.Should().HaveCount(5,
            "{T}: Add one mana of any color — one ManaAbility per WUBRG");
        coloured.Should().ContainSingle(m => m.ManaGenerated.White == 1);
        coloured.Should().ContainSingle(m => m.ManaGenerated.Blue == 1);
        coloured.Should().ContainSingle(m => m.ManaGenerated.Black == 1);
        coloured.Should().ContainSingle(m => m.ManaGenerated.Red == 1);
        coloured.Should().ContainSingle(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void PillarOfOrigins_AllManaAbilities_AreActivatable_WhenUntapped()
    {
        var pillar = (Artifact)NamedCardFactory.Create("Pillar of Origins", _alice);

        foreach (var m in pillar.Abilities.OfType<ManaAbility>())
        {
            m.CanActivate().Should().BeTrue(
                "an untapped artifact's mana abilities should be activatable");
        }
    }

    [Fact]
    public void PillarOfOrigins_ManaAbilities_NotActivatable_WhenTapped()
    {
        var pillar = (Artifact)NamedCardFactory.Create("Pillar of Origins", _alice);
        pillar.Tap();

        foreach (var m in pillar.Abilities.OfType<ManaAbility>())
        {
            m.CanActivate().Should().BeFalse(
                "a tapped artifact's {T}-cost mana abilities are not activatable");
        }
    }

    [Fact]
    public void PillarOfOrigins_HasNoTriggeredAbilities()
    {
        var pillar = (Artifact)NamedCardFactory.Create("Pillar of Origins", _alice);

        pillar.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "v1 defers the as-enters type choice — no triggered abilities");
    }

    [Fact]
    public void PillarOfOrigins_HasNoNonManaActivatedAbilities()
    {
        var pillar = (Artifact)NamedCardFactory.Create("Pillar of Origins", _alice);

        pillar.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Pillar of Origins has only mana abilities");
    }
}
