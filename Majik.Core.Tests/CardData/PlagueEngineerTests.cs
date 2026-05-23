using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PlagueEngineerFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, P/T, Human + Rogue subtypes,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Deathtouch keyword.
/// - Static effect: opponent's Goblin gets -1/-1 (2/2 → 1/1).
/// - Static effect: controller's own Goblin is NOT debuffed.
/// - Static effect: opponent's non-Goblin creature is NOT debuffed.
/// - LTB lifts the debuff (effect's IsActive gate falls when source
///   leaves the battlefield).
/// </summary>
public class PlagueEngineerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PlagueEngineer_Identity()
    {
        var c = PlagueEngineerFactory.Create(_alice);

        c.Name.Should().Be("Plague Engineer");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Human is part of the printed creature type");
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue("Rogue is part of the printed creature type");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PlagueEngineer_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Plague Engineer", _alice);

        c.Should().BeOfType<Creature>("Plague Engineer is a Creature");
        c.Name.Should().Be("Plague Engineer");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
    }

    [Fact]
    public void PlagueEngineer_HasDeathtouchKeyword()
    {
        var c = PlagueEngineerFactory.Create(_alice);
        c.Zone = ZoneType.Battlefield;

        CombatAbilities.HasDeathtouch(c).Should().BeTrue(
            "CR 702.2 — Plague Engineer's printed keyword set includes Deathtouch");
    }

    // -----------------------------------------------------------------------
    // ETB choice + static debuff
    // -----------------------------------------------------------------------

    [Fact]
    public void PlagueEngineer_ChoosesGoblin_AndDebuffsOpponentGoblin()
    {
        var svc = new ContinuousEffectsService();

        // Bob (opponent) controls a 2/2 Goblin.
        var oppGoblin = new Creature("Goblin Piker", "1R", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var plague = PlagueEngineerFactory.Create(
            _alice,
            continuousEffects: svc,
            typeChooser: _ => CardSubtype.Goblin);
        plague.Zone = ZoneType.Battlefield;
        plague.ActiveEffects = svc;

        PlagueEngineerFactory.GetChosenType(plague).Should().Be(CardSubtype.Goblin,
            "the ETB type choice is captured eagerly at factory time");

        oppGoblin.GetPower().Should().Be(1, "opponent's Goblin gets -1/-1");
        oppGoblin.GetToughness().Should().Be(1);
    }

    [Fact]
    public void PlagueEngineer_DoesNotDebuff_ControllersOwnGoblin()
    {
        var svc = new ContinuousEffectsService();

        // Alice (Plague Engineer's controller) also controls a 2/2 Goblin.
        var ownGoblin = new Creature("Goblin Piker", "1R", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var plague = PlagueEngineerFactory.Create(
            _alice,
            continuousEffects: svc,
            typeChooser: _ => CardSubtype.Goblin);
        plague.Zone = ZoneType.Battlefield;
        plague.ActiveEffects = svc;

        ownGoblin.GetPower().Should().Be(2,
            "CR 109.5 — 'your opponents control' excludes Plague Engineer's controller");
        ownGoblin.GetToughness().Should().Be(2);
    }

    [Fact]
    public void PlagueEngineer_DoesNotDebuff_OpponentNonGoblin()
    {
        var svc = new ContinuousEffectsService();

        // Bob controls a 2/2 Bear (NOT a Goblin).
        var oppBear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var plague = PlagueEngineerFactory.Create(
            _alice,
            continuousEffects: svc,
            typeChooser: _ => CardSubtype.Goblin);
        plague.Zone = ZoneType.Battlefield;
        plague.ActiveEffects = svc;

        oppBear.GetPower().Should().Be(2,
            "Plague Engineer only debuffs creatures matching the chosen type");
        oppBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void PlagueEngineer_LTB_LiftsDebuff()
    {
        var svc = new ContinuousEffectsService();

        var oppGoblin = new Creature("Goblin Piker", "1R", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var plague = PlagueEngineerFactory.Create(
            _alice,
            continuousEffects: svc,
            typeChooser: _ => CardSubtype.Goblin);
        plague.Zone = ZoneType.Battlefield;
        plague.ActiveEffects = svc;

        // Baseline: debuff active.
        oppGoblin.GetPower().Should().Be(1);
        oppGoblin.GetToughness().Should().Be(1);

        // Plague Engineer dies — LordStaticEffect.IsActive() short-circuits
        // when the source isn't on the battlefield (CR 613 — continuous
        // effects from a permanent stop applying when it leaves play).
        plague.SetZone(ZoneType.Graveyard);

        oppGoblin.GetPower().Should().Be(2, "debuff lifts on LTB");
        oppGoblin.GetToughness().Should().Be(2);
    }
}
