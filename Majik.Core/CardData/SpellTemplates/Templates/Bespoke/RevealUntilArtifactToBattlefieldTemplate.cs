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
/// Madcap Experiment: "Reveal cards from the top of your library until you
/// reveal an artifact card. Put that card onto the battlefield and the rest on
/// the bottom of your library in a random order. ~ deals damage to you equal
/// to the number of cards revealed this way."
///
/// v1 simplifications:
/// - Deterministic walk (top-first); "random order" for the bottomed pile is
///   lossy — we append in reveal order.
/// - If the library has no artifact card, every card is revealed and bottomed
///   and the caster takes damage equal to the library size (matches CR rulings
///   for "until you reveal X" when X never appears).
/// </summary>
public sealed class RevealUntilArtifactToBattlefieldTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^reveal\s+cards\s+from\s+the\s+top\s+of\s+your\s+library\s+until\s+you\s+reveal\s+an\s+artifact\s+card\.\s*put\s+that\s+card\s+onto\s+the\s+battlefield\s+and\s+the\s+rest\s+on\s+the\s+bottom\s+of\s+your\s+library\s+in\s+a\s+random\s+order\.\s*[^.]*deals\s+damage\s+to\s+you\s+equal\s+to\s+the\s+number\s+of\s+cards\s+revealed\s+this\s+way",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 70;
    public string Name => "RevealUntilArtifactToBattlefield";
    public BotIntent Intent => BotIntent.Tutor | BotIntent.Ramp;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        MadcapExperimentSpell(ctx.Caster);

    private static SpellDefinition MadcapExperimentSpell(Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("Madcap Experiment", () =>
        {
            var revealed = new List<ICard>();
            ICard? artifact = null;
            foreach (var card in caster.Zones.Library.GetCards().ToList())
            {
                revealed.Add(card);
                if (card.HasType(CardType.Artifact))
                {
                    artifact = card;
                    break;
                }
            }

            foreach (var card in revealed)
            {
                caster.Zones.Library.RemoveCard(card);
                if (ReferenceEquals(card, artifact))
                {
                    caster.Zones.Battlefield.AddCard(card);
                    card.SetZone(ZoneType.Battlefield);
                }
                else
                {
                    // Bottomed — append (lossy "random order" v1).
                    caster.Zones.Library.AddCard(card);
                    card.SetZone(ZoneType.Library);
                }
            }

            // Damage to the caster equals the number of cards revealed.
            if (revealed.Count > 0) caster.LoseLife(revealed.Count);
        }) });
}
