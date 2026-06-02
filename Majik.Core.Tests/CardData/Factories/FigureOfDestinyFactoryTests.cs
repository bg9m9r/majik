using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FigureOfDestinyFactory"/> (Eventide, {R/W}).
/// Creature — Kithkin 1/1:
///   "{R/W}: This creature becomes a Kithkin Spirit with base power and
///    toughness 2/2.
///    {R/W}{R/W}{R/W}: If this creature is a Spirit, it becomes a Kithkin
///    Spirit Warrior with base power and toughness 4/4.
///    {R/W}{R/W}{R/W}{R/W}{R/W}{R/W}: If this creature is a Warrior, it
///    becomes a Kithkin Spirit Warrior Avatar with base power and toughness
///    8/8, flying, and first strike."
///   (Oracle verified against Scryfall 2026-06-02.)
///
/// Mirrors <see cref="GhituEncampmentFactoryTests"/>: printed body from the
/// embedded JSON definition, with the level-up activated abilities layered on
/// in the factory. Each level registers a Layer-4 <see cref="AddSubtypeEffect"/>
/// + a Layer-7b <see cref="BecomesPTEffect"/> (and Flying + First strike
/// keyword markers at the top level), gated by the source's computed subtypes.
/// </summary>
[Trait("Color", "RW")]
public class FigureOfDestinyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FigureOfDestiny_Identity()
    {
        var figure = FigureOfDestinyFactory.Create(_alice);

        figure.Name.Should().Be("Figure of Destiny");
        figure.HasType(CardType.Creature).Should().BeTrue();
        figure.Subtypes.Should().Contain(CardSubtype.Kithkin,
            "printed subtype is Kithkin");
        figure.Power.Should().Be(1, "printed power is 1");
        figure.Toughness.Should().Be(1, "printed toughness is 1");
        figure.Owner.Should().BeSameAs(_alice);
        figure.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FigureOfDestiny()
    {
        var card = NamedCardFactory.Create("Figure of Destiny", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Figure of Destiny");
        card.HasType(CardType.Creature).Should().BeTrue();

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(3,
            "the three level-up activated abilities are wired");
    }

    // -----------------------------------------------------------------------
    // Printed hybrid mana cost per level (CR 107.4e)
    // -----------------------------------------------------------------------

    [Fact]
    public void FigureOfDestiny_LevelCosts_AreHybridRW()
    {
        var figure = FigureOfDestinyFactory.Create(_alice);
        var levels = figure.Abilities.OfType<ActivatedAbility>().ToList();

        levels.Should().HaveCount(3);
        foreach (var level in levels)
        {
            level.Costs.OfType<ManaCostCost>().Should().ContainSingle(
                "each level costs a single {R/W}-pip ManaCostCost");
            level.IsSorcerySpeed.Should().BeFalse(
                "the level-up abilities are instant-speed per oracle");
        }

        // {R/W} (mv 1), {R/W}{R/W}{R/W} (mv 3), {R/W}×6 (mv 6).
        var manaValues = levels
            .Select(l => l.Costs.OfType<ManaCostCost>().Single().Cost.TotalValue)
            .OrderBy(v => v)
            .ToList();
        manaValues.Should().Equal(1, 3, 6);
    }

    // -----------------------------------------------------------------------
    // Level 1 — {R/W}: becomes a Kithkin Spirit 2/2.
    // -----------------------------------------------------------------------

    [Fact]
    public void FigureOfDestiny_Level1_BecomesKithkinSpirit_2_2()
    {
        var effects = new ContinuousEffectsService();
        var figure = FigureOfDestinyFactory.Create(_alice, effects);
        OnBattlefield(figure);

        Level(figure, 0).Resolve();

        var chars = effects.Compute(figure);
        chars.Subtypes.Should().Contain(CardSubtype.Kithkin, "printed subtype stays");
        chars.Subtypes.Should().Contain(CardSubtype.Spirit, "Spirit added");
        chars.Power.Should().Be(2, "base power set to 2");
        chars.Toughness.Should().Be(2, "base toughness set to 2");
    }

    // -----------------------------------------------------------------------
    // Level 2 — {R/W}{R/W}{R/W}: if a Spirit, becomes Kithkin Spirit Warrior 4/4.
    // -----------------------------------------------------------------------

    [Fact]
    public void FigureOfDestiny_Level2_RequiresSpirit_NoOpWhenNotSpirit()
    {
        var effects = new ContinuousEffectsService();
        var figure = FigureOfDestinyFactory.Create(_alice, effects);
        OnBattlefield(figure);

        // Activate level 2 without first becoming a Spirit — gated no-op.
        Level(figure, 1).Resolve();

        var chars = effects.Compute(figure);
        chars.Subtypes.Should().NotContain(CardSubtype.Warrior,
            "the 'If this creature is a Spirit' gate fails — no Warrior");
        chars.Power.Should().Be(1, "still the printed 1/1 — no set-base applied");
        chars.Toughness.Should().Be(1);
    }

    [Fact]
    public void FigureOfDestiny_Level2_AfterSpirit_BecomesKithkinSpiritWarrior_4_4()
    {
        var effects = new ContinuousEffectsService();
        var figure = FigureOfDestinyFactory.Create(_alice, effects);
        OnBattlefield(figure);

        Level(figure, 0).Resolve(); // → Kithkin Spirit 2/2
        Level(figure, 1).Resolve(); // → Kithkin Spirit Warrior 4/4

        var chars = effects.Compute(figure);
        chars.Subtypes.Should().Contain(CardSubtype.Kithkin);
        chars.Subtypes.Should().Contain(CardSubtype.Spirit);
        chars.Subtypes.Should().Contain(CardSubtype.Warrior, "Warrior added at level 2");
        chars.Power.Should().Be(4, "the later 4/4 set-base overrides the 2/2");
        chars.Toughness.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // Level 3 — {R/W}×6: if a Warrior, becomes Kithkin Spirit Warrior Avatar
    // 8/8 with flying and first strike.
    // -----------------------------------------------------------------------

    [Fact]
    public void FigureOfDestiny_Level3_RequiresWarrior_NoOpWhenNotWarrior()
    {
        var effects = new ContinuousEffectsService();
        var figure = FigureOfDestinyFactory.Create(_alice, effects);
        OnBattlefield(figure);

        Level(figure, 0).Resolve(); // Spirit, but not yet a Warrior.
        Level(figure, 2).Resolve(); // gated no-op

        var chars = effects.Compute(figure);
        chars.Subtypes.Should().NotContain(CardSubtype.Avatar,
            "the 'If this creature is a Warrior' gate fails — no Avatar");
        chars.Keywords.Should().NotContain("Flying");
        chars.Power.Should().Be(2, "still the level-1 2/2");
    }

    [Fact]
    public void FigureOfDestiny_Level3_AfterWarrior_BecomesAvatar_8_8_Flying_FirstStrike()
    {
        var effects = new ContinuousEffectsService();
        var figure = FigureOfDestinyFactory.Create(_alice, effects);
        OnBattlefield(figure);

        Level(figure, 0).Resolve(); // Kithkin Spirit 2/2
        Level(figure, 1).Resolve(); // Kithkin Spirit Warrior 4/4
        Level(figure, 2).Resolve(); // Kithkin Spirit Warrior Avatar 8/8 + fly + FS

        var chars = effects.Compute(figure);
        chars.Subtypes.Should().Contain(CardSubtype.Kithkin);
        chars.Subtypes.Should().Contain(CardSubtype.Spirit);
        chars.Subtypes.Should().Contain(CardSubtype.Warrior);
        chars.Subtypes.Should().Contain(CardSubtype.Avatar, "Avatar added at level 3");
        chars.Power.Should().Be(8, "base P/T set to 8/8");
        chars.Toughness.Should().Be(8);
        chars.Keywords.Should().Contain("Flying", "the Avatar has flying");
        chars.Keywords.Should().Contain("First strike", "the Avatar has first strike");
    }

    // -----------------------------------------------------------------------
    // Shape-only path (no effects service): resolution is a safe no-op.
    // -----------------------------------------------------------------------

    [Fact]
    public void FigureOfDestiny_NoEffectsService_LevelsAreNoOp()
    {
        var figure = FigureOfDestinyFactory.Create(_alice);
        OnBattlefield(figure);

        var act = () =>
        {
            foreach (var level in figure.Abilities.OfType<ActivatedAbility>())
                level.Resolve();
        };

        act.Should().NotThrow("with no service wired each level is a no-op");
        figure.Subtypes.Should().NotContain(CardSubtype.Spirit);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void OnBattlefield(Creature figure)
    {
        _alice.Zones.Battlefield.AddCard(figure);
        figure.SetZone(ZoneType.Battlefield);
    }

    /// <summary>The level-up activated abilities in printed order (index 0 =
    /// {R/W}, 1 = {R/W}×3, 2 = {R/W}×6).</summary>
    private static ActivatedAbility Level(Creature figure, int index) =>
        figure.Abilities.OfType<ActivatedAbility>().ElementAt(index);
}
