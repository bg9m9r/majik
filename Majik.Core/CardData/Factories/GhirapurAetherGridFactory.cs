using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ghirapur Aether Grid (Kaladesh, {2}{R}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Tap two untapped artifacts you control: This enchantment deals 1
///    damage to any target."
///
/// A repeatable artifact-tap pinger — a red Kaladesh artifacts-matters
/// payoff that turns a board of mana rocks / Servos / Thopters into a
/// machine gun. Same "tap two untapped artifacts" cost shape as
/// <see cref="WhirlerRogueFactory"/>; the resolution effect mirrors
/// <see cref="PyriteSpellbombFactory"/>'s "1 damage to any target".
///
/// ## Build path
///
/// Identity (Enchantment, {2}{R}) is authored in the embedded JSON
/// definition (<c>Majik.Core/CardData/Cards/ghirapur-aether-grid.json</c>)
/// and materialized through <see cref="CardDefinitionFactory"/> — the same
/// vanilla shape used across the JSON-backed factories. The single
/// "tap two artifacts: 1 damage to any target" activated ability is
/// hand-attached on top, because the data-driven
/// <see cref="CardDefinitionFactory"/> schema does not express the
/// printed-word tap-as-cost (<see cref="TapTwoUntappedArtifactsCost"/>).
///
/// ## Implemented (v1)
///
/// - Card identity (Enchantment, mana cost {2}{R}, owner / controller
///   wiring). Ghirapur Aether Grid is NOT an artifact, so it cannot pay
///   its own "tap two artifacts" cost — same posture as Whirler Rogue.
/// - <b>"Tap two untapped artifacts you control" activated ability</b>
///   (CR 602.1): "This enchantment deals 1 damage to any target." Cost is a
///   single <see cref="TapTwoUntappedArtifactsCost"/> for two artifacts
///   (CR 602.2b / 118.12 — printed-word tap-as-cost, NOT a {T} symbol, so
///   summoning sickness never gates the artifact choice). A single
///   <see cref="TargetRequest"/> declares an any-target (player / creature /
///   planeswalker) chosen at activation (CR 602.2b). Resolution reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> so Planeswalker targets convert to
///   loyalty removal (CR 306.7) — same shape as Pyrite Spellbomb / Shock.
///   Untargeted / illegal-on-resolution targets fail silently (CR 608.2b).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Agent prompt for which two artifacts to tap</b> — the cost falls
///   back to the first two eligible (untapped, controller-owned) artifacts
///   in battlefield order via <see cref="TapTwoUntappedArtifactsCost"/>'s
///   deterministic pick. Agents may pre-set
///   <see cref="TapTwoUntappedArtifactsCost.Targets"/> to override.
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter the any-target choice — the resolution-time guard handles
///   illegal targets (CR 608.2b), same posture as Pyrite Spellbomb.
/// </summary>
[CardName("Ghirapur Aether Grid")]
public static class GhirapurAetherGridFactory
{
    public const string CardName = "Ghirapur Aether Grid";
    public const string Slug = "ghirapur-aether-grid";
    public const int ArtifactsToTap = 2;
    public const int DamageAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Ghirapur Aether Grid owned and controlled by
    /// <paramref name="owner"/>. The single "tap two untapped artifacts:
    /// 1 damage to any target" activated ability is attached structurally.
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity (Enchantment, {2}{R}) from the embedded JSON definition.
        var grid = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        grid.SetOwner(owner);
        grid.SetController(owner);

        // ----------------------------------------------------------------
        // Tap two untapped artifacts you control: ~ deals 1 damage to any
        // target. CR 602.1 — activated ability with a printed-word
        // tap-as-cost (CR 118.12) and a single any-target request.
        // Resolution reads ChosenTargets and gates on a damage-receiving
        // shape (Player / Creature / Planeswalker) via Fx.DealDamageAny.
        // Illegal-on-resolution targets fail silently (CR 608.2b) — the
        // cost was already paid, so the artifacts stay tapped.
        // ----------------------------------------------------------------
        ActivatedAbility? damageAbility = null;
        var damageEffect = new Effect(
            $"{CardName}: {DamageAmount} damage to any target",
            () =>
            {
                if (damageAbility != null
                    && damageAbility.ChosenTargets.Count > 0
                    && damageAbility.ChosenTargets[0].Count > 0)
                {
                    var target = damageAbility.ChosenTargets[0][0];
                    Fx.DealDamageAny(target, DamageAmount);
                }
            });

        damageAbility = new ActivatedAbility(
            source: grid,
            controller: owner,
            costs: new ICost[]
            {
                new TapTwoUntappedArtifactsCost(ArtifactsToTap),
            },
            effects: new IEffect[] { damageEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        grid.AddAbility(damageAbility);

        return grid;
    }
}
