using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goldhound (The Lost Caverns of Ixalan, {R}).
///
/// Artifact Creature — Treasure Dog 1/1. Oracle text (Scryfall, verified
/// 2026-06-02):
///   "First strike
///    Menace (This creature can't be blocked except by two or more creatures.)
///    {T}, Sacrifice this creature: Add one mana of any color."
///
/// A one-mana aggressive creature that doubles as a one-shot Treasure-style
/// mana source — sacrifice it for a mana of any color when you no longer need
/// the body.
///
/// ## Shape source
/// Card identity (name, {R}, 1/1, Artifact Creature — Treasure Dog) is loaded
/// from <c>Majik.Core/CardData/Cards/goldhound.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same JSON-driven posture as
/// <see cref="SphereOfTheSunsFactory"/>. The two keyword markers and the
/// five-colour mana ability suite are attached in code below.
///
/// ## Implemented (v1)
/// - 1/1 Artifact Creature — Treasure Dog at {R}, owner/controller stamped.
/// - <b>First strike (CR 702.7)</b>: <see cref="KeywordAbility"/> marker —
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/> reads it at
///   combat-damage time. Same wiring shape as <see cref="YouthfulKnightFactory"/>.
/// - <b>Menace (CR 702.110)</b>: <see cref="KeywordAbility"/> marker —
///   <see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/> reads it at
///   block-declaration time. Same wiring shape as <see cref="BoggartBruteFactory"/>.
/// - <b>{T}, Sacrifice this creature: Add one mana of any color (CR 605.1)</b>:
///   five <see cref="ManaAbility"/> instances (one per WUBRG) — the same
///   modal-colour shape <see cref="SphereOfTheSunsFactory"/> uses for "Add one
///   mana of any color". The activator picks a colour by picking the matching
///   ability slot, so no separate colour prompt is needed (CR 605.1 — mana
///   abilities don't use the stack). The printed cost includes {T}, so the
///   tap-as-cost overload is used (<c>tapsAsCost</c> defaults to true). Each
///   slot is gated on Goldhound still being on the battlefield (CR 605.3a — the
///   cost must be payable); the <c>additionalCostPayer</c> sacrifices Goldhound
///   inline (Battlefield -> Graveyard, CR 121.5 / CR 602.1 — paid up front in
///   the same atomic step as mana production). Because the cost sacrifices the
///   creature, the ability can only be activated once.
/// </summary>
[CardName("Goldhound")]
public static class GoldhoundFactory
{
    public const string CardName = "Goldhound";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("goldhound");

    /// <summary>
    /// Construct Goldhound owned and controlled by <paramref name="owner"/>.
    /// The two keyword markers (First strike, Menace) and the five WUBRG mana
    /// abilities ("{T}, Sacrifice this creature: Add one mana of any color")
    /// are attached. Single-arg dispatcher path — suitable for shape,
    /// dispatcher, and unit-test usage.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.7 — First strike marker. Combat-damage step enforces the
        // first-strike sub-step off this marker.
        card.AddAbility(new KeywordAbility("First strike", card, owner));

        // CR 702.110 — Menace marker. Consumed by CombatAbilities.HasMenace
        // at block-declaration time.
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        // ----------------------------------------------------------------
        // "{T}, Sacrifice this creature: Add one mana of any color."
        // (CR 605.1 — mana ability; CR 605.3b — doesn't use the stack.)
        //
        // Five ManaAbility instances (one per WUBRG) — same modal-colour
        // shape as Sphere of the Suns. The printed activation cost is {T}
        // PLUS "sacrifice this creature", so the standard tap-as-cost
        // overload is used (tapsAsCost defaults to true — the engine taps
        // in ManaAbility.Activate). Each is gated on Goldhound still being
        // on the battlefield (CR 605.3a — the {T} / sacrifice cost must be
        // payable). The additionalCostPayer sacrifices Goldhound inline
        // (CR 121.5 / CR 602.1).
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            card.AddAbility(new ManaAbility(
                source: card,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => card.Zone == ZoneType.Battlefield,
                additionalCostPayer: _ => Sacrifice(card)));
        }

        return card;
    }

    /// <summary>
    /// CR 121.5 / CR 602.1 — pay part of the activation cost by sacrificing
    /// Goldhound: move it Battlefield -> Graveyard. Defensive against the
    /// creature already having left the battlefield (the canActivateCheck
    /// gate makes that unreachable in practice).
    /// </summary>
    private static void Sacrifice(Creature card)
    {
        if (card.Zone != ZoneType.Battlefield) return;

        var controller = card.Controller ?? card.Owner;
        if (controller == null) return;

        controller.Zones.Battlefield.RemoveCard(card);
        controller.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
