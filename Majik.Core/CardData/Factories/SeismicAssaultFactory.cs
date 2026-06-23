using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Primitives;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seismic Assault (Tempest / Modern Horizons, {R}{R}{R}).
///
/// Enchantment. Oracle text (verified against Scryfall, 2026-06-23):
///   "Discard a land card: This enchantment deals 2 damage to any target."
///
/// ## Shape source
/// Card identity (name, {R}{R}{R}, Enchantment) is loaded from
/// <c>Majik.Core/CardData/Cards/seismic-assault.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The discard-a-land burn ability is
/// attached in code — the JSON ability schema does not express an activated
/// ability with a non-mana discard cost (same posture as the burn half of
/// <see cref="BorborygmosEnragedFactory"/>).
///
/// ## Implemented (v1)
/// - Plain Enchantment identity ({R}{R}{R}, no supertype, no subtype).
/// - <b>Discard-a-land burn ability (CR 602.1 / CR 118.5)</b>: "Discard a land
///   card: This enchantment deals 2 damage to any target." Cost is a single
///   <see cref="DiscardALandCardCost"/> (no mana — CR 118.3, a cost need not
///   include mana); a single 1..1 "any target" <see cref="TargetRequest"/>.
///   On resolution the closure reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> (Player → life loss CR 119.3, Creature →
///   marked damage CR 120.3, Planeswalker → loyalty removal CR 306.7) — the
///   same any-target damage primitive as Borborygmos Enraged / Lightning Bolt.
///   Illegal-on-resolution targets fail silently (CR 608.2b). The ability has
///   no sorcery-speed rider, so it is instant-speed and may be activated any
///   number of times so long as a land card is available to discard.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven discard pick</b>: <see cref="DiscardALandCardCost"/>
///   defaults to the first land card in hand when no <c>Target</c> is set —
///   the shared deferred discard-prompt queue (Faithless Looting / Liliana /
///   Borborygmos Enraged).
/// </summary>
[CardName("Seismic Assault")]
public static class SeismicAssaultFactory
{
    public const string CardName = "Seismic Assault";
    public const string Slug = "seismic-assault";

    /// <summary>Damage dealt by the discard-a-land burn ability.</summary>
    public const int BurnDamage = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Seismic Assault with its discard-a-land burn ability attached.
    /// The overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Discard a land card: This enchantment deals 2 damage to any target.
        // CR 602.1 activated ability; CR 118.5 (discard as a cost). The
        // any-target damage routes through Fx.DealDamageAny so Player /
        // Creature / Planeswalker targets each take the right shape of damage
        // (CR 119.3 / 120.3 / 306.7).
        // ----------------------------------------------------------------
        ActivatedAbility? burnAbility = null;
        var burnEffect = new Effect(
            $"{CardName}: deal {BurnDamage} damage to any target",
            () =>
            {
                if (burnAbility == null) return;
                if (burnAbility.ChosenTargets.Count == 0) return;
                if (burnAbility.ChosenTargets[0].Count == 0) return;

                var target = burnAbility.ChosenTargets[0][0];
                Fx.DealDamageAny(target, BurnDamage); // CR 608.2b — gated per shape
            });

        burnAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new DiscardALandCardCost() },
            effects: new IEffect[] { burnEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(burnAbility);

        return card;
    }
}
