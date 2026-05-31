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
    /// Reveal top 4, prompt the caster (CR 701.15) for a permanent card to
    /// put into hand (optional — "you may"), rest to graveyard, then create
    /// one Eldrazi Spawn token.
    /// </summary>
    private static SpellDefinition MalevolentRumbleSpell(Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("Malevolent Rumble", async ctx =>
        {
            // CR 701.15 — reveal top 4, may put a permanent card into hand,
            // rest into graveyard. Shared helper handles agent prompting
            // (including the "you may" opt-out + empty-eligible reveal so
            // the player still sees the reveal pile) and routes zone moves
            // through ZoneServiceRegistry so ETB-from-graveyard observers
            // see the discarded cards.
            await RevealAndChoose.RevealTopAndChooseAsync(
                ctx: ctx,
                caster: caster,
                count: 4,
                eligiblePredicate: IsPermanentCard,
                optional: true,
                label: "Permanent to put into hand",
                pickedDestination: ZoneType.Hand,
                restDestination: ZoneType.Graveyard,
                sourceTag: "malevolent-rumble").ConfigureAwait(false);

            // Token creation is unconditional — not gated on library size.
            TokenFactory.CreateEldraziSpawn(caster);
        }) });

    // CR 110.1 — permanent card types (artifact, creature, enchantment,
    // land, planeswalker, battle). Battle is in the printed list but
    // the engine's CardType enum predates MoM; add it here when shipped.
    private static bool IsPermanentCard(ICard c) =>
        c.HasType(CardType.Creature) ||
        c.HasType(CardType.Artifact) ||
        c.HasType(CardType.Enchantment) ||
        c.HasType(CardType.Land) ||
        c.HasType(CardType.Planeswalker);
}
