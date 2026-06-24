using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pearl of Wisdom (Bloomburrow, {2}{U}).
///
/// Sorcery. Oracle text (verified against the embedded Modern seed):
///   "This spell costs {1} less to cast if you control an Otter.
///    Draw two cards."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {2}{U}; mana value 3</item>
///   <item>Type line: Sorcery; colors: U</item>
/// </list>
///
/// Same controller-board conditional-cost-reduction shape as
/// <see cref="GeistlightSnareFactory"/> (a single {1}-less rider keyed off the
/// caster's board), with the trivial "Draw two cards" body modelled on
/// <see cref="ThoughtcastFactory.BuildResolveEffect"/>.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{U} (blue). The base shape (name, Sorcery,
///   {2}{U}, blue) is materialised from the embedded JSON definition
///   (<c>pearl-of-wisdom.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
///   <see cref="CardDefinitionFactory"/> — same posture as the other
///   data-backed factories.
/// - <b>Conditional cost reduction (CR 117.7 / 117.7a)</b>: a single
///   <see cref="CostReductionAbility"/> using the
///   <see cref="CostReductionAbility.TotalReducer"/> whole-reduction shape.
///   At cost-calc time the closure scans the caster's battlefield
///   (<see cref="Player.Zones"/> → Battlefield) and reduces the generic mana by
///   {1} iff the caster controls at least one Otter
///   (<see cref="Card.HasSubtype"/>(<see cref="CardSubtype.Otter"/>)). CR 117.7c
///   — only the generic mana is reduced; the floor-at-zero in
///   <see cref="CostReduction.GetEffectiveCost"/> keeps the {U} pip, so
///   {2}{U} → {1}{U} when an Otter is controlled.
/// - <b>Resolve effect (via <see cref="BuildResolveEffect"/>)</b>: draws two
///   cards top-of-library. Empty library mid-draw flags the player for the
///   SBA-driven loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>. Mirrors the
///   draw-loop shape used by
///   <see cref="ThoughtcastFactory.BuildResolveEffect"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-effect draws</b>: <see cref="BuildResolveEffect"/> does its
///   draws via direct top-of-library + zone moves (same posture as Thoughtcast /
///   Wrenn's Resolve), not through a centralised "Player.DrawCard" pipeline.
///   Draw-replacement effects won't see Pearl of Wisdom's draws until a unified
///   draw API lands — engine-wide gap, not card-specific.
/// </summary>
[CardName("Pearl of Wisdom")]
public static class PearlOfWisdomFactory
{
    public const string CardName = "Pearl of Wisdom";
    public const string Slug = "pearl-of-wisdom";

    /// <summary>Generic reduction granted when the caster controls an Otter
    /// (CR 117.7a).</summary>
    public const int OtterReduction = 1;

    /// <summary>
    /// Construct Pearl of Wisdom as a Sorcery card with owner / controller wired
    /// + the Otter-conditional cost-reduction ability attached. The resolve
    /// effect (draw two) is built on demand via <see cref="BuildResolveEffect"/>
    /// so tests / integrations can splice it into a
    /// <see cref="Majik.Core.Game.SpellDefinition"/> or pass it directly to a
    /// <see cref="Majik.Core.Spells.Spell"/>. The base shape (name, Sorcery,
    /// {2}{U}, blue) is materialised from the embedded JSON definition.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Sorcery)CardDefinitionFactory.Build(definition, owner);

        // CR 117.7 / 117.7a — "This spell costs {1} less to cast if you control
        // an Otter." Single conditional reduction; the whole-reducer closure
        // inspects the caster's battlefield and contributes {1} iff an Otter is
        // controlled. CR 117.7c — generic only; the {U} pip is floored by
        // CostReduction.GetEffectiveCost so {2}{U} → {1}{U} at most.
        card.AddAbility(new CostReductionAbility(
            totalReducer: ComputeReduction,
            description: "This spell costs {1} less to cast if you control an Otter."));

        return card;
    }

    /// <summary>
    /// Caster-board reduction (CR 117.7a): {1} for controlling an Otter.
    /// Tolerates a null roster / battlefield (shape-only + pre-board
    /// affordability calls).
    /// </summary>
    private static int ComputeReduction(Player? caster)
    {
        var battlefield = caster?.Zones?.Battlefield;
        if (battlefield == null) return 0;

        foreach (var permanent in battlefield.GetCards())
        {
            if (permanent.HasSubtype(CardSubtype.Otter))
            {
                return OtterReduction;
            }
        }

        return 0;
    }

    /// <summary>
    /// Build Pearl of Wisdom's resolve effect — draw two cards top-of-library.
    /// Mirrors <see cref="ThoughtcastFactory.BuildResolveEffect"/>'s draw loop.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Pearl of Wisdom: draw two cards.", () =>
            {
                // CR 121.1 — two simple top-of-library draws. Empty library
                // mid-draw flags the SBA loss (CR 704.5b) and short-circuits the
                // remaining draws.
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
