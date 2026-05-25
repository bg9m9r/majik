using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
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
/// Named-card factory for Voltage Surge (Modern Horizons 3, {R}).
///
/// Instant. Oracle text:
///   "You may sacrifice an artifact as you cast this spell.
///    This spell deals 2 damage to any target. You get {E}{E}.
///    If you sacrificed an artifact as you cast this spell, it deals
///    4 damage instead."
///
/// ## Why a named factory
/// Voltage Surge is the canonical "scaling burn that pairs with the
/// Modern Boros Energy artifact-sac substrate" — the printed text is
/// a *conditional* additional cost (the caster may choose to sac an
/// artifact for the upgraded damage, or skip the sac and resolve the
/// base body). The cost is structurally identical to Bone Splinters'
/// printed sacrifice rider <em>except</em> it is optional rather than
/// mandatory (Bone Splinters refuses the cast when no creature is
/// available — CR 601.2g; Voltage Surge happily resolves at the base
/// 2-damage tier when no artifact is sacrificed). The energy gain rider
/// (<c>{E}{E}</c>) fires unconditionally on resolution, which keeps the
/// card live as a pure {R}-for-2-and-2-energy when no artifact is
/// available to upgrade — the printed shape that makes it Boros
/// Energy's bread-and-butter Modern role-player.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {R}.
/// - <b>Damage</b>: 1..1 "any target" <see cref="TargetRequest"/>
///   (Intent: <see cref="BotIntent.Removal"/>) — same shape as
///   <see cref="GalvanicDischargeFactory"/> / Lightning Bolt. On
///   resolution deals <see cref="BaseDamage"/> (2) by default, or
///   <see cref="KickedDamage"/> (4) when the caster paid the
///   sacrifice-artifact additional cost — the resolve body reads
///   <see cref="SacrificeAnArtifactAdditionalCost.Sacrificed"/> off the
///   cost instance threaded through the spell definition (parallel
///   to Burst Lightning's <see cref="Card.WasKicked"/> sentinel read).
/// - <b>Energy gain</b>: unconditional <see cref="Player.GainEnergy"/>(2)
///   on the controller (CR 106.13 — energy is the player-scoped
///   resource the {E} pip pays out of; same ledger Aether Hub /
///   Guide of Souls / Static Prison feed). Fires whether or not the
///   sacrifice was paid — printed wording is two separate sentences,
///   the energy gain is not gated on the sacrifice.
/// - <b>Optional additional cost (CR 601.2f)</b>: the
///   <see cref="SacrificeAnArtifactAdditionalCost"/> instance returned
///   by <see cref="BuildAdditionalCost"/> is layered onto the cast
///   via <see cref="SpellCastFlow.CastAsync"/>'s <c>additionalCosts</c>
///   parameter <em>only when the caster opts in</em> — same opt-in
///   wiring as <see cref="BurstLightningFactory.BuildAdditionalCost"/>
///   for Kicker (CR 702.33). When the cost is supplied, the cast flow
///   refuses the cast if the caster controls no artifact
///   (CR 601.2g — additional cost that can't be paid → cast is
///   illegal); skip the cost path entirely to cast for base damage.
/// - <b>Cost-instance plumbing</b>: <see cref="BuildSpellDefinition"/>
///   carries the same <see cref="SacrificeAnArtifactAdditionalCost"/>
///   reference inside its <c>AdditionalCosts</c> array and exposes
///   <see cref="ReadSacrificeOutcome"/> so the resolve closure can
///   inspect whether <see cref="SacrificeAnArtifactAdditionalCost.Sacrificed"/>
///   has been stamped at resolution time (CR 702.x-style "if you
///   sacrificed X as you cast this spell" rider). When the caller
///   layers a different cost instance via the cast flow, that
///   instance is the one stamped — the resolve body therefore reads
///   from a closure-captured reference so it picks up the live state
///   regardless of which copy of the cost the cast flow paid.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent "would you like to sacrifice an artifact?" Yes/No
///   surface</b>: there is no <c>ChooseYesNoAsync</c> on
///   <see cref="IPlayerAgent"/> yet — same gap as Esper Sentinel's
///   "would you like to pay {X}?" decision. Callers (tests, bot EV
///   layer) decide whether to opt in by layering the cost onto the
///   cast manually. The factory surface mirrors Burst Lightning's
///   kicker shape so the bot probe pattern transfers directly when
///   the Yes/No prompt lands.
/// - <b>Sacrifice target prompt</b>: the
///   <see cref="SacrificeAnArtifactAdditionalCost"/> picker chooses
///   the first artifact on the caster's battlefield deterministically.
///   Real agent-driven sacrifice prompting awaits the
///   ITarget / TargetResolver pipeline (same v1 posture as
///   <see cref="TrashForTreasureFactory"/> / Bone Splinters).
/// - <b>Bot probe</b>: no
///   <see cref="AlternativeCostProbeRegistry"/> entry for the
///   optional sacrifice — same posture as Burst Lightning prior to
///   the Kicker probe; future EV pass plugs in a "sacrifice an
///   artifact when target survives 2 but not 4 damage" heuristic.
/// </summary>
[CardName("Voltage Surge")]
public static class VoltageSurgeFactory
{
    public const string CardName = "Voltage Surge";
    public const string PrintedManaCost = "{R}";

    public const int BaseDamage = 2;
    public const int KickedDamage = 4;
    public const int EnergyGain = 2;

    /// <summary>CardDef DSL — card shape only. Damage / energy / sac-
    /// conditional body is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Voltage Surge
    /// is cast. Declares a single 1..1 "any target" <see cref="TargetRequest"/>
    /// (Intent: <see cref="BotIntent.Removal"/>) plus the optional
    /// <see cref="SacrificeAnArtifactAdditionalCost"/> rider in
    /// <c>AdditionalCosts</c>. On resolution: deals
    /// <see cref="KickedDamage"/> (4) if the cost was paid, else
    /// <see cref="BaseDamage"/> (2); unconditionally grants the
    /// controller <see cref="EnergyGain"/> (2) energy.
    /// </summary>
    /// <param name="controller">Spell controller — receives the energy
    /// gain on resolution.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="sacrificeCost">Optional sacrifice-an-artifact
    /// additional cost. When supplied, the cast flow will require it to
    /// be paid (CR 601.2g) AND the resolve body branches on the
    /// stamped <see cref="SacrificeAnArtifactAdditionalCost.Sacrificed"/>
    /// for the 4-damage upgrade. Null = the caster has opted to skip
    /// the sacrifice (base 2-damage cast).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver,
        SacrificeAnArtifactAdditionalCost? sacrificeCost = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

        // NOTE: the sacrifice cost is NOT stamped into SpellDefinition.
        // AdditionalCosts here — that field is for definition-time costs
        // (template-bound "As an additional cost to cast this spell, …"
        // mandatory riders). Voltage Surge's sacrifice is optional and
        // is supplied at cast time via SpellCastFlow.CastAsync's
        // additionalCosts parameter. The factory keeps the cost
        // reference only for resolve-time inspection via the closure.
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "any target", 1, 1, Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: deal damage (sac-conditional) + gain {{E}}{{E}}",
                        () =>
                        {
                            // CR 601.2f — branch on whether the optional
                            // sacrifice-an-artifact cost was actually paid.
                            // SacrificeAnArtifactAdditionalCost.Sacrificed
                            // is non-null only after a successful Pay (the
                            // cast flow's additional-cost loop), so the
                            // closure-captured reference is the canonical
                            // resolve-time read — same posture as
                            // BurstLightningFactory reading Card.WasKicked.
                            // When the caller passed null (caster opted
                            // out), the read trivially yields false → base
                            // damage path. When the caller passed the SAME
                            // cost reference to both this factory and the
                            // cast flow's additionalCosts parameter, the
                            // Sacrificed sentinel is observed live.
                            var sacd = sacrificeCost?.Sacrificed != null;
                            var amount = sacd ? KickedDamage : BaseDamage;
                            Fx.DealDamage(target, amount);

                            // CR 106.13 — energy gain is unconditional;
                            // the printed shape ledgers two energy on the
                            // controller whether or not the sacrifice was
                            // paid.
                            controller.GainEnergy(EnergyGain);
                        }),
                };
            });
    }

    /// <summary>
    /// Build the optional sacrifice-an-artifact <see cref="IAdditionalCost"/>
    /// for Voltage Surge. Convenience builder for callers (tests, bot
    /// EV layer) that have already decided to opt into the upgraded
    /// damage tier; pass the returned instance both into
    /// <see cref="BuildSpellDefinition"/> AND into
    /// <see cref="SpellCastFlow.CastAsync"/>'s <c>additionalCosts</c>
    /// parameter so the same cost reference is paid by the cast flow
    /// and read by the resolve body.
    ///
    /// <para>The CR 601.2g legality gate fires automatically — the
    /// cast flow refuses the cast when the caster controls no
    /// artifact. Skip the cost path entirely (pass <c>null</c> to
    /// <see cref="BuildSpellDefinition"/>) to cast for base 2 damage.</para>
    /// </summary>
    public static SacrificeAnArtifactAdditionalCost BuildAdditionalCost()
        => new();

    /// <summary>
    /// Inspect the post-cast outcome of a previously-built cost
    /// instance. Returns <c>true</c> when an artifact was successfully
    /// sacrificed during the cast (CR 601.2f); <c>false</c> when the
    /// cost was skipped or unpayable. Convenience helper for tests
    /// asserting the sacrifice-conditional branch fired.
    /// </summary>
    public static bool ReadSacrificeOutcome(
        SacrificeAnArtifactAdditionalCost? cost)
        => cost?.Sacrificed != null;
}
