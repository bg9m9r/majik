using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cavern of Souls (Avacyn Restored).
///
/// Land. Oracle text:
///   "As Cavern of Souls enters, choose a creature type.
///    {T}: Add {C}.
///    {T}: Add one mana of any color. Spend this mana only to cast a
///    creature spell of the chosen type, and that spell can't be
///    countered."
///
/// ## Implemented (v1)
/// - Land with correct identity / owner / controller.
/// - <b>ETB type choice</b>: when constructed via the
///   <see cref="Create(Player, Func{Player, CardSubtype})"/> overload, the
///   chosen creature type is captured at construction time (engine has no
///   ChooseSubtype agent prompt yet — same shape Pithing Needle uses for
///   ChooseCardName). The choice is exposed via
///   <see cref="GetChosenType(Land)"/> so tests / dispatcher consumers
///   can introspect what was picked. CR 614.12 — strictly the choice is
///   made as part of the ETB replacement; v1 captures it eagerly at
///   factory time, observationally equivalent in the current ETB pipeline
///   (mirrors Pithing Needle's deferral note).
/// - <b>{T}: Add {C}</b> — first <see cref="ManaAbility"/> wired. {C}
///   currently rolls into the generic bucket per <see cref="ManaCost.Parse"/>
///   (see ManaCost.cs:170).
/// - <b>{T}: Add one mana of any color</b> — five <see cref="ManaAbility"/>
///   instances, one per WUBRG. Same shape as Delighted Halfling — the
///   mana picker chooses whichever colour is needed when paying spell
///   costs.
///
/// ## Spend-restriction (v1 data, payment-gate deferred)
/// - <b>"Spend this mana only to cast a creature spell of the chosen
///   type"</b>: the five "any colour" <see cref="ManaAbility"/> instances
///   stamp a <see cref="Majik.Core.Mana.SpendRestriction"/> with the
///   predicate <c>spell => spell.Card.HasType(Creature) &amp;&amp;
///   spell.Card.HasSubtype(chosenType)</c> (or just "creature spell"
///   when no type was chosen at factory time). When the chosen type is
///   resolved post-factory, the restriction always evaluates as
///   "creature spell" — the subtype refinement only kicks in when the
///   ETB choice was eagerly resolved. The {T}: Add {C} ability is
///   <b>unrestricted</b> per the printed oracle (the restriction rider
///   only applies to the second activated mana ability).
///
///   <b>Payment-gate enforcement</b> (filtering tagged pool entries
///   when paying a non-matching cost) is deferred until
///   <see cref="ManaPool"/> grows per-slot tags — today the pool stores
///   bucketed colour counts only. The restriction is observational
///   metadata on the ability until the resolver wires it up.
/// - <b>"That spell can't be countered"</b>: requires flagging the spell
///   object at cast time (when one of Cavern's mana entries pays a pip on
///   a chosen-type creature spell) and gating counter-spells in
///   <see cref="Majik.Core.Services.StackResolver"/>. Same deferral as
///   Delighted Halfling.
/// - <b>Agent-prompt integration</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   has no ChooseSubtype prompt yet. Selector closure stands in until
///   that lands — same pattern as Pithing Needle's <c>nameSelector</c>.
/// </summary>
[CardName("Cavern of Souls")]
public static class CavernOfSoulsFactory
{
    public const string CardName = "Cavern of Souls";

    // Per-card chosen type stored alongside the land. CR 614.12-shaped
    // ETB choice: the value is set as part of the ETB replacement;
    // engine-side we capture it at factory-build time. The retrieval API
    // is the static GetChosenType so the choice doesn't leak as a public
    // mutable property on Land (this is the only card with the concept
    // today; if more "choose a creature type" cards land — Coat of Arms,
    // Door of Destinies — extract into a shared Component).
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Land, ChoiceBox>
        _chosenType = new();

    private sealed class ChoiceBox { public CardSubtype Value; }

    /// <summary>
    /// Construct a Cavern of Souls with no ETB type choice resolved.
    /// Suitable for card-shape / dispatcher tests; the chosen-type slot
    /// is unset and <see cref="GetChosenType"/> returns null. Mana
    /// abilities are still wired.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, typeChooser: null);

    /// <summary>
    /// Construct a Cavern of Souls and resolve the printed ETB type
    /// choice eagerly via <paramref name="typeChooser"/>. The chosen
    /// subtype is stored on the card and retrievable via
    /// <see cref="GetChosenType"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="typeChooser">Resolves the creature subtype as the
    /// land enters. Called with the land's controller. May be null —
    /// the chosen-type slot stays empty (still legal — the {T}: Add one
    /// mana of any color ability still produces mana, just without the
    /// spend-restriction enforcement when that lands).</param>
    public static Land Create(Player owner, Func<Player, CardSubtype>? typeChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        if (typeChooser != null)
        {
            // v1: eager-resolve at factory time. When ETB-replacement
            // sequencing for "as ~ enters, choose ..." lands, this can
            // move into a replacement effect that fires from the ETB
            // event (same migration path Pithing Needle has queued).
            _chosenType.Add(land, new ChoiceBox { Value = typeChooser(owner) });
        }

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities don't use the stack. {C} lands as
        // +1 generic via ManaCost.Parse (see ManaCost.cs:170).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color. Spend this mana only to cast a
        //   creature spell of the chosen type, and that spell can't be
        //   countered.
        //   Modelled as 5 ManaAbility instances (one per WUBRG) — same
        //   pattern as Delighted Halfling and the Treasure token. The
        //   source-picker satisfies any single colour pip via this land.
        //
        //   Each instance stamps a SpendRestriction so that — once the
        //   payment resolver grows tag-awareness — the generated mana
        //   only pays a pip on a creature spell of the chosen type. When
        //   the type was eagerly resolved at factory time the predicate
        //   tightens to "creature spell of <chosenType>"; otherwise the
        //   predicate stays "creature spell" (still strictly narrower
        //   than vanilla mana — Lightning Bolt still gets rejected).
        //   The uncounterable rider is deferred — see class xmldoc.
        // ----------------------------------------------------------------
        var chosenType = _chosenType.TryGetValue(land, out var box) ? (CardSubtype?)box.Value : null;
        var restriction = chosenType.HasValue
            ? new SpendRestriction(
                $"creature spell of the chosen type ({chosenType.Value})",
                spell => spell.Card.HasType(CardType.Creature)
                         && spell.Card.HasSubtype(chosenType.Value))
            : new SpendRestriction(
                "creature spell",
                spell => spell.Card.HasType(CardType.Creature));

        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new ManaAbility(
                land, owner, ManaCost.Parse(color),
                canActivateCheck: null,
                spendRestriction: restriction));
        }

        return land;
    }

    /// <summary>
    /// Returns the chosen creature subtype if one was resolved at
    /// construction time, else null. The choice is per-card (not
    /// per-factory) — two Caverns can name two different types.
    /// </summary>
    public static CardSubtype? GetChosenType(Land cavern)
    {
        ArgumentNullException.ThrowIfNull(cavern);
        return _chosenType.TryGetValue(cavern, out var box) ? box.Value : null;
    }
}
