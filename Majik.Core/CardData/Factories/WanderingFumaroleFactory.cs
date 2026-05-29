using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wandering Fumarole (Oath of the Gatewatch).
///
/// Land. Oracle text:
///   "This land enters tapped.
///    {T}: Add {U} or {R}.
///    {2}{U}{R}: Until end of turn, this land becomes a 1/4 blue and red
///    Elemental creature with \"{0}: Switch this creature's power and
///    toughness until end of turn.\" It's still a land."
///
/// Shares the Worldwake / Battle for Zendikar / Oath of the Gatewatch
/// "manland" shape with the rest of the cycle — see
/// <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — adds Creature +
/// Elemental, keeps printed Land per CR 613.1c) and
/// <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — set base 1/4),
/// both EOT-expiring (CR 514.2). Unlike its allied-land siblings the
/// animated body ALSO gains an activated ability, modelled here with a
/// <see cref="GrantAbilityEffect"/> (CR 613.1f — Layer 6 ability grant,
/// EOT-expiring) that attaches a <c>{0}</c>-cost
/// <see cref="ActivatedAbility"/> whose resolution registers a
/// <see cref="SwitchPTEffect"/> (CR 613.7d — Layer 7d P/T switch).
///
/// ## v1 shim caveats (shared with the rest of the manland cycle)
/// - Colour identity (Layer 5) is not modelled: the "blue and red" text is
///   recorded only in the effect-name string.
/// - <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
///   <see cref="PermanentCharacteristics"/> for a Land runtime instance, so
///   the 1/4 base P/T and its switch are registered for layer-system
///   correctness (and inspection) but do not surface through Compute() until
///   the manland animates into a Creature runtime row. The granted
///   <c>{0}</c> ability's <see cref="SwitchPTEffect"/> registration is
///   therefore gated on a <see cref="Creature"/> bearer.
/// </summary>
[CardName("Wandering Fumarole")]
public static class WanderingFumaroleFactory
{
    public const string CardName = "Wandering Fumarole";

    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ETB-tapped (CR 614.1c) — "This land enters tapped." Unconditional.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // {T}: Add {U}  /  {T}: Add {R}
        // CR 605.1 — mana abilities don't use the stack. Modelled as two
        // distinct mana abilities (same pattern as the rest of the cycle).
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));

        // {2}{U}{R}: Until end of turn, this land becomes a 1/4 blue and red
        // Elemental creature with "{0}: Switch P/T", still a land.
        var animateEffect = new Effect(
            $"{CardName}: becomes 1/4 blue and red Elemental creature with \"{{0}}: Switch P/T\" until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature + Elemental. Printed Land stays
                // (CR 613.1c — "It's still a land").
                effects.Register(new ManlandCycleAnimateEffect(
                    land, keywords: Array.Empty<string>()));

                // Layer 7b — set base P/T 1/4.
                effects.Register(new ManlandCycleBecomesPTEffect(land, 1, 4));

                // Layer 6 — grant "{0}: Switch this creature's power and
                // toughness until end of turn" (CR 613.1f). EOT-expiring.
                effects.Register(new GrantAbilityEffect(
                    source: land,
                    targetSelector: () => land,
                    abilityFactory: bearer => new ActivatedAbility(
                        source: bearer,
                        controller: owner,
                        costs: new ICost[] { new ManaCostCost(ManaCost.Zero) },
                        effects: new IEffect[]
                        {
                            new Effect(
                                $"{CardName}: switch this creature's power and toughness until EOT",
                                () =>
                                {
                                    // CR 613.7d — Layer 7d P/T switch, EOT.
                                    // Gated on a Creature runtime instance:
                                    // the manland is a Land runtime row whose
                                    // P/T does not surface through Compute()
                                    // (shared cycle shim — see class xmldoc).
                                    if (bearer is Creature creature)
                                    {
                                        effects.Register(new SwitchPTEffect(creature));
                                    }
                                }),
                        }),
                    expiresAtEndOfTurn: true));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}{U}{R}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
