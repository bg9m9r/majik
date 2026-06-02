using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phyrexia's Core (Mirrodin Besieged).
///
/// Land. Oracle text (Scryfall, verified 2026-06-02):
///   "{T}: Add {C}.
///    {1}, {T}, Sacrifice an artifact: You gain 1 life."
///
/// A colourless utility land in the Mirrodin's Core / Buried Ruin shape: a
/// {C}-producing land whose second ability pays {1} + {T} + "sacrifice an
/// artifact" to gain 1 life. Every primitive already ships, so no new engine
/// mechanic is required — this is a thin composition of:
///   - the {C} mana ability (loaded from JSON, same as Buried Ruin), and
///   - an <see cref="ActivatedAbility"/> whose cost list is
///     {1} (<see cref="ManaCostCost"/>) + {T} (<see cref="AdditionalCost.Tap"/>)
///     + sacrifice an artifact (<see cref="SacrificeAnArtifactCost"/>), with a
///     resolve effect that gains the controller 1 life
///     (<see cref="Fx.GainLife"/>).
///
/// ## Implemented (v1)
///
/// - Land identity (nonbasic, no subtypes / supertypes), materialised from the
///   embedded JSON definition (<c>phyrexias-core.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>, which also supplies the
///   <b>{T}: Add {C}</b> mana ability (CR 605.1 — mana abilities do not use the
///   stack).
/// - <b>{1}, {T}, Sacrifice an artifact: You gain 1 life.</b> — an
///   <see cref="ActivatedAbility"/> (CR 602) with costs (CR 117.1 — everything
///   before the colon):
///     - {1} via <see cref="ManaCostCost"/>,
///     - {T} via <see cref="AdditionalCost.Tap"/>, and
///     - sacrifice an artifact via <see cref="SacrificeAnArtifactCost"/>
///       (CR 701.16 — move an artifact the controller controls to its owner's
///       graveyard). The land itself is NOT an artifact, so it is never an
///       eligible sacrifice (no <c>excludeSource</c> needed).
///   The resolve effect gains the controller 1 life (CR 119.3) via
///   <see cref="Fx.GainLife"/>. No targets.
///
/// The activation cost includes {T} and the sacrifice, so each cost's own
/// <c>CanPay</c> gate (an untapped land + at least one artifact to sacrifice)
/// governs legality — same posture as the analogous colourless utility lands
/// (Buried Ruin / Mirrodin's Core).
/// </summary>
[CardName("Phyrexia's Core")]
public static class PhyrexiasCoreFactory
{
    public const string CardName = "Phyrexia's Core";
    public const string Slug = "phyrexias-core";

    /// <summary>CR 117.1 — the {1} mana portion of the life-gain activation
    /// (everything before the colon is the cost).</summary>
    public const string LifeGainManaCost = "{1}";

    /// <summary>CR 119.3 — "You gain 1 life."</summary>
    public const int LifeGainAmount = 1;

    /// <summary>
    /// Construct Phyrexia's Core owned and controlled by
    /// <paramref name="owner"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {C} mana ability). The sacrifice-an-artifact life-gain
        // ability is layered on below — it is not expressible in the current
        // JSON AbilityDefinition schema (same posture as Buried Ruin).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice an artifact: You gain 1 life.
        //
        // CR 602 — activated ability. Costs (CR 117.1):
        //   - {1} via ManaCostCost
        //   - {T} via AdditionalCost.Tap(land)
        //   - "sacrifice an artifact" via SacrificeAnArtifactCost (CR 701.16).
        //     The land is not an artifact, so it can never sacrifice itself.
        // Resolve: the controller gains 1 life (CR 119.3) via Fx.GainLife.
        // ----------------------------------------------------------------
        var lifeGainEffect = new Effect(
            $"{CardName}: you gain {LifeGainAmount} life",
            () =>
            {
                // CR 110.2 / CR 119.3 — "You" is the ability's controller.
                var controller = land.Controller ?? owner;
                Fx.GainLife(controller, LifeGainAmount);
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(LifeGainManaCost),
                AdditionalCost.Tap(land),
                new SacrificeAnArtifactCost(),
            },
            effects: new IEffect[] { lifeGainEffect }));

        return land;
    }
}
