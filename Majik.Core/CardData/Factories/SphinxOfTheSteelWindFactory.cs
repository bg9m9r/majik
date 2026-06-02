using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sphinx of the Steel Wind (Alara Reborn,
/// {5}{W}{U}{B}). Artifact Creature — Sphinx 6/6. Oracle text (verified
/// against Scryfall):
///   "Flying, first strike, vigilance, lifelink, protection from red and
///    from green"
///
/// The card's base shape (name, Artifact + Creature types, Sphinx subtype,
/// {5}{W}{U}{B}, 6/6) is materialised from the embedded JSON definition
/// (<c>sphinx-of-the-steel-wind.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The five printed static
/// keyword riders (Flying, first strike, vigilance, lifelink, and the two
/// protections) are layered on top here — the JSON <c>AbilityDefinition</c>
/// schema doesn't yet express keyword markers or protection qualities, so
/// they live in the factory (same posture as
/// <see cref="MirranCrusaderFactory"/>, whose Double strike + two
/// protection riders use the identical wiring).
///
/// ## Implemented (v1)
/// - 6/6 Sphinx (CR 205.3m) at {5}{W}{U}{B}, Artifact Creature. Owner /
///   controller wired.
/// - <b>Flying (CR 702.9)</b>, <b>first strike (CR 702.7)</b>,
///   <b>vigilance (CR 702.21)</b>, <b>lifelink (CR 702.15)</b> — wired as
///   <see cref="KeywordAbility"/> markers so
///   <see cref="Majik.Core.Combat.CombatAbilities"/> surfaces the combat
///   properties (canonical keyword casing matching the
///   <c>CombatAbilities.Has*</c> lookups). Same marker shape as Mirran
///   Crusader's Double strike.
/// - <b>Protection from red and from green (CR 702.16)</b> — two
///   <see cref="ProtectionAbility"/> markers attached directly to the
///   creature. Sphinx of the Steel Wind's protection is intrinsic and
///   always-on, so the markers live on the creature itself — the canonical
///   shape that
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/> reads
///   (DEBT-A: can't be Damaged / Enchanted-Equipped / Blocked / Targeted by
///   anything red or green). Identical wiring shape to Mirran Crusader's
///   protection-from-black-and-green.
///
/// ## Deferred (v1 gaps)
/// - None. Sphinx of the Steel Wind has no triggered or activated
///   abilities — all five clauses are static evergreen keyword riders the
///   engine already models. The single-arg dispatcher path is fully wired
///   (there is no service-dependent behaviour to gate).
/// </summary>
[CardName("Sphinx of the Steel Wind")]
public static class SphinxOfTheSteelWindFactory
{
    public const string CardName = "Sphinx of the Steel Wind";
    public const string Slug = "sphinx-of-the-steel-wind";

    /// <summary>
    /// Construct Sphinx of the Steel Wind. Fully wired — all five keyword
    /// riders are static markers with no service dependency, so this single
    /// overload is also the <see cref="NamedCardFactory"/> dispatch target.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Artifact types, Sphinx subtype, {5}{W}{U}{B}, 6/6). The JSON
        // carries no abilities — the keyword riders are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Evergreen combat keywords. KeywordAbility markers so
        // CombatAbilities.Has{Flying,FirstStrike,Vigilance,Lifelink} surface
        // the combat behaviour. Casing matches the CombatAbilities lookups.
        card.AddAbility(new KeywordAbility("Flying", card, owner));       // CR 702.9
        card.AddAbility(new KeywordAbility("First strike", card, owner)); // CR 702.7
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));    // CR 702.21
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));     // CR 702.15

        // CR 702.16 — Protection from red and from green. Qualities stored
        // normalised; the Rules.Protection / TargetLegality / CombatAbilities
        // helpers interpret them (DEBT-A). Same intrinsic-marker shape as
        // Mirran Crusader's protection-from-black-and-green.
        card.AddAbility(new ProtectionAbility("red"));
        card.AddAbility(new ProtectionAbility("green"));

        return card;
    }
}
