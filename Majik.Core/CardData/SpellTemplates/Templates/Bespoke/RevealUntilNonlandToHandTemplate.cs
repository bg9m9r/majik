using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Treasure Hunt: "Reveal cards from the top of your library until you reveal
/// a nonland card, then put all cards revealed this way into your hand."
///
/// v1 simplifications:
/// - Deterministic walk (top-first).
/// - If the library has no nonland card, every card ends up in hand (matches
///   the literal reading of "until you reveal a nonland card").
/// </summary>
public sealed class RevealUntilNonlandToHandTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^reveal\s+cards\s+from\s+the\s+top\s+of\s+your\s+library\s+until\s+you\s+reveal\s+a\s+nonland\s+card,\s*then\s+put\s+all\s+cards\s+revealed\s+this\s+way\s+into\s+your\s+hand",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 70;
    public string Name => "RevealUntilNonlandToHand";
    public BotIntent Intent => BotIntent.Draw | BotIntent.Cantrip;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        TreasureHuntSpell(ctx.Caster);

    private static SpellDefinition TreasureHuntSpell(Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("Treasure Hunt", () =>
        {
            var revealed = new List<ICard>();
            foreach (var card in caster.Zones.Library.GetCards().ToList())
            {
                revealed.Add(card);
                if (!card.HasType(CardType.Land)) break;
            }

            foreach (var card in revealed)
            {
                caster.Zones.Library.RemoveCard(card);
                caster.Zones.Hand.AddCard(card);
                card.SetZone(ZoneType.Hand);
            }
        }) });
}
