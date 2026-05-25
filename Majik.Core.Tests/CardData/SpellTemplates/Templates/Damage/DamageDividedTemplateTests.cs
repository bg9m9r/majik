using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// Family-fold consumer. Parameterised across every Modern-legal printing
/// of "deals N damage divided as you choose among …" to lock in a single
/// template handles the cluster. PR-B replaces what would otherwise be a
/// per-card factory for each member.
/// </summary>
public class DamageDividedTemplateTests
{
    private static SpellBindContext Ctx(string name, string oracle)
    {
        var e = new CardEntity { Name = name, OracleText = oracle, TypeLine = "Instant" };
        return new SpellBindContext(e, new Player("X", 20), x => x, null, null);
    }

    [Theory]
    [InlineData("Arc Lightning",          "Arc Lightning deals 3 damage divided as you choose among one, two, or three targets.")]
    [InlineData("Chandra's Pyrohelix",    "Chandra's Pyrohelix deals 2 damage divided as you choose among one or two targets.")]
    [InlineData("Forked Bolt",            "Forked Bolt deals 2 damage divided as you choose among one or two targets.")]
    [InlineData("Twin Bolt",              "Twin Bolt deals 2 damage divided as you choose among one or two targets.")]
    [InlineData("Flames of the Firebrand","Flames of the Firebrand deals 3 damage divided as you choose among one, two, or three targets.")]
    [InlineData("Aerial Volley",          "Aerial Volley deals 3 damage divided as you choose among one, two, or three target creatures with flying.")]
    [InlineData("Deft Dismissal",         "Deft Dismissal deals 3 damage divided as you choose among one, two, or three target attacking or blocking creatures.")]
    public void Binds_RealPrintings(string name, string oracle)
    {
        var template = new DamageDividedTemplate();
        template.TryBind(Ctx(name, oracle)).Should().NotBeNull();
    }

    [Fact]
    public void RejectsNonDividedDamage()
    {
        // Plain "deals N damage to any target" goes through DamageAnyTarget,
        // not this template. The folded view still has "deals n damage" but
        // lacks the "divided as you choose among …" clause.
        var template = new DamageDividedTemplate();
        template.TryBind(Ctx("Lightning Bolt", "Lightning Bolt deals 3 damage to any target."))
            .Should().BeNull();
    }

    [Fact]
    public void RejectsWhenNumericValueCannotBeExtracted()
    {
        // If the unfolded body uses "X" instead of a literal digit, the
        // value extractor fails and we abstain — Conflagrate's "deals X
        // damage divided" needs a different template (HasVariableX=true).
        var template = new DamageDividedTemplate();
        template.TryBind(Ctx("Conflagrate",
            "Conflagrate deals X damage divided as you choose among any number of targets."))
            .Should().BeNull();
    }
}
