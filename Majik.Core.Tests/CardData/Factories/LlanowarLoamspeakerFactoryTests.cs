using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LlanowarLoamspeakerFactory"/> (Dominaria United,
/// {1}{G}).
///
/// Creature — Elf Druid 1/3. Oracle text:
///   "{T}: Add one mana of any color.
///    {T}: Target land you control becomes a 3/3 Elemental creature with haste
///    until end of turn. It's still a land. Activate only as a sorcery."
/// </summary>
[Trait("Color", "G")]
public class LlanowarLoamspeakerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity()
    {
        var loamspeaker = LlanowarLoamspeakerFactory.Create(_alice);

        loamspeaker.Name.Should().Be("Llanowar Loamspeaker");
        loamspeaker.ManaCost.Should().Be("{1}{G}");
        loamspeaker.HasType(CardType.Creature).Should().BeTrue();
        loamspeaker.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        loamspeaker.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        loamspeaker.BasePower.Should().Be(1);
        loamspeaker.BaseToughness.Should().Be(3);
        loamspeaker.Owner.Should().BeSameAs(_alice);
        loamspeaker.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HasFiveColorManaAbility_OnePerWubrg()
    {
        var loamspeaker = LlanowarLoamspeakerFactory.Create(_alice);

        // "{T}: Add one mana of any color." — modeled as five ManaAbility
        // instances (one per WUBRG), the Birds of Paradise / Paradise Druid
        // pattern. The mana picker satisfies any single colour pip via these.
        var manaAbilities = loamspeaker.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(5, "one ManaAbility per WUBRG colour");

        // Each colour is produced exactly once across the five abilities.
        manaAbilities.Count(a => a.ManaGenerated.White == 1).Should().Be(1);
        manaAbilities.Count(a => a.ManaGenerated.Blue == 1).Should().Be(1);
        manaAbilities.Count(a => a.ManaGenerated.Black == 1).Should().Be(1);
        manaAbilities.Count(a => a.ManaGenerated.Red == 1).Should().Be(1);
        manaAbilities.Count(a => a.ManaGenerated.Green == 1).Should().Be(1);
    }

    [Fact]
    public void HasActivatedAnimateAbility_TapCost_SorcerySpeed()
    {
        var loamspeaker = LlanowarLoamspeakerFactory.Create(_alice);

        // The non-mana activated ability is the land-animate ability. (Mana
        // abilities are ManaAbility, not ActivatedAbility, so this Single()
        // isolates the animate ability.)
        var ability = loamspeaker.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.Should().ContainSingle("the animate ability's only cost is {T}");
        ability.IsSorcerySpeed.Should().BeTrue(
            "the oracle text says \"Activate only as a sorcery\" (CR 605 / CR 307.1)");
    }

    [Fact]
    public void ActivatedAbility_AnimatesTargetLand_To3x3ElementalWithHaste_StillALand()
    {
        var effects = new ContinuousEffectsService();
        var land = new Land("Forest") { Owner = _alice };
        land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land);

        var loamspeaker = LlanowarLoamspeakerFactory.Create(
            _alice,
            continuousEffects: effects,
            targetLandResolver: () => new List<Land> { land });
        _alice.Zones.Battlefield.AddCard(loamspeaker);

        var ability = loamspeaker.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land, "CR 613.1c — it's still a land");
        chars.Types.Should().Contain(CardType.Creature, "animated into a creature");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental, "becomes an Elemental");
        chars.Keywords.Should().Contain("Haste");
    }

    [Fact]
    public void ActivatedAbility_SetsBasePT_To3x3_ExpiringEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var land = new Land("Forest") { Owner = _alice };
        land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land);

        var loamspeaker = LlanowarLoamspeakerFactory.Create(
            _alice,
            continuousEffects: effects,
            targetLandResolver: () => new List<Land> { land });
        _alice.Zones.Battlefield.AddCard(loamspeaker);

        var ability = loamspeaker.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        var pt = RegisteredEffects(effects).OfType<ManlandCycleBecomesPTEffect>().Single();
        pt.NewPower.Should().Be(3, "the land becomes a fixed 3/3 (CR 613.7b)");
        pt.NewToughness.Should().Be(3);
        pt.ExpiresAtEndOfTurn.Should().BeTrue("the animation lasts until end of turn (CR 514.2)");
    }

    [Fact]
    public void ActivatedAbility_OnlyAnimatesLandsTheControllerControls()
    {
        var effects = new ContinuousEffectsService();
        var bob = new Player("Bob", 20);

        // A land Bob controls — "target land you control" (CR 115.4) must skip it.
        var bobsLand = new Land("Forest") { Owner = bob };
        bobsLand.SetController(bob);
        bob.Zones.Battlefield.AddCard(bobsLand);

        var loamspeaker = LlanowarLoamspeakerFactory.Create(
            _alice,
            continuousEffects: effects,
            targetLandResolver: () => new List<Land> { bobsLand });
        _alice.Zones.Battlefield.AddCard(loamspeaker);

        var ability = loamspeaker.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        RegisteredEffects(effects).Should().BeEmpty(
            "the only candidate land is controlled by Bob, not the activating controller");
    }

    [Fact]
    public void ActivatedAbility_NoResolver_NoOps()
    {
        var effects = new ContinuousEffectsService();
        var loamspeaker = LlanowarLoamspeakerFactory.Create(_alice, effects, targetLandResolver: null);

        var ability = loamspeaker.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        RegisteredEffects(effects).Should().BeEmpty("no target land supplied — clean no-op");
    }

    private static IEnumerable<ContinuousEffect> RegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (IEnumerable<ContinuousEffect>)field!.GetValue(svc)!;
    }
}
