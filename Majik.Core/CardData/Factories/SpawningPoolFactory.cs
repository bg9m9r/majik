using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spawning Pool (Urza's Legacy / Modern Horizons
/// reprints). Land.
///
/// Oracle text (verified Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {B}.
///    {1}{B}: This land becomes a 1/1 black Skeleton creature with
///    \"{B}: Regenerate this creature\" until end of turn. It's still a land.
///    (If it regenerates, the next time it would be destroyed this turn,
///    instead tap it, remove it from combat, and heal all damage on it.)"
///
/// Shares the "manland" animate shape with the Worldwake / BFZ / OGW cycle
/// (<see cref="ManlandCycleAnimateEffect"/> — Layer 4 add Creature + the
/// animated body's subtype, keeps printed Land per CR 613.1c;
/// <see cref="ManlandCycleBecomesPTEffect"/> — Layer 7b set base P/T), and
/// like <see cref="WanderingFumaroleFactory"/> the animated body ALSO gains
/// a granted activated ability — here "{B}: Regenerate this creature"
/// (CR 701.18) — modelled via <see cref="GrantAbilityEffect"/> (CR 613.1f —
/// Layer 6 ability grant, EOT-expiring) whose granted
/// <see cref="ActivatedAbility"/> resolution adds a regeneration shield to
/// the bearer (<see cref="Permanent.AddRegenerationShield"/>, same primitive
/// as <see cref="MortivoreFactory"/>'s "{B}: Regenerate Mortivore").
///
/// The base shape (plain nonbasic Land, {T}: Add {B}) is materialised from
/// the embedded JSON definition (<c>spawning-pool.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the ETB-tapped replacement and
/// the animate ability are layered on here (neither is expressible in the
/// current JSON <c>AbilityDefinition</c> schema — same posture as
/// <see cref="CaveOfTheFrostDragonFactory"/>).
///
/// ## v1 shim caveats (shared with the rest of the manland cycle)
/// - Colour identity (Layer 5) is not modelled: the "black" of the animated
///   Skeleton is recorded only in the effect-name string.
/// - <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
///   <see cref="PermanentCharacteristics"/> for a Land runtime instance, so
///   the 1/1 base P/T is registered for layer-system correctness (and
///   inspection) but does not surface through Compute() until the manland
///   animates into a Creature runtime row. The granted "{B}: Regenerate"
///   ability adds the regeneration shield unconditionally (it does not need
///   a Creature runtime row — the shield consumes the next Destroy of the
///   permanent regardless of its current type).
/// </summary>
[CardName("Spawning Pool")]
public static class SpawningPoolFactory
{
    public const string CardName = "Spawning Pool";
    public const string Slug = "spawning-pool";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Spawning Pool with no <see cref="ContinuousEffectsService"/>
    /// or <see cref="ReplacementBus"/> wired. The {T}: Add {B} mana ability
    /// (from JSON) + the animate ability are attached so the card surface is
    /// complete; the layer effects are not registered and the ETB-tapped
    /// replacement is omitted (single-arg shape-only path). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Spawning Pool.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 / Layer
    /// 7b / Layer 6 registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// "This land enters tapped" rider (CR 614.1c). May be null — the land
    /// enters untapped in that posture (shape-only path).</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {B} mana ability). The ETB-tapped rider + the animate
        // ability are layered on below — neither is expressible in the
        // current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "This land enters tapped." (CR 614.1c) — unconditional.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {1}{B}: This land becomes a 1/1 black Skeleton creature with
        // "{B}: Regenerate this creature" until end of turn. It's still a
        // land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {1}{B}, no tap rider. Resolution registers Layer 4 + Layer 7b +
        // Layer 6 continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 1/1 black Skeleton creature with \"{{B}}: Regenerate this creature\" until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature + Skeleton. Printed Land stays
                // (CR 613.1c — "It's still a land"). No keyword grants.
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Skeleton },
                    extraTypes: null));

                // Layer 7b — set base P/T 1/1.
                effects.Register(new ManlandCycleBecomesPTEffect(land, Power, Toughness));

                // Layer 6 — grant "{B}: Regenerate this creature" until end
                // of turn (CR 613.1f). EOT-expiring. Resolution adds a
                // regeneration shield (CR 701.18 / 701.15a) to the bearer,
                // same primitive as Mortivore's "{B}: Regenerate Mortivore".
                effects.Register(new GrantAbilityEffect(
                    source: land,
                    targetSelector: () => land,
                    abilityFactory: bearer => new ActivatedAbility(
                        source: bearer,
                        controller: owner,
                        costs: new ICost[] { new ManaCostCost("{B}") },
                        effects: new IEffect[]
                        {
                            new Effect(
                                $"{CardName}: regenerate this creature",
                                () => bearer.AddRegenerationShield()),
                        }),
                    expiresAtEndOfTurn: true));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{B}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
