using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Delighted Halfling (The Lord of the Rings:
/// Tales of Middle-earth, {G}).
///
/// Creature — Halfling Citizen 1/2.
/// Oracle text:
///   "{T}: Add {C}.
///    {T}: Add one mana of any color. Spend this mana only to cast a
///    legendary spell, and that spell can't be countered."
///
/// ## Implemented (v1)
/// - Creature body / identity / owner / controller loaded from
///   <c>Majik.Core/CardData/Cards/delighted-halfling.json</c> via
///   <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> carried in the
///   JSON definition. {C} folds into the generic bucket per
///   <see cref="ManaCost.Parse"/> (see ManaCost.cs:170). Unrestricted, per
///   the printed oracle (only the second ability carries the rider).
/// - <b>{T}: Add one mana of any color</b> — five <see cref="ManaAbility"/>
///   instances (one per WUBRG), the same shape Cavern of Souls / Birds of
///   Paradise / Ornithopter of Paradise use; the mana picker satisfies any
///   single colour pip via this creature.
///
/// ## Spend-restriction (v1 data, payment-gate deferred)
/// - <b>"Spend this mana only to cast a legendary spell"</b>: the five "any
///   colour" <see cref="ManaAbility"/> instances stamp a
///   <see cref="Majik.Core.Mana.SpendRestriction"/> with the predicate
///   <c>spell => spell.Card.HasSupertype(CardSupertype.Legendary)</c>
///   (CR 205.4a / 106.4). The {T}: Add {C} ability stays unrestricted.
///
///   <b>Payment-gate enforcement</b> (filtering tagged pool entries when
///   paying a non-legendary cost) is deferred until
///   <see cref="ManaPool"/> grows per-slot tags — today the pool stores
///   bucketed colour counts only. The restriction is observational
///   metadata on the ability until the resolver consumes the tag. Same
///   posture as Cavern of Souls / Eldrazi Temple (see those factories'
///   xmldoc); all three unlock together.
/// - <b>"That spell can't be countered"</b>: requires flagging the spell
///   object at cast time (when one of Delighted Halfling's mana entries
///   pays a pip on a legendary spell) and gating counter-spells in
///   <see cref="Majik.Core.Services.StackResolver"/>. Deferred — identical
///   deferral noted on Cavern of Souls.
/// </summary>
[CardName("Delighted Halfling")]
public static class DelightedHalflingFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("delighted-halfling");

    // CR 106.4 — "Spend this mana only to cast a legendary spell." Shared
    // static restriction so every "any colour" ManaAbility stamps the same
    // by-reference predicate (SpendRestriction equality is delegate-by-ref;
    // reusing one instance keeps the five abilities structurally equal).
    private static readonly SpendRestriction LegendaryOnly =
        new("legendary spell",
            spell => spell.Card.HasSupertype(CardSupertype.Legendary));

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // {T}: Add one mana of any color. Spend this mana only to cast a
        // legendary spell, and that spell can't be countered.
        //   Modelled as five ManaAbility instances (one per WUBRG) — same
        //   pattern as Cavern of Souls and Ornithopter of Paradise. Each
        //   stamps the LegendaryOnly SpendRestriction so the generated mana
        //   will (once the payment resolver grows tag-awareness) only pay a
        //   pip on a legendary spell. The uncounterable rider is deferred
        //   (see class xmldoc). The {T}: Add {C} ability is carried in the
        //   JSON definition and stays unrestricted.
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            card.AddAbility(new ManaAbility(
                card, owner, ManaCost.Parse(color),
                canActivateCheck: null,
                spendRestriction: LegendaryOnly));
        }

        return card;
    }
}
