using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seal of Cleansing (Nemesis / reprints, {1}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall 2026-05-29):
///   "Sacrifice this enchantment: Destroy target artifact or enchantment."
///
/// Seal of Cleansing is the enchantment-permanent analogue of
/// <see cref="DisenchantFactory"/> (instant "Destroy target artifact or
/// enchantment") wrapped on the sacrifice-self activated-ability chassis used
/// by <see cref="PyriteSpellbombFactory"/> / <see cref="NihilSpellbombFactory"/>.
///
/// The base card shape (name / Enchantment type / {1}{W} cost) is materialised
/// from the embedded JSON definition (<c>seal-of-cleansing.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the activated ability is layered
/// on here because the JSON ability schema's <c>destroy_target_stub</c> effect
/// is a deliberate no-op (targeting isn't wired through the JSON path), whereas
/// the real destroy-target behaviour requires the
/// <see cref="ActivatedAbility"/> + <see cref="TargetRequest"/> +
/// <c>ChosenTargets</c> pipeline used below. Same posture as
/// <see cref="ArdentPleaFactory"/> (shape from JSON, behaviour in C#).
///
/// ## Implemented (v1)
/// - <b>Enchantment shape</b> at printed cost {1}{W}, White (mana value 2).
/// - <b>"Sacrifice this enchantment: Destroy target artifact or enchantment"</b>
///   — a single <see cref="ActivatedAbility"/> whose only cost is
///   <see cref="AdditionalCost.Sacrifice"/> on the Seal itself (no mana, no
///   tap). One 1..1 <see cref="TargetRequest"/> for "target artifact or
///   enchantment"; the <see cref="TargetRequest.CandidateGatherer"/> walks
///   every battlefield, yielding permanents that are artifacts or enchantments
///   (CR 301–303) — exactly mirroring Disenchant. On resolution the effect
///   reads <see cref="ActivatedAbility.ChosenTargets"/>, re-checks the target
///   is still a <see cref="Permanent"/> on the battlefield with type Artifact
///   or Enchantment (CR 608.2b illegal-target gate), then destroys it via
///   <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so Indestructible
///   (CR 702.12) and regeneration (CR 701.15) shields are honoured. The Seal
///   then sacrifices itself.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op stub,
///   so the effect closure performs the self-sacrifice zone move itself —
///   identical to Pyrite / Nihil Spellbomb. Remove the explicit move once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice.
/// </summary>
[CardName("Seal of Cleansing")]
public static class SealOfCleansingFactory
{
    public const string CardName = "Seal of Cleansing";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "seal-of-cleansing";

    /// <summary>
    /// Construct Seal of Cleansing owned and controlled by
    /// <paramref name="owner"/>, with its self-sacrifice destroy ability
    /// attached. The ability is not registered with any stack/priority
    /// manager — suitable for shape, dispatcher, and direct-resolution tests
    /// (same posture as the Spellbomb factories).
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Enchantment / {1}{W}) from the embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Enchantment seal)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Enchantment but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // "Sacrifice this enchantment: Destroy target artifact or
        // enchantment." (CR 602 — activated ability; cost is self-sacrifice
        // only.) A single 1..1 target request for "artifact or enchantment";
        // CandidateGatherer mirrors Disenchant (CR 301–303). Resolution reads
        // ChosenTargets and destroys the chosen permanent (CR 701.7) after the
        // illegal-target re-check (CR 608.2b). The Seal then sacrifices itself.
        // ----------------------------------------------------------------
        ActivatedAbility? destroyAbility = null;
        var destroyEffect = new Effect(
            $"{CardName}: destroy target artifact or enchantment + sac self",
            () =>
            {
                if (destroyAbility != null
                    && destroyAbility.ChosenTargets.Count > 0
                    && destroyAbility.ChosenTargets[0].Count > 0)
                {
                    var raw = destroyAbility.ChosenTargets[0][0];

                    // CR 608.2b — resolution-time legality re-check: the target
                    // must still be a permanent on the battlefield that is an
                    // artifact or enchantment.
                    if (raw is Permanent target
                        && target.Zone == ZoneType.Battlefield
                        && (target.HasType(CardType.Artifact)
                            || target.HasType(CardType.Enchantment)))
                    {
                        // CR 701.7 — Destroy. Indestructible (CR 702.12) and
                        // regeneration (CR 701.15) honoured via the
                        // Destroy-reason gate in MoveToGraveyard.
                        Fx.MoveToGraveyard(target, ZoneMoveReason.Destroy);
                    }
                }

                // Self-sacrifice (cost paid — happens regardless of whether a
                // legal target remained at resolution).
                SacrificeSelf(seal, owner);
            });

        destroyAbility = new ActivatedAbility(
            source: seal,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(seal),
            },
            effects: new IEffect[] { destroyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt: walk every battlefield, yield permanents
                    // that are artifacts or enchantments (CR 301–303).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                 || c.HasType(CardType.Enchantment))
                        .Cast<object>()
                        .ToList()),
            });

        seal.AddAbility(destroyAbility);

        return seal;
    }

    /// <summary>
    /// Move <paramref name="seal"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if already off the battlefield. Mirrors
    /// the closure used by Pyrite / Nihil Spellbomb (the generic
    /// <see cref="AdditionalCost.Pay"/> sacrifice path is a stub).
    /// </summary>
    private static void SacrificeSelf(Enchantment seal, Player owner)
    {
        if (seal.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(seal);
        owner.Zones.Graveyard.AddCard(seal);
        seal.SetZone(ZoneType.Graveyard);
    }
}
