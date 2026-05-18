using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class KeywordBinderTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Bind_AttachesOneKeywordAbilityPerEvergreenKeyword()
    {
        var card = new Creature("Test", "1", 1, 1);
        var entity = new CardEntity { Name = "Test", Keywords = "[\"Flying\",\"Vigilance\"]" };

        KeywordBinder.Bind(card, entity, _alice);

        var keywords = card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain(new[] { "Flying", "Vigilance" });
    }

    [Theory]
    [InlineData("Flying", true)]
    [InlineData("Trample", true)]
    [InlineData("Vigilance", true)]
    [InlineData("Haste", true)]
    [InlineData("First strike", true)]
    [InlineData("Double strike", true)]
    [InlineData("Deathtouch", true)]
    [InlineData("Lifelink", true)]
    [InlineData("Reach", true)]
    [InlineData("Menace", true)]
    [InlineData("Defender", true)]
    [InlineData("Indestructible", true)]
    [InlineData("Flash", true)]
    public void Bind_RecognisesEvergreen(string keyword, bool expected)
    {
        var card = new Creature("Test", "1", 1, 1);
        var entity = new CardEntity { Name = "Test", Keywords = $"[\"{keyword}\"]" };

        KeywordBinder.Bind(card, entity, _alice);

        card.Abilities.OfType<KeywordAbility>().Any(k => k.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            .Should().Be(expected);
    }

    [Fact]
    public void Bind_SkipsUnknownNonEvergreenKeywords()
    {
        var card = new Creature("Test", "1", 1, 1);
        var entity = new CardEntity { Name = "Test", Keywords = "[\"Storm\",\"Suspend\"]" };

        KeywordBinder.Bind(card, entity, _alice);

        card.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "non-combat keywords need bespoke handling, not just markers");
    }

    [Fact]
    public void Bind_EmptyKeywordsArray_NoOps()
    {
        var card = new Creature("Test", "1", 1, 1);
        var entity = new CardEntity { Name = "Test", Keywords = "[]" };

        KeywordBinder.Bind(card, entity, _alice);

        card.Abilities.Should().BeEmpty();
    }

    [Fact]
    public void Bind_FlyingFromDb_CombatAbilitiesSeesIt()
    {
        var c = new Creature("Air Elemental", "3UU", 4, 4);
        var entity = new CardEntity { Name = "Air Elemental", Keywords = "[\"Flying\"]" };

        KeywordBinder.Bind(c, entity, _alice);

        CombatAbilities.HasFlying(c).Should().BeTrue();
    }
}
