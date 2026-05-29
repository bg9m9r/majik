using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SylvanCaryatidFactory"/>.
///
/// Sylvan Caryatid (Theros, {1}{G}). Creature — Plant 0/3.
/// Oracle text (Scryfall):
///   "Defender, hexproof (This creature can't be the target of spells or
///    abilities your opponents control.)
///    {T}: Add one mana of any color."
///
/// Covers:
/// - Identity (name, {1}{G} cost, Plant subtype, 0/3, owner/controller).
/// - NamedCardFactory dispatch.
/// - Five any-colour mana abilities (one per WUBRG).
/// - Defender (CR 702.3) — can't attack.
/// - Hexproof (CR 702.11) — unconditional; opponents can't target, controller
///   can; unaffected by tap state (unlike Paradise Druid).
/// </summary>
public class SylvanCaryatidFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void SylvanCaryatid_Identity()
    {
        var c = SylvanCaryatidFactory.Create(_alice);

        c.Name.Should().Be("Sylvan Caryatid");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SylvanCaryatid_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sylvan Caryatid", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Sylvan Caryatid");
        ((Creature)c).HasSubtype(CardSubtype.Plant).Should().BeTrue();
    }

    // ── Mana abilities ─────────────────────────────────────────────────────

    [Fact]
    public void SylvanCaryatid_HasFiveManaAbilities_OnePerColor()
    {
        var c = SylvanCaryatidFactory.Create(_alice);

        var manaAbilities = c.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(5,
            "\"Add one mana of any color\" is modeled as one ManaAbility per WUBRG colour.");
    }

    [Fact]
    public void SylvanCaryatid_ManaAbilitiesCoverWubrg()
    {
        var c = SylvanCaryatidFactory.Create(_alice);

        var manaStrings = c.Abilities.OfType<ManaAbility>()
            .Select(a => a.ManaGenerated?.ToString())
            .OrderBy(s => s)
            .ToList();

        manaStrings.Should().BeEquivalentTo(new[] { "B", "G", "R", "U", "W" },
            "Sylvan Caryatid can tap for any single colour of mana.");
    }

    [Fact]
    public void SylvanCaryatid_GreenManaAbility_ProducesGreenAndTaps()
    {
        var c = SylvanCaryatidFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        c.ClearSummoningSickness();

        var green = c.Abilities.OfType<ManaAbility>()
            .FirstOrDefault(a => a.ManaGenerated?.ToString() == "G");

        green.Should().NotBeNull("{T}: Add {G} must be present.");
        green!.CanActivate().Should().BeTrue("creature is untapped.");

        var mana = green.Activate();
        mana.ToString().Should().Be("G");
        c.IsTapped.Should().BeTrue("activating the {T} mana ability taps Sylvan Caryatid.");
    }

    // ── Defender (CR 702.3) ─────────────────────────────────────────────────

    [Fact]
    public void SylvanCaryatid_HasDefender_CannotAttack()
    {
        var c = SylvanCaryatidFactory.Create(_alice);

        CombatAbilities.HasDefender(c).Should().BeTrue(
            "Sylvan Caryatid has Defender (CR 702.3) and can't attack.");
    }

    // ── Hexproof (CR 702.11) — unconditional ────────────────────────────────

    private static TargetSpec CreatureTargetSpec() =>
        new TargetSpec("target creature").Creatures();

    [Fact]
    public void SylvanCaryatid_IsHexproofFromOpponents()
    {
        var c = SylvanCaryatidFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        TargetLegality.IsLegal(CreatureTargetSpec(), c, _bob)
            .Should().BeFalse("Sylvan Caryatid has hexproof from opponents (CR 702.11).");
    }

    [Fact]
    public void SylvanCaryatid_ControllerCanStillTarget()
    {
        var c = SylvanCaryatidFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        TargetLegality.IsLegal(CreatureTargetSpec(), c, _alice)
            .Should().BeTrue("hexproof only blocks opponents' spells/abilities.");
    }

    [Fact]
    public void SylvanCaryatid_HexproofIsUnconditional_EvenWhenTapped()
    {
        var c = SylvanCaryatidFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        c.Tap();

        // Unlike Paradise Druid ("hexproof as long as it's untapped"), Sylvan
        // Caryatid's hexproof is unconditional — tapping does not remove it.
        TargetLegality.IsLegal(CreatureTargetSpec(), c, _bob)
            .Should().BeFalse("a tapped Sylvan Caryatid keeps hexproof (unconditional, CR 702.11).");
    }
}
