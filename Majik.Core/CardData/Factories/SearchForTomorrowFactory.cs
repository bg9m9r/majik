using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Search for Tomorrow (Time Spiral, {2}{G}).
///
/// Sorcery. Oracle text:
///   "Search your library for a basic land card, put it onto the
///    battlefield, then shuffle your library.
///    Suspend 2—{G} (Rather than cast this card from your hand, you may
///    pay {G} and exile it with two time counters on it. At the beginning
///    of your upkeep, remove a time counter. When the last is removed, you
///    may cast it without paying its mana cost.)"
///
/// ## Implemented (v1)
/// - Sorcery card with mana cost {2}{G}.
/// - Resolve effect: search library for a <b>basic land card</b> and put
///   it onto the battlefield <b>untapped</b> (distinct from Path to Exile
///   and Primeval Titan which enter tapped). Delegates to
///   <see cref="SearchSpellFactory.SearchLandToBattlefieldSpell"/> with
///   <c>kindRaw = "basic land"</c> and <c>tapped = false</c>. Deterministic
///   first-match fallback when no agent is registered.
///   Library shuffle deferred — same gap as every other search effect in
///   <see cref="SearchSpellFactory"/> (no IZone.Shuffle entry point yet).
/// - Suspend alt cost: pay {G}, exile Search for Tomorrow with 2 time
///   counters via <see cref="SuspendAlternativeCost"/>. On each of the
///   controller's upkeeps <see cref="SuspendedCardRegistry"/> decrements
///   the counter; when it hits zero the ready-callback fires and the spell
///   may be cast for free (CR 702.62d) — mirrors the Rift Bolt pattern
///   exactly, only the counter value (2) and cost ({G}) differ.
///
/// ## Deferred (v1 gaps)
/// - <b>Library shuffle</b> (CR 701.19c). No IZone.Shuffle entry point yet.
/// - <b>Oracle binder discovery</b>: Suspend is not yet auto-detected from
///   Scryfall oracle text by OracleSpellBinder. Bots see it via
///   <see cref="BuildSuspendCost"/> or direct factory construction.
/// </summary>
public static class SearchForTomorrowFactory
{
    public const string CardName = "Search for Tomorrow";
    public const string PrintedManaCost = "{2}{G}";
    public const string SuspendCostText = "{G}";
    public const int SuspendTimeCounters = 2;

    /// <summary>
    /// Build a Search for Tomorrow sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the spell definition (basic-land tutor to battlefield)
    /// is built on-demand by <see cref="BuildSpellDefinition"/> once the
    /// caster reference is available at the SpellCastFlow wire-up site.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Search for Tomorrow uses when
    /// cast — whether paid normally for {2}{G} or via the post-suspend free
    /// cast. No-target sorcery; on resolution searches the caster's library
    /// for a basic land card and moves it to the battlefield untapped.
    /// CR 701.19a (search) + CR 305 (land enters battlefield).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return SearchSpellFactory.SearchLandToBattlefieldSpell(caster, "basic land", tapped: false);
    }

    /// <summary>The suspend alt cost printed on Search for Tomorrow —
    /// Suspend 2—{G}. CR 702.62.</summary>
    public static SuspendAlternativeCost BuildSuspendCost() =>
        new(SuspendTimeCounters, ManaCost.Parse(SuspendCostText));
}
