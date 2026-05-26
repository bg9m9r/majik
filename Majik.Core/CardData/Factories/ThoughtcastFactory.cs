using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thoughtcast (Mirrodin, {4}{U}).
///
/// Sorcery. Oracle text:
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)
///    Draw two cards."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {4}{U}.
/// - <b>Affinity for artifacts (CR 702.40 / CR 117.7)</b>: wired the same
///   way as <see cref="FrogmiteFactory"/> / <see cref="MyrEnforcerFactory"/>
///   via <see cref="CostReductionAbility.AffinityFor"/>(<see cref="CardType.Artifact"/>).
///   The cost reducer scans the caster's battlefield at cast time
///   (<see cref="Costs.CostReduction.GetEffectiveCost"/>) and lowers the
///   generic-mana component by 1 per controller-controlled artifact;
///   floor-at-zero (CR 117.7c). Thoughtcast's lone coloured pip is {U}, so
///   four artifacts reduces the spell to {U} (the headline Affinity-blue
///   "cantrip-for-one-blue" turn).
/// - <b>Resolve effect (via <see cref="BuildResolveEffect"/>)</b>: draws
///   two cards top-of-library. Empty library mid-draw flags the player for
///   the SBA-driven loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>. Mirrors the
///   draw-loop shape used by <see cref="WrennsResolveFactory.BuildResolveEffect"/>
///   minus the exile-at-EOT rider — Thoughtcast has no follow-up clause.
/// - A <see cref="KeywordAbility"/> marker "Affinity" is attached so
///   keyword-scan callers (oracle inspectors, bot heuristics) can see
///   Thoughtcast carries the keyword without inspecting the
///   <see cref="CostReductionAbility"/> list — matches the Frogmite /
///   Myr Enforcer shape.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-effect draws</b>: <see cref="BuildResolveEffect"/>
///   does its draws via direct top-of-library + zone moves (same posture
///   as Wrenn's Resolve), not through a centralised "Player.DrawCard"
///   pipeline. Draw-replacement effects (e.g. Dredge, Maralen of the
///   Mornsong) won't see Thoughtcast's draws until a unified draw API
///   lands — engine-wide gap, not card-specific.
/// </summary>
[CardName("Thoughtcast")]
public static class ThoughtcastFactory
{
    public const string CardName = "Thoughtcast";
    public const string PrintedManaCost = "{4}{U}";

    /// <summary>
    /// Build a Thoughtcast sorcery owned by <paramref name="owner"/>.
    /// Card shape + Affinity-for-artifacts cost reducer + keyword marker.
    /// The resolve effect (draw two) is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can splice
    /// it into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
    /// directly to a <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // Affinity for artifacts (CR 702.40 / CR 117.7).
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Artifact));
        card.AddAbility(new KeywordAbility("Affinity", card, owner));

        return card;
    }

    /// <summary>
    /// Build Thoughtcast's resolve effect — draw two cards top-of-library.
    /// Mirrors <see cref="WrennsResolveFactory.BuildResolveEffect"/>'s draw
    /// loop (sans the exile-at-EOT rider).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Thoughtcast: draw two cards.", () =>
            {
                // CR 121.1 — two simple top-of-library draws. Empty
                // library mid-draw flags the SBA loss (CR 704.5b) and
                // short-circuits the remaining draws.
                for (var i = 0; i < 2; i++)
                {
                    var top = caster.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        caster.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    caster.Zones.Library.RemoveCard(top);
                    caster.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            }),
        };
    }
}
