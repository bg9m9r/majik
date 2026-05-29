using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pillar of the Paruns (Guildpact). Oracle text
/// (verified against Scryfall):
///   "{T}: Add one mana of any color. Spend this mana only to cast a
///   multicolored spell."
///
/// <para>
/// The Land shell (identity / owner / controller) is declared declaratively
/// in <c>Majik.Core/CardData/Cards/pillar-of-the-paruns.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="ManaConfluenceFactory"/>. The "any color" mana abilities are
/// attached on top in C# because the data-only
/// <see cref="ManaAbilityDefinition"/> schema only carries a
/// <c>Produces</c> string — it cannot express the five-colour any-colour
/// fan-out nor the multicolored-only spend rider. The JSON therefore
/// declares no mana abilities; this factory adds them.
/// </para>
///
/// ## Implemented (v1)
/// - Land identity (non-Basic, no subtype) via JSON.
/// - <b>{T}: Add one mana of any color</b> — modelled as five
///   <see cref="ManaAbility"/> instances, one per WUBRG (same any-colour
///   fan-out as Mana Confluence / Cavern of Souls). Unlike Mana Confluence
///   there is NO "Pay 1 life" additional cost: the only activation cost is
///   {T}. The mana picker chooses whichever colour is needed when paying
///   spell costs.
///
/// ## Spend-restriction (v1 data, payment-gate deferred)
/// - <b>"Spend this mana only to cast a multicolored spell"</b>: each of the
///   five any-colour <see cref="ManaAbility"/> instances stamps a
///   <see cref="SpendRestriction"/> whose predicate is
///   <c>CardColors.GetColors(spell.Card).Count &gt;= 2</c> (CR 105.4 — a
///   multicolored object is one with two or more colours; CR 202.2f). The
///   shared static restriction is reused across all five abilities so the
///   predicate compares by reference (structural equality — see
///   <see cref="SpendRestriction"/> xmldoc).
///
///   <b>Payment-gate enforcement</b> (filtering tagged pool entries when
///   paying a monocolored / colourless spell's cost) is deferred until
///   <see cref="Majik.Core.ValueObjects.ManaPool"/> grows per-slot tags —
///   today the pool stores bucketed colour counts only. Same posture as
///   Cavern of Souls / Eldrazi Temple (see those factories' xmldoc); all
///   unlock together when <see cref="Majik.Core.Costs.ManaPaymentResolver"/>
///   consumes the tag. Until then the rider is observational metadata.
/// </summary>
[CardName("Pillar of the Paruns")]
public static class PillarOfTheParunsFactory
{
    public const string CardName = "Pillar of the Paruns";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("pillar-of-the-paruns");

    // CR 105.4 / CR 202.2f — a multicolored object has two or more colours.
    // Shared static instance so the predicate is by-reference equal across
    // all five mana abilities (SpendRestriction equality is delegate-ref
    // based — reuse the static rather than allocating a closure per ability).
    private static readonly SpendRestriction MulticoloredOnly = new(
        "multicolored spell",
        spell => CardColors.GetColors(spell.Card).Count >= 2);

    /// <summary>Construct Pillar of the Paruns owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // {T}: Add one mana of any color. Spend this mana only to cast a
        //   multicolored spell.
        //   Five ManaAbility instances (one per WUBRG) — same any-colour
        //   fan-out as Mana Confluence / Cavern of Souls. The only
        //   activation cost is {T} (no life payment). Each stamps the
        //   shared multicolored-only SpendRestriction so that — once the
        //   payment resolver grows tag-awareness — the generated mana only
        //   pays a pip on a multicolored spell (CR 105.4).
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: null,
                spendRestriction: MulticoloredOnly));
        }

        return land;
    }
}
