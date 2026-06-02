using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Moq;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StonecoilSerpentFactory"/> (Throne of Eldraine,
/// {X}). Artifact Creature — Snake 0/0. Oracle text (verified against
/// Scryfall):
///   "Reach, trample, protection from multicolored
///    This creature enters with X +1/+1 counters on it."
///
/// Covers identity, NamedCardFactory dispatch, the two combat keyword
/// riders (Reach / Trample), protection from multicolored (spell-predicate
/// surface — CR 702.16), and the ETB X +1/+1 counters trigger (CR 122.1g).
/// </summary>
public class StonecoilSerpentFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Stonecoil_Identity()
    {
        var s = StonecoilSerpentFactory.Create(_alice);

        s.Name.Should().Be("Stonecoil Serpent");
        s.ManaCost.Should().Be("{X}");
        s.ManaCostValue.HasX.Should().BeTrue("printed cost has X (CR 202.3b)");
        s.HasType(CardType.Creature).Should().BeTrue();
        s.HasType(CardType.Artifact).Should().BeTrue("Stonecoil Serpent is an artifact creature");
        s.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        s.BasePower.Should().Be(0);
        s.BaseToughness.Should().Be(0);
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Stonecoil_IsColorless()
    {
        // {X} cost, no coloured mana symbols, no colour indicator → colourless.
        var s = StonecoilSerpentFactory.Create(_alice);
        CardColors.GetColors(s).Should().BeEmpty("Stonecoil Serpent is colourless");
    }

    [Fact]
    public void Stonecoil_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Stonecoil Serpent", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Stonecoil Serpent");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Snake).Should().BeTrue();
    }

    [Fact]
    public void Stonecoil_HasReachAndTrample()
    {
        var s = StonecoilSerpentFactory.Create(_alice);

        CombatAbilities.HasReach(s).Should().BeTrue("CR 702.17 — Reach");
        CombatAbilities.HasTrample(s).Should().BeTrue("CR 702.19 — trample");
    }

    [Fact]
    public void Stonecoil_HasProtectionFromMulticolored()
    {
        var s = StonecoilSerpentFactory.Create(_alice);

        // CR 702.16 — protection from multicolored: a spell with two or more
        // colours matches the predicate; a mono-colour / colourless spell
        // does not. Colour is derived from the spell card's mana cost
        // (CardColors.GetColors), so the mocks carry coloured-pip costs.
        var multi = MakeSpell("{R}{G}");      // 2 colours → multicolored
        var mono = MakeSpell("{R}");          // 1 colour
        var colourless = MakeSpell("{2}");    // 0 colours

        Protection.HasProtectionFromSpell(s, multi).Should().BeTrue(
            "a multicolored spell (2+ colours) can't target a creature with protection from multicolored");
        Protection.HasProtectionFromSpell(s, mono).Should().BeFalse(
            "a mono-colour spell is not multicolored");
        Protection.HasProtectionFromSpell(s, colourless).Should().BeFalse(
            "a colourless spell is not multicolored");
    }

    [Fact]
    public void Stonecoil_EtbWithXEquals4_GainsFourPlusOneCounters()
    {
        var s = StonecoilSerpentFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(s);
        s.SetZone(ZoneType.Battlefield);

        // SpellCastFlow stamps PendingCastX after ChooseXAsync; simulate.
        s.SetPendingCastX(4);

        var etb = s.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Effects.Any(e => e.Description.Contains("enters with X")));
        foreach (var e in etb.Effects) e.Execute();

        s.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(4,
            "Stonecoil Serpent enters with X (=4) +1/+1 counters per CR 122.1g");
        s.PendingCastX.Should().BeNull(
            "PendingCastX stamp consumed; re-entries don't double-count");
    }

    [Fact]
    public void Stonecoil_NonCastEntry_ZeroCounters()
    {
        var s = StonecoilSerpentFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(s);
        s.SetZone(ZoneType.Battlefield);
        s.PendingCastX.Should().BeNull();

        var etb = s.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Effects.Any(e => e.Description.Contains("enters with X")));
        foreach (var e in etb.Effects) e.Execute();

        s.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "non-cast entry → no PendingCastX → zero counters");
    }

    private static ISpell MakeSpell(string manaCost)
    {
        var card = new Mock<ICard>();
        card.SetupGet(c => c.ManaCost).Returns(manaCost);
        var spell = new Mock<ISpell>();
        spell.SetupGet(s => s.Card).Returns(card.Object);
        return spell.Object;
    }
}
