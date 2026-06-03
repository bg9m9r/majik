using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// <see cref="StaticDischargeTemplate"/> — production binder seam for the
/// Intensity/Intensify damage spell (Static Discharge).
/// </summary>
public class StaticDischargeTemplateTests
{
    private const string CardName = "Static Discharge";

    private static string NormalizedOracle() =>
        OracleTextNormalizer.NormalizeForCard(
            "Starting intensity 3\n" +
            "This sorcery deals damage equal to its intensity to any target. " +
            "Then cards you own named Static Discharge intensify by 1.",
            CardName);

    private static SpellBindContext Ctx(Player caster) =>
        new(new CardEntity
            {
                Name = CardName,
                OracleText =
                    "Starting intensity 3\n" +
                    "This sorcery deals damage equal to its intensity to any target. " +
                    "Then cards you own named Static Discharge intensify by 1.",
            },
            caster,
            _ => _,
            Effects: null,
            Stack: null);

    [Fact]
    public void Binds_OnIntensityDamageOracle()
    {
        new StaticDischargeTemplate().TryBind(Ctx(new Player("A", 20)))
            .Should().NotBeNull("template matches the intensity-damage shape");
    }

    [Theory]
    // Plain burn — no intensity.
    [InlineData("This sorcery deals 3 damage to any target.")]
    // Mass-damage-from-power family — out of scope.
    [InlineData("Target creature you control deals damage equal to its power to each other creature.")]
    public void DoesNotBind_OutOfFamily(string oracle)
    {
        var ctx = new SpellBindContext(
            new CardEntity { Name = "X", OracleText = oracle },
            new Player("A", 20), _ => _, Effects: null, Stack: null);

        new StaticDischargeTemplate().TryBind(ctx).Should().BeNull();
    }

    [Fact]
    public void TargetRequest_OneAnyTarget()
    {
        var def = new StaticDischargeTemplate().TryBind(Ctx(new Player("A", 20)))!;

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("any target");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void Resolve_DealsIntensityDamage_ThenIntensifies()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // The resolving Static Discharge on the stack carrying intensity 3.
        var sd = new Sorcery(CardName, "{1}{R}") { Owner = alice, Controller = alice };
        IntensifyHelper.Build(sd, 3);
        sd.SetZone(ZoneType.Stack);
        alice.Zones.GetZone(ZoneType.Stack).AddCard(sd);

        var def = new StaticDischargeTemplate().TryBind(Ctx(alice))!;
        var chosen = new ChosenSpellParams(
            null, null,
            new[] { (IReadOnlyList<object>)new object[] { bob } },
            ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bob.LifeTotal.Should().Be(17, "intensity 3 damage");
        sd.Intensity.Should().Be(4, "then intensify by 1");
    }
}
