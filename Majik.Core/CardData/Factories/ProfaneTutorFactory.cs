using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Profane Tutor (Time Spiral Remastered, no printed
/// mana cost — Suspend 2—{1}{B}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Suspend 2—{1}{B} (Rather than cast this card from your hand, pay
///    {1}{B} and exile it with two time counters on it. At the beginning of
///    your upkeep, remove a time counter. When the last is removed, you may
///    cast it without paying its mana cost.)
///    Search your library for a card, put that card into your hand, then
///    shuffle."
///
/// ## Why it gets its own factory
/// Profane Tutor combines two already-supported shapes that no single
/// JSON/binder template composes together:
/// - The Suspend wrapper + no-printed-mana-cost identity of
///   <see cref="LotusBloomFactory"/> / <see cref="SearchForTomorrowFactory"/>
///   (Suspend is surfaced via <see cref="SuspendAlternativeCost"/>, NOT yet
///   auto-detected from oracle text by the binders).
/// - The unconditional "search your library for a card, put it into your
///   hand, then shuffle" body of <see cref="GrimTutorFactory"/> — but
///   WITHOUT Grim Tutor's "You lose 3 life" rider. That bare search-to-hand
///   shape is exactly
///   <see cref="SearchSpellFactory.SearchLibrarySpell"/> with
///   <c>kindRaw = "card"</c> (any library card eligible, pick → hand,
///   shuffle).
///
/// ## Implemented (v1)
/// - Sorcery shape. Profane Tutor prints with NO mana cost (Scryfall
///   <c>mana_cost == ""</c>, distinct from <c>"{0}"</c> which renders the
///   zero pip). Per CR 117.7c / 202.1a a card with no mana cost can't be
///   cast for its mana cost, so the ONLY legal cast path is the Suspend
///   alt-cost. We stamp <see cref="ZoneType.Hand"/> via
///   <see cref="Card.AddRestrictedCastZone"/> (same plumbing as Lotus
///   Bloom) so any cast from hand is rejected by the ActionValidator;
///   suspend casts originate from exile and bypass that restriction.
/// - Resolve effect: search the caster's library for ANY card, put it into
///   the caster's hand, then shuffle (CR 701.19a / 701.20a). Empty library
///   or agent decline = no tutor; the library is still shuffled. Delegates
///   to <see cref="SearchSpellFactory.SearchLibrarySpell"/> with
///   <c>kindRaw = "card"</c>.
/// - Suspend 2—{1}{B} surfaced via <see cref="BuildSuspendCost"/> →
///   <see cref="SuspendAlternativeCost"/> (2 time counters, mana cost
///   {1}{B}). On each of the controller's upkeeps
///   <see cref="SuspendedCardRegistry"/> decrements the counter; when it
///   hits zero the spell may be cast for free (CR 702.62d). Mirrors the
///   Rift Bolt / Search for Tomorrow pattern; only the counter value (2)
///   and cost ({1}{B}) differ.
///
/// ## Deferred (v1 gaps)
/// - <b>Oracle binder discovery for Suspend</b>: Suspend is not yet
///   auto-detected from Scryfall oracle text by the binders; bots see it
///   via <see cref="BuildSuspendCost"/> or direct factory construction.
///   Same gap noted on Lotus Bloom / Search for Tomorrow.
/// </summary>
[CardName("Profane Tutor")]
public static class ProfaneTutorFactory
{
    public const string CardName = "Profane Tutor";

    /// <summary>
    /// Profane Tutor has no printed mana cost (Scryfall <c>mana_cost</c> is
    /// the empty string). Distinct from <c>"{0}"</c> (which renders the
    /// zero pip) — it can ONLY be cast via the Suspend alt-cost
    /// (CR 117.7c / 202.1a).
    /// </summary>
    public const string PrintedManaCost = "";

    public const string SuspendCostText = "{1}{B}";
    public const int SuspendTimeCounters = 2;

    /// <summary>
    /// Build a Profane Tutor sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the spell definition (tutor any card to hand) is
    /// built on-demand by <see cref="BuildSpellDefinition"/> once the caster
    /// reference is available at the SpellCastFlow wire-up site.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7c / 202.1a — no printed mana cost, so it can't be cast
        // from hand for its mana cost. Suspend is the only legal cast path;
        // a suspend cast originates from exile and bypasses this restriction.
        card.AddRestrictedCastZone(ZoneType.Hand);

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Profane Tutor uses when cast
    /// (only ever via the post-suspend free cast). No-target sorcery; on
    /// resolution searches the caster's library for ANY card, moves it to
    /// the caster's hand, then shuffles (CR 701.19a / 701.20a). No life
    /// loss (unlike Grim Tutor).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return SearchSpellFactory.SearchLibrarySpell(caster, "card");
    }

    /// <summary>The suspend alt cost printed on Profane Tutor —
    /// Suspend 2—{1}{B}. CR 702.62.</summary>
    public static SuspendAlternativeCost BuildSuspendCost() =>
        new(SuspendTimeCounters, ManaCost.Parse(SuspendCostText));
}
