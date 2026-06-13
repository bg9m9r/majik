using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Disrupting Shoal (Betrayers of Kamigawa, {X}{U}{U}).
///
/// Instant — Arcane. Oracle text:
///   "You may exile a blue card with mana value X from your hand rather
///    than pay this spell's mana cost.
///    Counter target spell if its mana value is X."
///
/// ## Implementation (v1)
///
/// - <b>Instant — Arcane</b> shape, printed cost <c>{X}{U}{U}</c>, blue.
///   Arcane subtype (CR 205.3k) stamped via
///   <see cref="Majik.Core.Cards.Types.CardSubtype.Arcane"/> — required for
///   Splice onto Arcane riders (<see cref="Majik.Core.Costs.SpliceOntoArcaneCost"/>)
///   to target this spell. Same shape posture as
///   <see cref="NourishingShoalFactory"/> (the green sibling of the cycle).
///
/// - <b>Alternative cost (Shoal pitch — CR 118.9):</b>
///   The blue sibling reuses <see cref="ExileGreenMVXAlternativeCost"/>
///   (which is colour-parameterised) with:
///     • <see cref="ManaColor.Blue"/> (required colour — oracle: "a blue card"),
///     • the declared X (required MV — oracle: "with mana value X"),
///     • the hand card the caster chose to exile.
///   <see cref="BuildAlternativeCost"/> is a convenience helper for this.
///   When the alt-cost is used, no mana is paid (<see cref="ManaCost.Zero"/>);
///   CR 107.3b — when an alternative cost replaces the printed mana cost and
///   does not specify a value for X, X is the value declared at cast time.
///
/// - <b>Counter target spell if its mana value is X</b>
///   (CR 202.3, CR 701.5, CR 608.2b):
///   <see cref="BuildSpellDefinition"/> wires <c>HasVariableX = true</c> (X is
///   chosen at cast time — whether paid in mana or set by the pitched card's
///   mana value) and a single 1..1 "target spell" <see cref="TargetRequest"/>.
///   On resolution the target's mana value is sampled (printed
///   <see cref="Card.ManaCostValue"/> + the target spell's own chosen
///   <see cref="Card.PendingCastX"/>, CR 202.3) and the spell is countered iff
///   that value equals the declared X. A target whose mana value differs from X
///   at resolution time is treated as an illegal target — the effect does
///   nothing for it (CR 608.2b) and the spell stays on the stack.
///   Uncounterable spells survive (CR 701.5b — honoured by
///   <see cref="OracleSpellBinder.RemoveFromStack"/>).
///
/// ## Design references
///
/// - Pitch alt-cost shape + Arcane subtype + manual X-cost card build:
///   <see cref="NourishingShoalFactory"/> (same cycle, green).
/// - Mana-value-conditional counter (printed mv + chosen X sampling at
///   resolution, RemoveFromStack → graveyard): <see cref="DisdainfulStrokeFactory"/>
///   (mv &gt;= 4) and <see cref="SpellSnareFactory"/> (mv == 2). Disrupting
///   Shoal differs only in that the comparison target is the declared X rather
///   than a printed constant.
/// </summary>
[CardName("Disrupting Shoal")]
public static class DisruptingShoalFactory
{
    public const string CardName = "Disrupting Shoal";
    public const string PrintedManaCost = "{X}{U}{U}";

    /// <summary>Required colour for the Shoal pitch (oracle: "a blue card").</summary>
    public const ManaColor PitchColor = ManaColor.Blue;

    /// <summary>
    /// Build a Disrupting Shoal Instant — Arcane owned and controlled by
    /// <paramref name="owner"/>. Card shape only; the counter body is built on
    /// demand via <see cref="BuildSpellDefinition"/>. The X-cost shape doesn't
    /// go through <c>CardDefRuntime</c>, so owner/controller are wired manually
    /// (same pattern as <see cref="NourishingShoalFactory.Create"/>).
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
    ///   1. Prompt the agent for X.
    ///   2. Prompt the agent to pick a blue card from hand with MV = X.
    ///   3. Call this helper and pass the result to <c>SpellCastFlow.CastAsync</c>
    ///      as <c>alternativeCost</c>.
    ///
    /// <see cref="ExileGreenMVXAlternativeCost.CanCastFor"/> will validate the
    /// card's colour (blue) and MV against X at the cast-time permission check.
    /// </summary>
    public static ExileGreenMVXAlternativeCost BuildAlternativeCost(int x, ICard exiledCard)
    {
        ArgumentNullException.ThrowIfNull(exiledCard);
        return new ExileGreenMVXAlternativeCost(PitchColor, x, exiledCard);
    }

    /// <summary>
    /// Build the "counter target spell if its mana value is X" SpellDefinition.
    /// <see cref="SpellDefinition.HasVariableX"/> = true so the engine prompts
    /// for X at cast time; resolution reads <c>ChosenSpellParams.X</c> as the
    /// mana-value threshold. CR 608.2b: if the chosen target's mana value is not
    /// equal to X at resolution time (illegal target), the effect does nothing
    /// for that target — the target spell remains on the stack.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[] { new TargetRequest("target spell with mana value X", 1, 1, Array.Empty<object>()) },
            EffectFactory: chosen =>
            {
                var x = chosen.X ?? 0;
                var raw = chosen.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect($"Disrupting Shoal — counter target spell if its mana value is {x}", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 202.3 — sample mana value at resolution time
                        // (printed mv + the target's own chosen X). Mirrors
                        // Disdainful Stroke / Spell Snare.
                        var castCard = spell.Card;
                        var printed = castCard is Card concrete
                            ? concrete.ManaCostValue.TotalValue
                            : Majik.Core.ValueObjects.ManaCost.Parse(castCard.ManaCost).TotalValue;
                        var targetX = (castCard as Card)?.PendingCastX ?? 0;
                        var manaValue = printed + targetX;

                        // CR 608.2b — illegal target at resolution (mv != X) →
                        // effect does nothing for that target.
                        if (manaValue != x) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
