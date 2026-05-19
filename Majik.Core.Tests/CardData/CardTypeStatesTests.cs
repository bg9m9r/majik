using FluentAssertions;
using Majik.Core.CardData.Adventures;
using Majik.Core.CardData.Battles;
using Majik.Core.CardData.Classes;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

public class CardTypeStatesTests
{
    private readonly Player _alice = new("Alice", 20);

    // ---------- Adventures ----------

    [Fact]
    public void Adventure_CastAsAdventure_ExilesCard()
    {
        var card = new Card("Brazen Borrower", "1U") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(card);
        var state = new AdventureState(card);

        state.CastAsAdventure(_alice);

        card.Zone.Should().Be(ZoneType.Exile);
        state.InAdventureExile.Should().BeTrue();
    }

    [Fact]
    public void Adventure_CastCreatureFromExile_OnlyAfterAdventure()
    {
        var card = new Card("Brazen Borrower", "1U") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(card);
        var state = new AdventureState(card);

        // Not in exile yet — cast-from-exile fails.
        state.CastCreatureFromExile(_alice).Should().BeFalse();

        state.CastAsAdventure(_alice);
        state.CastCreatureFromExile(_alice).Should().BeTrue();
        card.Zone.Should().Be(ZoneType.Hand);
        state.CreatureFaceCast.Should().BeTrue();
    }

    // ---------- MDFCs ----------

    [Fact]
    public void Mdfc_StartsOnFrontFace_TransformSwaps()
    {
        var m = new MdfcState("Hangarback Walker", "Hangarback Walker (back)");
        m.IsBackFace.Should().BeFalse();
        m.ActiveFaceName.Should().Be("Hangarback Walker");

        m.Transform();
        m.IsBackFace.Should().BeTrue();
        m.ActiveFaceName.Should().Be("Hangarback Walker (back)");

        m.Transform();
        m.IsBackFace.Should().BeFalse();
    }

    // ---------- Battles ----------

    [Fact]
    public void Battle_EntersWithDefenseCounters()
    {
        var b = new Enchantment("Siege", "3R") { Owner = _alice, Controller = _alice };
        var state = new BattleState(b, initialDefense: 5);
        state.DefenseCounters.Should().Be(5);
        state.ShouldBeSacrificed().Should().BeFalse();
    }

    [Fact]
    public void Battle_TakesDamage_ReducesDefense_ZeroSacrifices()
    {
        var b = new Enchantment("Siege", "3R") { Owner = _alice, Controller = _alice };
        var state = new BattleState(b, initialDefense: 3);

        state.TakeDamage(2);
        state.DefenseCounters.Should().Be(1);
        state.ShouldBeSacrificed().Should().BeFalse();

        state.TakeDamage(1);
        state.DefenseCounters.Should().Be(0);
        state.ShouldBeSacrificed().Should().BeTrue();
    }

    // ---------- Classes ----------

    [Fact]
    public void Class_StartsAtLevel1_LevelsUpSequentially()
    {
        var c = new ClassState(maxLevel: 3);
        c.CurrentLevel.Should().Be(1);

        c.LevelUp().Should().BeTrue();
        c.CurrentLevel.Should().Be(2);

        c.LevelUp().Should().BeTrue();
        c.CurrentLevel.Should().Be(3);

        c.LevelUp().Should().BeFalse(); // capped
        c.CurrentLevel.Should().Be(3);
    }

    [Fact]
    public void Class_CanLevelUp_FalseAtMax()
    {
        var c = new ClassState(maxLevel: 2);
        c.LevelUp();
        c.CanLevelUp().Should().BeFalse();
    }
}
