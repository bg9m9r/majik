using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Control;
using Majik.Core.CardData.SpellTemplates.Templates.Counter;
using Majik.Core.CardData.SpellTemplates.Templates.Counters;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.CardData.SpellTemplates.Templates.Destroy;
using Majik.Core.CardData.SpellTemplates.Templates.Resource;
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

    public class Counter
    {
        [Fact]
        public void CounterTargetSpell_Counter() =>
            IntentOf(new CounterTargetSpellTemplate()).Should().Be(BotIntent.Counter);

        [Fact]
        public void CounterCreature_Counter() =>
            IntentOf(new CounterCreatureTemplate()).Should().Be(BotIntent.Counter);

        [Fact]
        public void CounterNoncreature_Counter() =>
            IntentOf(new CounterNoncreatureTemplate()).Should().Be(BotIntent.Counter);

        [Fact]
        public void CounterUnlessPay_Counter() =>
            IntentOf(new CounterUnlessPayTemplate()).Should().Be(BotIntent.Counter);
    }

    public class Counters
    {
        [Fact]
        public void PumpCreature_BuffCombatTrick() =>
            IntentOf(new PumpCreatureTemplate()).Should().Be(BotIntent.Buff | BotIntent.CombatTrick);

        [Fact]
        public void PutPlusCounter_Buff() =>
            IntentOf(new PutPlusCounterTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void PutMinusCounter_Removal() =>
            IntentOf(new PutMinusCounterTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void DebuffCreature_Removal() =>
            IntentOf(new DebuffCreatureTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void AllCreaturesPump_Buff() =>
            IntentOf(new AllCreaturesPumpTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void CreaturesYouControlPump_Buff() =>
            IntentOf(new CreaturesYouControlPumpTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void CreaturesGetPlusCounter_Buff() =>
            IntentOf(new CreaturesGetPlusCounterTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void SameNamePump_Buff() =>
            IntentOf(new SameNamePumpTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void VarPumpPerCreature_Buff() =>
            IntentOf(new VarPumpPerCreatureTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void GrantKeywordTilEot_CombatTrickBuff() =>
            IntentOf(new GrantKeywordTilEotTemplate()).Should().Be(BotIntent.CombatTrick | BotIntent.Buff);

        [Fact]
        public void MultiKeywordGrantTilEot_CombatTrickBuff() =>
            IntentOf(new MultiKeywordGrantTilEotTemplate()).Should().Be(BotIntent.CombatTrick | BotIntent.Buff);

        [Fact]
        public void GrantProtectionFromColor_Protection() =>
            IntentOf(new GrantProtectionFromColorTemplate()).Should().Be(BotIntent.Protection);

        [Fact]
        public void CreaturesYouControlGainKeyword_Buff() =>
            IntentOf(new CreaturesYouControlGainKeywordTemplate()).Should().Be(BotIntent.Buff);
    }

    public class Control
    {
        [Fact]
        public void BounceTarget_Bounce() =>
            IntentOf(new BounceTargetTemplate()).Should().Be(BotIntent.Bounce);

        [Fact]
        public void ExileTarget_Removal() =>
            IntentOf(new ExileTargetTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void GainControl_Removal() =>
            IntentOf(new GainControlTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void TapTarget_Removal() =>
            IntentOf(new TapTargetTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void UntapTarget_Buff() =>
            IntentOf(new UntapTargetTemplate()).Should().Be(BotIntent.Buff);
    }

    public class Resource
    {
        [Fact]
        public void DrawCards_Draw() =>
            IntentOf(new DrawCardsTemplate()).Should().Be(BotIntent.Draw);

        [Fact]
        public void EachPlayerDraws_Draw() =>
            IntentOf(new EachPlayerDrawsTemplate()).Should().Be(BotIntent.Draw);

        [Fact]
        public void Discard_Discard() =>
            IntentOf(new DiscardTemplate()).Should().Be(BotIntent.Discard);

        [Fact]
        public void GainLife_Heal() =>
            IntentOf(new GainLifeTemplate()).Should().Be(BotIntent.Heal);

        [Fact]
        public void YouGainLife_Heal() =>
            IntentOf(new YouGainLifeTemplate()).Should().Be(BotIntent.Heal);

        [Fact]
        public void TargetPlayerLosesLife_Burn() =>
            IntentOf(new TargetPlayerLosesLifeTemplate()).Should().Be(BotIntent.Burn);

        [Fact]
        public void YouLoseLife_None() =>
            IntentOf(new YouLoseLifeTemplate()).Should().Be(BotIntent.None);
    }
}
