using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kessig Wolf Run (Innistrad, colorless-mana utility
/// land). Land.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {C}.
///    {X}{R}{G}, {T}: Target creature gets +X/+0 and gains trample until end
///    of turn."
///
/// Posture: combines two well-trodden shapes already in the engine —
/// <list type="bullet">
/// <item>The colorless mana utility-land base (name, Land type, {T}: Add {C}
///   mana ability), materialised from the embedded JSON definition
///   (<c>kessig-wolf-run.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="GhituEncampmentFactory"/> / <see cref="LairOfTheHydraFactory"/>.</item>
/// <item>A targeted, X-cost <b>combat-pump</b> activated ability layered on
///   here (the JSON <c>AbilityDefinition</c> schema expresses neither targets
///   nor X yet). This reuses the exact target-creature activated-ability shape
///   from <see cref="BlinkmothNexusFactory"/> (single 1..1 target request +
///   resolution that reads <see cref="ActivatedAbility.ChosenTargets"/>) and
///   the +X/+0 pump + Trample grant primitives from
///   <see cref="BerserkFactory"/>.</item>
/// </list>
///
/// ## Implemented (v1)
/// - Plain nonbasic Land identity (no printed subtypes / supertypes) + the
///   <c>{T}: Add {C}</c> mana ability — both from the JSON definition
///   (CR 605.1 mana ability, no stack).
/// - <b>{X}{R}{G}, {T}: Target creature gets +X/+0 and gains trample until end
///   of turn</b> — an <see cref="ActivatedAbility"/> (CR 602, uses the stack)
///   with cost <see cref="ManaCostCost"/>(<c>{X}{R}{G}</c>) + a
///   <see cref="AdditionalCost.Tap"/> rider, and a single 1..1 "target
///   creature" <see cref="TargetRequest"/>. On resolution it registers, against
///   the chosen creature's own <see cref="Creature.ActiveEffects"/>:
///     - A Layer 7c <see cref="PumpUntilEndOfTurnEffect"/> of <c>+X/+0</c>
///       (power only — toughness is unchanged), where X is sampled from the
///       wired <paramref name="xValueProvider"/> at resolution. X = 0 is a
///       legal activation (no "X can't be 0" rider on this card), in which case
///       the +0/+0 pump is a no-op for P/T but the trample grant still applies.
///     - A Layer 6 <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
///       Trample (CR 702.19), granted unconditionally regardless of X.
///   Both carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> = true so the
///   cleanup-step expiry (CR 514.2) reverts the target.
/// - CR 608.2b — no chosen target, a non-Creature target, an off-battlefield
///   target, or a target with no <see cref="Creature.ActiveEffects"/> service →
///   documented no-op (no throw). Same defence-in-depth posture as
///   <see cref="BlinkmothNexusFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>X-payment provenance</b>: the engine has no live per-activation X
///   ledger, so X comes from a caller-wired <paramref name="xValueProvider"/>
///   (same posture as <see cref="LairOfTheHydraFactory"/> /
///   <see cref="LavaclawReachesFactory"/>). The single-arg dispatcher path
///   defaults X to 0 — a legal activation that still grants trample.
/// </summary>
[CardName("Kessig Wolf Run")]
public static class KessigWolfRunFactory
{
    public const string CardName = "Kessig Wolf Run";
    public const string Slug = "kessig-wolf-run";

    /// <summary>Pump mana cost — {X}{R}{G} (plus a {T} additional cost).</summary>
    public const string PumpCost = "{X}{R}{G}";

    /// <summary>Granted keyword — CR 702.19 Trample.</summary>
    public const string GrantedTrample = "Trample";

    /// <summary>
    /// Construct Kessig Wolf Run with no <see cref="ContinuousEffectsService"/>
    /// wired and an X provider defaulting to <c>() =&gt; 0</c>. The {T}: Add {C}
    /// mana ability (from JSON) + the targeted pump ability are attached so the
    /// card surface is complete; the pump resolution no-ops because the chosen
    /// target carries no <see cref="Creature.ActiveEffects"/> service to
    /// register against. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, xValueProvider: null);

    /// <summary>
    /// Construct Kessig Wolf Run.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service. Unused for the pump
    /// itself (it registers against the chosen target creature's own
    /// <see cref="Creature.ActiveEffects"/>), but accepted for parity with the
    /// utility-land family and forward-compatibility. May be null.</param>
    /// <param name="xValueProvider">Callback supplying X at resolution time.
    /// Mirrors <see cref="LairOfTheHydraFactory"/> — the engine has no live
    /// X-payment ledger yet. Null defaults to <c>() =&gt; 0</c>.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        Func<int>? xValueProvider)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {C} mana ability). The targeted pump ability is layered on
        // below — neither targets nor {X} are expressible in the current JSON
        // AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {X}{R}{G}, {T}: Target creature gets +X/+0 and gains trample until
        // end of turn.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {X}{R}{G} + {T}. Resolution registers a Layer 7c +X/+0 pump and a
        // Layer 6 Trample grant on the chosen creature, both flagged
        // ExpiresAtEndOfTurn (CR 514.2). X is sampled from the wired provider.
        // ----------------------------------------------------------------
        ActivatedAbility? pumpAbility = null;
        var pumpEffect = new Effect(
            $"{CardName}: target creature gets +X/+0 and gains trample until end of turn",
            () =>
            {
                if (pumpAbility == null) return;
                if (pumpAbility.ChosenTargets.Count == 0) return;
                if (pumpAbility.ChosenTargets[0].Count == 0) return;

                // CR 608.2b — non-Creature / illegal target → no-op.
                if (pumpAbility.ChosenTargets[0][0] is not Creature creature) return;

                // Defence-in-depth: target left the battlefield → no-op.
                if (creature.Zone != ZoneType.Battlefield) return;

                // Without a continuous-effects service on the target (shape-only
                // path) the pump + grant are documented no-ops.
                if (creature.ActiveEffects == null) return;

                // X sampled at resolution. X = 0 is legal (no "X can't be 0"
                // rider); the +0/+0 pump is then a P/T no-op while trample is
                // still granted below.
                var x = Math.Max(0, xValueProvider?.Invoke() ?? 0);

                // Layer 7c — +X/+0 (power only). CR 613.7c.
                if (x > 0)
                {
                    creature.ActiveEffects.Register(
                        new PumpUntilEndOfTurnEffect(creature, p: x, t: 0));
                }

                // Layer 6 — gains Trample (CR 702.19), granted regardless of X.
                creature.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, GrantedTrample));
            });

        pumpAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(PumpCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { pumpEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(pumpAbility);

        return land;
    }

    /// <summary>The {X}{R}{G}, {T} targeted +X/+0 + trample pump ability.</summary>
    public static ActivatedAbility GetPumpAbility(Land land)
    {
        ArgumentNullException.ThrowIfNull(land);
        return land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);
    }
}
