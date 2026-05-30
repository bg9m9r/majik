using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="VoltaicKeyFactory"/>.
///
/// Voltaic Key (Urza's Legacy / Mirrodin) — Artifact, {1}.
/// Oracle text:
///   "{1}, {T}: Untap target artifact."
///
/// Covers:
/// - Card identity (name, Artifact type, {1} mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - {1}, {T} activated ability cost composition (ManaCostCost({1}) + Tap).
/// - The untap-target effect resolves without throwing (stub — targeting
///   system not wired yet; mirrors Minamo's untap_target_stub).
/// </summary>
public class VoltaicKeyTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void VoltaicKey_Identity()
    {
        var key = VoltaicKeyFactory.Create(_alice);

        key.Name.Should().Be("Voltaic Key");
        key.HasType(CardType.Artifact).Should().BeTrue();
        key.ManaCost.Should().Be("{1}");
        key.Owner.Should().BeSameAs(_alice);
        key.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VoltaicKey_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Voltaic Key", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Voltaic Key");
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: Untap target artifact — activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void VoltaicKey_HasExactlyOneActivatedAbility()
    {
        var key = VoltaicKeyFactory.Create(_alice);

        key.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {1}, {T}: Untap target artifact ability");
    }

    [Fact]
    public void VoltaicKey_UntapAbility_HasNoManaAbility()
    {
        var key = VoltaicKeyFactory.Create(_alice);

        key.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "Voltaic Key has no mana ability — its only ability untaps an artifact");
    }

    [Fact]
    public void VoltaicKey_UntapAbility_HasManaCostCost()
    {
        var key = VoltaicKeyFactory.Create(_alice);
        var ability = key.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1);
    }

    [Fact]
    public void VoltaicKey_UntapAbility_ManaCostIsGeneric1()
    {
        var key = VoltaicKeyFactory.Create(_alice);
        var ability = key.Abilities.OfType<ActivatedAbility>().Single();
        var manaCost = ability.Costs.OfType<ManaCostCost>().Single().Cost;

        manaCost.Generic.Should().Be(1, "the {1} component");
        manaCost.Blue.Should().Be(0, "no colored component");
        manaCost.White.Should().Be(0, "no colored component");
    }

    [Fact]
    public void VoltaicKey_UntapAbility_HasTapSelfCost()
    {
        var key = VoltaicKeyFactory.Create(_alice);
        var ability = key.Abilities.OfType<ActivatedAbility>().Single();

        // The {T} symbol is built as an AdditionalCost.Tap on the source.
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle("the {T} symbol composes a tap-self additional cost");
    }

    [Fact]
    public void VoltaicKey_UntapAbility_HasExactlyTwoCosts()
    {
        var key = VoltaicKeyFactory.Create(_alice);
        var ability = key.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2, "ManaCostCost({1}) + tap-self");
    }

    // -----------------------------------------------------------------------
    // Untap-target effect resolve (stub — targeting not wired yet)
    // -----------------------------------------------------------------------

    [Fact]
    public void VoltaicKey_UntapAbility_ResolvesWithoutThrowing()
    {
        var key = VoltaicKeyFactory.Create(_alice);
        var ability = key.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => ability.Resolve();

        act.Should().NotThrow("v1 untap-target effect is a no-op stub");
    }
}
