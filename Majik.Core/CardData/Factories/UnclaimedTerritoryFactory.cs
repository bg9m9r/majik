using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Unclaimed Territory (Ixalan).
///
/// Land. Oracle text (verified against Scryfall 2026-06-02):
///   "As this land enters, choose a creature type.
///    {T}: Add {C}.
///    {T}: Add one mana of any color. Spend this mana only to cast a
///    creature spell of the chosen type."
///
/// This is the un-counter-less, plain-colourless cousin of
/// <see cref="CavernOfSoulsFactory"/>: same "choose a creature type as it
/// enters" ETB choice (CR 614.12-shaped) and the same restricted
/// "any colour, spend only on a creature spell of the chosen type" mana
/// ability, but it drops Cavern's "and that spell can't be countered"
/// rider and adds the plain unrestricted <c>{T}: Add {C}</c>.
///
/// ## Composition
/// - Base shape (name, Land type, and the unrestricted <c>{T}: Add {C}</c>
///   mana ability) is materialised from the embedded JSON definition
///   (<c>unclaimed-territory.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. {C} rolls into the generic
///   bucket per <see cref="ManaCost.Parse"/> (the {C} slot isn't separated
///   yet — same posture as Cavern of Souls).
/// - The ETB type choice and the five restricted "any colour" mana
///   abilities are layered on here because the JSON
///   <see cref="ManaAbilityDefinition"/> schema expresses neither a
///   <see cref="SpendRestriction"/> nor a creature-type ETB choice (same
///   posture as <see cref="CavernOfSoulsFactory"/> /
///   <see cref="RestlessSpireFactory"/>).
///
/// ## ETB type choice (v1)
/// When constructed via the <see cref="Create(Player, Func{Player, CardSubtype})"/>
/// overload, the chosen creature type is captured at construction time
/// (the engine has no ChooseSubtype agent prompt yet — same shape Cavern
/// of Souls and Pithing Needle use). Exposed via
/// <see cref="GetChosenType(Land)"/>. CR 614.12 — strictly the choice is
/// made as part of the ETB replacement; v1 captures it eagerly at factory
/// time, observationally equivalent in the current ETB pipeline.
///
/// ## Spend-restriction (v1 data, payment-gate deferred)
/// The five "any colour" <see cref="ManaAbility"/> instances stamp a
/// <see cref="SpendRestriction"/> with the predicate
/// <c>spell => spell.Card.HasType(Creature) &amp;&amp;
/// spell.Card.HasSubtype(chosenType)</c> (or just "creature spell" when no
/// type was chosen at factory time). The <c>{T}: Add {C}</c> ability is
/// <b>unrestricted</b> per the printed oracle — the restriction rider only
/// applies to the second activated mana ability. Payment-gate enforcement
/// (filtering tagged pool entries when paying a non-matching cost) is
/// deferred until <see cref="ManaPool"/> grows per-slot tags — the same
/// deferral Cavern of Souls carries; the restriction is observational
/// metadata on the ability until the resolver wires it up.
/// </summary>
[CardName("Unclaimed Territory")]
public static class UnclaimedTerritoryFactory
{
    public const string CardName = "Unclaimed Territory";
    public const string Slug = "unclaimed-territory";

    // Per-card chosen type stored alongside the land. CR 614.12-shaped ETB
    // choice captured at factory-build time (see class doc). Retrieval via
    // the static GetChosenType so the choice doesn't leak as a public
    // mutable property on Land — same pattern as Cavern of Souls.
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Land, ChoiceBox>
        _chosenType = new();

    private sealed class ChoiceBox { public CardSubtype Value; }

    /// <summary>
    /// Construct an Unclaimed Territory with no ETB type choice resolved.
    /// Suitable for card-shape / dispatcher tests; the chosen-type slot is
    /// unset and <see cref="GetChosenType"/> returns null. Mana abilities
    /// are still wired. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, typeChooser: null);

    /// <summary>
    /// Construct an Unclaimed Territory and resolve the printed ETB type
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
            // migration path Cavern of Souls / Pithing Needle have queued).
            _chosenType.Add(land, new ChoiceBox { Value = typeChooser(owner) });
        }

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color. Spend this mana only to cast a
        //   creature spell of the chosen type.
        //   Modelled as 5 ManaAbility instances (one per WUBRG) — same
        //   pattern as Cavern of Souls / Delighted Halfling. The
        //   source-picker satisfies any single colour pip via this land.
        //
        //   Each instance stamps a SpendRestriction so that — once the
        //   payment resolver grows tag-awareness — the generated mana only
        //   pays a pip on a creature spell of the chosen type. When the type
        //   was eagerly resolved at factory time the predicate tightens to
        //   "creature spell of <chosenType>"; otherwise it stays "creature
        //   spell" (still strictly narrower than vanilla mana). Unlike
        //   Cavern of Souls there is NO uncounterable rider.
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
    /// per-factory) — two Territories can name two different types.
    /// </summary>
    public static CardSubtype? GetChosenType(Land territory)
    {
        ArgumentNullException.ThrowIfNull(territory);
        return _chosenType.TryGetValue(territory, out var box) ? box.Value : null;
    }
}
