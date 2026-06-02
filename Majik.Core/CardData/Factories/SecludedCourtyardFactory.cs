using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Secluded Courtyard (Dominaria United).
///
/// Land. Oracle text (verified against Scryfall 2026-06-02):
///   "As this land enters, choose a creature type.
///    {T}: Add {C}.
///    {T}: Add one mana of any color. Spend this mana only to cast a
///    creature spell of the chosen type or activate an ability of a
///    creature source of the chosen type."
///
/// This is a near-twin of <see cref="UnclaimedTerritoryFactory"/>: same
/// "choose a creature type as it enters" ETB choice (CR 614.12-shaped),
/// the same plain unrestricted <c>{T}: Add {C}</c>, and the same restricted
/// "any colour" mana ability. The only difference is the spend restriction
/// is slightly broader — Secluded Courtyard's mana may also be spent to
/// <i>activate an ability of a creature source of the chosen type</i>, where
/// Unclaimed Territory only allows casting a creature spell of the chosen
/// type. (Cavern of Souls is the other relative; it instead adds an
/// uncounterable rider.)
///
/// ## Composition
/// - Base shape (name, Land type, and the unrestricted <c>{T}: Add {C}</c>
///   mana ability) is materialised from the embedded JSON definition
///   (<c>secluded-courtyard.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. {C} rolls into the generic
///   bucket per <see cref="ManaCost.Parse"/> (the {C} slot isn't separated
///   yet — same posture as Unclaimed Territory / Cavern of Souls).
/// - The ETB type choice and the five restricted "any colour" mana
///   abilities are layered on here because the JSON
///   <see cref="ManaAbilityDefinition"/> schema expresses neither a
///   <see cref="SpendRestriction"/> nor a creature-type ETB choice (same
///   posture as <see cref="UnclaimedTerritoryFactory"/> /
///   <see cref="CavernOfSoulsFactory"/>).
///
/// ## ETB type choice (v1)
/// When constructed via the <see cref="Create(Player, Func{Player, CardSubtype})"/>
/// overload, the chosen creature type is captured at construction time
/// (the engine has no ChooseSubtype agent prompt yet — same shape Unclaimed
/// Territory / Cavern of Souls / Pithing Needle use). Exposed via
/// <see cref="GetChosenType(Land)"/>. CR 614.12 — strictly the choice is
/// made as part of the ETB replacement; v1 captures it eagerly at factory
/// time, observationally equivalent in the current ETB pipeline.
///
/// ## Spend-restriction (v1 data, payment-gate deferred)
/// The five "any colour" <see cref="ManaAbility"/> instances stamp a
/// <see cref="SpendRestriction"/>. Today the engine's
/// <see cref="SpendRestriction"/> predicate is keyed on the spell being
/// paid for (it gates casting, not ability activation), so the predicate
/// matches a creature spell of the chosen type — mirroring Unclaimed
/// Territory. The "or activate an ability of a creature source of the
/// chosen type" clause is captured in the restriction <i>description</i>
/// but is not yet enforceable: the payment resolver gates spell costs
/// only, and per-slot mana tagging / ability-activation payment gating is
/// deferred (the same <see cref="ManaPool"/> per-slot-tag deferral
/// Unclaimed Territory and Cavern of Souls carry). The <c>{T}: Add {C}</c>
/// ability is <b>unrestricted</b> per the printed oracle — the restriction
/// rider only applies to the second activated mana ability.
/// </summary>
[CardName("Secluded Courtyard")]
public static class SecludedCourtyardFactory
{
    public const string CardName = "Secluded Courtyard";
    public const string Slug = "secluded-courtyard";

    // Per-card chosen type stored alongside the land. CR 614.12-shaped ETB
    // choice captured at factory-build time (see class doc). Retrieval via
    // the static GetChosenType so the choice doesn't leak as a public
    // mutable property on Land — same pattern as Unclaimed Territory.
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Land, ChoiceBox>
        _chosenType = new();

    private sealed class ChoiceBox { public CardSubtype Value; }

    /// <summary>
    /// Construct a Secluded Courtyard with no ETB type choice resolved.
    /// Suitable for card-shape / dispatcher tests; the chosen-type slot is
    /// unset and <see cref="GetChosenType"/> returns null. Mana abilities
    /// are still wired. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, typeChooser: null);

    /// <summary>
    /// Construct a Secluded Courtyard and resolve the printed ETB type
    /// choice eagerly via <paramref name="typeChooser"/>. The chosen
    /// subtype is stored on the card and retrievable via
    /// <see cref="GetChosenType"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="typeChooser">Resolves the creature subtype as the land
    /// enters. Called with the land's controller. May be null — the
    /// chosen-type slot stays empty (still legal — the "any colour" ability
    /// still produces mana, just without the spend-restriction subtype
    /// refinement).</param>
    public static Land Create(Player owner, Func<Player, CardSubtype>? typeChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type, the
        // unrestricted {T}: Add {C} mana ability). The ETB choice + the five
        // restricted "any colour" mana abilities are layered on below — the
        // JSON ManaAbilityDefinition schema expresses neither.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        if (typeChooser != null)
        {
            // v1: eager-resolve at factory time. When ETB-replacement
            // sequencing for "as ~ enters, choose ..." lands, this can move
            // into a replacement effect that fires from the ETB event (same
            // migration path Unclaimed Territory / Cavern of Souls have).
            _chosenType.Add(land, new ChoiceBox { Value = typeChooser(owner) });
        }

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color. Spend this mana only to cast a
        //   creature spell of the chosen type OR activate an ability of a
        //   creature source of the chosen type.
        //   Modelled as 5 ManaAbility instances (one per WUBRG) — same
        //   pattern as Unclaimed Territory / Cavern of Souls. The
        //   source-picker satisfies any single colour pip via this land.
        //
        //   Each instance stamps a SpendRestriction so that — once the
        //   payment resolver grows tag-awareness — the generated mana only
        //   pays a pip on a creature spell of the chosen type. When the type
        //   was eagerly resolved at factory time the predicate tightens to
        //   "creature spell of <chosenType>"; otherwise it stays "creature
        //   spell" (still strictly narrower than vanilla mana). The
        //   "or activate an ability of a creature source of the chosen type"
        //   clause is captured in the description only — the SpendRestriction
        //   predicate gates spell casts, and ability-activation payment
        //   gating is deferred (same per-slot-tag ManaPool deferral as the
        //   relatives). Unlike Cavern of Souls there is NO uncounterable rider.
        // ----------------------------------------------------------------
        var chosenType = _chosenType.TryGetValue(land, out var box) ? (CardSubtype?)box.Value : null;
        var restriction = chosenType.HasValue
            ? new SpendRestriction(
                $"creature spell of the chosen type ({chosenType.Value}), or an ability of a creature source of that type",
                spell => spell.Card.HasType(CardType.Creature)
                         && spell.Card.HasSubtype(chosenType.Value))
            : new SpendRestriction(
                "creature spell, or an ability of a creature source of the chosen type",
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
    /// per-factory) — two Courtyards can name two different types.
    /// </summary>
    public static CardSubtype? GetChosenType(Land courtyard)
    {
        ArgumentNullException.ThrowIfNull(courtyard);
        return _chosenType.TryGetValue(courtyard, out var box) ? box.Value : null;
    }
}
