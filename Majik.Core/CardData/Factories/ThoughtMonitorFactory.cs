using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thought Monitor (Modern Horizons 2, {6}{U}).
///
/// Artifact Creature — Construct 2/2. Oracle text (verified against
/// Scryfall):
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)
///    Flying
///    When this creature enters, draw two cards."
///
/// The card's base shape (name, Artifact Creature, Construct subtype,
/// {6}{U}, 2/2) and the ETB "draw two cards" trigger are materialised from
/// the embedded JSON definition (<c>thought-monitor.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — the JSON schema already
/// expresses the <c>etb_self</c> trigger + <c>draw_card</c> effect (same
/// path as <see cref="LibrarySurveyorFactory"/>'s surveil ETB). Affinity
/// and Flying are layered on in code because the
/// <see cref="AbilityDefinition"/> schema doesn't yet express keyword
/// markers or cost reducers (same posture as
/// <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - 2/2 Artifact Creature — Construct at printed cost {6}{U}. The dual
///   Artifact + Creature typing comes from the JSON <c>types</c> array
///   (CR 301.1 / 302.1 — same shape as Vault Skirge / Frogmite).
/// - <b>Affinity for artifacts (CR 702.40 / CR 117.7)</b>: wired via
///   <see cref="CostReductionAbility.AffinityFor"/>(<see cref="CardType.Artifact"/>),
///   identical to <see cref="VaultSkirgeFactory"/> / <see cref="ThoughtcastFactory"/>.
///   <see cref="CostReduction.GetEffectiveCost"/> scans the caster's
///   battlefield at cast time and lowers the generic-mana component by 1
///   per controller-controlled artifact; floor-at-zero (CR 117.7c). The
///   lone coloured pip is {U}, so six artifacts reduces the spell to {U}.
/// - <b>Flying (CR 702.9)</b>: a <see cref="KeywordAbility"/> marker so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> /
///   <c>CanBlockFlying</c> surface the evasion / block-legality
///   properties (same marker shape as Vault Skirge / Stormscale Scion).
/// - <b>ETB draw two (CR 603.6)</b>: the JSON <c>etb_self</c> trigger +
///   <c>draw_card</c> (amount 2) effect. At resolution the controller
///   draws the top two cards of their library; empty library is a no-op
///   at the effect level and the SBA-driven loss (CR 704.5b) is handled
///   by the engine's state-based-action pass elsewhere.
/// - A <see cref="KeywordAbility"/>("Affinity") marker is attached so
///   keyword-scan callers see the keyword without inspecting the
///   <see cref="CostReductionAbility"/> list — matches the Frogmite /
///   Vault Skirge shape.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-effect draws</b>: the JSON <c>draw_card</c> effect
///   does its draws via direct top-of-library + zone moves rather than a
///   centralised "Player.DrawCard" pipeline, so draw-replacement effects
///   (Dredge, etc.) won't observe Thought Monitor's draws until a unified
///   draw API lands — engine-wide gap, not card-specific (same posture as
///   Thoughtcast).
/// </summary>
[CardName("Thought Monitor")]
public static class ThoughtMonitorFactory
{
    public const string CardName = "Thought Monitor";
    public const string Slug = "thought-monitor";

    /// <summary>
    /// Construct Thought Monitor owned and controlled by
    /// <paramref name="owner"/>. Base shape + ETB draw-two trigger come
    /// from the embedded JSON; Affinity-for-artifacts + Flying are layered
    /// on here.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (Artifact Creature — Construct, {6}{U}, 2/2) + the
        // ETB "draw two cards" trigger from the embedded JSON definition.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.40 / CR 117.7 — Affinity-for-artifacts cost reducer +
        // keyword marker. Vault Skirge / Thoughtcast wiring.
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Artifact));
        card.AddAbility(new KeywordAbility("Affinity", card, owner));

        // CR 702.9 — Flying. KeywordAbility marker so CombatAbilities
        // surfaces evasion / block-legality.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
