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
/// ## Prod note — true "becomes tapped" trigger lives in the binder
/// - This named factory is TEST-ONLY (lands are never routed through their
///   <c>[CardName]</c> factory — they build through the binder chain). The
///   PROD path now binds a faithful CR 603.2 "Whenever this land becomes
///   tapped, it deals 1 damage to you" trigger in
///   <see cref="Majik.Core.CardData.OracleTriggeredAbilityBinder"/>, firing on
///   the <see cref="Majik.Core.Domain.DomainEvents.PermanentTappedEvent"/>
///   (CR 701.21, published by <see cref="Permanent.Tap(Player?)"/> at EVERY
///   tap site) regardless of the tapper — so an opponent's "tap target land"
///   correctly pings the controller. The factory keeps the older
///   fold-pain-into-the-mana-ability model only for shape / dispatcher tests
///   (it taps itself only via its own mana ability, so the common case
///   coincides). When this factory's any-colour modes are activated in a test
///   alongside the binder-bound trigger they would double-count damage — so
///   tests use ONE path, not both (the prod path is the binder; the factory is
///   not run in prod).
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
