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
/// Recross the Paths: "Reveal cards from the top of your library until you
/// reveal a land card. Put that card onto the battlefield and the rest on the
/// bottom of your library in any order. Clash with an opponent…"
///
/// v1 simplifications:
/// - Deterministic walk (top-first); "any order" for the bottomed pile is
///   lossy — we append in reveal order.
/// - Clash rider is dropped at v1 (per spec). Bind still succeeds when the
///   trailing "Clash with an opponent" clause is present.
/// </summary>
public sealed class RevealUntilLandToBattlefieldTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^reveal\s+cards\s+from\s+the\s+top\s+of\s+your\s+library\s+until\s+you\s+reveal\s+a\s+land\s+card\.\s*put\s+that\s+card\s+onto\s+the\s+battlefield\s+and\s+the\s+rest\s+on\s+the\s+bottom\s+of\s+your\s+library\s+in\s+any\s+order",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 70;
    public string Name => "RevealUntilLandToBattlefield";
    public BotIntent Intent => BotIntent.Ramp;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        RecrossThePathsSpell(ctx.Caster);

    private static SpellDefinition RecrossThePathsSpell(Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("Recross the Paths", () =>
        {
            var revealed = new List<ICard>();
            ICard? land = null;
            foreach (var card in caster.Zones.Library.GetCards().ToList())
            {
                revealed.Add(card);
                if (card.HasType(CardType.Land))
                {
                    land = card;
                    break;
                }
            }

            foreach (var card in revealed)
            {
                caster.Zones.Library.RemoveCard(card);
                if (ReferenceEquals(card, land))
                {
                    caster.Zones.Battlefield.AddCard(card);
                    card.SetZone(ZoneType.Battlefield);
                }
                else
                {
                    // Bottomed — append (lossy "any order" v1).
                    caster.Zones.Library.AddCard(card);
                    card.SetZone(ZoneType.Library);
                }
            }

            // Clash rider deferred — no clash subsystem exists yet.
        }) });
}
