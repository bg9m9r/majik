using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vampire Hexmage (Zendikar, {B}{B}).
///
/// Creature — Vampire Shaman 2/1. Oracle text (verified against Scryfall):
///   "First strike
///    Sacrifice this creature: Remove all counters from target permanent."
///
/// The base shape (name, Creature, Vampire/Shaman subtypes, {B}{B}, 2/1) is
/// materialised from the embedded JSON definition
/// (<c>vampire-hexmage.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON declares no abilities
/// — the First strike marker and the sacrifice activated ability are layered
/// on here (same posture as <see cref="GlissaSunslayerFactory"/>, whose
/// "remove counters from target permanent" arm this card mirrors).
///
/// ## Implemented (v1)
/// - 2/1 Creature — Vampire Shaman, mana cost {B}{B}, owner/controller stamped.
/// - <b>First strike (CR 702.7)</b>: <see cref="KeywordAbility"/> marker read
///   by <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/> (same
///   wiring as <see cref="GlissaSunslayerFactory"/> / Phyrexian Crusader).
/// - <b>"Sacrifice this creature: Remove all counters from target permanent"</b>
///   — <see cref="ActivatedAbility"/> (CR 602.1) with a single
///   self-sacrifice cost and a 1..1 "target permanent" slot:
///   <list type="bullet">
///     <item><see cref="AdditionalCost.Sacrifice"/> on the Hexmage itself —
///       the cost surface registers the intent; the actual battlefield →
///       graveyard zone move is performed inside the effect closure (the
///       generic <see cref="AdditionalCost.Pay"/> sacrifice path is a no-op
///       stub, same pattern as <see cref="NihilSpellbombFactory"/> /
///       <see cref="InsolentNeonateFactory"/>).</item>
///     <item>Effect — remove ALL counters of every type from the target
///       permanent (CR 122.5 / CR 121.6). Unlike Glissa's "up to three", this
///       is the unconditional "remove all" variant: every counter type on the
///       target is drained to zero. Resolution-time legality is re-checked
///       (CR 608.2b) — the target must still be a permanent on the
///       battlefield.</item>
///   </list>
///
/// ## Order of operations
/// CR 117.1c — the sacrifice cost is paid as part of activation; the
/// implementation performs the sacrifice zone move inside the resolution
/// closure (the cost surface is a stub). The counter removal then runs on the
/// chosen target, which is independent of the source having left the
/// battlefield.
///
/// ## Deferred (v1 gaps)
/// - <b>Activation-zone gate</b>: <see cref="ActivatedAbility"/> doesn't gate
///   on <see cref="ZoneType.Battlefield"/> yet; the effect closure guards on
///   the Hexmage's current zone before sacrificing so a stale activation
///   re-entry can't double-sacrifice (same posture as
///   <see cref="InsolentNeonateFactory"/>).
/// </summary>
[CardName("Vampire Hexmage")]
public static class VampireHexmageFactory
{
    public const string CardName = "Vampire Hexmage";
    public const string Slug = "vampire-hexmage";

    /// <summary>
    /// Construct Vampire Hexmage owned and controlled by
    /// <paramref name="owner"/>. The First strike keyword marker + the
    /// sacrifice-to-remove-counters activated ability are attached to the
    /// card. The ability is self-contained — no service wiring required.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Vampire/Shaman subtypes, {B}{B}, 2/1). The JSON carries no abilities
        // — the keyword marker + activated ability are layered on here.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.7 — First strike. Marker keyword read by CombatAbilities
        // during combat damage assignment.
        card.AddAbility(new KeywordAbility("First strike", card, owner));

        // ----------------------------------------------------------------
        // Sacrifice this creature: Remove all counters from target permanent.
        // CR 602.1 — activated ability. Single self-sacrifice cost, single
        // 1..1 "target permanent" slot. The sacrifice payment is performed
        // inside the effect closure because the generic AdditionalCost.Sacrifice
        // payment is a no-op stub (same pattern as Nihil Spellbomb / Insolent
        // Neonate). On resolution: read the chosen permanent and drain every
        // counter type to zero (CR 122.5 — "remove all counters").
        // ----------------------------------------------------------------
        ActivatedAbility? removeAbility = null;
        var removeEffect = new Effect(
            $"{CardName}: sacrifice self + remove all counters from target permanent",
            () =>
            {
                // Sacrifice payment — battlefield → owner's graveyard.
                // CR 701.16 — idempotent guard against stale activations.
                if (card.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(card);
                    owner.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }

                if (removeAbility == null) return;

                var chosen = removeAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Permanent permanent) return;

                // CR 608.2b — target must still be a permanent on the
                // battlefield at resolution.
                if (permanent.Zone != ZoneType.Battlefield) return;

                // CR 122.5 — remove ALL counters of every type. Snapshot the
                // counter types before mutating (removing while enumerating the
                // backing dictionary would throw).
                var present = permanent.Counters.All
                    .Where(kvp => kvp.Value > 0)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var type in present)
                {
                    permanent.Counters.Remove(type, permanent.Counters.Count(type));
                }
            });

        removeAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { removeEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(removeAbility);

        return card;
    }
}
