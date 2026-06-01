using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hive of the Eye Tyrant (Adventures in the
/// Forgotten Realms manland cycle). Land.
///
/// Oracle text (verified Scryfall 2026-05-24):
///   "If you control two or more other lands, this land enters tapped.
///    {T}: Add {B}.
///    {3}{B}: Until end of turn, this land becomes a 3/3 black Beholder
///    creature with menace and \"Whenever this creature attacks, exile
///    target card from defending player's graveyard.\" It's still a land."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype).
/// - <b>Conditional ETB-tapped (CR 614.1c)</b> — registered as a
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate: enters untapped iff the
///   controller controls one or fewer OTHER lands (i.e. enters tapped
///   when ≥ 2 other lands are present). Mirrors the
///   <see cref="ConditionalEntersTappedBinder"/> "N or more other lands"
///   predicate at <c>threshold = 2, direction = more</c>.
/// - <b>{T}: Add {B}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1,
///   no stack).
/// - <b>{3}{B}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{3}{B}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="HiveOfTheEyeTyrantAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> to the permanent's effective
///       types and grants <see cref="CardSubtype.Beholder"/> as a
///       subtype. The printed Land type is left intact ("It's still a
///       land", CR 613.1c). Also grants the Menace
///       <see cref="StaticAbility"/> as a keyword marker (CR 702.110 —
///       same posture as Inkmoth Nexus' Infect / Flying marker), and
///       structurally records the per-instance attack-trigger
///       documentation (see Deferred for the live wiring gap).
///     - Layer 7b (<see cref="HiveOfTheEyeTyrantBecomesPTEffect"/>) —
///       set-base P/T 3/3 (CR 613.7b). Mirrors the
///       <see cref="MutavaultBecomesPTEffect"/> /
///       <see cref="CreepingTarPitBecomesPTEffect"/> pattern: Hive is a
///       <see cref="Land"/> runtime instance, so the P/T is recorded for
///       inspection but does not surface through
///       <see cref="ContinuousEffectsService.Compute(Permanent)"/> yet.
///   Both effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   = true so <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>
///   (CR 514.2 cleanup step) lifts the animation.
///
/// ## Deferred (v1 gaps)
/// - <b>Black colour identity of the animated form</b> — same gap as
///   Creeping Tar Pit: the engine's colour layer (Layer 5) has no
///   colour-setting effect primitive yet. The Beholder body should be
///   black while animated; v1 records the intent but doesn't apply.
/// - <b>Per-instance "Whenever this attacks, exile target card from
///   defending player's graveyard" trigger</b> — the animated body
///   carries an attack-trigger ability that only exists while animated.
///   v1 ships the trigger STRUCTURE attached unconditionally to the
///   card via <see cref="Triggers.OnAttackSelf"/> (so the shape is
///   inspectable) but documents that:
///     * The printed trigger should only fire while Hive is a creature
///       (CR 603.6 — a "becomes / has" body inherits its ability set
///       from the layer effect; if Hive is not animated it isn't a
///       creature and "attacks" is unreachable, so the structural
///       always-on attachment is observationally equivalent).
///     * The 1..1 "target card from defending player's graveyard"
///       <see cref="TargetRequest"/> resolves to an inline
///       <see cref="ChosenTargets"/> read (mirrors Tormod's Crypt's
///       target-player-graveyard pattern); when no target was chosen
///       the trigger no-ops at resolution (CR 608.2b).
///     * The effect body moves the chosen graveyard card to the
///       defending player's exile zone via raw zone moves (same shape
///       as Tormod's Crypt's exile loop, scoped to a single card).
/// - <b>Combat math through Compute</b>: same gap as Mutavault /
///   Creeping Tar Pit / Inkmoth — until
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades
///   to a <see cref="CreatureCharacteristics"/> row when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 3/3 doesn't surface for combat
///   resolution.
/// - <b>Activation gate / sorcery-speed</b>: none — the animate ability
///   is instant-speed per oracle, no restriction needed.
/// </summary>
[CardName("Hive of the Eye Tyrant")]
public static class HiveOfTheEyeTyrantFactory
{
    public const string CardName = "Hive of the Eye Tyrant";

    /// <summary>
    /// Construct Hive of the Eye Tyrant with no
    /// <see cref="ContinuousEffectsService"/> or
    /// <see cref="ReplacementBus"/> wired. The mana ability + the
    /// animate ability + the structural attack-trigger shape are all
    /// attached so the card surface is complete; the layer effects are
    /// not registered, and the conditional ETB-tapped replacement is
    /// omitted (single-arg shape-only path).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Hive of the Eye Tyrant.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the conditional
    /// "enters tapped unless you control ≤ 1 other land" rider
    /// (CR 614.1c). May be null — land enters untapped unconditionally
    /// in that posture (mirrors how every other conditional-tapped
    /// factory defers this to the production binder).</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Conditional ETB-tapped (CR 614.1c) — "If you control two or
        // more other lands, this land enters tapped."
        // Predicate: enters untapped iff controller controls ≤ 1 OTHER
        // land. Same shape as the ConditionalEntersTappedBinder's
        // "N or more other lands" → tapped form at threshold = 2.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    CountOtherLands(controller, self) <= 1));
        }

        // ----------------------------------------------------------------
        // {T}: Add {B}
        // CR 605.1 — mana ability, no stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        // ----------------------------------------------------------------
        // {3}{B}: Until end of turn, this land becomes a 3/3 black
        // Beholder creature with menace and "Whenever this creature
        // attacks, exile target card from defending player's graveyard."
        // It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {3}{B}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 3/3 black Beholder creature with menace + attack-exile-graveyard trigger until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type and Beholder subtype.
                // Printed Land type stays ("it's still a land").
                effects.Register(new HiveOfTheEyeTyrantAnimateEffect(land));

                // Layer 7b — set base P/T 3/3.
                effects.Register(new HiveOfTheEyeTyrantBecomesPTEffect(land, 3, 3));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{3}{B}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Per-instance attack trigger (animated form): "Whenever this
        // creature attacks, exile target card from defending player's
        // graveyard."
        //
        // CR 603.6 / CR 508.1f. v1: structurally attached unconditionally
        // (the trigger condition fires on CreatureAttacksEvent with
        // Attacker == this land; while not animated Hive can't attack so
        // the trigger is unreachable in practice). Target prompt is a
        // 1..1 TargetRequest mirroring Tormod's Crypt; resolution reads
        // ChosenTargets[0][0] for the chosen graveyard card and moves it
        // to the defending player's exile zone.
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;
        var exileEffect = new Effect(
            $"{CardName}: exile target card from defending player's graveyard",
            () =>
            {
                if (attackTrigger == null) return;
                if (attackTrigger.ChosenTargets.Count == 0) return;
                if (attackTrigger.ChosenTargets[0].Count == 0) return;
                if (attackTrigger.ChosenTargets[0][0] is not Card chosen) return;

                var graveyardOwner = chosen.Owner;
                if (graveyardOwner == null) return;
                if (chosen.Zone != Zones.ZoneType.Graveyard) return;

                graveyardOwner.Zones.Graveyard.RemoveCard(chosen);
                graveyardOwner.Zones.Exile.AddCard(chosen);
                chosen.SetZone(Zones.ZoneType.Exile);
            });

        attackTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { exileEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target card from defending player's graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(attackTrigger);

        return land;
    }

    /// <summary>
    /// CR 614 helper — count lands the controller controls excluding the
    /// candidate <paramref name="self"/>. Used by the conditional ETB-
    /// tapped predicate ("two or more OTHER lands").
    /// </summary>
    private static int CountOtherLands(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasType(CardType.Land));
}

/// <summary>
/// Hive of the Eye Tyrant — activated-ability resolution: Layer 4
/// type-adding effect. Adds <see cref="CardType.Creature"/> to the
/// permanent's effective types, plus the <see cref="CardSubtype.Beholder"/>
/// subtype and a Menace keyword marker. Expires at end of turn (CR 514.2).
///
/// CR 613.1c — types are added on top of printed values; the printed
/// <see cref="CardType.Land"/> remains intact, matching the oracle's
/// "It's still a land" rider.
///
/// Menace (CR 702.110) is registered as a keyword marker on the
/// permanent's keyword set — same posture as Inkmoth Nexus' Flying /
/// Infect markers. Combat-blocking enforcement of Menace ("can't be
/// blocked except by two or more creatures") is consulted by the
/// combat-block validator when Menace is present in the effective
/// keyword set.
/// </summary>
public sealed class HiveOfTheEyeTyrantAnimateEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public HiveOfTheEyeTyrantAnimateEffect(Permanent target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>The permanent being animated.</summary>
    public Permanent Target => _target;

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        chars.Types.Add(CardType.Creature);
        chars.Subtypes.Add(CardSubtype.Beholder);
        chars.Keywords.Add("Menace");
    }
}

/// <summary>
/// Hive of the Eye Tyrant — activated-ability resolution: Layer 7b
/// set-base P/T effect recording the 3/3 the manland turns into. Same
/// "Land runtime instance" shim posture as
/// <see cref="MutavaultBecomesPTEffect"/> and
/// <see cref="CreepingTarPitBecomesPTEffect"/>:
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a
/// plain <see cref="PermanentCharacteristics"/> with no P/T fields, so
/// the effect is registered for layer-system correctness and exposes
/// <see cref="NewPower"/> / <see cref="NewToughness"/> for inspection
/// until <see cref="ContinuousEffectsService.Compute(Permanent)"/> can
/// upgrade the chars row when Layer 4 grants Creature type.
/// </summary>
public sealed class HiveOfTheEyeTyrantBecomesPTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>The base power the target becomes (CR 613.7b).</summary>
    public int NewPower { get; }

    /// <summary>The base toughness the target becomes (CR 613.7b).</summary>
    public int NewToughness { get; }

    public HiveOfTheEyeTyrantBecomesPTEffect(Permanent target, int power, int toughness)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        NewPower = power;
        NewToughness = toughness;
    }

    public override Layer Layer => Layer.PT_SetBase;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);

    public override bool AppliesTo(Permanent permanent) => ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }

    // No Apply(PermanentCharacteristics) override: the base default
    // dispatches to Apply(CreatureCharacteristics) when the working set is a
    // creature row. ContinuousEffectsService.Compute upgrades an animated
    // Land to a creature row (CR 613.1c) on a Layer-4 Creature grant, so this
    // set-base lands and the animated body surfaces through combat math.
}
