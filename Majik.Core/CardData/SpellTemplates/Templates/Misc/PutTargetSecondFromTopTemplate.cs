using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Misc;

/// <summary>
/// Chronostutter / Oust / Isolation at Orthanc family:
///
///   "Put target creature into its owner's library second from the top."
///
/// Removes the target from the battlefield and inserts at library index 1
/// (top is index 0). v1 stub: when library has 0 or 1 cards, falls back to
/// inserting at the end of the library so the bounce still happens.
///
/// Trailing rider clauses (Oust's "Its controller gains 3 life") dropped at v1.
/// </summary>
public sealed class PutTargetSecondFromTopTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^put\s+target\s+(?:[\w-]+\s+)*?(?:creature|permanent|card|nonland\s+permanent)\s+into\s+its\s+owner'?s?\s+library\s+second\s+from\s+the\s+top",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "PutTargetSecondFromTop";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                return new IEffect[] { new Effect("to library 2nd from top", () =>
                {
                    if (target is not ICard card) return;
                    var owner = card.Owner;
                    if (owner == null) return;
                    switch (card.Zone)
                    {
                        case Majik.Core.Zones.ZoneType.Battlefield: owner.Zones.Battlefield.RemoveCard(card); break;
                        case Majik.Core.Zones.ZoneType.Graveyard:   owner.Zones.Graveyard.RemoveCard(card); break;
                        case Majik.Core.Zones.ZoneType.Hand:        owner.Zones.Hand.RemoveCard(card); break;
                    }
                    var libSize = owner.Zones.Library.GetCards().Count();
                    var insertIdx = libSize >= 1 ? 1 : libSize;
                    owner.Zones.Library.InsertCardAt(insertIdx, card);
                    card.SetZone(Majik.Core.Zones.ZoneType.Library);
                }) };
            });
    }
}
