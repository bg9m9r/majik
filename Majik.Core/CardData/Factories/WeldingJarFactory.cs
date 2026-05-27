using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Welding Jar (Mirrodin / reprints — {0}).
///
/// Artifact. Oracle text:
///   "Sacrifice this artifact: Regenerate target artifact."
///
/// ## Implemented (v1)
/// - Artifact {0} with owner/controller wiring (mirrors
///   <see cref="TormodsCryptFactory"/> / <see cref="MoxOpalFactory"/>
///   for the zero-mana artifact identity shape).
/// - Single <see cref="ActivatedAbility"/> with:
///     * <see cref="AdditionalCost.Sacrifice"/> as the only cost (the
///       generic <c>AdditionalCost.Pay</c> sacrifice path is a no-op
///       today, so the resolution closure performs the Battlefield →
///       Graveyard zone move on Welding Jar itself — mirrors
///       <see cref="TormodsCryptFactory"/> / <see cref="PyriteSpellbombFactory"/>).
///     * A 1..1 "target artifact" <see cref="TargetRequest"/>. Choose-time
///       legality (must be an artifact on the battlefield) is deferred to
///       the agent-prompt pipeline; resolve-time recheck (CR 608.2b)
///       guards illegal targets — the chosen permanent must still be on
///       the battlefield AND still carry <see cref="CardType.Artifact"/>.
/// - Resolution: read <c>ChosenTargets[0][0]</c>, recheck legality, then
///   call <see cref="Permanent.AddRegenerationShield"/> on the target.
///   CR 701.18 + CR 701.15a — "Regenerate [permanent]" creates one
///   regeneration shield on that permanent. Shields stack and clear at
///   EOT; the next time the permanent would be destroyed this turn the
///   shield is consumed, the permanent is tapped, and damage clears
///   (CR 701.15c). Mirrors <see cref="MortivoreFactory"/>'s use of
///   <see cref="Permanent.AddRegenerationShield"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice cost zone move</b>: <see cref="AdditionalCost.Pay"/>'s
///   sacrifice branch is a TODO stub today (same gap as Pyrite / Nihil /
///   Tormod's Crypt). The effect closure performs the Battlefield →
///   Graveyard move so behaviour is observable. Drop the in-closure
///   move once the cost infrastructure performs the sacrifice itself.
/// - <b>Choose-time target filtering</b>: <see cref="TargetRequest.LegalCandidates"/>
///   is empty by default (same posture as Pyrite Spellbomb / Solitude /
///   Heliod) — the agent picks any object; resolve-time legality is the
///   live gate. Choose-time filtering depends on the deferred battlefield
///   gather plumbing.
/// </summary>
[CardName("Welding Jar")]
public static class WeldingJarFactory
{
    public const string CardName = "Welding Jar";
    public const string PrintedManaCost = "{0}";

    /// <summary>
    /// Construct Welding Jar — a {0} Artifact whose sole printed ability is
    /// "Sacrifice this artifact: Regenerate target artifact."
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var jar = new Artifact(CardName, PrintedManaCost);
        jar.SetOwner(owner);
        jar.SetController(owner);

        // ----------------------------------------------------------------
        // Sacrifice this artifact: Regenerate target artifact.
        //
        // CR 602 — activated ability with a single 1..1 "target artifact"
        // request. Cost is AdditionalCost.Sacrifice on Welding Jar itself
        // (the generic Sacrifice path is a no-op stub today, so the effect
        // closure performs the self-sac zone move — same posture as
        // Tormod's Crypt / Pyrite Spellbomb).
        //
        // On resolve:
        //   1. Self-sacrifice Welding Jar (Battlefield → Graveyard).
        //   2. Read the chosen target. CR 608.2b recheck: the chosen
        //      permanent must still be on the battlefield AND still be an
        //      Artifact (type-changing effects could have stripped it).
        //   3. AddRegenerationShield on the target (CR 701.18 / 701.15a).
        // ----------------------------------------------------------------
        ActivatedAbility? regenerateAbility = null;
        var regenerateEffect = new Effect(
            $"{CardName}: regenerate target artifact (sacrifice self)",
            () =>
            {
                // Self-sacrifice: Battlefield → Graveyard. Idempotent if
                // somehow already off the battlefield (mirrors
                // TormodsCryptFactory.SacrificeSelf shape).
                if (jar.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(jar);
                    owner.Zones.Graveyard.AddCard(jar);
                    jar.SetZone(ZoneType.Graveyard);
                }

                if (regenerateAbility == null) return;
                if (regenerateAbility.ChosenTargets.Count == 0) return;
                if (regenerateAbility.ChosenTargets[0].Count == 0) return;

                var raw = regenerateAbility.ChosenTargets[0][0];
                if (raw is not Permanent target) return;

                // CR 608.2b — resolve-time legality recheck. The chosen
                // permanent must still be on the battlefield and still
                // carry the Artifact card type.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Artifact)) return;

                // CR 701.18 / 701.15a — "Regenerate [permanent]" creates
                // one regeneration shield on the target. Shields stack
                // and clear at EOT.
                target.AddRegenerationShield();
            });

        regenerateAbility = new ActivatedAbility(
            source: jar,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(jar),
            },
            effects: new IEffect[] { regenerateEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        jar.AddAbility(regenerateAbility);

        return jar;
    }
}
