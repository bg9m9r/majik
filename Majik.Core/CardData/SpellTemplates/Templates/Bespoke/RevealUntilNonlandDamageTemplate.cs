using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Calibrated Blast: "Reveal cards from the top of your library until you reveal
/// a nonland card. Put the revealed cards on the bottom of your library in a
/// random order. When you reveal a nonland card this way, ~ deals damage equal
/// to that card's mana value to any target."
///
/// v1 simplifications:
/// - Reveal walk is deterministic (top-first iteration), not a random shuffle of
///   the revealed pile back to the bottom; the revealed list is appended in
///   reveal order.
/// - "When you reveal a nonland card this way" is folded into the spell effect
///   instead of being a separate delayed trigger — same observable outcome at
///   resolution time.
/// - If no nonland card is found (mono-land library), the damage portion is
///   skipped; the entire library is moved to the bottom in original order (a
///   no-op for a normal library iterator).
/// </summary>
public sealed class RevealUntilNonlandDamageTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^reveal\s+cards\s+from\s+the\s+top\s+of\s+your\s+library\s+until\s+you\s+reveal\s+a\s+nonland\s+card\.\s*put\s+the\s+revealed\s+cards\s+on\s+the\s+bottom\s+of\s+your\s+library\s+in\s+a\s+random\s+order\.\s*when\s+you\s+reveal\s+a\s+nonland\s+card\s+this\s+way,\s*[^.]*deals\s+damage\s+equal\s+to\s+that\s+card'?s\s+mana\s+value\s+to\s+any\s+target",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 70;
    public string Name => "RevealUntilNonlandDamage";
    public BotIntent Intent => BotIntent.Burn | BotIntent.Reach;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        CalibratedBlastSpell(ctx.Caster, ctx.Resolver);

    private static SpellDefinition CalibratedBlastSpell(Player caster, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("any target", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("Calibrated Blast", () =>
            {
                // Walk the library top-down, collecting cards until the first
                // nonland is hit. The nonland card is included in the revealed
                // list and is the damage-source for the trigger.
                var revealed = new List<ICard>();
                ICard? trigger = null;
                foreach (var card in caster.Zones.Library.GetCards().ToList())
                {
                    revealed.Add(card);
                    if (!card.HasType(CardType.Land))
                    {
                        trigger = card;
                        break;
                    }
                }

                // Put every revealed card on the bottom of the library
                // (append in reveal order — "random order" is lossy v1).
                foreach (var card in revealed)
                {
                    caster.Zones.Library.RemoveCard(card);
                    caster.Zones.Library.AddCard(card);
                    card.SetZone(ZoneType.Library);
                }

                if (trigger == null) return;
                var damage = ManaCost.Parse(trigger.ManaCost).TotalValue;
                if (damage <= 0) return;
                OracleSpellBinder.DealDamage(target, damage);
            }) };
        });
}
