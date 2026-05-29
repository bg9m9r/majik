using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mishra's Foundry (The Brothers' War / reprints).
///
/// Land.
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}: This land becomes a 2/2 Assembly-Worker artifact creature
///    until end of turn. It's still a land.
///    {1}, {T}: Target attacking Assembly-Worker gets +2/+2 until end of
///    turn."
///
/// Near-identical in shape to <see cref="MishrasFactoryFactory"/> (the
/// suggested analogue): colorless mana ability, a mana-only animate to a
/// 2/2 Assembly-Worker artifact creature, and a tap-target pump for
/// Assembly-Workers. The only material differences:
///   - animate cost is {2} (vs Factory's {1}),
///   - pump cost is {1},{T} (vs Factory's {T}),
///   - pump magnitude is +2/+2 (vs Factory's +1/+1),
///   - pump targets an <i>attacking</i> Assembly-Worker (vs any).
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{2}: become a 2/2 Assembly-Worker artifact creature EOT; still a
///   land</b> — an <see cref="ActivatedAbility"/> with a
///   <see cref="ManaCostCost"/> of {2}. Resolution registers a
///   <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — adds Creature +
///   Artifact types and the <see cref="CardSubtype.AssemblyWorker"/>
///   subtype, no keyword grants) and a
///   <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — base 2/2).
///   Both flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
///   cleanup (CR 514.2) lifts the animation.
/// - <b>{1}, {T}: Target attacking Assembly-Worker gets +2/+2 until end
///   of turn</b> — an <see cref="ActivatedAbility"/> with a
///   <see cref="ManaCostCost"/> of {1} plus <see cref="AdditionalCost.Tap"/>
///   and a 1..1 target <see cref="TargetRequest"/>. The resolution effect
///   validates the chosen creature is on the battlefield (CR 608.2b) and
///   carries the Assembly-Worker subtype, then registers a
///   <see cref="PumpUntilEndOfTurnEffect"/> (Layer 7c, +2/+2, EOT expiry)
///   on its <see cref="Creature.ActiveEffects"/>.
///
/// ## Deferred (v1 gaps — mirrors <see cref="MishrasFactoryFactory"/>)
/// - <b>"attacking" target predicate</b>: the pump is restricted to an
///   <i>attacking</i> Assembly-Worker. Combat membership lives on the
///   <see cref="Majik.Core.Combat.Combat"/> object (the list of
///   <see cref="Majik.Core.Combat.Attacker"/>s), not on the
///   <see cref="Creature"/>, and the factory has no combat handle at
///   construction. The resolution-time gate therefore checks subtype +
///   battlefield only — the same posture as the analogue, whose pump
///   gates on subtype alone and defers target-prompt filtering to the
///   agent-prompt system.
/// - <b>Agent target-prompt filtering</b>: <see cref="ActivatedAbility"/>
///   honours pre-set <see cref="ActivatedAbility.ChosenTargets"/>; the
///   factory does not wire an <see cref="IPlayerAgent"/> prompt. Tests
///   call <see cref="ActivatedAbility.SetChosenTargets"/> directly.
/// - <b>Land-becomes-creature P/T pipeline</b>: see
///   <see cref="MishrasFactoryFactory"/> notes — Compute(Permanent) on a
///   Land instance doesn't yet surface the 2/2 base.
/// </summary>
[CardName("Mishra's Foundry")]
public static class MishrasFoundryFactory
{
    public const string CardName = "Mishra's Foundry";
    public const int AnimatedPower = 2;
    public const int AnimatedToughness = 2;
    public const int PumpPower = 2;
    public const int PumpToughness = 2;

    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Mishra's Foundry.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">ContinuousEffectsService for animate
    /// registration. May be null — the animate ability still resolves
    /// and pays its mana, but no continuous effect is recorded.</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("{C}")));

        // ----------------------------------------------------------------
        // {2}: become a 2/2 Assembly-Worker artifact creature until EOT.
        // It's still a land.
        // Layer 4 adds Creature + Artifact types and the AssemblyWorker
        // subtype (CR 613.1c); Layer 7b records base 2/2 (CR 613.7b).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 2/2 Assembly-Worker artifact creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // shape-only path

                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.AssemblyWorker },
                    extraTypes: new[] { CardType.Artifact }));

                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // {1}, {T}: Target attacking Assembly-Worker gets +2/+2 until end
        // of turn.
        // CR 602 — ordinary activated ability with a {1} mana cost plus a
        // tap cost. The resolution effect gates on:
        //   - Chosen target is still on the battlefield (CR 608.2b)
        //   - Chosen target's effective subtype set still contains
        //     AssemblyWorker (CR 608.2b — illegal target → effect does
        //     nothing for that target)
        // The "attacking" restriction is deferred (see class xmldoc — no
        // combat handle here; matches the analogue's subtype-only gate).
        // Registers a PumpUntilEndOfTurnEffect (Layer 7c) on the target's
        // ActiveEffects.
        // ----------------------------------------------------------------
        ActivatedAbility? pumpAbility = null;
        var pumpEffect = new Effect(
            $"{CardName}: target attacking Assembly-Worker gets +2/+2 until EOT",
            () =>
            {
                if (pumpAbility == null) return;
                var chosen = pumpAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b

                // Printed-subtype predicate (CR 205.3m — Assembly-Worker
                // is a creature subtype). Mishra's Foundry itself has the
                // subtype only while animated; the resolution-time gate
                // honours the current subtype set.
                if (!target.Subtypes.Contains(CardSubtype.AssemblyWorker)) return;

                if (target.ActiveEffects == null) return; // shape-only no-op

                target.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));
            });

        pumpAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}"), AdditionalCost.Tap(land) },
            effects: new IEffect[] { pumpEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target attacking Assembly-Worker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        land.AddAbility(pumpAbility);

        return land;
    }
}
