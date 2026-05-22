using System.Text.Json;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Stack;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Entry point for binding an instant/sorcery's oracle text to a runnable
/// <see cref="SpellDefinition"/>. All matching lives in
/// <see cref="Registry"/> — one <see cref="ISpellTemplate"/> per oracle
/// pattern, grouped by family under <c>Majik.Core/CardData/SpellTemplates/Templates/</c>.
///
/// Returns <c>null</c> when no template matches; callers (e.g.
/// <see cref="ScryfallCardFactory.LookupSpellDefinition"/>) fall back
/// to a vanilla shell so the card still loads.
/// </summary>
public static class OracleSpellBinder
{
    private static readonly SpellTemplates.ClauseCompositionTemplate _composer =
        new SpellTemplates.ClauseCompositionTemplate();
    private static readonly SpellTemplates.ModalChooseOneTemplate _modal =
        new SpellTemplates.ModalChooseOneTemplate();

    public static SpellTemplateRegistry Registry { get; } = BuildRegistry();

    private static SpellTemplateRegistry BuildRegistry()
    {
        var reg = new SpellTemplateRegistry(BuildTemplateList());
        _composer.SetRegistry(reg);
        _modal.SetRegistry(reg);
        return reg;
    }

    private static ISpellTemplate[] BuildTemplateList() =>
        new ISpellTemplate[]
        {
            _modal,
            new SpellTemplates.Templates.Counter.CounterUnlessPayTemplate(),
            new SpellTemplates.Templates.Counter.CounterNoncreatureTemplate(),
            new SpellTemplates.Templates.Counter.CounterCreatureTemplate(),
            new SpellTemplates.Templates.Counter.CounterTargetSpellTemplate(),
            new SpellTemplates.Templates.Damage.DealsXDamageAnyTemplate(),
            new SpellTemplates.Templates.Damage.DealsXDamageCreatureTemplate(),
            new SpellTemplates.Templates.Damage.DamageCreatureTemplate(),
            new SpellTemplates.Templates.Damage.DamageAnyTargetTemplate(),
            new SpellTemplates.Templates.Damage.DamagePlayerTemplate(),
            new SpellTemplates.Templates.Damage.DealsXDamageEachCreatureTemplate(),
            new SpellTemplates.Templates.Damage.DealsDamageEachCreatureTemplate(),
            new SpellTemplates.Templates.Damage.FightTemplate(),
            new SpellTemplates.Templates.Damage.SelfFightTemplate(),
            new SpellTemplates.Templates.Damage.AsymmetricFightTemplate(),
            new SpellTemplates.Templates.Damage.ChooseAndFightTemplate(),
            new SpellTemplates.Templates.Damage.EachOpponentLosesLifeTemplate(),
            new SpellTemplates.Templates.Damage.DealsNDamageEachOpponentTemplate(),
            new SpellTemplates.Templates.Counters.AllCreaturesPumpTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyAllCreaturesTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyAllPermanentsTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyCreatureCmcLimitTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyUpToArtifactEnchantmentTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyNonlandPermanentTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyArtifactEnchantmentTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyCreatureTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyLandTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyPermanentTemplate(),
            new SpellTemplates.Templates.Resource.DrawCardsTemplate(),
            new SpellTemplates.Templates.Resource.DiscardTemplate(),
            new SpellTemplates.Templates.Resource.GainLifeTemplate(),
            new SpellTemplates.Templates.Resource.YouGainLifeTemplate(),
            new SpellTemplates.Templates.Resource.YouLoseLifeTemplate(),
            new SpellTemplates.Templates.Resource.EachPlayerDrawsTemplate(),
            new SpellTemplates.Templates.Resource.TargetPlayerLosesLifeTemplate(),
            new SpellTemplates.Templates.Library.LookAtTopPutKInHandTemplate(),
            new SpellTemplates.Templates.Library.ImpulseMayRevealFilterTemplate(),
            new SpellTemplates.Templates.Library.LookAtTopPutOneInHandTemplate(),
            new SpellTemplates.Templates.Library.MillTargetTemplate(),
            new SpellTemplates.Templates.Library.MillSelfTemplate(),
            new SpellTemplates.Templates.Library.EachOpponentMillsTemplate(),
            new SpellTemplates.Templates.Library.EachPlayerMillsTemplate(),
            new SpellTemplates.Templates.Library.SurveilSelfTemplate(),
            new SpellTemplates.Templates.Library.ScrySelfTemplate(),
            new SpellTemplates.Templates.Library.ScryNTemplate(),
            new SpellTemplates.Templates.Library.ReanimateToBattlefieldTemplate(),
            new SpellTemplates.Templates.Library.ReanimateFromGraveyardTemplate(),
            new SpellTemplates.Templates.Library.WheelTemplate(),
            new SpellTemplates.Templates.Library.ReturnAllFromGraveyardTemplate(),
            new SpellTemplates.Templates.Library.ExileFromGraveyardTemplate(),
            new SpellTemplates.Templates.Control.TapTargetTemplate(),
            new SpellTemplates.Templates.Control.UntapTargetTemplate(),
            new SpellTemplates.Templates.Control.BounceTargetTemplate(),
            new SpellTemplates.Templates.Control.ExileTargetTemplate(),
            new SpellTemplates.Templates.Control.GainControlTemplate(),
            new SpellTemplates.Templates.Search.SearchLandToBattlefieldTappedTemplate(),
            new SpellTemplates.Templates.Search.SearchLandToBattlefieldTemplate(),
            new SpellTemplates.Templates.Search.GreenSunsZenithPatternTemplate(),
            new SpellTemplates.Templates.Search.SearchLibraryTemplate(),
            new SpellTemplates.Templates.Search.GenericTutorTemplate(),
            new SpellTemplates.Templates.Search.KindedTutorTemplate(),
            new SpellTemplates.Templates.Counters.SupportTemplate(),
            new SpellTemplates.Templates.Counters.PutPlusCounterTemplate(),
            new SpellTemplates.Templates.Counters.PutMinusCounterTemplate(),
            new SpellTemplates.Templates.Counters.CreaturesGetPlusCounterTemplate(),
            new SpellTemplates.Templates.Counters.PumpCreatureTemplate(),
            new SpellTemplates.Templates.Counters.DebuffCreatureTemplate(),
            new SpellTemplates.Templates.Counters.GrantProtectionFromColorTemplate(),
            new SpellTemplates.Templates.Counters.VarPumpPerCreatureTemplate(),
            new SpellTemplates.Templates.Counters.SameNamePumpTemplate(),
            new SpellTemplates.Templates.Counters.MultiKeywordGrantTilEotTemplate(),
            new SpellTemplates.Templates.Counters.GrantKeywordTilEotTemplate(),
            new SpellTemplates.Templates.Counters.CreaturesYouControlPumpTemplate(),
            new SpellTemplates.Templates.Counters.CreaturesYouControlGainKeywordTemplate(),
            new SpellTemplates.Templates.Tokens.InvestigateNTimesTemplate(),
            new SpellTemplates.Templates.Tokens.InvestigateSingleTemplate(),
            new SpellTemplates.Templates.Tokens.CreateTreasureTokensTemplate(),
            new SpellTemplates.Templates.Tokens.CreateFoodTokensTemplate(),
            new SpellTemplates.Templates.Tokens.CreateClueTokensTemplate(),
            new SpellTemplates.Templates.Tokens.CreateCopyTokenTemplate(),
            new SpellTemplates.Templates.Tokens.MultiTargetCopyTokenTemplate(),
            new SpellTemplates.Templates.Tokens.PopulateTemplate(),
            new SpellTemplates.Templates.Tokens.CreateTokensTemplate(),
            new SpellTemplates.Templates.Misc.FogTemplate(),
            new SpellTemplates.Templates.Misc.TargetCantBeBlockedTemplate(),
            new SpellTemplates.Templates.Misc.UpToNCantBlockTemplate(),
            new SpellTemplates.Templates.Misc.UntapAllYourCreaturesTemplate(),
            new SpellTemplates.Templates.Misc.TargetPlayerSacrificesCreatureTemplate(),
            new SpellTemplates.Templates.Misc.PlusOneCounterEachYouControlTemplate(),
            new SpellTemplates.Templates.Misc.RegenerateTargetTemplate(),
            new SpellTemplates.Templates.Misc.PutTargetSecondFromTopTemplate(),
            new SpellTemplates.Templates.Misc.PutTargetOnTopOfLibraryTemplate(),
            new SpellTemplates.Templates.Misc.PutTargetOnBottomOfLibraryTemplate(),
            new SpellTemplates.Templates.Misc.TargetPlayerDrawsXTemplate(),
            new SpellTemplates.Templates.Misc.MixedSignPumpTemplate(),
            new SpellTemplates.Templates.Misc.VarXPumpTemplate(),
            new SpellTemplates.Templates.Misc.AddFixedManaTemplate(),
            new SpellTemplates.Templates.Misc.DestroyNTargetCreaturesTemplate(),
            new SpellTemplates.Templates.Misc.CounterUpToNTargetSpellsTemplate(),
            new SpellTemplates.Templates.Misc.TakeExtraTurnTemplate(),
            new SpellTemplates.Templates.Misc.ShuffleGraveyardIntoLibraryTemplate(),
            new SpellTemplates.Templates.Misc.EachOpponentSacrificesCreatureTemplate(),
            new SpellTemplates.Templates.Misc.DestroyAllBasicLandTypeTemplate(),
            new SpellTemplates.Templates.Misc.TargetPlayerDrawsNTemplate(),
            new SpellTemplates.Templates.Misc.MultiTargetCreaturesGainKeywordTemplate(),
            new SpellTemplates.Templates.Misc.MultiTargetCreaturesEachGetPumpTemplate(),
            new SpellTemplates.Templates.Misc.CreaturesYourOpponentsControlDebuffTemplate(),
            new SpellTemplates.Templates.Misc.AttackingCreaturesPumpTemplate(),
            new SpellTemplates.Templates.Misc.PermanentsYouControlGainKeywordTemplate(),
            new SpellTemplates.Templates.Misc.CreaturesCantBlockTemplate(),
            new SpellTemplates.Templates.Misc.ReturnAllPermanentsTemplate(),
            new SpellTemplates.Templates.Bespoke.DeflectingPalmFamilyTemplate(),
            new SpellTemplates.Templates.Bespoke.PumpThenReturnTemplate(),
            new SpellTemplates.Templates.Bespoke.ThoughtseizePatternTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealHandMayChooseTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealHandThenDiscardTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealHandThenExileTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealHandGraveOrHandExileTemplate(),
            new SpellTemplates.Templates.Bespoke.MalevolentRumblePatternTemplate(),
            new SpellTemplates.Templates.Bespoke.ExpressiveIterationTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealUntilNonlandDamageTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealUntilArtifactToBattlefieldTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealUntilLandToBattlefieldTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealUntilNonlandToHandTemplate(),
            _composer,
        };

    public static SpellDefinition? Bind(
        CardEntity entity,
        Player caster,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack) =>
        Bind(entity, caster, resolver, null, stack);

    public static SpellDefinition? Bind(
        CardEntity entity,
        Player caster,
        Func<object, object> resolver,
        Majik.Core.Effects.ContinuousEffectsService? effects,
        Majik.Core.Stack.Stack? stack) =>
        Bind(entity, caster, resolver, effects, stack, replacements: null);

    public static SpellDefinition? Bind(
        CardEntity entity,
        Player caster,
        Func<object, object> resolver,
        Majik.Core.Effects.ContinuousEffectsService? effects,
        Majik.Core.Stack.Stack? stack,
        Majik.Core.Effects.ReplacementBus? replacements) =>
        Bind(entity, caster, resolver, effects, stack, replacements, triggers: null, eventBus: null, zones: null);

    /// <summary>
    /// Full Bind overload — accepts the runtime managers that delayed-trigger
    /// and replacement-shield templates depend on. Existing callers pass
    /// nullable; templates gate themselves via <see cref="ISpellTemplate.CanBind"/>
    /// when a required manager is missing.
    /// </summary>
    public static SpellDefinition? Bind(
        CardEntity entity,
        Player caster,
        Func<object, object> resolver,
        Majik.Core.Effects.ContinuousEffectsService? effects,
        Majik.Core.Stack.Stack? stack,
        Majik.Core.Effects.ReplacementBus? replacements,
        Majik.Core.Abilities.TriggerManager? triggers,
        Majik.Core.Events.IEventBus? eventBus,
        Majik.Core.Services.ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);
        return Registry.TryBind(new SpellBindContext(
            entity, caster, resolver, effects, stack, replacements,
            triggers, eventBus, zones));
    }

    /// <summary>
    /// Compiled fast-path entry. When a card has a row in
    /// <c>CompiledSpellTemplates</c>, the caller (typically
    /// <see cref="ScryfallCardFactory.LookupSpellDefinition"/>) passes the
    /// stored <paramref name="templateName"/> + <paramref name="paramsJson"/>
    /// here and we skip the registry's regex scan entirely.
    ///
    /// Falls back to <see cref="Bind"/>'s live registry walk when:
    /// - <paramref name="templateName"/> doesn't resolve in
    ///   <see cref="Registry.OrderedTemplates"/> (template was removed or
    ///   renamed since compile time), or
    /// - the resolved template's <see cref="ISpellTemplate.CanBind"/>
    ///   returns false for this <paramref name="ctx"/> (e.g. a
    ///   Effects-gated template asked at vanilla-cast time).
    /// </summary>
    public static SpellDefinition? BindCompiled(
        string templateName,
        string paramsJson,
        CardEntity entity,
        Player caster,
        Func<object, object> resolver,
        Majik.Core.Effects.ContinuousEffectsService? effects,
        Majik.Core.Stack.Stack? stack,
        Majik.Core.Effects.ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(templateName);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        var ctx = new SpellBindContext(entity, caster, resolver, effects, stack, replacements);

        var template = Registry.OrderedTemplates
            .FirstOrDefault(t => string.Equals(t.Name, templateName, StringComparison.Ordinal));
        if (template is null)
        {
            // Compiled DB references a template this build doesn't know
            // about — fall back to live walk so the request still resolves.
            return Registry.TryBind(ctx);
        }

        if (!template.CanBind(ctx)) return null;

        var @params = string.IsNullOrEmpty(paramsJson)
            ? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(paramsJson)
              ?? new Dictionary<string, string>();

        return template.Rehydrate(@params, ctx).WithIntentStamp(template.Intent);
    }

    internal static void MoveToExile(ICard card)
    {
        var owner = card.Owner;
        if (owner != null)
        {
            if (card.Zone == ZoneType.Battlefield) owner.Zones.Battlefield.RemoveCard(card);
            else if (card.Zone == ZoneType.Graveyard) owner.Zones.Graveyard.RemoveCard(card);
            else if (card.Zone == ZoneType.Hand) owner.Zones.Hand.RemoveCard(card);
            else if (card.Zone == ZoneType.Library) owner.Zones.Library.RemoveCard(card);
            owner.Zones.Exile.AddCard(card);
        }
        card.SetZone(ZoneType.Exile);
    }

    internal static void DealDamage(object target, int n)
    {
        switch (target)
        {
            case Player p: p.LoseLife(n); break;
            case Creature c: c.TakeDamage(n); break;
        }
    }

    internal static void MoveToGraveyard(ICard card)
    {
        var owner = card.Owner;
        if (owner != null)
        {
            owner.Zones.Battlefield.RemoveCard(card);
            owner.Zones.Graveyard.AddCard(card);
        }
        card.SetZone(ZoneType.Graveyard);
    }

    internal static void RemoveFromStack(Majik.Core.Stack.Stack stack, IStackObject spell)
    {
        var keep = new List<IStackObject>();
        while (!stack.IsEmpty)
        {
            var top = stack.Pop()!;
            if (!ReferenceEquals(top, spell)) keep.Add(top);
        }
        for (var i = keep.Count - 1; i >= 0; i--)
        {
            stack.Push(keep[i]);
        }
    }
}
