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
/// Tests for Patchwork Banner (The Brothers' War — Artifact {3}).
///
/// Oracle (verified against Scryfall 2026-06-14):
///   "As this artifact enters, choose a creature type.
///    Creatures you control of the chosen type get +1/+1.
///    {T}: Add one mana of any color."
///
/// Coverage:
///   * Identity: Artifact, {3}.
///   * "{T}: Add one mana of any color" — five WUBRG mana abilities.
///   * Unwired single-arg path: no chosen type, no anthem.
///   * As-enters type choice stored + exposed via GetChosenType.
///   * A creature of the chosen type you control gets +1/+1 (CR 613.7c).
///   * A creature NOT of the chosen type is unaffected.
///   * An opponent's creature of the chosen type is unaffected
///     ("Creatures YOU control", CR 109.5).
///   * Patchwork Banner leaving the battlefield lifts the buff.
/// </summary>
[Trait("Color", "C")]
public class PatchworkBannerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly ContinuousEffectsService _effects = new();

    private static Func<Player, CardSubtype> Choose(CardSubtype t) => _ => t;

    private Creature OwnCreature(string name, CardSubtype subtype, int p = 2, int t = 2)
    {
        var c = new Creature(name, "{1}{G}", p, t, subtypes: new[] { subtype })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = _effects,
        };
        return c;
    }

    private Artifact BannerOnBattlefield(CardSubtype chosen)
    {
        var banner = PatchworkBannerFactory.Create(_alice, _effects, Choose(chosen));
        banner.ActiveEffects = _effects;
        banner.SetZone(ZoneType.Battlefield);
        return banner;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PatchworkBanner_IsArtifact_AtCost3()
    {
        var c = PatchworkBannerFactory.Create(_alice);

        c.Name.Should().Be("Patchwork Banner");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeFalse("Patchwork Banner is a non-creature Artifact");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // "{T}: Add one mana of any color" (CR 605.1 / CR 106.6)
    // -----------------------------------------------------------------------

    [Fact]
    public void PatchworkBanner_ManaAbilitiesCoverEveryColor()
    {
        var c = PatchworkBannerFactory.Create(_alice);

        var manaStrings = c.Abilities.OfType<ManaAbility>()
            .Select(a => a.ManaGenerated?.ToString())
            .OrderBy(s => s)
            .ToList();

        manaStrings.Should().BeEquivalentTo(new[] { "B", "G", "R", "U", "W" },
            "Patchwork Banner taps for one mana of any color.");
    }

    [Fact]
    public void PatchworkBanner_GreenManaAbility_ProducesGreenAndTaps()
    {
        var c = PatchworkBannerFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var greenAbility = c.Abilities.OfType<ManaAbility>()
            .FirstOrDefault(a => a.ManaGenerated?.ToString() == "G");

        greenAbility.Should().NotBeNull("{T}: Add {G} must be present.");
        greenAbility!.CanActivate().Should().BeTrue("the banner is untapped.");

        var mana = greenAbility.Activate();
        mana.ToString().Should().Be("G");
        c.IsTapped.Should().BeTrue("activating the {T} mana ability taps the banner.");
    }

    // -----------------------------------------------------------------------
    // As-enters choice (CR 614.12)
    // -----------------------------------------------------------------------

    [Fact]
    public void PatchworkBanner_SingleArgPath_NoChoice_NoAnthem()
    {
        var c = PatchworkBannerFactory.Create(_alice);

        PatchworkBannerFactory.GetChosenType(c).Should().BeNull(
            "the single-arg path resolves no creature-type choice");
    }

    [Fact]
    public void PatchworkBanner_StoresChosenType()
    {
        var c = PatchworkBannerFactory.Create(_alice, _effects, Choose(CardSubtype.Goblin));

        PatchworkBannerFactory.GetChosenType(c).Should().Be(CardSubtype.Goblin);
    }

    // -----------------------------------------------------------------------
    // "Creatures you control of the chosen type get +1/+1." (CR 613.7c)
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatureOfChosenType_YouControl_GetsPlusOnePlusOne()
    {
        BannerOnBattlefield(CardSubtype.Goblin);

        var goblin = OwnCreature("Goblin Piker", CardSubtype.Goblin);

        goblin.GetPower().Should().Be(3, "CR 613.7c — a Goblin you control gets +1/+1");
        goblin.GetToughness().Should().Be(3);
    }

    [Fact]
    public void CreatureOfDifferentType_IsUnaffected()
    {
        BannerOnBattlefield(CardSubtype.Goblin);

        var bear = OwnCreature("Grizzly Bears", CardSubtype.Bear);

        bear.GetPower().Should().Be(2, "a Bear is not the chosen creature type (Goblin)");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void OpponentCreatureOfChosenType_IsUnaffected()
    {
        BannerOnBattlefield(CardSubtype.Goblin);

        var oppGoblin = new Creature("Goblin Piker", "{1}{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = _effects,
        };

        oppGoblin.GetPower().Should().Be(2,
            "'Creatures YOU control' is controller-scoped (CR 109.5)");
        oppGoblin.GetToughness().Should().Be(2);
    }

    [Fact]
    public void BannerLeavesBattlefield_BuffLifts()
    {
        var banner = BannerOnBattlefield(CardSubtype.Goblin);
        var goblin = OwnCreature("Goblin Piker", CardSubtype.Goblin);

        // Baseline: buff active.
        goblin.GetPower().Should().Be(3);
        goblin.GetToughness().Should().Be(3);

        // Patchwork Banner leaves — LordStaticEffect.IsActive() short-circuits
        // when the source isn't on the battlefield (CR 613 — continuous effects
        // from a permanent stop applying when it leaves play).
        banner.SetZone(ZoneType.Graveyard);

        goblin.GetPower().Should().Be(2, "buff lifts on LTB");
        goblin.GetToughness().Should().Be(2);
    }
}
