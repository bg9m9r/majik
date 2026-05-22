using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.CardData.SpellTemplates.Templates.Destroy;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// Per-template <see cref="ISpellTemplate.Intent"/> assertions, grouped
/// by <c>Templates/&lt;dir&gt;/</c> family. Each template is constructed
/// directly and its declared <c>Intent</c> read via the interface (so a
/// missing override falls through to the default and fails the
/// assertion).
/// </summary>
public class BotIntentPerTemplateTests
{
    private static BotIntent IntentOf(ISpellTemplate template) => template.Intent;

    public class Damage
    {
        [Fact]
        public void DamageAnyTarget_BurnReach() =>
            IntentOf(new DamageAnyTargetTemplate()).Should().Be(BotIntent.Burn | BotIntent.Reach);

        [Fact]
        public void DamageCreature_Burn() =>
            IntentOf(new DamageCreatureTemplate()).Should().Be(BotIntent.Burn);

        [Fact]
        public void DamagePlayer_BurnReach() =>
            IntentOf(new DamagePlayerTemplate()).Should().Be(BotIntent.Burn | BotIntent.Reach);

        [Fact]
        public void DealsXDamageAny_BurnReach() =>
            IntentOf(new DealsXDamageAnyTemplate()).Should().Be(BotIntent.Burn | BotIntent.Reach);

        [Fact]
        public void DealsXDamageCreature_Burn() =>
            IntentOf(new DealsXDamageCreatureTemplate()).Should().Be(BotIntent.Burn);

        [Fact]
        public void DealsDamageEachCreature_Wrath() =>
            IntentOf(new DealsDamageEachCreatureTemplate()).Should().Be(BotIntent.Wrath);

        [Fact]
        public void DealsXDamageEachCreature_Wrath() =>
            IntentOf(new DealsXDamageEachCreatureTemplate()).Should().Be(BotIntent.Wrath);

        [Fact]
        public void DealsNDamageEachOpponent_BurnReach() =>
            IntentOf(new DealsNDamageEachOpponentTemplate()).Should().Be(BotIntent.Burn | BotIntent.Reach);

        [Fact]
        public void EachOpponentLosesLife_BurnReach() =>
            IntentOf(new EachOpponentLosesLifeTemplate()).Should().Be(BotIntent.Burn | BotIntent.Reach);

        [Fact]
        public void Fight_Burn() =>
            IntentOf(new FightTemplate()).Should().Be(BotIntent.Burn);

        [Fact]
        public void SelfFight_Burn() =>
            IntentOf(new SelfFightTemplate()).Should().Be(BotIntent.Burn);

        [Fact]
        public void AsymmetricFight_Burn() =>
            IntentOf(new AsymmetricFightTemplate()).Should().Be(BotIntent.Burn);

        [Fact]
        public void ChooseAndFight_Burn() =>
            IntentOf(new ChooseAndFightTemplate()).Should().Be(BotIntent.Burn);
    }

    public class Destroy
    {
        [Fact]
        public void DestroyAllCreatures_Wrath() =>
            IntentOf(new DestroyAllCreaturesTemplate()).Should().Be(BotIntent.Wrath);

        [Fact]
        public void DestroyAllPermanents_Wrath() =>
            IntentOf(new DestroyAllPermanentsTemplate()).Should().Be(BotIntent.Wrath);

        [Fact]
        public void DestroyCreature_Removal() =>
            IntentOf(new DestroyCreatureTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void DestroyCreatureCmcLimit_Removal() =>
            IntentOf(new DestroyCreatureCmcLimitTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void DestroyArtifactEnchantment_Removal() =>
            IntentOf(new DestroyArtifactEnchantmentTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void DestroyLand_Removal() =>
            IntentOf(new DestroyLandTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void DestroyNonlandPermanent_Removal() =>
            IntentOf(new DestroyNonlandPermanentTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void DestroyPermanent_Removal() =>
            IntentOf(new DestroyPermanentTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void DestroyUpToArtifactEnchantment_Removal() =>
            IntentOf(new DestroyUpToArtifactEnchantmentTemplate()).Should().Be(BotIntent.Removal);
    }
}
