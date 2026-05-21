using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Effects;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614.1c — unconditional ETB-tapped binder. Detects oracle text of the
/// form "[Card] enters tapped." (with or without "the battlefield") and
/// registers an <see cref="EntersTappedReplacement"/> on the supplied
/// <see cref="ReplacementBus"/>.
///
/// Conditional variants — "enters tapped unless …", "you may pay 2 life. If
/// you don't, it enters tapped" — are NOT matched here. The shock-land
/// variant has its own binder (<see cref="ShockLandBinder"/>); slow lands
/// and other conditional clauses await dedicated binders.
/// </summary>
public static class EntersTappedBinder
{
    // Matches "<anything> enters tapped." or "<anything> enters the battlefield tapped."
    // as a sentence (preceded by start-of-string or period). The leading clause
    // captures the card-name / "this permanent" / "~" form — we don't anchor on
    // the actual name so the binder works for any card whose first applicable
    // sentence asserts an unconditional ETB-tapped.
    private static readonly Regex EntersTappedSentence = new(
        @"(?:^|\.)\s*[^.]*?\benters\s+(?:the\s+battlefield\s+)?tapped\s*\.",
        RegexOptions.IgnoreCase);

    // Disqualifiers — clauses elsewhere in the oracle text that mean the
    // unconditional binder should NOT fire (a more specific binder owns it).
    private static readonly Regex ConditionalQualifier = new(
        @"\b(?:unless\s+|may\s+pay\b|if\s+you\s+don'?t\b)",
        RegexOptions.IgnoreCase);

    public static bool Bind(ICard card, CardEntity entity, ReplacementBus replacements)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (replacements == null) throw new ArgumentNullException(nameof(replacements));

        var text = entity.OracleText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (!EntersTappedSentence.IsMatch(text)) return false;
        if (ConditionalQualifier.IsMatch(text)) return false;

        replacements.Register(new EntersTappedReplacement(card));
        return true;
    }
}
