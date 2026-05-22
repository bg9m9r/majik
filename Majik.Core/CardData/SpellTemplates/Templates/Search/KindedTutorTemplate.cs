using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Search;

/// <summary>
/// Lossy fallback tutor for "Search your library for a/an &lt;modifier&gt; card,
/// ... put it/them into your hand, then shuffle." Catches kinded tutors whose
/// type rider is a creature subtype (Demon, Mercenary, Vehicle), a color
/// (white card, blue card), or anything else not in
/// <see cref="SearchLibraryTemplate"/>'s known-kind list. The runtime stub
/// resolves to the generic "any card" tutor — v1 ignores the type restriction
/// so the chosen card is whatever the agent picks first (CR 701.19a).
///
/// Priority 5 — strictly below the typed search templates so kind-specific
/// tutors (creature/artifact/land/etc) still bind to their typed stub. Only
/// cards that miss the typed regexes fall through here.
/// </summary>
public sealed class KindedTutorTemplate : ISpellTemplate
{
    // Anchors on the full tutor shape: "search your library for <head> card[s] …
    // put it/them into your hand … shuffle". The intervening prose (reveal it,
    // reveal those cards, and, then) is consumed by [^.]* so single-sentence
    // tutors of any shape are caught. Multi-sentence tutors (e.g. conditional
    // riders) are intentionally skipped — they need bespoke handling.
    // [^.]*? scoping keeps the head, middle, and tail in one logical sentence
    // (or at most one period between "into your hand" and "Then shuffle.", since
    // the Then-shuffle tail is a common two-sentence phrasing).
    private static readonly Regex Pattern = new(
        @"search\s+your\s+library\s+for\s+(?:a|an|up\s+to\s+\w+)\s+[^.]*?\bcards?[^.]*?put\s+(?:it|that\s+card|them|those\s+cards)\s+into\s+your\s+hand[^.]*?\.?\s*(?:then\s+)?shuffle",
        RegexOptions.IgnoreCase);

    public int Priority => 5;
    public string Name => "KindedTutor";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        SearchSpellFactory.SearchLibrarySpell(ctx.Caster, "card");
}
