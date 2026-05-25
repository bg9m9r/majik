using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class NextSpellCopyTemplateTests
{
    private static SpellBindContext Ctx(string text)
    {
        var stack = new Majik.Core.Stack.Stack();
        var triggers = new TriggerManager(stack);
        return new SpellBindContext(
            new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20),
            _ => _,
            Effects: null,
            Stack: stack,
            Replacements: null,
            Triggers: triggers);
    }

    // The three oracle texts the family is supposed to bind.
    public static IEnumerable<object[]> BoundOracles => new[]
    {
        // Doublecast — sorcery, no rider.
        new object[]
        {
            "When you next cast an instant or sorcery spell this turn, copy that spell. " +
            "You may choose new targets for the copy."
        },
        // Galvanic Iteration — sorcery, with Flashback rider (dropped at v1).
        new object[]
        {
            "When you next cast an instant or sorcery spell this turn, copy that spell. " +
            "You may choose new targets for the copy.\n" +
            "Flashback {1}{U}{R}"
        },
        // Howl of the Horde — sorcery, with Raid additional-copy rider (dropped at v1).
        new object[]
        {
            "When you next cast an instant or sorcery spell this turn, copy that spell. " +
            "You may choose new targets for the copy.\n" +
            "Raid — If you attacked this turn, when you next cast an instant or sorcery " +
            "spell this turn, copy that spell an additional time. You may choose new " +
            "targets for the copies."
        },
    };

    [Theory]
    [MemberData(nameof(BoundOracles))]
    public void NextSpellCopyTemplate_MatchesGalvanicIterationFamily(string oracle)
    {
        new NextSpellCopyTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull("the Doublecast / Galvanic Iteration / Howl of the Horde family must bind");
    }

    [Theory]
    // Cards without the leading "copy that spell" clause must not bind.
    [InlineData("Counter target spell.")]
    [InlineData("Destroy target creature.")]
    [InlineData("Create a token that's a copy of target creature.")]
    // "When you next cast" clause but for a different effect — must not bind.
    [InlineData("When you next cast an instant or sorcery spell this turn, it gains lifelink.")]
    // "Copy target spell" (Twincast / Reverberate family) is a different shape —
    // it copies a spell already on the stack, not a future cast. Must not bind here.
    [InlineData("Copy target instant or sorcery spell. You may choose new targets for the copy.")]
    public void NextSpellCopyTemplate_DoesNotMatchOutOfFamily(string oracle)
    {
        new NextSpellCopyTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }

    [Fact]
    public void NextSpellCopyTemplate_CanBind_RequiresTriggersAndStack()
    {
        var template = new NextSpellCopyTemplate();
        var oracle = "When you next cast an instant or sorcery spell this turn, copy that spell. " +
                     "You may choose new targets for the copy.";

        // No Triggers / Stack — must not bind.
        var ctxBare = new SpellBindContext(
            new CardEntity { Name = "X", OracleText = oracle },
            new Player("A", 20),
            _ => _,
            Effects: null,
            Stack: null);
        template.TryBind(ctxBare).Should().BeNull();

        // Triggers + Stack present — binds.
        template.TryBind(Ctx(oracle)).Should().NotBeNull();
    }
}
