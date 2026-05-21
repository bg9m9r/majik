using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Pattern-matches an instant/sorcery's oracle text to one of a handful of
/// canonical templates, returning a runnable <see cref="SpellDefinition"/>.
/// Returns null when no template matches — caller can fall back to a
/// vanilla shell so the card still loads.
///
/// Templates handled (first match wins):
///   - "Counter target spell."                       → counter
///   - "Deals N damage to any target."               → damage (creature or player)
///   - "Deals N damage to target player [or planeswalker]." → player damage
///   - "Destroy target creature."                    → destroy creature
///   - "Destroy target artifact or enchantment."     → destroy permanent
///   - "Draw N cards."                               → draw (caster)
///   - "Target player discards N cards."             → discard
///
/// Word numerals ("one", "two", "three", …) are translated to digits.
/// </summary>
public static class OracleSpellBinder
{
    internal static SpellTemplateRegistry Registry { get; } =
        new SpellTemplateRegistry(new ISpellTemplate[]
        {
            new SpellTemplates.Templates.Counter.CounterUnlessPayTemplate(),
            new SpellTemplates.Templates.Counter.CounterNoncreatureTemplate(),
            new SpellTemplates.Templates.Counter.CounterCreatureTemplate(),
            new SpellTemplates.Templates.Counter.CounterTargetSpellTemplate(),
            new SpellTemplates.Templates.Damage.DealsXDamageAnyTemplate(),
            new SpellTemplates.Templates.Damage.DamageAnyTargetTemplate(),
            new SpellTemplates.Templates.Damage.DamagePlayerTemplate(),
            new SpellTemplates.Templates.Damage.DealsDamageEachCreatureTemplate(),
            new SpellTemplates.Templates.Damage.EachOpponentLosesLifeTemplate(),
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
            new SpellTemplates.Templates.Library.MillTargetTemplate(),
            new SpellTemplates.Templates.Library.MillSelfTemplate(),
            new SpellTemplates.Templates.Library.EachOpponentMillsTemplate(),
            new SpellTemplates.Templates.Library.EachPlayerMillsTemplate(),
            new SpellTemplates.Templates.Library.SurveilSelfTemplate(),
            new SpellTemplates.Templates.Library.ScrySelfTemplate(),
            new SpellTemplates.Templates.Library.ScryNTemplate(),
            new SpellTemplates.Templates.Library.ReanimateFromGraveyardTemplate(),
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
            new SpellTemplates.Templates.Counters.PutPlusCounterTemplate(),
            new SpellTemplates.Templates.Counters.PutMinusCounterTemplate(),
            new SpellTemplates.Templates.Counters.CreaturesGetPlusCounterTemplate(),
            new SpellTemplates.Templates.Counters.PumpCreatureTemplate(),
            new SpellTemplates.Templates.Counters.GrantKeywordTilEotTemplate(),
            new SpellTemplates.Templates.Counters.CreaturesYouControlPumpTemplate(),
            new SpellTemplates.Templates.Counters.CreaturesYouControlGainKeywordTemplate(),
            new SpellTemplates.Templates.Tokens.InvestigateNTimesTemplate(),
            new SpellTemplates.Templates.Tokens.InvestigateSingleTemplate(),
            new SpellTemplates.Templates.Tokens.CreateTreasureTokensTemplate(),
            new SpellTemplates.Templates.Tokens.CreateFoodTokensTemplate(),
            new SpellTemplates.Templates.Tokens.CreateClueTokensTemplate(),
            new SpellTemplates.Templates.Tokens.CreateTokensTemplate(),
            new SpellTemplates.Templates.Bespoke.ThoughtseizePatternTemplate(),
            new SpellTemplates.Templates.Bespoke.MalevolentRumblePatternTemplate(),
        });

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
        Majik.Core.Stack.Stack? stack)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (resolver == null) throw new ArgumentNullException(nameof(resolver));

        var ctx = new SpellBindContext(entity, caster, resolver, effects, stack);
        if (Registry.TryBind(ctx) is { } fromRegistry) return fromRegistry;

        return null;
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

    // ---------- Primitives ----------

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

    private static void DrawCards_(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) return;
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
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
