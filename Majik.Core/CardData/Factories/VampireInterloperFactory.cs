using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vampire Interloper (Innistrad, {1}{B}).
///
/// Creature — Vampire Scout 2/1. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Flying
///    This creature can't block."
///
/// A near-vanilla evasive beater that composes two analogue shapes already in
/// the engine:
/// - <b>Flying</b> (CR 702.9): <see cref="KeywordAbility"/> marker read by
///   <c>CombatAbilities.HasFlying</c> for evasion in the combat validator —
///   same wire-up shape as <see cref="FaerieSeerFactory"/>.
/// - <b>"This creature can't block." (CR 509.1c)</b>: the same non-expiring
///   <see cref="CombatRestrictionEffect"/> rider as
///   <see cref="BloodsoakedChampionFactory"/> / <see cref="GravecrawlerFactory"/>
///   / Bloodghast — <see cref="CombatRestriction.CannotBlock"/> scoped to this
///   creature. <see cref="Majik.Core.Combat.CombatValidator"/> consults the
///   restriction directly when validating block declarations.
///
/// The base card shape (name / Creature type / Vampire Scout subtypes /
/// {1}{B} cost / 2/1 body) is materialised from the embedded JSON definition
/// (<c>vampire-interloper.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the Flying keyword and the
/// can't-block rider are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither yet (same posture as
/// <see cref="BloodsoakedChampionFactory"/> / <see cref="FaerieSeerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Creature shape</b> Vampire Scout 2/1 at printed cost {1}{B}.
///   Color identity black (derived from the {B} pip per CR 202.2c). Mana
///   value 2 (CR 202.3).
/// - <b>Flying</b> (CR 702.9): <see cref="KeywordAbility"/> marker.
/// - <b>"This creature can't block." (CR 509.1c)</b>: non-expiring
///   <see cref="CombatRestriction.CannotBlock"/> rider, registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied (the shape-only
///   dispatcher path has no service, mirroring Bloodsoaked Champion /
///   Gravecrawler).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + Flying only. The can't-block rider
///   is NOT registered (no effects service available). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — full wiring.
///   When the service is supplied the can't-block restriction is registered so
///   <see cref="Majik.Core.Combat.CombatValidator"/> rejects block
///   declarations naming this creature (CR 509.1c).
/// </summary>
[CardName("Vampire Interloper")]
public static class VampireInterloperFactory
{
    public const string CardName = "Vampire Interloper";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "vampire-interloper";

    public const int Power = 2;
    public const int Toughness = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Vampire Interloper with no continuous-effects service. The
    /// card has the correct shape (name, type, P/T, mana cost, subtypes) and
    /// the Flying keyword, but the can't-block restriction is NOT registered
    /// (no service to register against). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Vampire Interloper with an optional
    /// <see cref="ContinuousEffectsService"/>. When the service is supplied the
    /// "can't block" rider is registered as a non-expiring
    /// <see cref="CombatRestrictionEffect"/> bound to this creature so
    /// <see cref="Majik.Core.Combat.CombatValidator"/> rejects block
    /// declarations naming it (CR 509.1c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service. May be null — the
    /// can't-block restriction is then skipped (shape + Flying only).</param>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Vampire
        // Scout subtypes, {1}{B}, 2/1). The JSON carries no abilities — Flying
        // and the can't-block rider are layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Flying (CR 702.9). Keyword marker — CombatAbilities.HasFlying
        // reads this for evasion in the combat validator. Same wire-up
        // shape as Faerie Seer.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // "This creature can't block." — CR 509.1c.
        // Permanent restriction (expiresAtEndOfTurn = false) registered on
        // the ContinuousEffectsService so CombatValidator.CanBlock returns
        // false for this creature. Mirrors Bloodsoaked Champion /
        // Gravecrawler / Bloodghast (same shape, same gate).
        // ----------------------------------------------------------------
        effects?.Register(new CombatRestrictionEffect(
            CombatRestriction.CannotBlock,
            target: card,
            expiresAtEndOfTurn: false));

        return card;
    }
}
