using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Misc;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Misc;

public class CreaturesCantBlockTemplateTests
{
    // CanBind on the cant-block templates now requires a non-null
    // ContinuousEffectsService — installing a real CombatRestrictionEffect
    // needs somewhere to register it.
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, new ContinuousEffectsService(), null);

    [Theory]
    [InlineData("Creatures can't block this turn.")]
    [InlineData("Creatures without flying can't block this turn.")]
    [InlineData("Nonartifact creatures can't block this turn.")]
    [InlineData("Monocolored creatures can't block this turn.")]
    [InlineData("Green creatures and white creatures can't block this turn.")]
    public void Matches_SingleClauseLockouts(string oracle)
    {
        new CreaturesCantBlockTemplate().TryBind(Ctx(oracle)).Should().NotBeNull();
    }

    [Theory]
    [InlineData("Target creature can't block this turn.")]
    [InlineData("X target creatures can't block this turn.")]
    [InlineData("Up to two target creatures can't block this turn.")]
    public void DoesNotMatch_TargetedVariants(string oracle)
    {
        new CreaturesCantBlockTemplate().TryBind(Ctx(oracle)).Should().BeNull();
    }

    [Fact]
    public void Rehydrate_RegistersMassCannotBlockOnResolve()
    {
        var effects = new ContinuousEffectsService();
        var ctx = new SpellBindContext(
            new CardEntity { Name = "X", OracleText = "Creatures can't block this turn." },
            new Player("A", 20), _ => _, effects, null);

        var spell = new CreaturesCantBlockTemplate().TryBind(ctx);
        spell.Should().NotBeNull();

        // Execute the spell's effect list — simulates resolution.
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);
        foreach (var fx in spell!.EffectFactory(chosen))
        {
            fx.Execute();
        }

        // Probe with an arbitrary creature; mass restrictions match all.
        var probe = new Majik.Core.Cards.Creature("probe", "", 1, 1);
        effects.HasRestriction(probe, CombatRestriction.CannotBlock).Should().BeTrue();
    }

    [Fact]
    public void CanBind_FalseWhenContinuousEffectsServiceMissing()
    {
        var ctx = new SpellBindContext(
            new CardEntity { Name = "X", OracleText = "Creatures can't block this turn." },
            new Player("A", 20), _ => _, Effects: null, Stack: null);

        new CreaturesCantBlockTemplate().TryBind(ctx).Should().BeNull();
    }
}
