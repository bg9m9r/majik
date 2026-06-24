using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Llanowar Loamspeaker (Dominaria United, {1}{G}).
///
/// Creature — Elf Druid 1/3. Oracle text (Scryfall, verified 2026-06-24):
///   "{T}: Add one mana of any color.
///    {T}: Target land you control becomes a 3/3 Elemental creature with haste
///    until end of turn. It's still a land. Activate only as a sorcery."
///
/// The base card body + the five-colour mana ability load from
/// <c>Majik.Core/CardData/Cards/llanowar-loamspeaker.json</c> (the
/// "add one mana of any color" ability is modeled as five
/// <see cref="Abilities.ManaAbility"/> instances — one per WUBRG — the Birds of
/// Paradise / Paradise Druid pattern). The targeted land-animate ability is
/// hand-attached here because the JSON
/// <see cref="CardDefinitionFactory"/> pipeline cannot express a targeted
/// land animation; it is composed from the shared manland primitives, mirroring
/// <see cref="DestinySpinnerFactory"/>.
///
/// ## Implemented (v1)
/// - 1/3 Elf Druid at {1}{G} + five WUBRG <see cref="ManaAbility"/> instances
///   (from JSON).
/// - <b>"{T}: Target land you control becomes a 3/3 Elemental creature with
///   haste until end of turn. It's still a land." (CR 602 / CR 613)</b> — an
///   <see cref="ActivatedAbility"/> with a <see cref="TapCost"/> whose
///   resolution animates the chosen target land via the shared manland
///   primitives: a <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — add
///   Creature + Elemental subtype + Haste; printed Land type stays, CR 613.1c)
///   and a <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — set base P/T
///   3/3), both flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   (CR 514.2 cleanup lifts the animation). Mirrors
///   <see cref="DestinySpinnerFactory"/>'s targeted land-animate (fixed 3/3 +
///   Haste here, instead of X/X + Trample + Haste).
///
/// ## v1 posture
/// - <b>Target selection</b> — like the manland animate cluster + Destiny
///   Spinner, the chosen land is supplied by a resolver injected at construction
///   rather than via an agent <see cref="Majik.Core.Targeting.TargetRequest"/>;
///   v1 picks the first land the controller controls. The resolver must only
///   return lands the controller controls (CR 115.4 — "target land you
///   control").
/// - <b>"Activate only as a sorcery"</b> — enforced via the
///   <see cref="ActivatedAbility"/> <c>sorcerySpeed</c> flag (CR 605 / CR 307.1
///   timing rider), gating activation to the controller's main phase with an
///   empty stack.
/// - <b>Animated colour</b> — the printed 3/3 Elemental body has no colour, so
///   no Layer-5 colour grant is registered (the body inherits the land's colour
///   identity, typically colourless). No colour gap.
/// </summary>
[CardName("Llanowar Loamspeaker")]
public static class LlanowarLoamspeakerFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("llanowar-loamspeaker");

    /// <summary>The fixed body the target land becomes (CR 613.7b).</summary>
    public const int AnimatedPower = 3;
    public const int AnimatedToughness = 3;

    /// <summary>
    /// Construct Llanowar Loamspeaker with no continuous-effects service /
    /// target resolver wired. The animate ability is attached but its resolution
    /// is a no-op (no target, no effect service). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, continuousEffects: null, targetLandResolver: null);

    /// <summary>
    /// Construct Llanowar Loamspeaker with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Continuous-effects service for the
    /// animate ability's Layer 4 / Layer 7b registration. May be null — the
    /// ability resolves but no animation is recorded.</param>
    /// <param name="targetLandResolver">Returns the candidate "target land you
    /// control" for the {T} animate ability. v1 animates the first land
    /// returned. May be null — the ability no-ops.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<IReadOnlyList<Land>>? targetLandResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}: Target land you control becomes a 3/3 Elemental creature with
        // haste until end of turn. It's still a land. Activate only as a
        // sorcery.
        //
        // CR 602 (activated) + CR 613 (animate; "still a land", CR 613.1c).
        // v1 animates the first land returned by the resolver (no agent target
        // prompt yet — mirrors Destiny Spinner / Koth of the Hammer). The
        // "activate only as a sorcery" timing restriction is enforced by the
        // sorcerySpeed flag on the ActivatedAbility (set below).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"Llanowar Loamspeaker: target land you control becomes a 3/3 Elemental with haste until EOT (still a land)",
            () =>
            {
                if (continuousEffects == null) return; // shape-only path
                var candidates = targetLandResolver?.Invoke();
                if (candidates == null) return;

                var controller = card.Controller ?? owner;

                foreach (var land in candidates)
                {
                    if (land == null) continue;
                    if (land.Zone != ZoneType.Battlefield) continue;
                    // "land you control" (CR 115.4) — restrict to the controller's lands.
                    if (!ReferenceEquals(land.Controller, controller)) continue;

                    // Layer 4 — add Creature + Elemental subtype + Haste.
                    // Printed Land type stays ("It's still a land").
                    continuousEffects.Register(new ManlandCycleAnimateEffect(
                        land,
                        keywords: new[] { "Haste" },
                        subtypes: new[] { CardSubtype.Elemental },
                        extraTypes: null));

                    // Layer 7b — set base P/T to 3/3.
                    continuousEffects.Register(new ManlandCycleBecomesPTEffect(
                        land, AnimatedPower, AnimatedToughness));

                    return; // "target land" — a single permanent.
                }
            });

        // "Activate only as a sorcery" (CR 605 / CR 307.1 timing rider) — the
        // sorcerySpeed flag gates activation to the controller's main phase with
        // an empty stack, the same restriction the rules timing layer enforces.
        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { Primitives.Costs.TapSelf(card) },
            effects: new IEffect[] { animateEffect },
            sorcerySpeed: true));

        return card;
    }
}
