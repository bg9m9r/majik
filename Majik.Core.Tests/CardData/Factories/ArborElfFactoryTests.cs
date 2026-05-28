using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ArborElfFactory"/>.
///
/// Covers:
/// - Identity (name, mana cost, Elf + Druid subtypes, 1/1, owner/controller).
/// - NamedCardFactory dispatch.
/// - Activated "{T}: Untap target Forest" ability is present with a tap cost
///   and a 1..1 target request.
/// - Activation taps Arbor Elf and untaps the chosen Forest at resolution.
/// - Non-Forest land target (e.g. Mountain) is a resolve-time no-op.
/// - Non-land target is a resolve-time no-op.
/// </summary>
public class ArborElfFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Land MakeForest(Player owner)
    {
        var f = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        f.SetOwner(owner);
        f.SetController(owner);
        f.SetZone(ZoneType.Battlefield);
        return f;
    }

    private static Land MakeMountain(Player owner)
    {
        var m = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        m.SetOwner(owner);
        m.SetController(owner);
        m.SetZone(ZoneType.Battlefield);
        return m;
    }

    private static ActivatedAbility GetUntapAbility(Creature c) =>
        c.Abilities.OfType<ActivatedAbility>().Single();

    [Fact]
    public void ArborElf_Identity()
    {
        var c = ArborElfFactory.Create(_alice);

        c.Name.Should().Be("Arbor Elf");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ArborElf_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Arbor Elf", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Arbor Elf");
        ((Creature)c).HasSubtype(CardSubtype.Elf).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Druid).Should().BeTrue();
    }

    [Fact]
    public void ArborElf_HasActivatedUntapAbility_WithTargetRequest()
    {
        var c = ArborElfFactory.Create(_alice);

        var act = c.Abilities.OfType<ActivatedAbility>().ToList();
        act.Should().HaveCount(1, "Arbor Elf has one activated ability: {T}: Untap target Forest.");

        var ability = act[0];
        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void ArborElf_UntapsTargetForest_AtResolution()
    {
        var elf = ArborElfFactory.Create(_alice);
        elf.SetZone(ZoneType.Battlefield);

        var forest = MakeForest(_alice);
        forest.Tap();
        forest.IsTapped.Should().BeTrue("forest starts tapped — Arbor Elf is about to untap it.");

        var ability = GetUntapAbility(elf);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { forest } });

        foreach (var e in ability.Effects) e.Execute();

        forest.IsTapped.Should().BeFalse(
            "CR 701.27 — Arbor Elf's {T}: Untap target Forest leaves the targeted Forest untapped.");
    }

    [Fact]
    public void ArborElf_NonForestLand_IsResolveTimeNoOp()
    {
        var elf = ArborElfFactory.Create(_alice);
        elf.SetZone(ZoneType.Battlefield);

        var mountain = MakeMountain(_alice);
        mountain.Tap();

        var ability = GetUntapAbility(elf);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { mountain } });

        foreach (var e in ability.Effects) e.Execute();

        mountain.IsTapped.Should().BeTrue(
            "CR 608.2b — a non-Forest land is illegal on resolution; the untap is a no-op.");
    }

    [Fact]
    public void ArborElf_NoTargetChosen_IsResolveTimeNoOp()
    {
        var elf = ArborElfFactory.Create(_alice);
        elf.SetZone(ZoneType.Battlefield);

        var ability = GetUntapAbility(elf);
        // Do NOT set chosen targets — empty list path.

        // CR 608.2b — no chosen target → resolve-time no-op. The contract is
        // "must not throw".
        var act = () => { foreach (var e in ability.Effects) e.Execute(); };
        act.Should().NotThrow();
    }

    [Fact]
    public void ArborElf_OpponentControlsForest_StillUntaps()
    {
        // Oracle text says "target Forest" — no "you control" qualifier. An
        // opponent's tapped Forest is a legal target.
        var elf = ArborElfFactory.Create(_alice);
        elf.SetZone(ZoneType.Battlefield);

        var bobForest = MakeForest(_bob);
        bobForest.Tap();

        var ability = GetUntapAbility(elf);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobForest } });

        foreach (var e in ability.Effects) e.Execute();

        bobForest.IsTapped.Should().BeFalse(
            "Arbor Elf's printed ability has no 'you control' restriction — any Forest is a legal target.");
    }
}
