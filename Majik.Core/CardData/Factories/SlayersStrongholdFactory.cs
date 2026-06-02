using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Slayers' Stronghold (Avacyn Restored, Land).
///
/// Oracle text (verified against the task brief / Scryfall):
///   "{T}: Add {C}.
///    {R}{W}, {T}: Target creature gets +2/+0 and gains vigilance and haste
///    until end of turn."
///
/// Structural near-twin of <see cref="BlinkmothNexusFactory"/>'s third
/// ability — a colourless-mana land carrying a single targeted activated
/// pump ability (mana cost + {T} + one "target creature" request, resolved
/// against the chosen creature's continuous-effects service). Where Blinkmoth
/// grants +1/+1, Slayers' Stronghold grants +2/+0 plus two keyword grants
/// (vigilance, haste).
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain nonbasic <see cref="Land"/> (no printed
///   supertype / subtype), materialised from the embedded JSON definition
///   (<c>slayers-stronghold.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>{T}: Add {C}</b> — vanilla colourless <see cref="ManaAbility"/>
///   (CR 605.1), declared in the JSON. {C} folds to one colourless mana via
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> (same bucketing as
///   <see cref="BlinkmothNexusFactory"/> / <see cref="ReliquaryTowerFactory"/>).
/// - <b>{R}{W}, {T}: Target creature gets +2/+0 and gains vigilance and haste
///   until end of turn</b> — an <see cref="ActivatedAbility"/> (CR 602) with
///   cost <see cref="ManaCostCost"/>("{R}{W}") + <see cref="AdditionalCost.Tap"/>
///   and a single 1..1 "target creature" request (same target-creature
///   activated-ability shape as <see cref="BlinkmothNexusFactory"/>'s pump).
///   On resolution it registers, against the chosen creature's own
///   <see cref="Creature.ActiveEffects"/> service:
///   <list type="bullet">
///     <item>a Layer-7c <see cref="PumpUntilEndOfTurnEffect"/>(+2/+0)
///       (CR 613.4d), and</item>
///     <item>a Layer-6 <see cref="GrantKeywordUntilEndOfTurnEffect"/> for each
///       of "Vigilance" and "Haste" (CR 613.1c).</item>
///   </list>
///   All three expire in the cleanup step (CR 514.2). The keyword strings
///   match <see cref="Majik.Core.Combat.CombatAbilities.HasVigilance"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> (the keyword
///   set is OrdinalIgnoreCase, so casing is irrelevant).
///
/// ## v1 posture (CR 608.2b guards)
/// - No chosen target, an off-battlefield target, a non-Creature target, or a
///   target without a live continuous-effects service → documented no-op
///   (nothing happens; resolution does not throw). Same defence-in-depth
///   posture as Blinkmoth Nexus's pump.
/// </summary>
[CardName("Slayers' Stronghold")]
public static class SlayersStrongholdFactory
{
    public const string CardName = "Slayers' Stronghold";
    public const string Slug = "slayers-stronghold";

    /// <summary>Pump amount — +2/+0 (CR 613.4d).</summary>
    public const int PumpPower = 2;
    public const int PumpToughness = 0;

    /// <summary>Activation mana cost of the pump ability — {R}{W}.</summary>
    public const string PumpCost = "{R}{W}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Slayers' Stronghold with no continuous-effects wiring (the
    /// <see cref="NamedCardFactory"/> dispatcher / shape path). Both abilities
    /// are attached so the card surface is complete; the pump resolution is a
    /// documented no-op without a target / effects service.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Land, {T}: Add {C}) from the embedded JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {R}{W}, {T}: Target creature gets +2/+0 and gains vigilance and
        // haste until end of turn.
        // CR 602 activated ability; CR 613.4d Layer-7c +2/+0 pump; CR 613.1c
        // Layer-6 keyword grants; CR 514.2 cleanup expiry. Same target-creature
        // activated-ability shape as BlinkmothNexusFactory's pump; reuses the
        // PumpUntilEndOfTurnEffect + GrantKeywordUntilEndOfTurnEffect
        // primitives (Berserk / Legion Leadership).
        // ----------------------------------------------------------------
        ActivatedAbility? pumpAbility = null;
        var pumpEffect = new Effect(
            $"{CardName}: target creature gets +{PumpPower}/+{PumpToughness} and gains vigilance and haste until end of turn",
            () =>
            {
                if (pumpAbility == null) return;
                if (pumpAbility.ChosenTargets.Count == 0) return;
                if (pumpAbility.ChosenTargets[0].Count == 0) return;
                if (pumpAbility.ChosenTargets[0][0] is not Creature creature) return;

                // CR 608.2b — illegal target on resolution (left the
                // battlefield) → no-op. Defence-in-depth zone check.
                if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return;

                // Without a continuous-effects service on the target (shape-only
                // target) the grants simply aren't tracked — documented no-op.
                if (creature.ActiveEffects == null) return;

                // CR 613.4d — Layer-7c +2/+0 until end of turn.
                creature.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));

                // CR 613.1c — Layer-6 keyword grants until end of turn.
                creature.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, "Vigilance"));
                creature.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, "Haste"));
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
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.CombatTrick),
            });

        land.AddAbility(pumpAbility);

        return land;
    }

    /// <summary>The {R}{W}, {T} targeted +2/+0-and-keywords pump ability.</summary>
    public static ActivatedAbility GetPumpAbility(Land land)
    {
        ArgumentNullException.ThrowIfNull(land);
        return land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);
    }
}
