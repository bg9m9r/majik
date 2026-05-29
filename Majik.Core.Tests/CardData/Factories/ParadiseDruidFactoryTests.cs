using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ParadiseDruidFactory"/>.
///
/// Covers:
/// - Identity (name, {1}{G} cost, Elf + Druid subtypes, 2/1, owner/controller).
/// - NamedCardFactory dispatch.
/// - Five any-colour mana abilities (one per WUBRG).
/// - Conditional hexproof (CR 702.11): untapped → untargetable by opponents;
///   tapped → legal target; controller can always target either way.
/// </summary>
public class ParadiseDruidFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void ParadiseDruid_Identity()
    {
        var c = ParadiseDruidFactory.Create(_alice);

        c.Name.Should().Be("Paradise Druid");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ParadiseDruid_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Paradise Druid", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Paradise Druid");
        ((Creature)c).HasSubtype(CardSubtype.Elf).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Druid).Should().BeTrue();
    }

    // ── Mana abilities ─────────────────────────────────────────────────────

    [Fact]
    public void ParadiseDruid_HasFiveManaAbilities_OnePerColor()
    {
        var c = ParadiseDruidFactory.Create(_alice);

        var manaAbilities = c.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(5,
            "\"Add one mana of any color\" is modeled as one ManaAbility per WUBRG colour.");
    }

    [Fact]
    public void ParadiseDruid_ManaAbilitiesCoverWubrg()
    {
        var c = ParadiseDruidFactory.Create(_alice);

        // ManaCost.ToString() returns bare letters (no braces).
        var manaStrings = c.Abilities.OfType<ManaAbility>()
            .Select(a => a.ManaGenerated?.ToString())
            .OrderBy(s => s)
            .ToList();

        manaStrings.Should().BeEquivalentTo(new[] { "B", "G", "R", "U", "W" },
            "Paradise Druid can tap for any single colour of mana.");
    }

    [Fact]
    public void ParadiseDruid_GreenManaAbility_ProducesGreenAndTaps()
    {
        var c = ParadiseDruidFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        c.ClearSummoningSickness();

        var green = c.Abilities.OfType<ManaAbility>()
            .FirstOrDefault(a => a.ManaGenerated?.ToString() == "G");

        green.Should().NotBeNull("{T}: Add {G} must be present.");
        green!.CanActivate().Should().BeTrue("creature is untapped.");

        var mana = green.Activate();
        mana.ToString().Should().Be("G");
        c.IsTapped.Should().BeTrue("activating the {T} mana ability taps Paradise Druid.");
    }

    // ── Conditional hexproof (CR 702.11) ───────────────────────────────────

    private static TargetSpec CreatureTargetSpec() =>
        new TargetSpec("target creature").Creatures();

    [Fact]
    public void ParadiseDruid_Untapped_IsHexproofFromOpponents()
    {
        var svc = new ContinuousEffectsService();
        var druid = ParadiseDruidFactory.Create(_alice, svc);
        druid.SetZone(ZoneType.Battlefield);

        // CR 702.11 — an opponent (Bob) can't target an untapped Paradise Druid.
        TargetLegality.IsLegal(CreatureTargetSpec(), druid, _bob)
            .Should().BeFalse("an untapped Paradise Druid has hexproof from opponents.");
    }

    [Fact]
    public void ParadiseDruid_Untapped_ControllerCanStillTarget()
    {
        var svc = new ContinuousEffectsService();
        var druid = ParadiseDruidFactory.Create(_alice, svc);
        druid.SetZone(ZoneType.Battlefield);

        // CR 702.11 — hexproof only blocks OPPONENTS' spells/abilities.
        TargetLegality.IsLegal(CreatureTargetSpec(), druid, _alice)
            .Should().BeTrue("hexproof doesn't stop the controller from targeting.");
    }

    [Fact]
    public void ParadiseDruid_Tapped_IsNotHexproof()
    {
        var svc = new ContinuousEffectsService();
        var druid = ParadiseDruidFactory.Create(_alice, svc);
        druid.SetZone(ZoneType.Battlefield);

        druid.Tap();

        // "...as long as it's untapped." A tapped druid loses hexproof and is
        // a legal target for opponents.
        TargetLegality.IsLegal(CreatureTargetSpec(), druid, _bob)
            .Should().BeTrue("a tapped Paradise Druid has lost hexproof and can be targeted.");
    }

    [Fact]
    public void ParadiseDruid_HexproofToggles_WithTapState()
    {
        var svc = new ContinuousEffectsService();
        var druid = ParadiseDruidFactory.Create(_alice, svc);
        druid.SetZone(ZoneType.Battlefield);

        // Untapped → hexproof.
        TargetLegality.IsLegal(CreatureTargetSpec(), druid, _bob).Should().BeFalse();

        // Tap → no hexproof.
        druid.Tap();
        TargetLegality.IsLegal(CreatureTargetSpec(), druid, _bob).Should().BeTrue();

        // Untap → hexproof restored (the continuous effect is re-evaluated).
        druid.Untap();
        TargetLegality.IsLegal(CreatureTargetSpec(), druid, _bob).Should().BeFalse();
    }
}
