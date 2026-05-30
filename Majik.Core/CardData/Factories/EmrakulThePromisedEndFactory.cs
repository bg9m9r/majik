using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Spells;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Emrakul, the Promised End (Eldritch Moon, {13}).
///
/// Legendary Creature — Eldrazi 13/13. Oracle text (Scryfall, verified):
///   "This spell costs {1} less to cast for each card type among cards in
///    your graveyard.
///    When you cast this spell, you gain control of target opponent during
///    that player's next turn. After that turn, that player takes an extra
///    turn.
///    Flying, trample, protection from instants"
///
/// The card's base shape (name, Legendary supertype, Eldrazi subtype, {13},
/// 13/13) is materialised from the embedded JSON definition
/// (<c>emrakul-the-promised-end.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed behaviours are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express cost reduction, protection, or cast triggers, so they live in the
/// factory (same posture as <see cref="KozilekButcherOfTruthFactory"/> /
/// <see cref="EmrakulTheAeonsTornFactory"/>).
///
/// ## Implemented (v1)
/// - <b>13/13 Legendary Creature — Eldrazi at {13}</b> (mana value 13,
///   colourless — CR 105.2c, no coloured symbols).
/// - <b>Graveyard cost reduction (CR 117.7)</b>: a
///   <see cref="CostReductionAbility"/> using the
///   <see cref="CostReductionAbility.TotalReducer"/> whole-reduction shape
///   (same shape as <see cref="TolarianTerrorFactory"/> / Domain). The
///   function counts the <em>distinct card types</em> present among cards in
///   the caster's graveyard at cost-calc time (one {1} reduction per type —
///   Artifact, Creature, Enchantment, Instant, Land, Planeswalker, Sorcery,
///   Tribal — so the reduction is bounded by the eight card types, not the
///   graveyard count). Coloured pips are untouched (CR 117.7c) and the
///   generic floor-at-zero is enforced inside
///   <see cref="CostReduction.GetEffectiveCost"/>; Emrakul has no coloured
///   pips, so e.g. five distinct card types in the graveyard makes it {8}.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/>("Flying") marker
///   — combat code reads via
///   <see cref="Majik.Core.Combat.CombatAbilities"/> (same wiring shape as
///   every other named factory).
/// - <b>Trample (CR 702.19)</b>: <see cref="KeywordAbility"/>("Trample")
///   marker.
/// - <b>Protection from instants (CR 702.16)</b>: shipped as a
///   <see cref="ProtectionAbility"/> carrying a
///   <see cref="ProtectionAbility.SpellPredicate"/> closure
///   <c>spell =&gt; spell.Card.HasType(CardType.Instant)</c> — same surface
///   as <see cref="EmrakulTheAeonsTornFactory"/>'s "protection from coloured
///   spells". Targeting / damage / blocking gates that hold a live spell
///   handle consult
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromSpell"/>. The
///   quality string "instants" is the discoverability / marker label.
///
/// ## Deferred (v1 gap — known, named precisely)
/// - <b>Cast trigger — "gain control of target opponent during that
///   player's next turn" (Mindslaver / CR 720 "Controlling Another
///   Player")</b>: the engine has NO take-control-of-opponent's-turn
///   primitive. <see cref="MindslaverFactory"/> documents the same gap (its
///   activated ability records the chosen target via a sink and sacrifices
///   the artifact, but no turn-substitution runs). Until a ControlPlayer
///   primitive lands there is no faithful way to model "you gain control of
///   target opponent during that player's next turn. After that turn, that
///   player takes an extra turn." Rather than half-build a cast trigger that
///   silently does nothing (or worse, mis-models the extra-turn rider in
///   isolation), the cast trigger is deliberately NOT attached here — it
///   ships when the take-control-of-opponent's-turn infra ships, alongside
///   Mindslaver's mind-control half. Emrakul's body (stats, keywords, cost
///   reduction) is fully faithful; only the cast trigger is deferred.
/// </summary>
[CardName("Emrakul, the Promised End")]
public static class EmrakulThePromisedEndFactory
{
    public const string CardName = "Emrakul, the Promised End";
    public const string Slug = "emrakul-the-promised-end";
    public const int Power = 13;
    public const int Toughness = 13;

    /// <summary>
    /// The eight MTG card types that "card type among cards in your
    /// graveyard" counts over (CR 305.1 / 300.1). Each distinct type present
    /// among graveyard cards contributes one {1} to the cost reduction.
    /// </summary>
    private static readonly CardType[] CountedCardTypes =
    {
        CardType.Artifact,
        CardType.Creature,
        CardType.Enchantment,
        CardType.Instant,
        CardType.Land,
        CardType.Planeswalker,
        CardType.Sorcery,
        CardType.Tribal,
    };

    /// <summary>
    /// Construct Emrakul, the Promised End owned and controlled by
    /// <paramref name="owner"/>. The graveyard-card-type cost reducer, the
    /// Flying + Trample keyword markers, and the protection-from-instants
    /// predicate are attached. The on-cast take-control trigger is deferred
    /// (see class xmldoc — no CR 720 ControlPlayer primitive exists yet).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Creature, Eldrazi subtype, {13}, 13/13). The JSON carries no
        // abilities — the cost reducer / keywords / protection are layered
        // on below (same posture as KozilekButcherOfTruthFactory).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 117.7 — "This spell costs {1} less to cast for each card type
        // among cards in your graveyard." Whole-reduction shape
        // (CostReductionAbility(totalReducer)) — count the DISTINCT card
        // types present among the caster's graveyard cards at cost-calc
        // time, one {1} per type. Bounded by the eight card types
        // (CountedCardTypes), not the graveyard size. CR 117.7c — coloured
        // pips can't reduce; the generic floor-at-zero is enforced inside
        // CostReduction.GetEffectiveCost. Mirrors TolarianTerrorFactory /
        // Domain's reducer shape.
        // ----------------------------------------------------------------
        card.AddAbility(new CostReductionAbility(
            totalReducer: caster =>
            {
                if (caster?.Zones?.Graveyard == null) return 0;
                var graveyard = caster.Zones.Graveyard.GetCards().ToList();
                if (graveyard.Count == 0) return 0;

                var distinctTypes = 0;
                foreach (var type in CountedCardTypes)
                {
                    if (graveyard.Any(g => g.HasType(type))) distinctTypes++;
                }
                return distinctTypes;
            },
            description:
                "This spell costs {1} less to cast for each card type among " +
                "cards in your graveyard."));

        // CR 702.9 — Flying marker. Combat-side reads via CombatAbilities;
        // the marker keeps the keyword-scan surface uniform.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.19 — Trample marker.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // CR 702.16 — Protection from instants. The predicate closes over
        // the live spell's types so the same spell instance flowing through
        // targeting / damage / blocking gates is evaluated against its
        // current type line (CR 105/305 — types can be mutated by
        // continuous effects; we read off the card at gate time). The
        // quality string "instants" is the discoverability marker. Same
        // SpellPredicate surface as Emrakul, the Aeons Torn.
        card.AddAbility(new ProtectionAbility(
            "instants",
            spellPredicate: spell => spell.Card.HasType(CardType.Instant)));

        // NOTE (deferred): the on-cast trigger "you gain control of target
        // opponent during that player's next turn. After that turn, that
        // player takes an extra turn." is NOT attached — the engine has no
        // CR 720 take-control-of-opponent's-turn (Mindslaver) primitive.
        // See class xmldoc + MindslaverFactory for the shared gap.

        return card;
    }
}
