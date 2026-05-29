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
/// Unit tests for <see cref="OvergrownBattlementFactory"/>.
///
/// Overgrown Battlement (Rise of the Eldrazi, {1}{G}) — Creature — Wall 0/4.
/// Oracle text:
///   "Defender
///    {T}: Add {G} for each creature you control with defender."
///
/// Covers:
/// - Identity (name, mana cost, Wall subtype, 0/4, owner/controller).
/// - Defender keyword marker (CR 702.3) surfaced via
///   <see cref="CombatAbilities.HasDefender"/>.
/// - NamedCardFactory dispatch.
/// - {T} mana ability counts creatures the controller controls that have
///   defender (INCLUDES the Battlement itself) and produces that many {G}.
/// - Opponents' defenders and the controller's non-defender creatures are
///   excluded (CR 109.5 — "you control"; "with defender" filter).
/// </summary>
public class OvergrownBattlementFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeDefender(Player owner, string name)
    {
        var c = new Creature(name, "G", 0, 3, subtypes: new[] { CardSubtype.Wall });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        c.AddAbility(new KeywordAbility("Defender", c, owner));
        return c;
    }

    private static Creature MakeNonDefender(Player owner)
    {
        var c = new Creature("Grizzly Bears", "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // ── Identity ───────────────────────────────────────────────────────

    [Fact]
    public void OvergrownBattlement_Identity()
    {
        var c = OvergrownBattlementFactory.Create(_alice);

        c.Name.Should().Be("Overgrown Battlement");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wall).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OvergrownBattlement_IsNotLegendary()
    {
        var c = OvergrownBattlementFactory.Create(_alice);

        c.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void OvergrownBattlement_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Overgrown Battlement", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Overgrown Battlement");
        ((Creature)c).HasSubtype(CardSubtype.Wall).Should().BeTrue();
        ((Creature)c).Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender");
        ((Creature)c).Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    // ── Defender keyword ───────────────────────────────────────────────

    [Fact]
    public void OvergrownBattlement_HasDefenderKeyword()
    {
        var c = OvergrownBattlementFactory.Create(_alice);

        // CR 702.3 — Defender wired as a KeywordAbility marker.
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender");
        CombatAbilities.HasDefender(c).Should().BeTrue();
    }

    // ── Mana ability ───────────────────────────────────────────────────

    [Fact]
    public void OvergrownBattlement_HasManaAbility()
    {
        var c = OvergrownBattlementFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "Overgrown Battlement has one mana ability: {T}: Add {G} per defender you control.");
    }

    [Fact]
    public void OvergrownBattlement_ManaAbility_AloneProducesOneGreen()
    {
        // Only defender in play is the Battlement itself — count = 1
        // (oracle reads "each creature you control with defender" with no
        // "other" qualifier, and the Battlement has defender).
        var battlement = OvergrownBattlementFactory.Create(_alice);
        battlement.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(battlement);
        // CR 302.6 — clear summoning sickness so this test exercises the
        // defender count rather than the {T} sickness gate.
        battlement.ClearSummoningSickness();

        var manaAbility = battlement.Abilities.OfType<ManaAbility>().Single();
        manaAbility.CanActivate().Should().BeTrue();

        var mana = manaAbility.Activate();
        mana.ToString().Should().Be("G",
            "with just the Battlement in play, X = 1 → produces one green mana.");
        battlement.IsTapped.Should().BeTrue("the {T} cost is paid on activation.");
    }

    [Fact]
    public void OvergrownBattlement_ManaAbility_ScalesWithDefenderCount()
    {
        var battlement = OvergrownBattlementFactory.Create(_alice);
        battlement.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(battlement);
        // CR 302.6 — clear summoning sickness so Activate() is legal.
        battlement.ClearSummoningSickness();

        // Two other defenders on the controller's battlefield.
        _alice.Zones.Battlefield.AddCard(MakeDefender(_alice, "Wall of Roots"));
        _alice.Zones.Battlefield.AddCard(MakeDefender(_alice, "Axebane Guardian"));

        var manaAbility = battlement.Abilities.OfType<ManaAbility>().Single();
        var mana = manaAbility.Activate();

        // Three defenders total → three green pips.
        mana.ToString().Should().Be("GGG",
            "X = controller's defenders (Battlement + two more = 3) → three green mana.");
    }

    [Fact]
    public void OvergrownBattlement_ManaAbility_IgnoresNonDefenderCreatures()
    {
        var battlement = OvergrownBattlementFactory.Create(_alice);
        battlement.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(battlement);
        battlement.ClearSummoningSickness();

        // A non-defender creature you control does NOT count.
        _alice.Zones.Battlefield.AddCard(MakeNonDefender(_alice));

        var manaAbility = battlement.Abilities.OfType<ManaAbility>().Single();
        var mana = manaAbility.Activate();

        mana.ToString().Should().Be("G",
            "only creatures WITH defender count — the Bears are excluded.");
    }

    [Fact]
    public void OvergrownBattlement_ManaAbility_IgnoresOpponentDefenders()
    {
        var battlement = OvergrownBattlementFactory.Create(_alice);
        battlement.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(battlement);
        battlement.ClearSummoningSickness();

        // Bob has a defender — should NOT count toward Alice's X.
        _bob.Zones.Battlefield.AddCard(MakeDefender(_bob, "Wall of Omens"));

        var manaAbility = battlement.Abilities.OfType<ManaAbility>().Single();
        var mana = manaAbility.Activate();

        mana.ToString().Should().Be("G",
            "CR 109.5 — 'you control' filters to the controller's battlefield only.");
    }
}
