using System.Text.RegularExpressions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Effects;

namespace Majik.Core.CardData;

/// <summary>
/// CR 706.10 — detects "you may have this creature enter as a copy of …"
/// oracle text and registers an <see cref="EntersAsCopyReplacement"/> on
/// the supplied <see cref="ReplacementBus"/>.
///
/// Pool detection (in priority order):
///   - "of a creature you control" / "of an artifact or creature you control"
///     → <c>BattlefieldYouControl</c> (Mirror Image, Waxen Shapethief).
///   - "in a graveyard" / "in your graveyard" → <c>GraveyardAny</c> (Body
///     Double).
///   - Default fallthrough → <c>AnyBattlefield</c> (Clone, Stunt Double,
///     Clever Impersonator, Altered Ego, Evil Twin, Quicksilver
///     Gargantuan, Gigantoplasm, etc).
///
/// "Except …" riders are not parsed at v1; the copy mirrors printed
/// characteristics only.
/// </summary>
public static class EntersAsCopyBinder
{
    private static readonly Regex EntersAsCopyAnyone = new(
        @"\benters?\s+(?:the\s+battlefield\s+)?as\s+a\s+copy\s+of\s+(?<pool>[^.]*?\bbattlefield\b|[^.]*?\bgraveyard\b|[^.]*?\byou\s+control\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool Bind(
        ICard card,
        CardEntity entity,
        ReplacementBus replacements,
        ContinuousEffectsService effects)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (replacements == null) throw new ArgumentNullException(nameof(replacements));
        if (effects == null) throw new ArgumentNullException(nameof(effects));

        var text = entity.OracleText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = EntersAsCopyAnyone.Match(text);
        if (!m.Success) return false;

        var pool = ClassifyPool(m.Groups["pool"].Value);
        replacements.Register(new EntersAsCopyReplacement(card, pool, effects));
        return true;
    }

    private static EntersAsCopyReplacement.CopyPool ClassifyPool(string poolPhrase)
    {
        var lower = poolPhrase.ToLowerInvariant();
        if (lower.Contains("graveyard")) return EntersAsCopyReplacement.CopyPool.GraveyardAny;
        if (lower.Contains("you control")) return EntersAsCopyReplacement.CopyPool.BattlefieldYouControl;
        return EntersAsCopyReplacement.CopyPool.AnyBattlefield;
    }
}
