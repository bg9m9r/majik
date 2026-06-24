using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Salt Road Packbeast ({5}{W}).
///
/// Creature — Beast 4/3. Oracle text (verified against Scryfall):
///   "Affinity for creatures (This spell costs {1} less to cast for each
///    creature you control.)
///    When this creature enters, draw a card."
///
/// The card's base shape (name, Creature — Beast, {5}{W}, 4/3) and the ETB
/// "draw a card" trigger are materialised from the embedded JSON definition
/// (<c>salt-road-packbeast.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — the JSON schema already
/// expresses the <c>etb_self</c> trigger + <c>draw_card</c> effect (same
/// path as <see cref="ThoughtMonitorFactory"/>'s ETB draw). Affinity is
/// layered on in code because the <see cref="AbilityDefinition"/> schema
/// doesn't yet express keyword markers or cost reducers (same posture as
/// <see cref="ThoughtMonitorFactory"/>).
///
/// ## Implemented (v1)
/// - 4/3 Creature — Beast at printed cost {5}{W} (from the JSON
///   <c>types</c>/<c>subtypes</c>/<c>power</c>/<c>toughness</c>).
/// - <b>Affinity for creatures (CR 702.40 / CR 117.7)</b>: wired via
///   <see cref="CostReductionAbility.AffinityFor"/>(<see cref="CardType.Creature"/>),
///   identical in shape to <see cref="ThoughtMonitorFactory"/> /
///   <see cref="ThoughtcastFactory"/> but counting creatures rather than
///   artifacts. <see cref="CostReduction.GetEffectiveCost"/> scans the
///   caster's battlefield at cast time and lowers the generic-mana
///   component by 1 per controller-controlled creature; floor-at-zero
///   (CR 117.7c). The lone coloured pip is {W}, so five creatures reduces
///   the spell to {W}.
/// - <b>ETB draw a card (CR 603.6)</b>: the JSON <c>etb_self</c> trigger +
///   <c>draw_card</c> (amount 1) effect. At resolution the controller draws
///   the top card of their library; empty library is handled by the
///   engine's SBA-driven loss pass (CR 704.5b) elsewhere.
/// - A <see cref="KeywordAbility"/>("Affinity") marker is attached so
///   keyword-scan callers see the keyword without inspecting the
///   <see cref="CostReductionAbility"/> list — matches the Thought Monitor /
///   Frogmite shape.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-effect draws</b>: the JSON <c>draw_card</c> effect does
///   its draw via direct top-of-library + zone moves rather than a
///   centralised "Player.DrawCard" pipeline, so draw-replacement effects
///   (Dredge, etc.) won't observe the draw until a unified draw API lands —
///   engine-wide gap, not card-specific (same posture as Thought Monitor).
/// </summary>
[CardName("Salt Road Packbeast")]
public static class SaltRoadPackbeastFactory
{
    public const string CardName = "Salt Road Packbeast";
    public const string Slug = "salt-road-packbeast";

    /// <summary>
    /// Construct Salt Road Packbeast owned and controlled by
    /// <paramref name="owner"/>. Base shape + ETB draw-a-card trigger come
    /// from the embedded JSON; Affinity-for-creatures is layered on here.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (Creature — Beast, {5}{W}, 4/3) + the ETB "draw a card"
        // trigger from the embedded JSON definition.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.40 / CR 117.7 — Affinity-for-creatures cost reducer +
        // keyword marker. Thought Monitor / Thoughtcast wiring, counting
        // creatures rather than artifacts.
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Creature));
        card.AddAbility(new KeywordAbility("Affinity", card, owner));

        return card;
    }
}
