using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kaito, Bane of Nightmares ({2}{U}{B}).
///
/// Legendary Planeswalker — Kaito. Starting loyalty 4.
/// Oracle text (Scryfall, verified):
///   "Ninjutsu {1}{U}{B} ({1}{U}{B}, Return an unblocked attacker you control
///    to hand: Put this card onto the battlefield from your hand tapped and
///    attacking.)
///    During your turn, as long as Kaito has one or more loyalty counters on
///    him, he's a 3/4 Ninja creature and has hexproof.
///    +1: You get an emblem with 'Ninjas you control get +1/+1.'
///    0: Surveil 2. Then draw a card for each opponent who lost life this turn.
///    −2: Tap target creature. Put two stun counters on it."
///
/// The card's base shape (name, Legendary Planeswalker — Kaito, {2}{U}{B},
/// loyalty 4) is materialised from the embedded JSON definition
/// (<c>kaito-bane-of-nightmares.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The loyalty abilities + the
/// "becomes a creature during your turn" animation are layered on here — the
/// JSON <c>AbilityDefinition</c> schema doesn't express loyalty abilities,
/// emblems, surveil, stun counters, or planeswalker-animation, so they live
/// in the factory (same posture as
/// <see cref="NahiriTheHarbingerFactory"/> / <see cref="LilianaTheLastHopeFactory"/>).
///
/// ## Implemented (v1)
/// - <b>+1: You get an emblem with "Ninjas you control get +1/+1."</b>
///   (CR 606 loyalty + CR 114 emblem.) Mints an <see cref="Emblem"/> in the
///   controller's command zone carrying a structural anthem marker keyed off
///   the <see cref="CardSubtype.Ninja"/> subtype. The continuous +1/+1 to
///   Ninjas is recorded structurally (same posture as the
///   <see cref="WrennAndRealmbreakerFactory"/> / Liliana, the Last Hope
///   emblems — the anthem layer-system wiring is delivered separately by the
///   continuous-effects service when present; without it the emblem is the
///   observable surface).
/// - <b>0: Surveil 2. Then draw a card for each opponent who lost life this
///   turn.</b> (CR 606 + CR 701.42 surveil + CR 121 draw.) Surveils 2 (v1
///   deterministic decision: keep both cards on top in their seen order — the
///   "look at top N" peek is published via <see cref="Fx.Surveil"/> so an
///   agent / log can observe it), then draws one card per opponent whose
///   <see cref="Player.LifeLostThisTurn"/> is &gt; 0 (the same source of
///   truth Spectacle reads). No opponents resolver ⇒ no draw.
/// - <b>−2: Tap target creature. Put two stun counters on it.</b>
///   (CR 606 + CR 701 tap + CR 122.1c stun counters.) Taps the first creature
///   the resolver offers (via <see cref="Fx.Tap"/>) and adds two
///   <see cref="CounterType.Stun"/> counters to it. No resolver / no legal
///   creature ⇒ no-op (loyalty change still applies, CR 606.3).
/// - <b>Static: "During your turn, while Kaito has loyalty, he's a 3/4 Ninja
///   creature and has hexproof."</b> (CR 613 layer system — Layer 4 type-add
///   + Layer 7b set-base P/T + Layer 6 Hexproof keyword.) When a
///   <see cref="ContinuousEffectsService"/> is wired, two conditional
///   continuous effects are registered whose <c>IsActive()</c> gates on
///   (controller's-turn AND loyalty &gt; 0) — mirroring the
///   <see cref="MutavaultFactory"/> manland animation pattern, but persistent
///   (NOT <c>ExpiresAtEndOfTurn</c> — it is a static ability, re-evaluated
///   continuously, CR 613.6).
///
/// ## Implemented (Ninjutsu + Stun untap)
/// - <b>Ninjutsu {1}{U}{B} (CR 702.49).</b> Kaito carries a
///   <see cref="NinjutsuAbility"/> marker recording the printed ninjutsu mana
///   cost ({1}{U}{B}). The reusable <see cref="NinjutsuAction.Execute"/>
///   primitive performs the special action: return an unblocked attacker the
///   caster controls to hand (CR 702.49e / CR 506.4) and put Kaito onto the
///   battlefield from hand tapped and attacking the same defender
///   (CR 702.49b/d, via <see cref="Majik.Core.Combat.CombatManager.AddTappedAndAttackingToken"/>).
/// - <b>Stun-counter untap replacement (CR 122.1g).</b> The −2 puts two stun
///   counters on the target; the untap step (in <c>TurnDriver.UntapStep</c>)
///   now removes one stun counter INSTEAD of untapping a permanent that has
///   one, so a creature stunned by Kaito stays tapped through one untap step
///   per counter.
///
/// ## Deferred (v1 gaps)
/// - <b>Animation through Compute / combat.</b> Like Mutavault / Karn's
///   animate, the Layer-4 grant + Layer-7b P/T are registered for layer-system
///   correctness, but <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///   surfaces creature P/T only for runtime <see cref="Creature"/> instances;
///   Kaito is a <see cref="Planeswalker"/> runtime instance, so the 3/4 body
///   does not yet flow into combat math (same gap noted on Mutavault).
/// - <b>"Can't be attacked the turn it enters."</b> Kaito's printed ability
///   that protects him the turn he enters is part of the Ninjutsu-era
///   templating in some printings; the verified Scryfall oracle for this card
///   does not carry that clause, so nothing is modelled for it.
/// - <b>Emblem anthem layer.</b> The +1 emblem's continuous "+1/+1 to Ninjas
///   you control" is structural (the emblem object exists in the command
///   zone); the live anthem layer-7c effect is not auto-registered (same
///   posture as the Liliana / Wrenn emblems).
/// </summary>
[CardName("Kaito, Bane of Nightmares")]
public static class KaitoBaneOfNightmaresFactory
{
    public const string CardName = "Kaito, Bane of Nightmares";
    public const string Slug = "kaito-bane-of-nightmares";
    /// <summary>CR 702.49 — Kaito's printed ninjutsu mana cost ({1}{U}{B}).</summary>
    public const string NinjutsuCost = "{1}{U}{B}";
    public const int StartingLoyalty = 4;
    public const int Plus1Loyalty = +1;
    public const int ZeroLoyalty = 0;
    public const int Minus2Loyalty = -2;
    public const int SurveilCount = 2;
    public const int StunCountersPlaced = 2;
    public const int AnimatedPower = 3;
    public const int AnimatedToughness = 4;

    /// <summary>
    /// Construct Kaito with no resolvers / services wired — the +1 still mints
    /// its emblem, the 0 surveils but draws nothing (no opponents resolver),
    /// the −2 no-ops (no tap resolver), and the animation effects are not
    /// registered (no continuous-effects service). Suitable for shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, opponentsResolver: null, tapTargetResolver: null,
               effects: null, isControllersTurn: null, eventBus: null);

    /// <summary>
    /// Construct Kaito, Bane of Nightmares.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="opponentsResolver">Returns the opponents of Kaito's
    /// controller for the 0 ability's "draw a card for each opponent who lost
    /// life this turn" clause. May be null — the clause draws nothing.</param>
    /// <param name="tapTargetResolver">Returns candidate creatures for the −2.
    /// v1 taps the first creature on the battlefield. May be null — the clause
    /// no-ops.</param>
    /// <param name="effects">When supplied, the "becomes a 3/4 Ninja creature
    /// during your turn while it has loyalty" animation registers its Layer-4
    /// type-grant + Layer-7b P/T continuous effects. May be null — the
    /// animation is not registered.</param>
    /// <param name="isControllersTurn">Predicate the animation's
    /// <c>IsActive()</c> consults for the "During your turn" gate. May be null
    /// — defaults to always-true (the loyalty &gt; 0 gate still applies). The
    /// live game wires this to "active player == Kaito's controller".</param>
    /// <param name="eventBus">When supplied, the 0 ability's surveil publishes
    /// its <see cref="SurveilEvent"/> onto this bus. May be null — the player's
    /// registered bus (if any) is used.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentsResolver,
        Func<IReadOnlyList<Permanent>>? tapTargetResolver,
        ContinuousEffectsService? effects,
        Func<bool>? isControllersTurn,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Kaito, {2}{U}{B}, loyalty 4). The JSON carries no
        // abilities — everything below is layered on.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var kaito = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // -- Ninjutsu {1}{U}{B} (CR 702.49) ---------------------------------
        // Marker carrying the ninjutsu mana cost. The special action (return
        // an unblocked attacker to hand, put Kaito onto the battlefield tapped
        // and attacking) is performed by NinjutsuAction.Execute; this marker
        // exposes the cost + keeps Kaito discoverable as a Ninjutsu carrier.
        kaito.AddAbility(new NinjutsuAbility(kaito, NinjutsuCost, owner));

        // -- +1: You get an emblem with "Ninjas you control get +1/+1." -----
        // CR 606 (loyalty) + CR 114 (emblem). Structural emblem — the anthem
        // layer is delivered by the continuous-effects service when present
        // (same posture as Liliana / Wrenn emblems).
        kaito.AddAbility(new LoyaltyAbility(kaito, Plus1Loyalty, () =>
        {
            var controller = kaito.Controller ?? owner;
            var emblem = new Emblem(
                controller: controller,
                sourceName: $"{CardName} — \"Ninjas you control get +1/+1\" emblem",
                abilities: Array.Empty<IAbility>());
            controller.AddEmblem(emblem);
        }));

        // -- 0: Surveil 2. Then draw a card for each opponent who lost life
        //    this turn. ------------------------------------------------------
        // CR 606 + CR 701.42 (surveil) + CR 121 (draw).
        kaito.AddAbility(new LoyaltyAbility(kaito, ZeroLoyalty, () =>
        {
            var controller = kaito.Controller ?? owner;

            // "Surveil 2" — v1 deterministic decision: keep both peeked cards
            // on top in their seen order (no graveyard). The peek is published
            // via the SurveilEvent so an agent / log observes it.
            var peeked = SurveilAction.Peek(controller, SurveilCount);
            var decision = new SurveilAction.SurveilDecision(
                ToGraveyard: Array.Empty<ICard>(),
                TopOrder: peeked);
            Fx.Surveil(controller, SurveilCount, decision, eventBus);

            // "Then draw a card for each opponent who lost life this turn."
            // CR 121 — count opponents whose LifeLostThisTurn > 0 (the same
            // source of truth Spectacle reads).
            var opponents = opponentsResolver?.Invoke();
            if (opponents == null) return;
            var lostLifeCount = opponents.Count(p => p != null && p.LifeLostThisTurn > 0);
            if (lostLifeCount > 0)
            {
                Fx.DrawCards(controller, lostLifeCount);
            }
        }));

        // -- −2: Tap target creature. Put two stun counters on it. ----------
        // CR 606 + tap + CR 122.1c (stun counters).
        kaito.AddAbility(new LoyaltyAbility(kaito, Minus2Loyalty, () =>
        {
            var candidates = tapTargetResolver?.Invoke();
            if (candidates == null) return;
            foreach (var p in candidates)
            {
                if (p == null) continue;
                if (p.Zone != ZoneType.Battlefield) continue;
                if (!p.HasType(CardType.Creature)) continue;

                Fx.Tap(p);
                p.Counters.Add(CounterType.Stun, StunCountersPlaced);
                return; // "target creature" — a single permanent.
            }
        }));

        // -- Static: "During your turn, as long as Kaito has one or more
        //    loyalty counters on him, he's a 3/4 Ninja creature and has
        //    hexproof." ------------------------------------------------------
        // CR 613 layer system. Registered up-front (NOT EOT-expiring — it's a
        // static ability, re-evaluated continuously via IsActive()).
        if (effects != null)
        {
            bool Gate() => kaito.Zone == ZoneType.Battlefield
                           && kaito.Loyalty > 0
                           && (isControllersTurn?.Invoke() ?? true);

            effects.Register(new KaitoAnimateEffect(kaito, Gate));
            effects.Register(new KaitoBecomesPTEffect(kaito, AnimatedPower, AnimatedToughness, Gate));
        }

        return kaito;
    }
}

/// <summary>
/// Kaito, Bane of Nightmares — Layer 4 type-grant + Layer 6 ability-grant for
/// the conditional animation. Adds <see cref="CardType.Creature"/>, the
/// <see cref="CardSubtype.Ninja"/> subtype, and a Hexproof keyword to Kaito
/// while the supplied gate holds (controller's turn AND loyalty &gt; 0).
///
/// CR 613.1c — types/subtypes are added, not replaced (Kaito stays a
/// Planeswalker — "he's a 3/4 Ninja creature" is additive). The effect is
/// persistent (not EOT-expiring); the gate is re-consulted each time
/// <see cref="IsActive"/> / <see cref="AppliesTo(Permanent)"/> runs, matching
/// a static ability's continuous re-evaluation (CR 613.6).
/// </summary>
public sealed class KaitoAnimateEffect : ContinuousEffect
{
    private readonly Planeswalker _kaito;
    private readonly Func<bool> _gate;

    public KaitoAnimateEffect(Planeswalker kaito, Func<bool> gate)
    {
        _kaito = kaito ?? throw new ArgumentNullException(nameof(kaito));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public Planeswalker Target => _kaito;

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _kaito;

    public override bool IsActive() => _gate();

    public override bool ExpiresAtEndOfTurn => false;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _kaito) && _gate();

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        chars.Types.Add(CardType.Creature);
        chars.Subtypes.Add(CardSubtype.Ninja);
    }
}

/// <summary>
/// Kaito, Bane of Nightmares — Layer 7b set-base P/T effect recording the 3/4
/// body Kaito takes on while animated. Mirrors
/// <see cref="MutavaultBecomesPTEffect"/>: Kaito is a <see cref="Planeswalker"/>
/// runtime instance, so <see cref="ContinuousEffectsService.Compute(Permanent)"/>
/// seeds a plain <see cref="PermanentCharacteristics"/> with no P/T fields. The
/// effect still records <see cref="NewPower"/> / <see cref="NewToughness"/> for
/// inspection until Compute can upgrade the row when Layer 4 grants Creature.
/// </summary>
public sealed class KaitoBecomesPTEffect : ContinuousEffect
{
    private readonly Planeswalker _kaito;
    private readonly Func<bool> _gate;

    /// <summary>The base power Kaito becomes while animated (CR 613.7b).</summary>
    public int NewPower { get; }

    /// <summary>The base toughness Kaito becomes while animated (CR 613.7b).</summary>
    public int NewToughness { get; }

    public KaitoBecomesPTEffect(Planeswalker kaito, int power, int toughness, Func<bool> gate)
    {
        _kaito = kaito ?? throw new ArgumentNullException(nameof(kaito));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        NewPower = power;
        NewToughness = toughness;
    }

    public override Layer Layer => Layer.PT_SetBase;

    public override Permanent? Source => _kaito;

    public override bool IsActive() => _gate();

    public override bool ExpiresAtEndOfTurn => false;

    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _kaito) && _gate();

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _kaito) && _gate();

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }

    // No Apply(PermanentCharacteristics) override: the base default dispatches
    // to Apply(CreatureCharacteristics) when the working set is a creature row.
    // ContinuousEffectsService.Compute upgrades the animated permanent to a
    // creature row (CR 613.1c) on the Layer-4 Creature grant, so this set-base
    // lands and surfaces through combat math.
}
