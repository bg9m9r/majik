using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData;

/// <summary>
/// Synthesizes mana abilities from a card's TypeLine + OracleText:
///  - Basic lands (subtype Mountain/Forest/Plains/Island/Swamp) → fixed
///    tap-for-one-color, regardless of oracle text (Scryfall sometimes
///    omits the rules-text on a basic).
///  - Otherwise, scan oracle text for "{T}: Add {COLOR}." patterns —
///    handles Llanowar Elves, Mox, etc.
///
/// Limitations: ignores cost prefixes that aren't pure {T}, multi-color
/// modal mana ("Add one mana of any color"), and conditional triggers.
/// Those are deliberate gaps left for a richer binder.
/// </summary>
public static class OracleManaBinder
{
    // Shared with Majik.Core.Effects.EffectiveManaAbilities so printed
    // basic-land binding and Layer-4 retyping-induced override stay in
    // sync. See <see cref="BasicLandManaColors"/>.
    private static IReadOnlyDictionary<CardSubtype, string> BasicLandColors
        => BasicLandManaColors.Map;

    // {T}: Add {R}.  or  {T}: Add {R}{R}.  or  {T}: Add one {G}.
    private static readonly Regex TapForManaRegex = new(
        @"\{T\}\s*:\s*Add\s+((?:\{[WUBRGC]\}\s*)+)",
        RegexOptions.IgnoreCase);

    // {T}: Add {R} or {W}.   — dual-color lands (Ravnica shocks, Triomes,
    // checks, painlands, etc.). Binds a separate ManaAbility per option;
    // the bot picks whichever colour it needs at activation time.
    private static readonly Regex TapForModalManaRegex = new(
        @"\{T\}\s*:\s*Add\s+(\{[WUBRGC]\})(?:\s*,\s*(\{[WUBRGC]\}))?(?:\s*,?\s*or\s+(\{[WUBRGC]\}))",
        RegexOptions.IgnoreCase);

    // {T}: Add one mana of any color.  — Mox Opal, City of Brass, etc.
    // Bind as five separate ManaAbility options (one per WUBRG).
    private static readonly Regex TapForAnyColorRegex = new(
        @"\{T\}\s*:\s*Add\s+one\s+mana\s+of\s+any\s+color",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Attaches the correct <see cref="ManaAbility"/> to a basic land that
    /// already has its controller set. Idempotent — if the card is not a
    /// Basic land with a known subtype, nothing is added.
    /// </summary>
    public static bool BindBasicLandMana(ICard card, Player controller)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        return TryBindBasicLand(card, controller);
    }

    public static void Bind(ICard card, CardEntity entity, Player controller)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        if (TryBindBasicLand(card, controller)) return;
        TryBindFromOracle(card, entity, controller);
    }

    private static bool TryBindBasicLand(ICard card, Player controller)
    {
        if (!card.HasSupertype(CardSupertype.Basic)) return false;

        foreach (var (subtype, color) in BasicLandColors)
        {
            if (card.HasSubtype(subtype))
            {
                card.AddAbility(new ManaAbility(card, controller, ManaCost.Parse(color)));
                return true;
            }
        }
        return false;
    }

    private static void TryBindFromOracle(ICard card, CardEntity entity, Player controller)
    {
        var text = entity.OracleText;
        if (string.IsNullOrWhiteSpace(text)) return;

        // Any-colour mana sources (Mox Opal, City of Brass, command tower).
        if (TapForAnyColorRegex.IsMatch(text))
        {
            foreach (var color in new[] { "W", "U", "B", "R", "G" })
            {
                card.AddAbility(new ManaAbility(card, controller, ManaCost.Parse(color)));
            }
            return;
        }

        // Dual / triple colour modal — bind each option as a separate
        // ManaAbility. Bot's source-picker scans abilities and picks the
        // first that produces the needed colour.
        foreach (Match m in TapForModalManaRegex.Matches(text))
        {
            for (var i = 1; i <= 3; i++)
            {
                if (!m.Groups[i].Success) continue;
                var raw = m.Groups[i].Value.Replace("{", "").Replace("}", "");
                card.AddAbility(new ManaAbility(card, controller, ManaCost.Parse(raw)));
            }
            return; // matched a modal — don't double-add via the non-modal regex
        }

        foreach (Match m in TapForManaRegex.Matches(text))
        {
            var symbols = m.Groups[1].Value;
            var stripped = symbols.Replace("{", "").Replace("}", "").Replace(" ", "");
            if (string.IsNullOrEmpty(stripped)) continue;

            card.AddAbility(new ManaAbility(card, controller, ManaCost.Parse(stripped)));
        }
    }
}
