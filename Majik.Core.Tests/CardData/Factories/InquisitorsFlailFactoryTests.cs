using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="InquisitorsFlailFactory"/>.
///
/// Card: Inquisitor's Flail — Artifact — Equipment {2} (Innistrad).
///   "If equipped creature would deal combat damage, it deals double that
///    damage instead."
///   "If a source would deal combat damage to equipped creature, it deals
///    double that damage instead."
///   "Equip {2}."
///
/// v1 ships the structural shape + Equip {2}. The two damage-doubling
/// replacements are deferred pending an <c>IsCombatDamage</c> flag on
/// <c>DamageIntent</c>.
/// </summary>
public class InquisitorsFlailFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void InquisitorsFlail_Identity()
    {
        var c = InquisitorsFlailFactory.Create(_alice);

        c.Name.Should().Be("Inquisitor's Flail");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Inquisitor's Flail is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void InquisitorsFlail_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Inquisitor's Flail", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Inquisitor's Flail");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    [Fact]
    public void InquisitorsFlail_EquipAbility_HasGenericTwoCost()
    {
        var c = InquisitorsFlailFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    [Fact]
    public void InquisitorsFlail_HasOnlyEquipAbility_v1()
    {
        // v1 ships the structural shape only — the two damage-doubling
        // replacements are deferred (no IsCombat flag on DamageIntent
        // today). Only the Equip activated ability is attached.
        var c = InquisitorsFlailFactory.Create(_alice);

        c.Abilities.OfType<EquipActivatedAbility>().Should().HaveCount(1);
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Flail has no printed triggered ability");
        c.Abilities.OfType<StaticAbility>().Should().BeEmpty(
            "the damage-doubling statics are deferred");
    }
}
