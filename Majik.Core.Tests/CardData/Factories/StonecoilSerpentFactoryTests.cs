using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
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
/// surface — CR 702.16), and the "enters with X +1/+1 counters" mechanism.
/// The latter is owned by the generic <see cref="EntersWithCountersBinder"/>
/// (NOT a self-managed ETB trigger): the factory attaches no ETB-counters
/// trigger and does not self-manage; the binder reads
/// <see cref="Card.PendingCastX"/> and places the counters as Stonecoil enters
/// (CR 614.1d).
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
    public void Stonecoil_DoesNotAttachEtbTrigger()
    {
        // CR 614.1d — the ETB counters are a binder-registered replacement, NOT
        // a factory-attached TriggeredAbility.
        var s = StonecoilSerpentFactory.Create(_alice);

        s.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Stonecoil's ETB counters are a binder-registered replacement, " +
            "not a self-managed ETB trigger");
    }

    [Fact]
    public void Stonecoil_DoesNotSelfManageEntersWithCounters()
    {
        // The factory must leave SelfManagesEntersWithCounters false so the
        // EntersWithCountersBinder DOES register the variable-X replacement on
        // the prod route. Setting the flag suppresses the binder → 0 counters.
        var s = StonecoilSerpentFactory.Create(_alice);

        s.SelfManagesEntersWithCounters.Should().BeFalse(
            "the binder owns the ETB-X replacement; self-managing suppresses it " +
            "and yields zero counters on the Approach-B prod route");
    }

    [Fact]
    public void Stonecoil_BinderReplacement_EntersWithXEquals4_Counters()
    {
        // The prod mechanism: factory build + binder (reads the card's real
        // oracle text) + ZoneService move. X = 4 (cast {4}).
        var bus = new ReplacementBus();
        var s = StonecoilSerpentFactory.Create(_alice);

        EntersWithCountersBinder.Bind(s, StonecoilEntity(), bus).Should().BeTrue(
            "the binder matches 'enters with X +1/+1 counters on it' and registers " +
            "the variable-X replacement");

        s.SetOwner(_alice);
        s.SetController(_alice);
        _alice.Zones.Library.AddCard(s);
        s.SetZone(ZoneType.Library);
        s.SetPendingCastX(4);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(s, ZoneType.Library, ZoneType.Battlefield, _alice);

        s.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(4,
            "Stonecoil Serpent enters WITH X (=4) +1/+1 counters per CR 614.1d → 4/4");
    }

    [Fact]
    public void Stonecoil_BinderReplacement_ZeroX_NoCounters()
    {
        // No PendingCastX stamp → X = 0 → a 0/0 the SBA layer sends to the
        // graveyard (CR 704.5f). Non-cast entries (blink, copy) take this path.
        var bus = new ReplacementBus();
        var s = StonecoilSerpentFactory.Create(_alice);

        EntersWithCountersBinder.Bind(s, StonecoilEntity(), bus).Should().BeTrue();

        s.SetOwner(_alice);
        s.SetController(_alice);
        _alice.Zones.Library.AddCard(s);
        s.SetZone(ZoneType.Library);
        // No SetPendingCastX → X defaults to 0.

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(s, ZoneType.Library, ZoneType.Battlefield, _alice);

        s.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "X = 0 → zero counters placed → 0/0 SBA-fodder (CR 704.5f)");
    }

    private static CardEntity StonecoilEntity() =>
        new EmbeddedCardRepository().GetByName("Stonecoil Serpent")!;

    private static ISpell MakeSpell(string manaCost)
    {
        var card = new Mock<ICard>();
        card.SetupGet(c => c.ManaCost).Returns(manaCost);
        var spell = new Mock<ISpell>();
        spell.SetupGet(s => s.Card).Returns(card.Object);
        return spell.Object;
    }
}
