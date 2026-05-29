using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spire of Industry (Aether Revolt).
///
/// Land. Oracle text:
///   "{T}: Add {C}."
///   "{T}, Pay 1 life: Add one mana of any color. Activate only if you
///    control an artifact."
///
/// ## Implementation (v1)
///
/// This card composes two ability shapes the engine already supports — it
/// needs no new mechanic:
///
/// ### "{T}: Add {C}."
/// A vanilla <see cref="ManaAbility"/> producing colourless mana ({C}
/// parses into the <see cref="ManaCost.Generic"/> slot — same as Darksteel
/// Citadel / Wasteland's tap-for-{C}). Gated on the land being untapped
/// (CR 605.1 — mana abilities do not use the stack). No life cost, no
/// artifact gate.
///
/// ### "{T}, Pay 1 life: Add one mana of any color. Activate only if you
///      control an artifact."
/// Modelled as five <see cref="ManaAbility"/> instances (one per WUBRG) —
/// the same any-colour pattern as <see cref="GlimmervoidFactory"/> /
/// Mox Opal / City of Brass. Each uses the additional-cost overload of
/// <see cref="ManaAbility"/> (the Horizon-land "{T}, Pay 1 life" shape —
/// see <see cref="HorizonLandBinder.AttachPayLifeMana"/>):
///   - <c>additionalCostPayer</c> = <c>p =&gt; p.LoseLife(1)</c> — the
///     printed "Pay 1 life" cost.
///   - <c>canActivateCheck</c> gates legality on three conditions:
///       1. the land is untapped ({T} availability),
///       2. CR 119.4 — life total &gt; 1 ("you can't pay life you don't
///          have"),
///       3. the controller controls at least one artifact ("Activate only
///          if you control an artifact" — a CR 602.5 activation
///          restriction; the gate is the negation of
///          <see cref="GlimmervoidFactory.ControlsNoArtifacts"/>).
///
/// Spire of Industry is a Land, not an Artifact, so it never satisfies its
/// own artifact gate (mirrors Glimmervoid's "you control no artifacts"
/// predicate excluding itself).
///
/// ## Deferred (v1 gaps)
/// - "Add one mana of any color" is five separate ManaAbility instances;
///   a single modal-colour ability (choose at activation) is not yet in
///   the engine — same gap as Glimmervoid / Mox Opal / City of Brass.
/// - The artifact-control gate is evaluated against the controller's
///   battlefield via <see cref="GlimmervoidFactory.ControlsNoArtifacts"/>;
///   it is a live read at <c>CanActivate</c> time, so transient artifacts
///   are handled correctly.
///
/// ## Authoring note
/// Written as a hand-rolled C# factory (not a JSON
/// <c>CardDefinition</c>) because the JSON schema's
/// <c>ManaAbilityDefinition</c> cannot yet express any-colour mana, a
/// "Pay N life" cost, or an "activate only if …" restriction — exactly
/// the same reason Glimmervoid and the Horizon-land cycle are C# factories
/// (see <see cref="HorizonLandBinder"/>'s xmldoc). When those schema
/// variants land, this factory can collapse to the thin JSON-loader shape.
/// </summary>
[CardName("Spire of Industry")]
public static class SpireOfIndustryFactory
{
    public const string CardName = "Spire of Industry";

    /// <summary>
    /// Construct Spire of Industry owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ------------------------------------------------------------------
        // {T}: Add {C}. Vanilla colourless mana ability — no life cost, no
        // artifact gate. CR 605.1 — mana abilities do not use the stack.
        // ------------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("C"),
            canActivateCheck: () => !land.IsTapped));

        // ------------------------------------------------------------------
        // {T}, Pay 1 life: Add one mana of any color.
        // Activate only if you control an artifact.
        //
        // Five ManaAbility instances (one per WUBRG), each with:
        //   - the "Pay 1 life" additional cost (additionalCostPayer),
        //   - a canActivateCheck enforcing: untapped, life > 1 (CR 119.4),
        //     and "control an artifact" (CR 602.5 activation restriction).
        // The land's Controller is read live so a control-change effect
        // routes the artifact-gate check to the current controller.
        // ------------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () =>
                    !land.IsTapped
                    && owner.LifeTotal > 1
                    && ControllerControlsAnArtifact(land, owner),
                additionalCostPayer: p => p.LoseLife(1)));
        }

        return land;
    }

    /// <summary>
    /// True when the land's current controller controls at least one
    /// artifact permanent. Negation of
    /// <see cref="GlimmervoidFactory.ControlsNoArtifacts"/>; Spire of
    /// Industry itself is a Land (not an Artifact) so it never counts
    /// toward its own gate.
    /// </summary>
    private static bool ControllerControlsAnArtifact(Land land, Player owner)
    {
        var controller = land.Controller ?? owner;
        return !GlimmervoidFactory.ControlsNoArtifacts(controller);
    }
}
