using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

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
/// - <b>Cast trigger — "When you cast this spell, you gain control of target
///   opponent during that player's next turn. After that turn, that player
///   takes an extra turn." (CR 603.1 / CR 720.1 / CR 500.7)</b>: attached as
///   a <see cref="TriggeredAbility"/> on Emrakul's own
///   <see cref="SpellCastEvent"/> while it is on the stack
///   (<c>activeZones = {Stack}</c>, same shape as Bloodbraid Elf's cascade —
///   the <see cref="TriggerManager"/> auto-binds the card on its Hand → Stack
///   move and registers the trigger). A 1..1 "target opponent" request
///   gathers the controller's opponents live; on resolution the chosen
///   opponent's next turn is taken over via the
///   <see cref="ControlPlayerRegistry"/> (shipped #1688, wired through
///   <see cref="ControlPlayerRegistryProvider"/> exactly as
///   <see cref="MindslaverFactory"/>), carrying the extra-turn-after rider so
///   the controlled opponent takes an extra turn once the controlled turn
///   ends.
///
/// ## Deferred sub-caveats (CR 720.5 / 720.6 — documented, not modelled)
/// - The controller still can't make the controlled opponent concede
///   (CR 720.6); engine-resolved random choices (discard at random) are
///   unaffected. Neither regresses existing behaviour — see
///   <see cref="ControlPlayerRegistry"/>'s class doc.
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

        // ----------------------------------------------------------------
        // Cast trigger (CR 603.1 / CR 720.1 / CR 500.7):
        //   "When you cast this spell, you gain control of target opponent
        //    during that player's next turn. After that turn, that player
        //    takes an extra turn."
        // Fires on Emrakul's own SpellCastEvent while it's on the stack
        // (activeZones = {Stack}, same shape as Bloodbraid Elf's cascade —
        // the TriggerManager auto-binds the card's triggers when it crosses
        // Hand → Stack, and registers this one because its active zone is the
        // Stack). The 1..1 target-opponent request gathers opponents live
        // from the controller's GameContext; on resolution the chosen
        // opponent's next turn is taken over via the live
        // ControlPlayerRegistry, with Emrakul's extra-turn rider
        // (CR 500.7) so that opponent takes an extra turn afterwards.
        // ----------------------------------------------------------------
        var castTrigger = BuildCastTrigger(card, owner);
        card.AddAbility(castTrigger);

        return card;
    }

    /// <summary>
    /// CR 603.1 / CR 720.1 / CR 500.7 — Emrakul's on-cast take-control
    /// trigger. Fires on Emrakul's own <see cref="SpellCastEvent"/> while it
    /// is on the stack. The 1..1 target request gathers the controller's
    /// opponents live; on resolution the chosen opponent's next turn is taken
    /// over via the live <see cref="ControlPlayerRegistry"/> (resolved through
    /// <see cref="ControlPlayerRegistryProvider"/>, the same indirection
    /// Mindslaver uses), carrying the extra-turn-after rider so the controlled
    /// opponent takes an extra turn once the controlled turn ends.
    /// </summary>
    private static TriggeredAbility BuildCastTrigger(Creature card, Player owner)
    {
        // CR 603.6a — "when you cast this spell": fires only for Emrakul's
        // own SpellCastEvent (reference identity against the card).
        var condition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        TriggeredAbility? trigger = null;
        var effect = new Effect(
            $"{CardName}: control target opponent's next turn + extra turn after (CR 720.1, 500.7)",
            () =>
            {
                if (trigger == null) return;
                if (trigger.ChosenTargets.Count == 0) return;
                if (trigger.ChosenTargets[0].Count == 0) return;
                // CR 608.2b — illegal / no target → no-op.
                if (trigger.ChosenTargets[0][0] is not Player targetOpponent) return;

                // CR 720.1 + CR 500.7 — gain control of the target opponent's
                // next turn, with Emrakul's "after that turn, that player
                // takes an extra turn" rider. Registry resolved at resolution
                // time via the provider (keyed by the controlling player).
                // Null in shape-only construction → no-op (the trigger is
                // still attached for shape inspection).
                var registry = ControlPlayerRegistryProvider.Get(owner);
                registry?.GrantControl(
                    controller: owner,
                    controlled: targetOpponent,
                    extraTurnAfter: true);
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    // CR 109.5 / 720.1 — "target opponent": every player in
                    // the game except Emrakul's controller (ctx.Self is the
                    // trigger's controller when the agent is prompted).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, ctx.Self))
                        .Cast<object>()
                        .ToList()),
            },
            // CR 702.85-style on-cast trigger — active while the spell is on
            // the stack (mirrors Bloodbraid Elf's cascade). The TriggerManager
            // auto-binds the card on its Hand → Stack move and registers this
            // trigger because its active zone is the Stack.
            activeZones: new[] { ZoneType.Stack });

        return trigger;
    }
}
