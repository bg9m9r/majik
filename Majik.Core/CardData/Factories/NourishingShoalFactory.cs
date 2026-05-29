using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nourishing Shoal (Betrayers of Kamigawa, {X}{G}{G}).
///
/// Instant — Arcane. Oracle text:
///   "You may exile a green card with mana value X from your hand rather
///    than pay this spell's mana cost.
///    You gain X life."
///
/// ## Implementation (v1)
///
/// - <b>Instant — Arcane</b> shape, printed cost <c>{X}{G}{G}</c>, green.
///   Arcane subtype (CR 205.3k) stamped via
///   <see cref="Majik.Core.Cards.Types.CardSubtype.Arcane"/> — required for
///   Splice onto Arcane riders (<see cref="Majik.Core.Costs.SpliceOntoArcaneCost"/>)
///   to target this spell.
///
/// - <b>Alternative cost (Shoal pitch — CR 118.9):</b>
///   Callers construct <see cref="ExileGreenMVXAlternativeCost"/> with:
///     • <see cref="ManaColor.Green"/> (required color — oracle: "a green card"),
///     • the declared X (required MV — oracle: "with mana value X"),
///     • the hand card the caster chose to exile.
///   <see cref="BuildAlternativeCost"/> is a convenience helper for this.
///   The alt-cost is fully validated in <see cref="ExileGreenMVXAlternativeCost.CanCastFor"/>:
///   owner check, hand check, color check, MV == X check.
///   When the alt-cost is used, no mana is paid (<see cref="ManaCost.Zero"/>).
///   CR 107.3b — when an alternative cost replaces the printed mana cost and
///   does not specify a value for X, X is the value declared at cast time (the
///   player chose X beforehand; the alt cost stores that same X as
///   <see cref="ExileGreenMVXAlternativeCost.RequiredManaValue"/>).
///
/// - <b>Gain X life</b> (CR 119.4 — simple life gain):
///   <see cref="BuildSpellDefinition"/> wires <c>HasVariableX = true</c>; on
///   resolution <see cref="Fx.GainLife"/> is called with
///   <c>ChosenSpellParams.X</c>. When X = 0 the gain is 0 (no-op).
///
/// ## Design references
///
/// - Alt-cost shape: <see cref="ExileGreenMVXAlternativeCost"/> (new — this
///   cycle). Analogues for the exile-from-hand pattern:
///   <see cref="ExileColoredCardAlternativeCost"/> (Force cycle) and
///   <see cref="PitchAlternativeCost"/> (Force of Will).
/// - X + life gain: <see cref="MarchOfWretchedSorrowFactory"/> for the
///   <c>HasVariableX = true</c> + <see cref="Fx.GainLife"/> wiring idiom.
/// - Arcane subtype: <see cref="LavaSpikeFactory"/> (same subtype stamp).
/// - CardDef DSL alternative construction via <c>new Instant(...)</c>:
///   <see cref="MarchOfWretchedSorrowFactory.Create"/> (the X-cost shape
///   doesn't go through CardDefRuntime so the owner/controller are wired
///   manually — same pattern here).
/// </summary>
[CardName("Nourishing Shoal")]
public static class NourishingShoalFactory
{
    public const string CardName = "Nourishing Shoal";
    public const string PrintedManaCost = "{X}{G}{G}";

    /// <summary>Required color for the Shoal pitch (oracle: "a green card").</summary>
    public const ManaColor PitchColor = ManaColor.Green;

    /// <summary>
    /// Build a Nourishing Shoal Instant — Arcane owned and controlled by
    /// <paramref name="owner"/>. Card shape only; the life-gain body is
    /// built on demand via <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost,
            subtypes: new[] { CardSubtype.Arcane });
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Convenience helper — build the Shoal pitch alternative cost for the
    /// given declared <paramref name="x"/> and chosen <paramref name="exiledCard"/>.
    ///
    /// The caller must:
    ///   1. Prompt the agent for X (via <c>ChooseXAsync</c> or equivalent).
    ///   2. Prompt the agent to pick a green card from hand with MV = X.
    ///   3. Call this helper and pass the result to <c>SpellCastFlow.CastAsync</c>
    ///      as <c>alternativeCost</c>.
    ///
    /// <see cref="ExileGreenMVXAlternativeCost.CanCastFor"/> will validate
    /// the card's color and MV against X at the cast-time permission check.
    /// </summary>
    public static ExileGreenMVXAlternativeCost BuildAlternativeCost(int x, ICard exiledCard)
    {
        ArgumentNullException.ThrowIfNull(exiledCard);
        return new ExileGreenMVXAlternativeCost(PitchColor, x, exiledCard);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Nourishing Shoal.
    /// <see cref="SpellDefinition.HasVariableX"/> = true so the engine
    /// prompts for X at cast time; resolution reads
    /// <c>ChosenSpellParams.X</c> and calls <see cref="Fx.GainLife"/>
    /// on the caster. No targets — the oracle text has no target clause.
    /// </summary>
    /// <param name="caster">Spell controller — gains X life on
    /// resolution (CR 119.4).</param>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: chosen =>
            {
                var x = chosen.X ?? 0;
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: gain {x} life (X={x})",
                        () =>
                        {
                            // CR 119.4 — you gain X life. X is the value
                            // declared at cast time (CR 202.3b). When X = 0
                            // no life is gained.
                            if (x > 0)
                            {
                                Fx.GainLife(caster, x);
                            }
                        }),
                };
            });
    }
}
