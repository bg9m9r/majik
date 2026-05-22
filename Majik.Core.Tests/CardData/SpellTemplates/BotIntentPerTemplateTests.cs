using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Control;
using Majik.Core.CardData.SpellTemplates.Templates.Counter;
using Majik.Core.CardData.SpellTemplates.Templates.Counters;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.CardData.SpellTemplates.Templates.Destroy;
using Majik.Core.CardData.SpellTemplates.Templates.Library;
using Majik.Core.CardData.SpellTemplates.Templates.Resource;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.CardData.SpellTemplates.Templates.Misc;
using Majik.Core.CardData.SpellTemplates.Templates.Tokens;
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

    public class Search
    {
        [Fact]
        public void SearchLandToBattlefield_Ramp() =>
            IntentOf(new SearchLandToBattlefieldTemplate()).Should().Be(BotIntent.Ramp);

        [Fact]
        public void SearchLandToBattlefieldTapped_Ramp() =>
            IntentOf(new SearchLandToBattlefieldTappedTemplate()).Should().Be(BotIntent.Ramp);

        [Fact]
        public void SearchLibrary_Tutor() =>
            IntentOf(new SearchLibraryTemplate()).Should().Be(BotIntent.Tutor);

        [Fact]
        public void GenericTutor_Tutor() =>
            IntentOf(new GenericTutorTemplate()).Should().Be(BotIntent.Tutor);

        [Fact]
        public void KindedTutor_Tutor() =>
            IntentOf(new KindedTutorTemplate()).Should().Be(BotIntent.Tutor);

        [Fact]
        public void GreenSunsZenithPattern_Tutor() =>
            IntentOf(new GreenSunsZenithPatternTemplate()).Should().Be(BotIntent.Tutor);
    }

    public class Library
    {
        [Fact]
        public void MillTarget_Mill() =>
            IntentOf(new MillTargetTemplate()).Should().Be(BotIntent.Mill);

        [Fact]
        public void EachOpponentMills_Mill() =>
            IntentOf(new EachOpponentMillsTemplate()).Should().Be(BotIntent.Mill);

        [Fact]
        public void EachPlayerMills_Mill() =>
            IntentOf(new EachPlayerMillsTemplate()).Should().Be(BotIntent.Mill);

        [Fact]
        public void MillSelf_None() =>
            IntentOf(new MillSelfTemplate()).Should().Be(BotIntent.None);

        [Fact]
        public void ReanimateFromGraveyard_Reanimate() =>
            IntentOf(new ReanimateFromGraveyardTemplate()).Should().Be(BotIntent.Reanimate);

        [Fact]
        public void ReanimateToBattlefield_Reanimate() =>
            IntentOf(new ReanimateToBattlefieldTemplate()).Should().Be(BotIntent.Reanimate);

        [Fact]
        public void ReturnAllFromGraveyard_Reanimate() =>
            IntentOf(new ReturnAllFromGraveyardTemplate()).Should().Be(BotIntent.Reanimate);

        [Fact]
        public void ScryN_Cantrip() =>
            IntentOf(new ScryNTemplate()).Should().Be(BotIntent.Cantrip);

        [Fact]
        public void ScrySelf_Cantrip() =>
            IntentOf(new ScrySelfTemplate()).Should().Be(BotIntent.Cantrip);

        [Fact]
        public void SurveilSelf_Cantrip() =>
            IntentOf(new SurveilSelfTemplate()).Should().Be(BotIntent.Cantrip);

        [Fact]
        public void LookAtTopPutOneInHand_Cantrip() =>
            IntentOf(new LookAtTopPutOneInHandTemplate()).Should().Be(BotIntent.Cantrip);

        [Fact]
        public void LookAtTopPutKInHand_Cantrip() =>
            IntentOf(new LookAtTopPutKInHandTemplate()).Should().Be(BotIntent.Cantrip);

        [Fact]
        public void Wheel_DrawDiscard() =>
            IntentOf(new WheelTemplate()).Should().Be(BotIntent.Draw | BotIntent.Discard);

        [Fact]
        public void ImpulseMayRevealFilter_TutorCantrip() =>
            IntentOf(new ImpulseMayRevealFilterTemplate()).Should().Be(BotIntent.Tutor | BotIntent.Cantrip);

        [Fact]
        public void ExileFromGraveyard_Removal() =>
            IntentOf(new ExileFromGraveyardTemplate()).Should().Be(BotIntent.Removal);
    }

    public class Tokens
    {
        [Fact]
        public void CreateTokens_Token() =>
            IntentOf(new CreateTokensTemplate()).Should().Be(BotIntent.Token);

        [Fact]
        public void CreateClueTokens_Token() =>
            IntentOf(new CreateClueTokensTemplate()).Should().Be(BotIntent.Token);

        [Fact]
        public void CreateTreasureTokens_Token() =>
            IntentOf(new CreateTreasureTokensTemplate()).Should().Be(BotIntent.Token);

        [Fact]
        public void CreateFoodTokens_TokenHeal() =>
            IntentOf(new CreateFoodTokensTemplate()).Should().Be(BotIntent.Token | BotIntent.Heal);

        [Fact]
        public void InvestigateNTimes_Token() =>
            IntentOf(new InvestigateNTimesTemplate()).Should().Be(BotIntent.Token);

        [Fact]
        public void InvestigateSingle_Token() =>
            IntentOf(new InvestigateSingleTemplate()).Should().Be(BotIntent.Token);

        [Fact]
        public void CreateCopyToken_Token() =>
            IntentOf(new CreateCopyTokenTemplate()).Should().Be(BotIntent.Token);

        [Fact]
        public void MultiTargetCopyToken_Token() =>
            IntentOf(new MultiTargetCopyTokenTemplate()).Should().Be(BotIntent.Token);

        [Fact]
        public void Populate_Token() =>
            IntentOf(new PopulateTemplate()).Should().Be(BotIntent.Token);
    }

    public class Bespoke
    {
        [Fact]
        public void ThoughtseizePattern_Discard() =>
            IntentOf(new ThoughtseizePatternTemplate()).Should().Be(BotIntent.Discard);

        [Fact]
        public void RevealHandThenDiscard_Discard() =>
            IntentOf(new RevealHandThenDiscardTemplate()).Should().Be(BotIntent.Discard);

        [Fact]
        public void RevealHandThenExile_Discard() =>
            IntentOf(new RevealHandThenExileTemplate()).Should().Be(BotIntent.Discard);

        [Fact]
        public void RevealHandMayChoose_DiscardRemoval() =>
            IntentOf(new RevealHandMayChooseTemplate()).Should().Be(BotIntent.Discard | BotIntent.Removal);

        [Fact]
        public void RevealHandGraveOrHandExile_DiscardRemoval() =>
            IntentOf(new RevealHandGraveOrHandExileTemplate()).Should().Be(BotIntent.Discard | BotIntent.Removal);

        [Fact]
        public void MalevolentRumblePattern_CantripDraw() =>
            IntentOf(new MalevolentRumblePatternTemplate()).Should().Be(BotIntent.Cantrip | BotIntent.Draw);

        [Fact]
        public void ExpressiveIteration_CantripDraw() =>
            IntentOf(new ExpressiveIterationTemplate()).Should().Be(BotIntent.Cantrip | BotIntent.Draw);
    }

    public class Misc
    {
        [Fact]
        public void Fog_None_AllowedListed() =>
            IntentOf(new FogTemplate()).Should().Be(BotIntent.None);

        [Fact]
        public void TargetCantBeBlocked_Buff() =>
            IntentOf(new TargetCantBeBlockedTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void UpToNCantBlock_Removal() =>
            IntentOf(new UpToNCantBlockTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void UntapAllYourCreatures_Buff() =>
            IntentOf(new UntapAllYourCreaturesTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void TargetPlayerSacrificesCreature_Removal() =>
            IntentOf(new TargetPlayerSacrificesCreatureTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void PlusOneCounterEachYouControl_Buff() =>
            IntentOf(new PlusOneCounterEachYouControlTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void RegenerateTarget_Protection() =>
            IntentOf(new RegenerateTargetTemplate()).Should().Be(BotIntent.Protection);

        [Fact]
        public void PutTargetOnTopOfLibrary_Removal() =>
            IntentOf(new PutTargetOnTopOfLibraryTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void PutTargetOnBottomOfLibrary_Removal() =>
            IntentOf(new PutTargetOnBottomOfLibraryTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void TargetPlayerDrawsX_Draw() =>
            IntentOf(new TargetPlayerDrawsXTemplate()).Should().Be(BotIntent.Draw);

        [Fact]
        public void MixedSignPump_BuffCombatTrick() =>
            IntentOf(new MixedSignPumpTemplate()).Should().Be(BotIntent.Buff | BotIntent.CombatTrick);

        [Fact]
        public void VarXPump_BuffCombatTrick() =>
            IntentOf(new VarXPumpTemplate()).Should().Be(BotIntent.Buff | BotIntent.CombatTrick);

        [Fact]
        public void AddFixedMana_Ramp() =>
            IntentOf(new AddFixedManaTemplate()).Should().Be(BotIntent.Ramp);

        [Fact]
        public void DestroyNTargetCreatures_Removal() =>
            IntentOf(new DestroyNTargetCreaturesTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void CounterUpToNTargetSpells_Counter() =>
            IntentOf(new CounterUpToNTargetSpellsTemplate()).Should().Be(BotIntent.Counter);

        [Fact]
        public void TakeExtraTurn_None_AllowedListed() =>
            IntentOf(new TakeExtraTurnTemplate()).Should().Be(BotIntent.None);

        [Fact]
        public void ShuffleGraveyardIntoLibrary_None_AllowedListed() =>
            IntentOf(new ShuffleGraveyardIntoLibraryTemplate()).Should().Be(BotIntent.None);

        [Fact]
        public void EachOpponentSacrificesCreature_Removal() =>
            IntentOf(new EachOpponentSacrificesCreatureTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void DestroyAllBasicLandType_Wrath() =>
            IntentOf(new DestroyAllBasicLandTypeTemplate()).Should().Be(BotIntent.Wrath);

        [Fact]
        public void TargetPlayerDrawsN_Draw() =>
            IntentOf(new TargetPlayerDrawsNTemplate()).Should().Be(BotIntent.Draw);

        [Fact]
        public void MultiTargetCreaturesGainKeyword_Buff() =>
            IntentOf(new MultiTargetCreaturesGainKeywordTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void MultiTargetCreaturesEachGetPump_Buff() =>
            IntentOf(new MultiTargetCreaturesEachGetPumpTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void CreaturesYourOpponentsControlDebuff_Removal() =>
            IntentOf(new CreaturesYourOpponentsControlDebuffTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void AttackingCreaturesPump_BuffCombatTrick() =>
            IntentOf(new AttackingCreaturesPumpTemplate()).Should().Be(BotIntent.Buff | BotIntent.CombatTrick);

        [Fact]
        public void PermanentsYouControlGainKeyword_Buff() =>
            IntentOf(new PermanentsYouControlGainKeywordTemplate()).Should().Be(BotIntent.Buff);

        [Fact]
        public void CreaturesCantBlock_Removal() =>
            IntentOf(new CreaturesCantBlockTemplate()).Should().Be(BotIntent.Removal);

        [Fact]
        public void ReturnAllPermanents_BounceWrath() =>
            IntentOf(new ReturnAllPermanentsTemplate()).Should().Be(BotIntent.Bounce | BotIntent.Wrath);
    }
}
