using System.Text.RegularExpressions;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Aetherize (Gatecrash). Instant — {3}{U}.
///
///   "Return all attacking creatures to their owner's hand."
///
/// Bespoke template dispatching to <see cref="AetherizeFactory"/>. This is a
/// no-target mass bounce keyed off live combat state (CR 506.2 — every
/// creature attacking in the current combat), which no declarative bounce verb
/// expresses: <c>BounceTargetTemplate</c> / <c>MultiBounceTargetTemplate</c>
/// require a target, and <see cref="Misc.ReturnAllPermanentsTemplate"/> bounces
/// every permanent on the battlefield (no combat-state filter). So this matches
/// the exact oracle text and hands resolution to the factory's
/// <see cref="AetherizeFactory.BuildSpellDefinition"/>, whose default attacker
/// lookup reads the live per-game
/// <see cref="Majik.Core.Combat.CombatMembershipRegistryProvider.Current"/>.
/// </summary>
public sealed class AetherizeTemplate : ISpellTemplate
{
    // Whole-sentence anchor. The normalizer has already collapsed whitespace;
    // owner('s) is singular in the printed text ("their owner's hand").
    private static readonly Regex Pattern = new(
        @"^\s*return\s+all\s+attacking\s+creatures\s+to\s+their\s+owner'?s'?\s+hands?\.?\s*$",
        RegexOptions.IgnoreCase);

    // Above the generic ReturnAllPermanents template (Priority 70): the
    // combat-state filter is the more specific form. (ReturnAllPermanents
    // would not match "attacking creatures" anyway — it requires the literal
    // "permanents" — but rank it higher for intent clarity.)
    public int Priority => 72;
    public string Name => "Aetherize";
    public BotIntent Intent => BotIntent.Bounce;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        oracleText is not null && Pattern.IsMatch(oracleText)
            ? EmptyParams.Instance
            : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        // Default attacker lookup (live combat registry) + the caller's
        // ZoneService so the bounce fires zone-change events / replacements.
        AetherizeFactory.BuildSpellDefinition(attackerLookup: null, zoneService: ctx.Zones);
}
