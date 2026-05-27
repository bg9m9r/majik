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
/// Named-card factory for Mishra's Factory (Antiquities / reprints).
///
/// Land.
/// Oracle text:
///   "{T}: Add {C}.
///    {1}: Mishra's Factory becomes a 2/2 Assembly-Worker artifact
///    creature until end of turn. It's still a land.
///    {T}: Target Assembly-Worker creature gets +1/+1 until end of turn."
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{1}: become a 2/2 Assembly-Worker artifact creature EOT; still a
///   land</b> — wired as an <see cref="ActivatedAbility"/> with a
///   <see cref="ManaCostCost"/> of {1}. Resolution registers a
///   <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — adds Creature +
///   Artifact types, <see cref="CardSubtype.AssemblyWorker"/> subtype,
///   no keyword grants) and a <see cref="ManlandCycleBecomesPTEffect"/>
///   (Layer 7b — base 2/2). Both flagged
///   <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so cleanup
///   (CR 514.2) lifts the animation.
/// - <b>{T}: Target Assembly-Worker creature gets +1/+1 until end of
///   turn</b> — wired as an <see cref="ActivatedAbility"/> with
///   <see cref="AdditionalCost.Tap"/> and a 1..1 target-creature
///   <see cref="TargetRequest"/>. The resolution effect validates the
///   chosen creature is on the battlefield (CR 608.2b) and carries the
///   Assembly-Worker subtype (the printed type predicate) and registers
///   a <see cref="PumpUntilEndOfTurnEffect"/> against its
///   <see cref="Creature.ActiveEffects"/>. The +1/+1 buff is a Layer 7c
///   pump (CR 613.7c) with EOT expiry.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent target-prompt filtering</b>: <see cref="ActivatedAbility"/>
///   honours pre-set <see cref="ActivatedAbility.ChosenTargets"/>; the
///   factory does not wire an <see cref="IPlayerAgent"/> prompt. Tests
///   call <see cref="ActivatedAbility.SetChosenTargets"/> directly
///   (same posture as Guide of Souls / Earthshaker Khenra).
/// - <b>Self-target</b>: Mishra's Factory's pump ability can legally
///   target itself when it is animated (it has the Assembly-Worker
///   subtype while animated). The resolution-time predicate gates on
///   the current subtype set, so a Factory pump-targeting-self path
///   works as long as both factories on the controller's battlefield
///   activate their animate ability and the pump-target is resolved
///   after the animate.
/// - <b>Land-becomes-creature P/T pipeline</b>: see
///   <see cref="MutavaultFactory"/> notes — Compute(Permanent) on a Land
///   instance doesn't yet surface the 2/2 base.
/// </summary>
[CardName("Mishra's Factory")]
public static class MishrasFactoryFactory
{
    public const string CardName = "Mishra's Factory";
    public const int AnimatedPower = 2;
    public const int AnimatedToughness = 2;
    public const int PumpPower = 1;
    public const int PumpToughness = 1;

    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Mishra's Factory.
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
        // {1}: become a 2/2 Assembly-Worker artifact creature until EOT.
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
            costs: new ICost[] { new ManaCostCost("{1}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // {T}: Target Assembly-Worker creature gets +1/+1 until end of
        // turn.
        // CR 602 — ordinary activated ability, tap as the only cost. The
        // resolution effect gates on:
        //   - Chosen target is still on the battlefield (CR 608.2b)
        //   - Chosen target's effective subtype set still contains
        //     AssemblyWorker (CR 608.2b — illegal target → effect does
        //     nothing for that target)
        // Registers a PumpUntilEndOfTurnEffect (Layer 7c) on the
        // target's ActiveEffects.
        // ----------------------------------------------------------------
        ActivatedAbility? pumpAbility = null;
        var pumpEffect = new Effect(
            $"{CardName}: target Assembly-Worker creature gets +1/+1 until EOT",
            () =>
            {
                if (pumpAbility == null) return;
                var chosen = pumpAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b

                // Printed-subtype predicate (CR 205.3m — Assembly-Worker
                // is a creature subtype). Mishra's Factory itself has
                // the subtype only while animated; the resolution-time
                // gate honours the current subtype set so a self-target
                // is legal iff the Factory is currently animated.
                if (!target.Subtypes.Contains(CardSubtype.AssemblyWorker)) return;

                if (target.ActiveEffects == null) return; // shape-only no-op

                target.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));
            });

        pumpAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { pumpEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target Assembly-Worker creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        land.AddAbility(pumpAbility);

        return land;
    }
}
