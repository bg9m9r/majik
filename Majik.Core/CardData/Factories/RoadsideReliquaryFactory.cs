using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Roadside Reliquary (March of the Machine: The
/// Aftermath).
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}, {T}, Sacrifice this land: Draw a card if you control an artifact.
///    Draw a card if you control an enchantment."
///
/// ## Why it gets its own factory
/// Same skeleton as <see cref="HedronArchiveFactory"/> /
/// <see cref="RenegadeMapFactory"/>: a colourless-mana permanent whose
/// "{cost}, {T}, Sacrifice this ~:" activated ability resolves a payoff. Here
/// the payoff is two INDEPENDENT conditional card-draws rather than a fixed
/// "draw two" or a tutor. The base shape ({T}: Add {C}) ships in the embedded
/// JSON (<c>roadside-reliquary.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>; the sac-draw ability is layered on
/// here because the JSON schema doesn't express conditional draws. No new
/// engine mechanic is required.
///
/// ## Implemented (v1)
/// - Card identity (Land, no printed supertypes / subtypes) from the JSON
///   definition, plus owner / controller wiring.
/// - <b>{T}: Add {C}</b> — single <see cref="ManaAbility"/> (CR 605.1),
///   declared in the JSON. {C} folds to one generic/colourless via
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>.
/// - <b>{2}, {T}, Sacrifice this land: Draw a card if you control an
///   artifact. Draw a card if you control an enchantment.</b> — a
///   <see cref="ActivatedAbility"/> with three costs:
///   <see cref="ManaCostCost"/>("{2}") for the generic pip,
///   <see cref="AdditionalCost.Tap"/> on the land, and
///   <see cref="AdditionalCost.Sacrifice"/> on the land itself (CR 602).
///   On resolve the land is sacrificed (battlefield → owner's graveyard,
///   CR 701.16), then the two "if you control" clauses are evaluated
///   INDEPENDENTLY against the controller's battlefield (CR 121.1): one draw
///   per clause whose condition holds. Each draw routes through
///   <see cref="Fx.DrawCards"/>, so an empty library marks the SBA loss flag
///   (CR 704.5b) instead of throwing.
///
/// ## Rules notes
/// - The two clauses are separate sentences → two independent draws; controlling
///   both an artifact and an enchantment draws two, controlling one draws one,
///   controlling neither draws zero.
/// - The land sacrifices itself as a cost BEFORE the effect resolves, so a lone
///   Reliquary (a Land, neither artifact nor enchantment) contributes nothing
///   to either condition.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op stub.
///   The effect closure performs the zone move directly so behaviour is
///   observable — same posture as <see cref="HedronArchiveFactory"/> /
///   Renegade Map. Remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// </summary>
[CardName("Roadside Reliquary")]
public static class RoadsideReliquaryFactory
{
    public const string CardName = "Roadside Reliquary";
    public const string Slug = "roadside-reliquary";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Roadside Reliquary owned and controlled by
    /// <paramref name="owner"/>. The {T}: Add {C} mana ability comes from the
    /// embedded JSON; the "{2}, {T}, Sacrifice this land:" conditional-draw
    /// activated ability is layered on structurally.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Land, {T}: Add {C}) from the embedded JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {2}, {T}, Sacrifice this land: Draw a card if you control an
        // artifact. Draw a card if you control an enchantment.
        // CR 602 — activated ability with three costs (mana pip, tap, sac).
        // CR 121.1 — two independent conditional draws.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: sac self, then draw per controlled artifact/enchantment",
            () =>
            {
                var controller = land.Controller ?? owner;

                SacrificeSelf(land, owner, controller);

                // Each clause is its own sentence → independent draw (CR 121.1).
                // Evaluated against the controller's battlefield.
                var battlefield = controller.Zones.Battlefield.GetCards().ToList();

                if (battlefield.Any(c => c.HasType(CardType.Artifact)))
                    Fx.DrawCards(controller, 1);

                if (battlefield.Any(c => c.HasType(CardType.Enchantment)))
                    Fx.DrawCards(controller, 1);
            });

        var drawAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { drawEffect });

        land.AddAbility(drawAbility);

        return land;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="land"/> from the battlefield to its
    /// owner's graveyard (sacrifice). Idempotent. Mirrors the closure used by
    /// Hedron Archive / Renegade Map.
    /// </summary>
    private static void SacrificeSelf(Land land, Player owner, Player controller)
    {
        if (land.Zone != ZoneType.Battlefield) return;
        controller.Zones.Battlefield.RemoveCard(land);
        owner.Zones.Graveyard.AddCard(land);
        land.SetZone(ZoneType.Graveyard);
    }
}
