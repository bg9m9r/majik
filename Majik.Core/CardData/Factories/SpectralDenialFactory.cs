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
/// Named-card factory for Spectral Denial (Marvel's Spider-Man, {X}{U}).
///
/// Instant. Oracle text (verified via Scryfall 2026-06):
///   "This spell costs {1} less to cast for each creature you control with
///    power 4 or greater.
///    Counter target spell unless its controller pays {X}."
///
/// ## Why a named factory (no template covers it)
/// This is the soft "counter target spell unless its controller pays {X}"
/// body of <see cref="LogicKnotFactory"/> (CR 118.4) combined with a printed
/// CR 117.7 cost reduction ("{1} less per creature you control with power 4 or
/// greater"). No spell template binds the variable-X counter rider together
/// with a conditional per-creature cost reducer, so it gets a named factory.
///
/// ## Implemented (v1)
/// - Instant shape, printed mana cost {X}{U}, blue. Card shape comes from the
///   embedded JSON (<c>spectral-denial.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Cost reduction (CR 117.7)</b>: "{1} less to cast for each creature you
///   control with power 4 or greater". Wired via the per-instance +
///   predicate <see cref="CostReductionAbility"/> shape (same seam as
///   <see cref="ThoughtcastFactory"/>'s Affinity, with a creature-power
///   predicate instead of a type predicate). The reducer scans the caster's
///   battlefield at cost-calc time (<see cref="CostReduction.GetEffectiveCost"/>)
///   and removes {1} of generic mana per qualifying creature. The {X} and {U}
///   pips are coloured/variable and untouched by the reduction (CR 117.7c);
///   the printed generic of {X}{U} is zero, so in practice this reducer floors
///   at zero immediately — it matters when the chosen X contributes generic to
///   the payment, which the cast flow computes after X is declared.
/// - <b>Variable X</b>: <see cref="SpellDefinition.HasVariableX"/> is true so
///   <see cref="SpellCastFlow"/> prompts for an X choice and stamps it onto the
///   card via <see cref="Card.SetPendingCastX"/>; the chosen X also flows into
///   <see cref="ChosenSpellParams.X"/>.
/// - <b>Counter unless pay {X}</b>: identical body to
///   <see cref="LogicKnotFactory.BuildDefinition"/>. At resolution the target
///   spell's controller is auto-prompted for {X}; if they can pay, the counter
///   no-ops (CR 118.4). Otherwise the spell is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and moved to graveyard
///   (CR 701.5). Uncounterable spells survive (CR 701.5b). X == 0 means the
///   rider is "pay {0}", which any controller satisfies trivially, so the
///   spell is not countered — that matches the printed text.
///
/// ## Deferred (v1 gaps)
/// - Real "do you want to pay {X}?" agent prompt — v1 auto-pays when able,
///   same posture as Logic Knot / Mana Leak / Mystical Dispute.
/// </summary>
[CardName("Spectral Denial")]
public static class SpectralDenialFactory
{
    public const string CardName = "Spectral Denial";
    public const string Slug = "spectral-denial";
    public const string PrintedManaCost = "{X}{U}";

    /// <summary>Minimum power a creature you control must have to count toward
    /// the cost reduction (CR 117.7 — "power 4 or greater").</summary>
    public const int CostReductionPowerThreshold = 4;

    /// <summary>
    /// Construct Spectral Denial. The card shape (Instant {X}{U}, blue) is
    /// materialized from the embedded JSON definition; the CR 117.7 cost
    /// reducer ("{1} less per creature you control with power 4 or greater")
    /// is attached on top (same posture as <see cref="ThoughtcastFactory"/>).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 117.7 — "This spell costs {1} less to cast for each creature you
        // control with power 4 or greater." Per-instance reducer (1 generic
        // per qualifying creature); the predicate matches a battlefield card
        // that is a Creature whose live power is >= 4 (same power read as
        // BondersEnclaveFactory.ControlsCreatureWithPower4OrGreater).
        card.AddAbility(new CostReductionAbility(
            perInstance: 1,
            predicate: c => c is Creature cr && cr.Power >= CostReductionPowerThreshold,
            description: $"This spell costs {{1}} less to cast for each creature you control with power {CostReductionPowerThreshold} or greater."));

        return card;
    }

    /// <summary>
    /// Build the "counter target spell unless its controller pays {X}"
    /// SpellDefinition. X is read from <see cref="ChosenSpellParams.X"/>
    /// (preferred) and falls back to the card's <see cref="Card.PendingCastX"/>
    /// for paths that hand-build the params without the X field set. Mirrors
    /// <see cref="LogicKnotFactory.BuildDefinition"/>.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen by
    /// the caster to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered
    /// spell. Null in pure-shape tests — the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest("target spell", 1, 1, Array.Empty<object>(), BotIntent.Counter),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                // CR 107.3 — X in costs equals the value the caster chose.
                // Read from ChosenSpellParams.X; fall back to the card's
                // PendingCastX stamp for hand-built params.
                var xChoice = p.X ?? 0;
                return new IEffect[]
                {
                    new Effect("Spectral Denial — counter target spell unless its controller pays {X}", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        var x = xChoice;
                        if (x == 0 && spell.Card is Card concrete && concrete.PendingCastX.HasValue)
                        {
                            x = concrete.PendingCastX.Value;
                        }

                        var unlessCost = ManaCost.Zero.AddGenericCost(x);

                        // CR 118.4 — the target's controller may pay {X};
                        // v1 auto-pays when able (parallels Logic Knot).
                        // X == 0 means "pay {0}", which any controller can
                        // satisfy trivially — PayMana succeeds with an empty
                        // cost, so the spell is not countered.
                        if (spell.Controller is not null
                            && spell.Controller.PayMana(unlessCost))
                        {
                            return;
                        }

                        // CR 701.5 / 701.5b — counter the spell: remove from
                        // stack, move card to graveyard. Uncounterable spells
                        // survive (RemoveFromStack returns false).
                        if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
