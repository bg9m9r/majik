using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
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
/// Named-card factory for Geistlight Snare (Duskmourn: House of Horror, {2}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "This spell costs {1} less to cast if you control a Spirit. It also costs
///    {1} less to cast if you control an enchantment.
///    Counter target spell unless its controller pays {3}."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {2}{U}; mana value 3</item>
///   <item>Type line: Instant; colors: U</item>
/// </list>
///
/// Same two-part shape as <see cref="MysticalDisputeFactory"/> (conditional
/// cost reduction + soft "counter unless pays {N}"), but the two reductions are
/// keyed off the CASTER'S board state rather than the chosen target, and the
/// tax is {3}.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{U} (blue). The card shape is loaded from the
///   embedded JSON definition (<c>geistlight-snare.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
///   <see cref="CardDefinitionFactory"/> — same posture as the other
///   data-backed factories.
/// - <b>Two independent cost reductions (CR 117.7)</b>: a single
///   <see cref="CostReductionAbility"/> using the
///   <see cref="CostReductionAbility.TotalReducer"/> whole-reduction shape.
///   At cost-calc time the closure scans the caster's battlefield
///   (<see cref="Player.Zones"/> → Battlefield) and adds:
///     - {1} if the caster controls at least one Spirit
///       (<see cref="Card.HasSubtype"/>(<see cref="CardSubtype.Spirit"/>)), and
///     - {1} if the caster controls at least one enchantment
///       (<see cref="Card.HasType"/>(<see cref="CardType.Enchantment"/>)).
///   The two conditions are independent (CR 117.7a — each reduction applies on
///   its own), so the total reduction is 0, {1}, or {2}. CR 117.7c — only the
///   generic mana is reduced and the floor-at-zero in
///   <see cref="CostReduction.GetEffectiveCost"/> keeps the {U} pip:
///     - neither   → {2}{U}
///     - one of two → {1}{U}
///     - both       → {U}
///   Mirrors <see cref="TolarianTerrorFactory"/>'s caster-board reducer shape
///   (which reads the caster's graveyard).
/// - <b>Counter unless pay {3}</b>: <see cref="BuildSpellDefinition"/> declares
///   one 1..1 "target spell" <see cref="TargetRequest"/>. At resolution the
///   target's controller is auto-prompted via <see cref="Player.PayMana"/> for
///   {3}; if they can pay, the counter no-ops (CR 118.4). Otherwise the spell
///   is removed from the stack via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and moved to graveyard
///   (CR 701.5). Identical body to <see cref="MysticalDisputeFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for "pay {3}?"</b> — same auto-pay posture as Mystical
///   Dispute / Mana Leak / Daze. The real "would you like to pay?" choice is
///   queued behind a future agent-prompt surface.
/// </summary>
[CardName("Geistlight Snare")]
public static class GeistlightSnareFactory
{
    public const string CardName = "Geistlight Snare";
    public const string Slug = "geistlight-snare";

    /// <summary>Generic reduction granted for each satisfied condition
    /// (control a Spirit; control an enchantment) — CR 117.7a.</summary>
    public const int PerConditionReduction = 1;

    /// <summary>Pay-or-counter rider (CR 118.4 — "unless its controller pays
    /// {3}").</summary>
    public const int UnlessPayGeneric = 3;

    /// <summary>
    /// Construct Geistlight Snare as an Instant card with owner / controller
    /// wired + the board-conditional cost-reduction ability attached. The
    /// resolve-time SpellDefinition (counter-unless-pay-{3}) is built on demand
    /// via <see cref="BuildSpellDefinition"/> — mirrors the shape of
    /// <see cref="MysticalDisputeFactory"/>. The base shape (name, Instant,
    /// {2}{U}, blue) is materialised from the embedded JSON definition.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(definition, owner);

        // CR 117.7 / 117.7a — "This spell costs {1} less to cast if you control
        // a Spirit. It also costs {1} less to cast if you control an
        // enchantment." Two independent conditional reductions; the
        // whole-reducer closure inspects the caster's battlefield and sums the
        // two {1}s that apply. CR 117.7c — generic only; the {U} pip is floored
        // by CostReduction.GetEffectiveCost so {2}{U} → {U} at most.
        card.AddAbility(new CostReductionAbility(
            totalReducer: ComputeReduction,
            description:
                "This spell costs {1} less to cast if you control a Spirit. " +
                "It also costs {1} less to cast if you control an enchantment."));

        return card;
    }

    /// <summary>
    /// Caster-board reduction (CR 117.7a): {1} for controlling a Spirit plus
    /// {1} for controlling an enchantment, independently — total 0..2.
    /// Tolerates a null roster / battlefield (shape-only + pre-board
    /// affordability calls).
    /// </summary>
    private static int ComputeReduction(Player? caster)
    {
        var battlefield = caster?.Zones?.Battlefield;
        if (battlefield == null) return 0;

        var controlsSpirit = false;
        var controlsEnchantment = false;
        foreach (var permanent in battlefield.GetCards())
        {
            if (!controlsSpirit && permanent.HasSubtype(CardSubtype.Spirit))
            {
                controlsSpirit = true;
            }
            if (!controlsEnchantment && permanent.HasType(CardType.Enchantment))
            {
                controlsEnchantment = true;
            }
            if (controlsSpirit && controlsEnchantment) break;
        }

        var reduction = 0;
        if (controlsSpirit) reduction += PerConditionReduction;
        if (controlsEnchantment) reduction += PerConditionReduction;
        return reduction;
    }

    /// <summary>
    /// Build the "counter target spell unless its controller pays {3}"
    /// SpellDefinition. Mirrors
    /// <see cref="MysticalDisputeFactory.BuildSpellDefinition"/> with N=3.
    /// CR 608.2b — illegal target at resolution is handled by the pre-resolve
    /// target-legality check; this body assumes the resolved target is still a
    /// live <see cref="ISpell"/>.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen by the
    /// caster to a live engine object (pass-through in tests; production callers
    /// route via a TargetResolver service).</param>
    /// <param name="stack">Active stack; required to remove the countered spell.
    /// Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var unlessCost = ManaCost.Zero.AddGenericCost(UnlessPayGeneric);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "Geistlight Snare — counter target spell unless its controller pays {3}",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;

                            // CR 118.4 — target's controller may pay {3} to
                            // prevent the counter. v1 auto-pays when able (same
                            // posture as Mystical Dispute / Mana Leak / Daze).
                            if (spell.Controller is not null
                                && spell.Controller.PayMana(unlessCost))
                            {
                                return;
                            }

                            // CR 701.5 — counter the spell: remove from stack,
                            // move card to graveyard.
                            OracleSpellBinder.RemoveFromStack(stack, spell);
                            spell.Card.SetZone(ZoneType.Graveyard);
                        }),
                };
            });
    }
}
