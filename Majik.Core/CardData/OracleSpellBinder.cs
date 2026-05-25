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
    private static readonly SpellTemplates.Templates.Bespoke.StriveTemplate _strive =
        new SpellTemplates.Templates.Bespoke.StriveTemplate();
    private static readonly SpellTemplates.Templates.Bespoke.ConvokeTemplate _convoke =
        new SpellTemplates.Templates.Bespoke.ConvokeTemplate();

    public static SpellTemplateRegistry Registry { get; } = BuildRegistry();

    private static SpellTemplateRegistry BuildRegistry()
    {
        var reg = new SpellTemplateRegistry(BuildTemplateList());
        _composer.SetRegistry(reg);
        _modal.SetRegistry(reg);
        _strive.SetRegistry(reg);
        _convoke.SetRegistry(reg);
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
            new SpellTemplates.Templates.Control.MultiBounceTargetTemplate(),
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
            new SpellTemplates.Templates.Bespoke.UntapUpToTwoAndPumpTemplate(),
            new SpellTemplates.Templates.Bespoke.MassDamageFromSourcePowerTemplate(),
            new SpellTemplates.Templates.Bespoke.PumpThenReturnTemplate(),
            new SpellTemplates.Templates.Bespoke.NextSpellCopyTemplate(),
            new SpellTemplates.Templates.Bespoke.ThoughtseizePatternTemplate(),
            new SpellTemplates.Templates.Bespoke.InquisitionOfKozilekPatternTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealHandMayChooseTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealHandThenDiscardTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealHandThenExileTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealHandGraveOrHandExileTemplate(),
            new SpellTemplates.Templates.Bespoke.MalevolentRumblePatternTemplate(),
            new SpellTemplates.Templates.Bespoke.ExpressiveIterationTemplate(),
            new SpellTemplates.Templates.Bespoke.BrainstormTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealUntilNonlandDamageTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealUntilArtifactToBattlefieldTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealUntilLandToBattlefieldTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealUntilNonlandToHandTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealNPutOneCreatureOntoBattlefieldTemplate(),
            new SpellTemplates.Templates.Bespoke.RevealNPutAnyNumberOntoBattlefieldTemplate(),
            new SpellTemplates.Templates.Bespoke.BloodForBonesTemplate(),
            new SpellTemplates.Templates.Bespoke.InfernalPlungeTemplate(),
            new SpellTemplates.Templates.Bespoke.FlingLikeTemplate(),
            new SpellTemplates.Templates.Bespoke.IchorExplosionTemplate(),
            new SpellTemplates.Templates.Bespoke.LifesLegacyTemplate(),
            new SpellTemplates.Templates.Bespoke.TormentedThoughtsTemplate(),
            new SpellTemplates.Templates.Bespoke.HatredTemplate(),
            new SpellTemplates.Templates.Bespoke.RedirectTemplate(),
            new SpellTemplates.Templates.Bespoke.PreventNextNDamageToAnyTargetTemplate(),
            new SpellTemplates.Templates.Bespoke.PreventAllDamageToYouAndPermanentsTemplate(),
            new SpellTemplates.Templates.Bespoke.PreventAllCombatDamageToPlayersTemplate(),
            _strive,
            _convoke,
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
        Majik.Core.Effects.ReplacementBus? replacements = null,
        Majik.Core.Abilities.TriggerManager? triggers = null,
        Majik.Core.Events.IEventBus? eventBus = null,
        Majik.Core.Services.ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(templateName);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        var ctx = new SpellBindContext(
            entity, caster, resolver, effects, stack, replacements,
            triggers, eventBus, zones);

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

    /// <summary>
    /// Move <paramref name="card"/> from the battlefield to its owner's
    /// graveyard. Backward-compatible overload — implicitly treats the
    /// move as a "destroy" effect (CR 701.7), which means
    /// <see cref="ZoneMoveReason.Destroy"/>'s indestructible (CR 702.12b)
    /// and regeneration (CR 701.15c) gates apply.
    ///
    /// Callers that aren't issuing a destroy (sacrifice, SBA, mill, etc.)
    /// should pass the explicit
    /// <see cref="MoveToGraveyard(ICard, ZoneMoveReason)"/> overload with
    /// a non-Destroy reason to bypass those gates.
    /// </summary>
    internal static void MoveToGraveyard(ICard card)
        => MoveToGraveyard(card, ZoneMoveReason.Destroy);

    /// <summary>
    /// CR 701.7 / 701.15 / 702.12 — move <paramref name="card"/> from the
    /// battlefield to its owner's graveyard, gated by
    /// <paramref name="reason"/>.
    ///
    /// When <paramref name="reason"/> is <see cref="ZoneMoveReason.Destroy"/>:
    /// <list type="number">
    ///   <item>If the target is a <see cref="Permanent"/> with the
    ///     "Indestructible" keyword, the move is cancelled
    ///     (CR 702.12b — "a permanent with indestructible can't be
    ///     destroyed").</item>
    ///   <item>Otherwise, if the target has one or more active
    ///     regeneration shields (<see cref="Permanent.HasRegenerationShield"/>),
    ///     one shield is consumed: the permanent is tapped, its damage
    ///     cleared, and the destroy is cancelled (CR 701.15c).</item>
    ///   <item>Otherwise, the card moves to its owner's graveyard.</item>
    /// </list>
    ///
    /// For any other <paramref name="reason"/> the gates are skipped —
    /// sacrifice (CR 701.16), SBA-driven death (which gates upstream in
    /// <see cref="Majik.Core.Rules.Sba.Checks.CreatureDeathCheck"/>), and
    /// other graveyard-bound moves all bypass indestructible /
    /// regeneration per CR 702.12b.
    /// </summary>
    internal static void MoveToGraveyard(ICard card, ZoneMoveReason reason)
    {
        ArgumentNullException.ThrowIfNull(card);

        var isDestroy = reason == ZoneMoveReason.Destroy
            || reason == ZoneMoveReason.DestroyNoRegeneration;
        if (isDestroy && card is Permanent permanent)
        {
            // CR 702.12b — indestructible permanents can't be destroyed.
            // Applies even to "can't be regenerated" wording — the
            // no-regen rider only suppresses CR 701.15, not indestructible.
            if (HasIndestructible(permanent)) return;

            // CR 701.15c — consume one regeneration shield (tap + clear
            // damage) in place of the destroy. Skipped when the destroy
            // effect explicitly says "can't be regenerated"
            // (e.g. Wrath of God, Damnation, Terminate).
            if (reason == ZoneMoveReason.Destroy
                && permanent.ConsumeRegenerationShield())
            {
                return;
            }
        }

        var owner = card.Owner;
        if (owner != null)
        {
            owner.Zones.Battlefield.RemoveCard(card);
            owner.Zones.Graveyard.AddCard(card);
        }
        card.SetZone(ZoneType.Graveyard);
    }

    private static bool HasIndestructible(Permanent permanent)
    {
        // Creature path goes through CombatAbilities so layer-system
        // grants (CR 613.1f) are picked up; non-creature permanents
        // (Darksteel Citadel, The One Ring under its mark, etc.) fall
        // back to the printed KeywordAbility marker.
        if (permanent is Creature creature)
        {
            return Majik.Core.Combat.CombatAbilities.HasIndestructible(creature);
        }

        return permanent.Abilities
            .OfType<Majik.Core.Abilities.KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Indestructible", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// CR 701.5 — pop <paramref name="spell"/> off <paramref name="stack"/>
    /// (the canonical "counter target spell" mechanic). Returns
    /// <c>true</c> when the spell was actually removed; <c>false</c> when
    /// the counter-attempt was vetoed by CR 701.5b — i.e. the target
    /// carries the cast-time <see cref="ISpell.CannotBeCountered"/>
    /// sentinel (stamped by <see cref="Majik.Core.Game.SpellCastFlow"/>
    /// off a <see cref="Majik.Core.Abilities.KeywordAbility"/>("Uncounterable")
    /// marker — Emrakul, the Aeons Torn cycle).
    ///
    /// Callers that pair the pop with a "card → graveyard" zone tail
    /// (<see cref="Majik.Core.Primitives.Fx.Counter"/> + every counter
    /// template) MUST gate that tail on this return so an uncounterable
    /// spell stays on the stack and resolves normally instead of being
    /// silently sent to the graveyard while the stack still references it.
    /// </summary>
    internal static bool RemoveFromStack(Majik.Core.Stack.Stack stack, IStackObject spell)
    {
        // CR 701.5b — "An uncounterable spell can't be countered."
        if (spell is Majik.Core.Spells.ISpell typed && typed.CannotBeCountered)
        {
            return false;
        }

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
        return true;
    }
}
