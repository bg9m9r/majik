using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Torch the Witness (Murders at Karlov Manor, {X}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Torch the Witness deals twice X damage to target creature. If excess
///    damage was dealt to that creature this way, investigate. (Create a
///    Clue token. It's an artifact with '{2}, Sacrifice this token: Draw a
///    card.')"
///
/// ## Implemented (v1)
///
/// - <b>Sorcery</b> at <c>{X}{R}</c>, mono-red. The base shape (name, type,
///   cost, colour) is materialised from the embedded JSON definition
///   (<c>torch-the-witness.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>X-keyed damage</b> — built via <see cref="BuildSpellDefinition"/>.
///   <see cref="SpellDefinition.HasVariableX"/> = true so the cast flow
///   prompts for X. One 1..1 "target creature" request (same single-creature
///   shape posture as <see cref="MarchOfWretchedSorrowFactory"/>, narrowed
///   to creatures only). Resolution reads <c>ChosenSpellParams.X</c> and
///   deals <c>2 * X</c> damage (CR 119.2) via <see cref="Fx.DealDamage"/>.
/// - <b>Excess-damage investigate rider (CR 120.10 / 121.4 / 701.18)</b> —
///   "excess damage" is damage exceeding the minimum amount that would be
///   lethal to the creature. For a one-shot spell this is the creature's
///   toughness minus the damage already marked on it this turn
///   (per the official ruling: "consider the damage already dealt to it
///   this turn"). The lethal threshold is captured BEFORE the damage is
///   dealt; if the <c>2 * X</c> dealt exceeds that threshold (and at least
///   1 damage was dealt to a legal creature), the spell's controller
///   investigates — one Clue token via <see cref="Fx.Investigate"/> /
///   <see cref="TokenFactory.CreateClue"/>, the same Clue primitive used by
///   <see cref="NoviceInspectorFactory"/> / Thraben Inspector.
///
/// ## Deferred / not-applicable (v1)
///
/// - <b>Deathtouch-sourced lethal (ruling 2)</b>: if Torch the Witness were
///   granted deathtouch (e.g. by Judith, Carnage Connoisseur) any damage
///   greater than 1 would be excess. Torch has no printed deathtouch and the
///   spell-source deathtouch-granting path is not wired into one-shot
///   damage's excess accounting yet, so this edge is intentionally not
///   modelled here. The base toughness-threshold rule covers every other
///   case.
///
/// ## Design references
///
/// - X-spell damage shape: <see cref="MarchOfWretchedSorrowFactory"/> /
///   <see cref="BonfireOfTheDamnedFactory"/> for the
///   <see cref="SpellDefinition.HasVariableX"/> idiom + single-creature
///   target gatherer.
/// - Investigate / Clue rider: <see cref="NoviceInspectorFactory"/> for the
///   shared Clue primitive (<see cref="Fx.Investigate"/>).
/// </summary>
[CardName("Torch the Witness")]
public static class TorchTheWitnessFactory
{
    public const string CardName = "Torch the Witness";
    public const string Slug = "torch-the-witness";

    /// <summary>
    /// Construct the runtime Sorcery shape from the embedded JSON definition.
    /// The damage + investigate body is built on demand via
    /// <see cref="BuildSpellDefinition"/> because the resolution needs the
    /// caller's target resolver (and zone service for the Clue token).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Torch the Witness is
    /// cast. <see cref="SpellDefinition.HasVariableX"/> is true so the cast
    /// flow prompts for X at cast time; resolution reads
    /// <c>ChosenSpellParams.X</c>, deals <c>2 * X</c> damage to the chosen
    /// creature (CR 119.2), then — if excess damage was dealt
    /// (CR 120.10) — the controller investigates (CR 701.18).
    /// </summary>
    /// <param name="caster">Spell controller — the player who investigates
    /// when excess damage is dealt.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game object).</param>
    /// <param name="zoneService">Zone service used to route the Clue token's
    /// battlefield ETB through <see cref="ZoneService"/> so its arrival event
    /// fires. Null → direct zone move (shape-only tests).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer — every creature on the battlefield across
                    // every player. The bot's score function handles the
                    // ownership flip for Removal intent.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var x = chosen.X ?? 0;
                // Oracle: "deals twice X damage" (CR 119.2).
                var total = x * 2;

                var rawTarget = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? resolver(chosen.Targets[0][0])
                    : null;

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: deal {total} damage to target creature (X={x}); " +
                        "investigate if excess damage was dealt (CR 120.10).",
                        () =>
                        {
                            // CR 608.2b — illegal-at-resolution target (off
                            // battlefield / not a creature) → the spell does
                            // nothing; no damage, no investigate.
                            if (rawTarget is not Creature creature
                                || creature.Zone != ZoneType.Battlefield)
                            {
                                return;
                            }

                            if (total <= 0) return;

                            // CR 120.10 — capture the LETHAL threshold BEFORE
                            // dealing damage: the minimum damage that would be
                            // lethal = toughness minus damage already marked
                            // this turn (per the official ruling). Excess was
                            // dealt iff the damage exceeds that threshold.
                            var lethalNeeded = Math.Max(0, creature.Toughness - creature.Damage);

                            // CR 119.2 — mark twice-X damage on the creature.
                            Fx.DealDamage(creature, total);

                            // CR 121.4 / 701.18 — if excess damage was dealt
                            // this way, the controller investigates (one Clue
                            // token). total > 0 is guaranteed above, so when
                            // lethalNeeded is 0 any damage is excess.
                            if (total > lethalNeeded)
                            {
                                Fx.Investigate(caster, zoneService);
                            }
                        }),
                };
            });
    }
}
