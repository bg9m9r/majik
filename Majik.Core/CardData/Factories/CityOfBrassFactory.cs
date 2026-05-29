using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for City of Brass (Arabian Nights + many reprints).
/// Oracle text (verified against Scryfall):
///   "Whenever City of Brass becomes tapped, it deals 1 damage to you.
///    {T}: Add one mana of any color."
///
/// <para>
/// The Land shell (identity / owner / controller) is declared declaratively
/// in <c>Majik.Core/CardData/Cards/city-of-brass.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="ManaConfluenceFactory"/>. The any-colour mana abilities and
/// the pain rider are attached on top in C# because the data-only
/// <see cref="ManaAbilityDefinition"/> schema only carries a
/// <c>Produces</c> string — it can express neither the five-colour
/// any-colour fan-out nor the "deals 1 damage to you" rider. The JSON
/// therefore declares no abilities; this factory adds them.
/// </para>
///
/// ## Implemented (v1)
/// - Land identity (non-Basic, no subtype) via JSON.
/// - <b>{T}: Add one mana of any color.</b> — modelled as five
///   <see cref="ManaAbility"/> instances, one per WUBRG (the same
///   any-colour fan-out as <see cref="ManaConfluenceFactory"/> / Aether
///   Hub's coloured modes). There is NO <c>{C}</c> mode. The mana picker
///   chooses whichever colour is needed when paying spell costs.
/// - <b>"Whenever this land becomes tapped, it deals 1 damage to you."</b>
///   — folded into each coloured mana ability as an
///   <c>additionalCostPayer = controller.LoseLife(1)</c> rider, identical
///   in shape to <see cref="PainLandCycleFactory"/>'s pain rider (CR 120.3
///   — damage to a player reduces life by that amount), running after the
///   {T} tap pays the activation. NO life-floor gate (unlike Mana
///   Confluence's "Pay 1 life", CR 119.4): pain can drop you to 0 or below
///   and you then lose to SBAs.
///
/// ## Deferred (v1 gap)
/// - <b>True "becomes tapped" trigger (Rule 603.2).</b> The printed card is
///   a triggered ability that fires whenever the land becomes tapped for
///   ANY reason (e.g. an opponent's "tap target land"), not only when
///   activating its own mana ability. The engine has no faithful
///   becomes-tapped event trigger: there is no tapped-event on the bus, and
///   <see cref="StateChangeTriggerCondition"/> is evaluated only after an
///   SBA pass — which a mana-ability tap (CR 605.3, never on the stack)
///   does not run. Since the only way City of Brass taps itself is its own
///   mana ability, folding the damage into that activation reproduces the
///   common case exactly. Taps caused by other effects do NOT deal the
///   damage under this model — the same simplification the merged
///   <see cref="PainLandCycleFactory"/> takes for its pain rider. See the
///   v1-deferrals backlog.
/// </summary>
[CardName("City of Brass")]
public static class CityOfBrassFactory
{
    public const string CardName = "City of Brass";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("city-of-brass");

    /// <summary>Construct City of Brass owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // {T}: Add one mana of any color. + "becomes tapped: deals 1 damage
        //   to you." Five ManaAbility instances (one per WUBRG) — same
        //   any-colour fan-out as Mana Confluence. Each carries:
        //     - canActivateCheck: land untapped (the {T} cost). No
        //       life-floor gate — CR 120.3 damage, not CR 119.4 "pay life",
        //       so it can drop you to 0 or below.
        //     - additionalCostPayer: lose 1 life after the tap pays {T}
        //       (CR 120.3 — damage to a player reduces life by that amount).
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            var mana = ManaCost.Parse(color);
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: mana,
                canActivateCheck: () => !land.IsTapped,
                additionalCostPayer: controller => controller.LoseLife(1)));
        }

        return land;
    }
}
