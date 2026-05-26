using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pyrokinesis (Alliances, {4}{R}).
///
/// Sorcery. Oracle text:
///   "You may exile a red card from your hand rather than pay this spell's
///    mana cost.
///    Pyrokinesis deals 4 damage divided as you choose among any number of
///    target creatures."
///
/// ## Implemented (v1)
/// - Sorcery card shape ({4}{R}, Red) — built via the fluent
///   <see cref="CardDef"/> DSL.
/// - Pitch alternative cost via
///   <see cref="Majik.Core.Costs.ExileColoredCardAlternativeCost"/>
///   (<c>RequiredColor = Red</c>) — the no-timing-gate / no-life-rider
///   pitch primitive (same one Soul Spike / Snapback use). Pyrokinesis's
///   printed pitch carries NO "if it's not your turn" restriction (unlike
///   the Force-of-Will cycle), so this is the correct primitive.
/// - Resolve effect (<see cref="BuildDefinition"/>): "deals 4 damage
///   divided as you choose among any number of target creatures" — delegates
///   to <see cref="Majik.Core.CardData.SpellTemplates.Templates.Damage.DamageSpellFactory"/>'s
///   shared <c>DamageDividedAmongAnyTargetsSpell</c> with <c>n=4</c>,
///   <c>maxTargets=4</c> (you can't divide 4 damage among more than 4
///   targets and still deal 1 to each — same cap the
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Damage.DamageDividedTemplate"/>
///   uses for the "any number of" family).
///
/// ## Deferred (v1 gaps)
/// - <b>Even-split distribution</b>: the shared
///   <c>DamageDividedAmongAnyTargetsSpell</c> distributes damage with an
///   even split (remainder front-loaded) rather than honouring an
///   agent-driven distribution prompt. Same lossy v1 every other
///   "deals N damage divided" card (Arc Lightning, Boulderfall, Fury's
///   ETB trigger) inherits. CR 601.2d / CR 119.4 require the caster to
///   announce the distribution during target selection; will fix once
///   the engine has an agent-driven distribute-damage prompt.
/// - <b>"Target creatures" narrowing</b>: the v1 target slot reuses the
///   shared "any target" predicate rather than the printed "target
///   creature" narrower. Same posture as the
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Damage.DamageDividedTemplate"/>
///   family fold; tightening waits for richer target-predicate plumbing.
/// - <b>Bot probe</b>: not surfaced through
///   <see cref="PitchAltCostProbe.DefaultLookup"/> for the same reason
///   Snapback / Soul Spike aren't — the Force-cycle probe is keyed on
///   the not-your-turn-gated <see cref="Majik.Core.Costs.PitchAlternativeCost"/>.
/// </summary>
[CardName("Pyrokinesis")]
public static class PyrokinesisFactory
{
    public const string CardName = "Pyrokinesis";
    public const string PrintedManaCost = "{4}{R}";

    /// <summary>CR 119.x — Pyrokinesis deals 4 damage divided as you choose.</summary>
    public const int Damage = 4;

    /// <summary>The "any number of target creatures" cap. You can't divide
    /// 4 damage among more than 4 targets and still deal 1 to each —
    /// same cap the shared divided-damage family uses for "any number".</summary>
    public const int MaxTargets = 4;

    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "deals 4 damage divided among any number of target
    /// creatures" SpellDefinition. Delegates to the shared
    /// divided-damage spell factory (same path Arc Lightning /
    /// Boulderfall bind through).
    /// </summary>
    /// <param name="targetResolver">Cast-time target slot resolver (the
    /// raw stack token → live <see cref="Permanent"/> reference). Same
    /// shape every <see cref="SpellDefinition"/> in this assembly uses.</param>
    /// <param name="caster">Optional. Threaded into the shared factory so
    /// the resulting <see cref="DamageDealtEvent"/>s carry a source player
    /// (Searing-Blaze / Punishing-Fire damage-source listeners).</param>
    /// <param name="eventBus">Optional. Same as <paramref name="caster"/> —
    /// shape-only callers can pass null.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Player? caster = null,
        IEventBus? eventBus = null) =>
        Majik.Core.CardData.SpellTemplates.Templates.Damage.DamageSpellFactory
            .DamageDividedAmongAnyTargetsSpell(
                n: Damage,
                maxTargets: MaxTargets,
                resolver: targetResolver,
                replacements: null,
                caster: caster,
                bus: eventBus);
}
