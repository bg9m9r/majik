using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

[Trait("Color", "R")]
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

    // -----------------------------------------------------------------------
    // Kuldotha Rebirth — sacrifice-an-artifact additional cost + 3 Goblins
    // -----------------------------------------------------------------------

    private const string KuldothaOracle =
        "As an additional cost to cast this spell, sacrifice an artifact. " +
        "Create three 1/1 red Goblin creature tokens.";

    [Fact]
    public void KuldothaRebirth_BindsWithSacrificeAnArtifactAdditionalCost()
    {
        var def = new KuldothaRebirthTemplate().TryBind(Ctx(KuldothaOracle));

        def.Should().NotBeNull();
        def!.AdditionalCostsOrEmpty.Should().HaveCount(1,
            "CR 601.2f — Kuldotha Rebirth carries one additional cost");
        def.AdditionalCostsOrEmpty[0].Should().BeOfType<SacrificeAnArtifactAdditionalCost>();
        def.TargetRequests.Should().BeEmpty("the token clause resolves on the caster, no targets");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void KuldothaRebirth_DoesNotMatchWithoutSacrificeCostPrefix()
    {
        // Same token effect text WITHOUT the additional-cost prefix must NOT
        // bind here — the template is gated on the raw sac-artifact sentence,
        // so a generic "create three 1/1 red Goblins" card falls through.
        new KuldothaRebirthTemplate()
            .TryBind(Ctx("Create three 1/1 red Goblin creature tokens."))
            .Should().BeNull();
    }

    [Fact]
    public void KuldothaRebirth_Resolve_CreatesThreeRedGoblinTokens()
    {
        var caster = new Player("A", 20);
        caster.Zones.Battlefield.GetCards().Should().BeEmpty();

        var def = new KuldothaRebirthTemplate().TryBind(
            new SpellBindContext(
                new CardEntity { Name = "Kuldotha Rebirth", OracleText = KuldothaOracle },
                caster, o => o, new ContinuousEffectsService(), null));
        def.Should().NotBeNull();

        var chosen = new Majik.Core.Game.ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), Majik.Core.Players.Agents.ManaPayment.Empty);
        foreach (var effect in def!.EffectFactory(chosen))
            effect.Execute();

        var tokens = caster.Zones.Battlefield.GetCards().Cast<Creature>().ToList();
        tokens.Should().HaveCount(3, "Kuldotha Rebirth creates exactly three tokens");
        foreach (var token in tokens)
        {
            token.IsToken.Should().BeTrue();
            token.Name.Should().Be("Goblin");
            token.BasePower.Should().Be(1);
            token.BaseToughness.Should().Be(1);
            token.HasSubtype(CardSubtype.Goblin).Should().BeTrue(
                "CR 111.4 — token carries the Goblin creature subtype");
            CardColors.GetColors(token).Should().Contain(ManaColor.Red,
                "CR 111.4 — the token is explicitly red");
        }
    }

    [Fact]
    public void KuldothaRebirth_BindsThroughLiveRegistry_WithSacCost()
    {
        var entity = new CardEntity { Name = "Kuldotha Rebirth", OracleText = KuldothaOracle };
        var def = Majik.Core.CardData.OracleSpellBinder.Bind(
            entity, new Player("A", 20), o => o,
            effects: new ContinuousEffectsService(), stack: null);

        def.Should().NotBeNull("Kuldotha Rebirth must bind via the live OracleSpellBinder registry");
        def!.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeAnArtifactAdditionalCost>(
                "the bespoke template must win over the generic CreateTokens path so the " +
                "sacrifice-an-artifact cost (CR 601.2f) is not silently dropped");
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
