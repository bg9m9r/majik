using Majik.Core.CardData;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Card-level tests for the four cards closed by the pump_target /
/// grant_keyword_until_eot_target / becomes_artifact_target verbs, loaded from
/// their embedded JSON defs through <see cref="NamedCardFactory"/> (the same
/// dispatch the production repository uses). Oracle text verified via Scryfall.
/// Verb resolution + EOT expiry is covered end-to-end in
/// <see cref="Definitions.JsonPumpKeywordArtifactTargetEffectsTests"/>.
/// </summary>
public class PumpKeywordArtifactCardsTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Okina, Temple to the Grandfathers ──────────────────────────────────────
    // "{T}: Add {G}.  {G}, {T}: Target legendary creature gets +1/+1 until EOT."

    [Fact]
    public void Okina_IsLegendaryLand_WithGreenManaAbility()
    {
        var okina = (Land)NamedCardFactory.Create("Okina, Temple to the Grandfathers", _alice);

        okina.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        okina.HasType(CardType.Land).Should().BeTrue();
        var mana = okina.Abilities.OfType<ManaAbility>().Single();
        mana.ManaGenerated.Green.Should().Be(1, "Okina taps for {G}");
    }

    [Fact]
    public void Okina_PumpAbility_CostsGreenAndTap_TargetsLegendaryCreature()
    {
        var okina = (Land)NamedCardFactory.Create("Okina, Temple to the Grandfathers", _alice);
        var ability = okina.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Single().Cost.Green.Should().Be(1, "the {G} cost");
        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle("the {T} symbol");
        ability.TargetRequests.Should().ContainSingle();
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── Soaring Seacliff ───────────────────────────────────────────────────────
    // "{T}: Add {U}.  When this land enters, target creature gains flying until EOT."

    [Fact]
    public void SoaringSeacliff_IsLand_TapsForBlue()
    {
        var seacliff = (Land)NamedCardFactory.Create("Soaring Seacliff", _alice);

        seacliff.HasType(CardType.Land).Should().BeTrue();
        seacliff.Abilities.OfType<ManaAbility>().Single().ManaGenerated.Blue.Should().Be(1);
    }

    [Fact]
    public void SoaringSeacliff_HasEnterTrigger_GrantingFlyingTarget()
    {
        var seacliff = (Land)NamedCardFactory.Create("Soaring Seacliff", _alice);

        var triggered = seacliff.Abilities.OfType<TriggeredAbility>().Single();
        triggered.TargetRequests.Should().ContainSingle(
            "the ETB 'target creature gains flying' declares one 1..1 target");
    }

    // ── Sunhome, Fortress of the Legion ────────────────────────────────────────
    // "{T}: Add {C}.  {2}{R}{W}, {T}: Target creature gains double strike until EOT."

    [Fact]
    public void Sunhome_IsLand_TapsForColorless()
    {
        var sunhome = (Land)NamedCardFactory.Create("Sunhome, Fortress of the Legion", _alice);

        sunhome.HasType(CardType.Land).Should().BeTrue();
        sunhome.Abilities.OfType<ManaAbility>().Single().ManaGenerated.Generic.Should().Be(1,
            "{C} produces one colorless");
    }

    [Fact]
    public void Sunhome_DoubleStrikeAbility_Costs2RW_AndTap_TargetsCreature()
    {
        var sunhome = (Land)NamedCardFactory.Create("Sunhome, Fortress of the Legion", _alice);
        var ability = sunhome.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ability.Costs.OfType<ManaCostCost>().Single().Cost;

        cost.Generic.Should().Be(2, "the {2}");
        cost.Red.Should().Be(1);
        cost.White.Should().Be(1);
        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle("the {T} symbol");
        ability.TargetRequests.Should().ContainSingle();
    }

    // ── Liquimetal Torque ──────────────────────────────────────────────────────
    // "{T}: Add {C}.  {T}: Target nonland permanent becomes an artifact until EOT."

    [Fact]
    public void LiquimetalTorque_IsArtifact_TapsForColorless()
    {
        var torque = (Artifact)NamedCardFactory.Create("Liquimetal Torque", _alice);

        torque.HasType(CardType.Artifact).Should().BeTrue();
        torque.Abilities.OfType<ManaAbility>().Single().ManaGenerated.Generic.Should().Be(1);
    }

    [Fact]
    public void LiquimetalTorque_BecomesArtifactAbility_TapOnly_TargetsNonlandPermanent()
    {
        var torque = (Artifact)NamedCardFactory.Create("Liquimetal Torque", _alice);
        var ability = torque.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle("only a {T} cost");
        ability.Costs.OfType<ManaCostCost>().Should().BeEmpty("no mana in the activation cost");
        ability.TargetRequests.Should().ContainSingle();
        ability.TargetRequests[0].MinTargets.Should().Be(1);
    }
}
