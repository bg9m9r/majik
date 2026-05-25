using System.Text.RegularExpressions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614 — Shock-land replacement effect binder. The Ravnica shock-land
/// cycle (Sacred Foundry, Steam Vents, Breeding Pool, Blood Crypt,
/// Hallowed Fountain, Stomping Ground, etc. — and modern reprints) all
/// share the same oracle clause:
///
///   "As this land enters, you may pay 2 life. If you don't, it enters
///    tapped."
///
/// We detect that clause and register a <see cref="ShockLandReplacement"/>
/// on the supplied <see cref="ReplacementBus"/>. The replacement watches
/// for the land's own ETB <see cref="ZoneMoveIntent"/> and applies the
/// auto-policy "pay 2 life if controller has life to spare; else enter
/// tapped" (until a proper agent prompt is wired through SpellCastFlow).
/// </summary>
public static class ShockLandBinder
{
    /// <summary>
    /// Oracle-text regex shared with
    /// <see cref="Majik.Core.CardData.Coverage.CoverageClassifier"/> so
    /// the classifier can recognise shock-land coverage without standing
    /// up a full <see cref="ReplacementBus"/>.
    /// </summary>
    public static readonly Regex ShockClause = new(
        @"as this (?:land |permanent |~ )?enters,?\s+you may pay 2 life\.\s+if you don'?t,?\s+it enters tapped",
        RegexOptions.IgnoreCase);

    public static bool Bind(ICard card, CardEntity entity, ReplacementBus replacements)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (replacements == null) throw new ArgumentNullException(nameof(replacements));

        var text = entity.OracleText ?? string.Empty;
        if (!ShockClause.IsMatch(text)) return false;

        replacements.Register(new ShockLandReplacement(card));
        return true;
    }
}
