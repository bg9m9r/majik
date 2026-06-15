using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ezuri, Renegade Leader (Scars of Mirrodin block /
/// reprints — Legendary Creature — Elf Warrior {1}{G}{G} 2/2).
///
/// Oracle text (verified against Scryfall):
///   "{G}: Regenerate another target Elf.
///    {2}{G}{G}{G}: Elf creatures you control get +3/+3 and gain trample until
///    end of turn."
///
/// The Elf-tribal anthem-on-a-stick: a repeatable cheap regeneration shield to
/// protect the swarm, plus a {2}{G}{G}{G} overrun that turns a board of mana
/// dorks lethal. The base shape (name, Legendary Creature — Elf Warrior,
/// {1}{G}{G}, 2/2) is materialised from the embedded JSON definition
/// (<c>ezuri-renegade-leader.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two activated abilities are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema expresses
/// neither a target-OTHER regenerate (the binder only covers regenerate-self)
/// nor a tribal overrun (same posture as <see cref="ElvishWarmasterFactory"/> /
/// <see cref="CraterhoofBehemothFactory"/>).
///
/// ## Implemented (v1)
///
/// ### "{G}: Regenerate another target Elf." (CR 602 / 701.18 / 701.15a)
/// An <see cref="ActivatedAbility"/> costing {G} with a single 1..1
/// "another target Elf" <see cref="TargetRequest"/>. On resolution it reads the
/// chosen target, applies CR 608.2b resolve-time legality rechecks — still on the
/// battlefield, still an Elf creature, and NOT Ezuri itself ("another") — then
/// calls <see cref="Permanent.AddRegenerationShield"/> on the target (one
/// regeneration shield; shields stack and clear at EOT, consumed the next time
/// the permanent would be destroyed this turn — CR 701.15c). Same target-regen
/// shape as <see cref="WeldingJarFactory"/>, but the target must be an Elf
/// OTHER than the source. Choose-time filtering is deferred to the agent-prompt
/// pipeline (empty <see cref="TargetRequest.LegalCandidates"/>, same posture as
/// Welding Jar); resolve-time recheck is the live gate.
///
/// ### "{2}{G}{G}{G}: Elf creatures you control get +3/+3 and gain trample until end of turn." (CR 602 / 613)
/// An <see cref="ActivatedAbility"/> costing {2}{G}{G}{G}. On resolution it
/// snapshots the Elves the controller controls and, for each, registers a
/// <see cref="PumpUntilEndOfTurnEffect"/>(+3/+3, Layer 7c per CR 613.1c) and a
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Trample", Layer 6 per CR
/// 613.1c / 702.19). Both expire at cleanup (CR 514.2). Same temporary-team-pump
/// shape as <see cref="ElvishWarmasterFactory.ApplyOverrun"/> /
/// <see cref="CraterhoofBehemothFactory.ApplyTrampleAndPump"/>, scoped to Elves
/// and granting Trample. Elves without a wired
/// <see cref="ContinuousEffectsService"/> no-op cleanly (shape-only guard).
///
/// ## Deferred (v1 gaps)
/// - <b>Choose-time target filtering</b>: <see cref="TargetRequest.LegalCandidates"/>
///   is empty (same posture as Welding Jar / Pyrite Spellbomb) — the agent picks
///   any object; resolve-time legality is the live gate.
/// </summary>
[CardName("Ezuri, Renegade Leader")]
public static class EzuriRenegadeLeaderFactory
{
    public const string CardName = "Ezuri, Renegade Leader";
    public const string Slug = "ezuri-renegade-leader";

    public const int PumpPower = 3;
    public const int PumpToughness = 3;

    /// <summary>Granted evergreen keyword — CR 702.19 Trample.</summary>
    public const string GrantedTrample = "Trample";

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Legendary Creature —
        // Elf Warrior, {1}{G}{G}, 2/2). The JSON carries no abilities — both
        // activated abilities are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        BuildRegenerateAbility(card, owner);
        BuildOverrunAbility(card, owner);

        return card;
    }

    // --- {G}: Regenerate another target Elf. (CR 602 / 701.18 / 701.15a) ----

    private static void BuildRegenerateAbility(Creature card, Player owner)
    {
        ActivatedAbility? regenerateAbility = null;

        var regenerateEffect = new Effect(
            $"{CardName}: regenerate another target Elf",
            () =>
            {
                if (regenerateAbility == null) return;
                if (regenerateAbility.ChosenTargets.Count == 0) return;
                if (regenerateAbility.ChosenTargets[0].Count == 0) return;

                var raw = regenerateAbility.ChosenTargets[0][0];
                if (raw is not Creature target) return;

                // CR 608.2b — resolve-time legality recheck:
                //   * still on the battlefield,
                //   * still an Elf,
                //   * NOT Ezuri itself ("another").
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasSubtype(CardSubtype.Elf)) return;
                if (ReferenceEquals(target, card)) return; // "another"

                // CR 701.18 / 701.15a — "Regenerate [permanent]" creates one
                // regeneration shield on the target. Shields stack, clear at EOT.
                target.AddRegenerationShield();
            });

        regenerateAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{G}") },
            effects: new IEffect[] { regenerateEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "another target Elf",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(regenerateAbility);
    }

    // --- {2}{G}{G}{G} tribal overrun (CR 602 / 613) ------------------------

    private static void BuildOverrunAbility(Creature card, Player owner)
    {
        // CR 602 — "{2}{G}{G}{G}: Elf creatures you control get +3/+3 and gain
        // trample until end of turn." On resolution each Elf the controller
        // controls gets a +3/+3 pump (Layer 7c) + a Trample grant (Layer 6),
        // both until end of turn (CR 514.2).
        var overrunEffect = new Effect(
            $"{CardName}: Elves you control get +{PumpPower}/+{PumpToughness} and gain trample until end of turn",
            () => ApplyOverrun(card.Controller ?? owner));

        var overrunAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}{G}{G}{G}") },
            effects: new IEffect[] { overrunEffect });

        card.AddAbility(overrunAbility);
    }

    /// <summary>
    /// Apply the {2}{G}{G}{G} overrun rider to every Elf
    /// <paramref name="controller"/> controls at the moment this effect runs.
    /// Each Elf: +3/+3 pump (CR 613.1c Layer 7c) + Trample grant (CR 613.1c
    /// Layer 6 / CR 702.19), both until end of turn (CR 514.2). Elves without a
    /// wired <see cref="ContinuousEffectsService"/> no-op cleanly.
    /// </summary>
    public static void ApplyOverrun(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // Snapshot to a list so same-step side effects don't disturb the
        // enumeration (mirrors ElvishWarmasterFactory.ApplyOverrun).
        var elves = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.HasSubtype(CardSubtype.Elf))
            .ToList();

        foreach (var elf in elves)
        {
            // Shape-only safety — without a live ContinuousEffectsService the
            // grant/pump silently no-ops rather than NRE'ing.
            if (elf.ActiveEffects == null) continue;

            // CR 613.1c Layer 7c — +3/+3 pump (until end of turn).
            elf.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(elf, PumpPower, PumpToughness));

            // CR 613.1c Layer 6 — Trample grant (CR 702.19, until end of turn).
            elf.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(elf, GrantedTrample));
        }
    }
}
