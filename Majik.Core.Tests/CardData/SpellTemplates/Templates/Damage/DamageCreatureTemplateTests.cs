using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Damage;

public class DamageCreatureTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Theory]
    [InlineData("Lightning Strike deals 3 damage to target creature.")]
    [InlineData("Magma Spray deals 2 damage to target creature.")]
    [InlineData("Char deals four damage to target creature.")]
    [InlineData("Breath of Fire deals 2 damage to target creature.")]
    public void Matches_DealsNDamageToTargetCreature(string oracle)
    {
        new DamageCreatureTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData("Lightning Bolt deals 3 damage to any target.")]   // DamageAnyTarget owns this
    [InlineData("Stab Wound deals 1 damage to target player.")]    // DamagePlayer owns this
    [InlineData("Wrath of God destroys all creatures.")]
    [InlineData("")]
    public void DoesNotMatch_OtherShapes(string oracle)
    {
        new DamageCreatureTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }

    [Fact]
    public void Priority_HigherThanGenericAnyTarget()
    {
        // Both Damage templates use the same "deals N damage" prefix —
        // the more specific "to target creature" form must outrank the
        // generic "to any target" so a card with text "deals 3 damage to
        // target creature" doesn't route to the broader DamageAny path.
        new DamageCreatureTemplate().Priority
            .Should().BeGreaterThan(new DamageAnyTargetTemplate().Priority);
    }
}
