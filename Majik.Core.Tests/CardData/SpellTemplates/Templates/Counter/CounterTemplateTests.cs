using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Counter;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Counter;

public class CounterTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void CounterTargetSpellTemplate_MatchesPlainCounterspell()
    {
        new CounterTargetSpellTemplate().TryBind(Ctx("Counter target spell."))
            .Should().NotBeNull();
    }

    [Fact]
    public void CounterCreatureTemplate_MatchesCreatureClause_NotPlain()
    {
        var t = new CounterCreatureTemplate();
        t.TryBind(Ctx("Counter target creature spell.")).Should().NotBeNull();
        t.TryBind(Ctx("Counter target spell.")).Should().BeNull();
    }

    [Fact]
    public void CounterNoncreatureTemplate_MatchesNoncreatureClause()
    {
        new CounterNoncreatureTemplate().TryBind(Ctx("Counter target noncreature spell."))
            .Should().NotBeNull();
    }

    [Fact]
    public void CounterUnlessPayTemplate_HasHigherPriority_ThanPlainCounter()
    {
        new CounterUnlessPayTemplate().Priority
            .Should().BeGreaterThan(new CounterTargetSpellTemplate().Priority);
    }
}
