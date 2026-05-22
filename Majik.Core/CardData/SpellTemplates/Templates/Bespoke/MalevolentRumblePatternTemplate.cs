using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

public sealed class MalevolentRumblePatternTemplate : ISpellTemplate
{
    // Malevolent Rumble: "Reveal the top four cards of your library. You may put
    // a permanent card from among them into your hand. Put the rest into your
    // graveyard. Create a 0/1 colorless Eldrazi Spawn creature token…"
    private static readonly Regex Pattern = new(
        @"reveal\s+the\s+top\s+four\s+cards.*permanent\s+card.*into\s+your\s+hand.*create\s+a\s+0/1\s+colorless\s+eldrazi\s+spawn",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 100;
    public string Name => "MalevolentRumblePattern";
    public BotIntent Intent => BotIntent.Cantrip | BotIntent.Draw;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        MalevolentRumbleSpell(ctx.Caster);

    /// <summary>
    /// Malevolent Rumble (Duskmourn).
    /// Reveal top 4 — auto-pick first permanent card to caster's hand, rest to
    /// graveyard, create one Eldrazi Spawn token.
    ///
    /// v1 gaps (deferred):
    /// - Real player choice among the revealed permanents (no prompt yet).
    /// - "You may put … into your hand" is optional — v1 always picks if a
    ///   permanent is present (opt-out awaits agent prompt system).
    /// </summary>
    private static SpellDefinition MalevolentRumbleSpell(Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("Malevolent Rumble", () =>
        {
            // Reveal top 4 (may be fewer if library is smaller).
            var top4 = caster.Zones.Library.GetCards().Take(4).ToList();

            if (top4.Count > 0)
            {
                // CR 603 / 700.3a: permanent cards — creature, artifact, enchantment,
                // land, planeswalker, battle.
                var permanentCard = top4.FirstOrDefault(c =>
                    c.HasType(CardType.Creature) ||
                    c.HasType(CardType.Artifact) ||
                    c.HasType(CardType.Enchantment) ||
                    c.HasType(CardType.Land) ||
                    c.HasType(CardType.Planeswalker));

                foreach (var c in top4)
                {
                    caster.Zones.Library.RemoveCard(c);
                    if (ReferenceEquals(c, permanentCard))
                    {
                        caster.Zones.Hand.AddCard(c);
                        c.SetZone(ZoneType.Hand);
                    }
                    else
                    {
                        caster.Zones.Graveyard.AddCard(c);
                        c.SetZone(ZoneType.Graveyard);
                    }
                }
            }

            // Token creation is unconditional — not gated on library size.
            TokenFactory.CreateEldraziSpawn(caster);
        }) });
}
