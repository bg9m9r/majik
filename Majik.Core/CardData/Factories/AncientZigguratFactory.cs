using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ancient Ziggurat (Conflux).
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add one mana of any color. Spend this mana only to cast a
///    creature spell."
///
/// ## Implemented (v1)
/// - Land body / identity / owner / controller loaded from
///   <c>Majik.Core/CardData/Cards/ancient-ziggurat.json</c> via
///   <see cref="CardDefinitionFactory"/>. No mana cost (CR 305.1 — lands
///   have no mana cost) and nonbasic (no Basic supertype).
/// - <b>{T}: Add one mana of any color</b> — five
///   <see cref="ManaAbility"/> instances (one per WUBRG). CR 106.1b — "any
///   color" is one of the five colours; the mana picker satisfies any
///   single coloured pip via this land. Same any-colour modelling used by
///   Cavern of Souls / Delighted Halfling / Manalith. The implicit {T}
///   self-tap is baked into each mana ability's cost. The mana abilities
///   are carried in the factory (not the JSON) because each one stamps a
///   <see cref="SpendRestriction"/> — the JSON <c>{ "kind": "mana" }</c>
///   shape produces only unrestricted abilities.
///
/// ## Spend-restriction (v1 data, payment-gate deferred)
/// - <b>"Spend this mana only to cast a creature spell"</b>: each of the
///   five "any colour" <see cref="ManaAbility"/> instances stamps a shared
///   <see cref="SpendRestriction"/> with the predicate
///   <c>spell => spell.Card.HasType(CardType.Creature)</c> (CR 106.4 —
///   mana with a spend restriction can only pay for objects matching the
///   restriction). Unlike Cavern of Souls there is no chosen-type
///   refinement — Ancient Ziggurat restricts to any creature spell.
///
///   <b>Payment-gate enforcement</b> (filtering tagged pool entries when
///   paying a non-creature cost) is deferred until <see cref="ManaPool"/>
///   grows per-slot tags — today the pool stores bucketed colour counts
///   only. The restriction is observational metadata on the ability until
///   the resolver consumes the tag. Same posture as Cavern of Souls /
///   Delighted Halfling (see those factories' xmldoc); all unlock
///   together.
/// </summary>
[CardName("Ancient Ziggurat")]
public static class AncientZigguratFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("ancient-ziggurat");

    // CR 106.4 — "Spend this mana only to cast a creature spell." Shared
    // static restriction so every "any colour" ManaAbility stamps the same
    // by-reference predicate (SpendRestriction equality is delegate-by-ref;
    // reusing one instance keeps the five abilities structurally equal).
    private static readonly SpendRestriction CreatureSpellOnly =
        new("creature spell",
            spell => spell.Card.HasType(CardType.Creature));

    /// <summary>
    /// Construct Ancient Ziggurat owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // {T}: Add one mana of any color. Spend this mana only to cast a
        // creature spell.
        //   Five ManaAbility instances (one per WUBRG) — same pattern as
        //   Cavern of Souls / Delighted Halfling. Each stamps the
        //   CreatureSpellOnly SpendRestriction so the generated mana will
        //   (once the payment resolver grows tag-awareness) only pay a pip
        //   on a creature spell.
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new ManaAbility(
                land, owner, ManaCost.Parse(color),
                canActivateCheck: null,
                spendRestriction: CreatureSpellOnly));
        }

        return land;
    }
}
