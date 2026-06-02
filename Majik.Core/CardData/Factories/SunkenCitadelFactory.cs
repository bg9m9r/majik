using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sunken Citadel (Tarkir: Dragonstorm).
///
/// Land — Cave. Oracle text (verified against Scryfall):
///   "This land enters tapped. As it enters, choose a color.
///    {T}: Add one mana of the chosen color.
///    {T}: Add two mana of the chosen color. Spend this mana only to
///    activate abilities of land sources."
///
/// <para>
/// The Land — Cave identity is declared in
/// <c>Majik.Core/CardData/Cards/sunken-citadel.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>. As with
/// <see cref="ColdsteelHeartFactory"/> / <see cref="TempleOfTheDragonQueenFactory"/>,
/// the {T} mana abilities' produced color isn't known until the "as it enters,
/// choose a color" decision (CR 614.12) is made, so both mana abilities are
/// wired in the factory once the chosen color is supplied — not declared in
/// JSON.
/// </para>
///
/// <para>
/// ## Choose a color (CR 614.12 — "as this enters" replacement)
/// "As it enters, choose a color." is resolved up front: the chosen
/// <see cref="ManaColor"/> is supplied to the full overload. A live agent
/// prompt for the choice is deferred engine-wide (same posture as
/// <see cref="ColdsteelHeartFactory"/> / <see cref="TempleOfTheDragonQueenFactory"/> /
/// <see cref="UtopiaSprawlFactory"/>); callers / tests pass the already-chosen
/// color. Both {T} mana abilities then produce exactly that color (CR 605.1a —
/// mana abilities don't use the stack).
/// </para>
///
/// <para>
/// ## Enters tapped (CR 614.1c) — unconditional
/// "This land enters tapped." is an unconditional ETB-tapped clause. On the
/// production load path it is registered automatically by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (the seed oracle text
/// matches its sentence pattern with no conditional qualifier). When a
/// <see cref="ReplacementBus"/> is supplied to the full overload here, an
/// <see cref="EntersTappedReplacement"/> is registered directly so the
/// behaviour is exercisable in isolation (mirrors the ETB-tapped wiring in
/// <see cref="ColdsteelHeartFactory"/>).
/// </para>
///
/// <para>
/// ## Restricted double-mana ability (CR 605.1a / 106.4)
/// "{T}: Add two mana of the chosen color. Spend this mana only to activate
/// abilities of land sources." — a second <see cref="ManaAbility"/> producing
/// two pips of the chosen color, stamped with a
/// <see cref="Majik.Core.Mana.SpendRestriction"/>. This is the land-ability
/// analogue of Eldrazi Temple's "{T}: Add {C}{C}. Spend this mana only to cast
/// Eldrazi spells or activate abilities of Eldrazi" rider (see
/// <see cref="EldraziTempleFactory"/>): the restriction's
/// <see cref="Majik.Core.Mana.SpendRestriction.Predicate"/> is spell-side only
/// (<c>Func&lt;ISpell, bool&gt;</c>), and Sunken Citadel's restriction permits
/// <i>no</i> spell — the mana may only pay activation costs of land sources'
/// abilities — so the predicate returns <c>false</c> for every spell.
///
/// <b>Payment-gate enforcement</b> (filtering tagged pool entries when paying a
/// non-land-ability cost) is deferred until
/// <see cref="Majik.Core.ValueObjects.ManaPool"/> grows per-slot tags — today
/// the pool stores bucketed colour counts only, so the rider is observational
/// metadata on the ability. Same posture as Eldrazi Temple / Cavern of Souls /
/// Delighted Halfling; all unlock together when the resolver consumes the tag.
/// </para>
///
/// <para>
/// The shape-only single-arg dispatcher path constructs identity only: no color
/// is known, so no mana ability is attached and no ETB-tapped replacement is
/// wired (matching every other ETB-replacement factory's single-arg posture).
/// </para>
/// </summary>
[CardName("Sunken Citadel")]
public static class SunkenCitadelFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sunken-citadel");

    // CR 106.4 — "Spend this mana only to activate abilities of land sources."
    // Predicate is spell-side only (Func<ISpell,bool>); this mana can never pay
    // a spell pip, only an activation cost of a land source's ability — so the
    // predicate denies every spell. Shared static instance keeps the rider
    // structurally stable (SpendRestriction delegate equality is by-reference).
    private static readonly SpendRestriction LandAbilitiesOnly =
        new("land source ability", _ => false);

    /// <summary>Construct Sunken Citadel owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no chosen color, no mana
    /// abilities, no ETB-tapped replacement wired).</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }

    /// <summary>
    /// Construct a fully-wired Sunken Citadel.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenColor">The color chosen "as it enters" (CR 614.12).
    /// Must be one of W/U/B/R/G — both {T} abilities add mana of that color.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> for the
    /// unconditional "enters tapped" wiring (CR 614.1c). When <c>null</c>, only
    /// the mana abilities are attached (the production load path wires
    /// ETB-tapped via <see cref="Majik.Core.CardData.EntersTappedBinder"/>).</param>
    public static Land Create(
        Player owner,
        ManaColor chosenColor,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = Create(owner);

        // {T}: Add one mana of the chosen color (CR 605.1a). One pip of the
        // up-front-chosen color; throws for a non-W/U/B/R/G choice.
        var produced = ManaCostForColor(chosenColor);
        land.AddAbility(new ManaAbility(land, owner, produced));

        // {T}: Add two mana of the chosen color. Spend this mana only to
        // activate abilities of land sources (CR 605.1a / 106.4). Two pips of
        // the chosen color, stamped with the land-ability-only spend rider.
        // Payment-gate enforcement is deferred (see class xmldoc).
        var producedDouble = DoubleManaCostForColor(chosenColor);
        land.AddAbility(new ManaAbility(
            land, owner, producedDouble,
            canActivateCheck: null,
            spendRestriction: LandAbilitiesOnly));

        // "This land enters tapped." — unconditional ETB-tapped (CR 614.1c).
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }

    /// <summary>Single-pip <see cref="ManaCost"/> for a chosen color.</summary>
    private static ManaCost ManaCostForColor(ManaColor color) => color switch
    {
        ManaColor.White => ManaCost.Parse("W"),
        ManaColor.Blue => ManaCost.Parse("U"),
        ManaColor.Black => ManaCost.Parse("B"),
        ManaColor.Red => ManaCost.Parse("R"),
        ManaColor.Green => ManaCost.Parse("G"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(color), color,
            "Sunken Citadel's chosen color must be one of W/U/B/R/G (CR 105.1)."),
    };

    /// <summary>Double-pip <see cref="ManaCost"/> for a chosen color (the
    /// "{T}: Add two mana of the chosen color" ability).</summary>
    private static ManaCost DoubleManaCostForColor(ManaColor color) => color switch
    {
        ManaColor.White => ManaCost.Parse("WW"),
        ManaColor.Blue => ManaCost.Parse("UU"),
        ManaColor.Black => ManaCost.Parse("BB"),
        ManaColor.Red => ManaCost.Parse("RR"),
        ManaColor.Green => ManaCost.Parse("GG"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(color), color,
            "Sunken Citadel's chosen color must be one of W/U/B/R/G (CR 105.1)."),
    };
}
