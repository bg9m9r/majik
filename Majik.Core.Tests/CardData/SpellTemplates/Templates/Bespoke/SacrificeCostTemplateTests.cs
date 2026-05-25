using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class SacrificeCostTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _,
            new ContinuousEffectsService(), null);

    [Fact]
    public void BloodForBones_MatchesOracle()
    {
        var def = new BloodForBonesTemplate().TryBind(Ctx(
            "As an additional cost to cast this spell, sacrifice a creature. " +
            "Return a creature card from your graveyard to the battlefield, then return another creature card from your graveyard to your hand."));
        def.Should().NotBeNull();
        def!.AdditionalCostsOrEmpty.Should().HaveCount(1);
        def.AdditionalCostsOrEmpty[0].Should().BeOfType<SacrificeACreatureAdditionalCost>();
    }

    [Fact]
    public void InfernalPlunge_MatchesOracle()
    {
        var def = new InfernalPlungeTemplate().TryBind(Ctx(
            "As an additional cost to cast this spell, sacrifice a creature. " +
            "Add {R}{R}{R}."));
        def.Should().NotBeNull();
        def!.AdditionalCostsOrEmpty.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("As an additional cost to cast this spell, sacrifice a creature. Fling deals damage equal to the sacrificed creature's power to any target.")]
    [InlineData("As an additional cost to cast this spell, sacrifice a creature. Thud deals damage equal to the sacrificed creature's power to any target.")]
    public void FlingLike_MatchesAnyTargetFamily(string oracle)
    {
        var def = new FlingLikeTemplate().TryBind(Ctx(oracle));
        def.Should().NotBeNull();
        def!.AdditionalCostsOrEmpty.Should().HaveCount(1);
        def.TargetRequests.Should().HaveCount(1);
    }

    [Fact]
    public void IchorExplosion_MatchesOracle()
    {
        var def = new IchorExplosionTemplate().TryBind(Ctx(
            "As an additional cost to cast this spell, sacrifice a creature. " +
            "All creatures get -X/-X until end of turn, where X is the sacrificed creature's power."));
        def.Should().NotBeNull();
        def!.AdditionalCostsOrEmpty.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("As an additional cost to cast this spell, sacrifice a creature. Draw cards equal to the sacrificed creature's power.")]
    [InlineData("As an additional cost to cast this spell, sacrifice a creature. You draw cards equal to the sacrificed creature's power, then you gain life equal to its toughness.")]
    public void LifesLegacy_MatchesDrawAndDrawGainFamilies(string oracle)
    {
        new LifesLegacyTemplate().TryBind(Ctx(oracle)).Should().NotBeNull();
    }

    [Fact]
    public void TormentedThoughts_MatchesOracle()
    {
        var def = new TormentedThoughtsTemplate().TryBind(Ctx(
            "As an additional cost to cast this spell, sacrifice a creature. " +
            "Target player discards a number of cards equal to the sacrificed creature's power."));
        def.Should().NotBeNull();
        def!.AdditionalCostsOrEmpty.Should().HaveCount(1);
    }

    [Fact]
    public void Hatred_MatchesOracle()
    {
        var def = new HatredTemplate().TryBind(Ctx(
            "As an additional cost to cast this spell, pay X life. " +
            "Target creature gets +X/+0 until end of turn."));
        def.Should().NotBeNull();
        def!.HasVariableX.Should().BeTrue();
    }

    [Theory]
    // Real Scryfall oracle text — these must all bind to one of the new
    // sacrifice-cost templates via the live registry. Coverage proof.
    [InlineData("Blood for Bones", "As an additional cost to cast this spell, sacrifice a creature. Return a creature card from your graveyard to the battlefield, then return another creature card from your graveyard to your hand.")]
    [InlineData("Infernal Plunge", "As an additional cost to cast this spell, sacrifice a creature. Add {R}{R}{R}.")]
    [InlineData("Fling", "As an additional cost to cast this spell, sacrifice a creature. Fling deals damage equal to the sacrificed creature's power to any target.")]
    [InlineData("Thud", "As an additional cost to cast this spell, sacrifice a creature. Thud deals damage equal to the sacrificed creature's power to any target.")]
    [InlineData("Rite of Consumption", "As an additional cost to cast this spell, sacrifice a creature. Rite of Consumption deals damage equal to the sacrificed creature's power to target player or planeswalker. You gain life equal to the damage dealt this way.")]
    [InlineData("Ichor Explosion", "As an additional cost to cast this spell, sacrifice a creature. All creatures get -X/-X until end of turn, where X is the sacrificed creature's power.")]
    [InlineData("Life's Legacy", "As an additional cost to cast this spell, sacrifice a creature. Draw cards equal to the sacrificed creature's power.")]
    [InlineData("Momentous Fall", "As an additional cost to cast this spell, sacrifice a creature. You draw cards equal to the sacrificed creature's power, then you gain life equal to its toughness.")]
    [InlineData("Tormented Thoughts", "As an additional cost to cast this spell, sacrifice a creature. Target player discards a number of cards equal to the sacrificed creature's power.")]
    [InlineData("Hatred", "As an additional cost to cast this spell, pay X life. Target creature gets +X/+0 until end of turn.")]
    public void RealCardOracles_BindThroughRegistry(string name, string oracle)
    {
        var entity = new CardEntity { Name = name, OracleText = oracle };
        var def = Majik.Core.CardData.OracleSpellBinder.Bind(
            entity, new Player("A", 20), o => o,
            effects: new ContinuousEffectsService(),
            stack: null);
        def.Should().NotBeNull(
            $"{name} (oracle: {oracle}) should bind through the live OracleSpellBinder registry");
    }

    [Fact]
    public void Templates_DoNotMatchWithoutCostPrefix()
    {
        // Sanity: a card with the same effect text but no cost prefix must
        // not bind here — these templates are gated on the additional-cost
        // sentence in the raw oracle.
        var raw = "Return a creature card from your graveyard to the battlefield, then return another creature card from your graveyard to your hand.";
        new BloodForBonesTemplate().TryBind(Ctx(raw)).Should().BeNull();
        new InfernalPlungeTemplate().TryBind(Ctx("Add {R}{R}{R}.")).Should().BeNull();
        new FlingLikeTemplate().TryBind(Ctx("Fling deals damage equal to the sacrificed creature's power to any target.")).Should().BeNull();
    }
}
