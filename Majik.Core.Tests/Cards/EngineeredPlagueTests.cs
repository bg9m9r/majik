using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Unit tests for <see cref="EngineeredPlagueFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Static effect: chosenType=Zombie → ALL Zombie creatures (both players)
///   get -1/-1.
/// - Static effect: controller's own Zombie is ALSO debuffed (unlike Plague
///   Engineer whose opponentsOnly: true spares the controller's creatures).
/// - Static effect: chosenType=Human → Human creatures get -1/-1, Zombies
///   are unaffected.
/// - LTB lifts the debuff (IsActive gate falls when source leaves the
///   battlefield).
/// </summary>
public class EngineeredPlagueTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EngineeredPlague_Identity()
    {
        var card = EngineeredPlagueFactory.Create(_alice);

        card.Name.Should().Be("Engineered Plague");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeFalse("Engineered Plague is a pure Enchantment, not a creature");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EngineeredPlague_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Engineered Plague", _alice);

        card.Should().BeOfType<Enchantment>("Engineered Plague is an Enchantment");
        card.Name.Should().Be("Engineered Plague");
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB choice + static debuff
    // -----------------------------------------------------------------------

    [Fact]
    public void EngineeredPlague_ChoosesZombie_DebuffsOpponentZombie()
    {
        var svc = new ContinuousEffectsService();

        // Bob (opponent) controls a 2/2 Zombie.
        var oppZombie = new Creature("Zombie Token", "1B", 2, 2,
            subtypes: new[] { CardSubtype.Zombie })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var plague = EngineeredPlagueFactory.Create(
            _alice,
            continuousEffects: svc,
            typeChooser: _ => CardSubtype.Zombie);
        plague.Zone = ZoneType.Battlefield;

        EngineeredPlagueFactory.GetChosenType(plague).Should().Be(CardSubtype.Zombie,
            "the ETB type choice is captured eagerly at factory time");

        oppZombie.GetPower().Should().Be(1, "opponent's Zombie gets -1/-1 (CR 613.7c)");
        oppZombie.GetToughness().Should().Be(1);
    }

    [Fact]
    public void EngineeredPlague_ChoosesZombie_AlsoDebuffsControllersOwnZombie()
    {
        var svc = new ContinuousEffectsService();

        // Alice (Plague controller) ALSO controls a 2/2 Zombie.
        // Unlike Plague Engineer (opponentsOnly: true), Engineered Plague
        // says "all creatures of the chosen type" — no "opponents" qualifier.
        var ownZombie = new Creature("Zombie Token", "1B", 2, 2,
            subtypes: new[] { CardSubtype.Zombie })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var plague = EngineeredPlagueFactory.Create(
            _alice,
            continuousEffects: svc,
            typeChooser: _ => CardSubtype.Zombie);
        plague.Zone = ZoneType.Battlefield;

        ownZombie.GetPower().Should().Be(1,
            "Engineered Plague debuffs ALL creatures of the chosen type, " +
            "including the controller's own (unlike Plague Engineer)");
        ownZombie.GetToughness().Should().Be(1);
    }

    [Fact]
    public void EngineeredPlague_ChoosesHuman_HumansDebuffed_ZombiesUnaffected()
    {
        var svc = new ContinuousEffectsService();

        var oppZombie = new Creature("Zombie Token", "1B", 2, 2,
            subtypes: new[] { CardSubtype.Zombie })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var oppHuman = new Creature("Elite Vanguard", "W", 2, 1,
            subtypes: new[] { CardSubtype.Human })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var plague = EngineeredPlagueFactory.Create(
            _alice,
            continuousEffects: svc,
            typeChooser: _ => CardSubtype.Human);
        plague.Zone = ZoneType.Battlefield;

        EngineeredPlagueFactory.GetChosenType(plague).Should().Be(CardSubtype.Human);

        oppHuman.GetPower().Should().Be(1, "Human gets -1/-1");
        oppHuman.GetToughness().Should().Be(0, "Human toughness 1 → 0 after -1/-1");

        oppZombie.GetPower().Should().Be(2, "Zombie is unaffected when Human was chosen");
        oppZombie.GetToughness().Should().Be(2);
    }

    [Fact]
    public void EngineeredPlague_LeavesPlay_DebuffLifted()
    {
        var svc = new ContinuousEffectsService();

        var oppZombie = new Creature("Zombie Token", "1B", 2, 2,
            subtypes: new[] { CardSubtype.Zombie })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var plague = EngineeredPlagueFactory.Create(
            _alice,
            continuousEffects: svc,
            typeChooser: _ => CardSubtype.Zombie);
        plague.Zone = ZoneType.Battlefield;

        // Baseline: debuff is active.
        oppZombie.GetPower().Should().Be(1);
        oppZombie.GetToughness().Should().Be(1);

        // Engineered Plague leaves the battlefield (e.g. destroyed).
        // LordStaticEffect.IsActive() short-circuits when source isn't on
        // the battlefield (CR 613 — continuous effects from a permanent stop
        // applying when it leaves play).
        plague.SetZone(ZoneType.Graveyard);

        oppZombie.GetPower().Should().Be(2, "debuff lifts when Engineered Plague leaves play");
        oppZombie.GetToughness().Should().Be(2);
    }
}
