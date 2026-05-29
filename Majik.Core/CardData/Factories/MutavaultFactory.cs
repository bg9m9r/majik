using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mutavault (Morningtide / reprints).
///
/// Land.
/// Oracle text:
///   "{T}: Add {C}.
///    {1}: Until end of turn, Mutavault becomes a 2/2 creature that's
///    every creature type. It's still a land."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1, no
///   stack).
/// - <b>{1}: become a 2/2 every-creature-type creature until EOT</b> —
///   wired as an <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>
///   of <c>{1}</c>. Resolution registers three end-of-turn-expirable
///   continuous effects against the supplied <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="MutavaultAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> to the permanent's effective
///       types and grants every creature subtype enumerated in
///       <see cref="MutavaultAnimateEffect.EveryCreatureType"/>. The
///       printed Land type is left intact ("It's still a land", CR
///       613.1c — types and subtypes are added, not replaced).
///     - Layer 7b (<see cref="MutavaultBecomesPTEffect"/>) — set-base
///       P/T = 2/2. Mirrors the
///       <c>KarnAnimatedShimPTEffect</c> pattern: Mutavault is a
///       <see cref="Land"/> runtime instance, so
///       <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds
///       a plain <see cref="PermanentCharacteristics"/> with no P/T
///       fields. The effect still records <see cref="MutavaultBecomesPTEffect.NewPower"/>
///       and <see cref="MutavaultBecomesPTEffect.NewToughness"/> so
///       tests / bots / a future Compute upgrade can surface the values.
///   All three effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   = true so <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>
///   (CR 514.2 cleanup step) lifts the animation.
///
/// ## "Every creature type" simplification (v1 gap)
/// CR 205.3m enumerates ~250 creature subtypes; the engine's
/// <see cref="CardSubtype"/> enum lists ~50 of them. The animate effect
/// grants every creature subtype currently enumerated. This is a v1
/// observable equivalent — tribal-lord interactions are covered for the
/// tribes the engine knows about (Goblin, Elf, Human, etc.). When the
/// enum grows, this set auto-grows with it (the list is built from the
/// declared <see cref="MutavaultAnimateEffect.EveryCreatureType"/> array,
/// edited in one place).
///
/// ## Deferred (v1 gaps)
/// - <b>Combat math through Compute</b>: same gap as Karn's animate-
///   artifact (<see cref="KarnAnimateArtifactEffect"/>). Until
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades
///   to <see cref="CreatureCharacteristics"/> when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 2/2 doesn't surface for combat
///   resolution. <see cref="MutavaultBecomesPTEffect"/> stores the
///   intended P/T for inspection meanwhile.
/// - <b>Summoning sickness</b>: a Land animated mid-turn would have
///   summoning sickness (CR 302.1) until controlled continuously since
///   the start of the controller's turn. Mutavault was on the
///   battlefield long enough but its Creature-ness is fresh — the
///   intricate "had Creature type continuously since untap step"
///   bookkeeping is deferred; the test suite asserts shape, not
///   attack legality.
/// </summary>
[CardName("Mutavault")]
public static class MutavaultFactory
{
    public const string CardName = "Mutavault";

    /// <summary>
    /// Construct Mutavault with no <see cref="ContinuousEffectsService"/>
    /// wired. The activated ability's mana cost still pays and the
    /// ability resolves, but the animate / P/T effects are not
    /// registered — vanilla / shape-only path for tests that only need
    /// the card identity + mana ability.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct a fully-wired Mutavault.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effect is recorded.</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            CardName,
            supertypes: null,
            subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {1}: Until end of turn, Mutavault becomes a 2/2 creature
        // that's every creature type. It's still a land.
        // CR 602 — ordinary activated ability (uses the stack); the cost
        // is {1} only (no tap). Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            "Mutavault: becomes a 2/2 every-creature-type creature until EOT (it's still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type and every creature subtype
                // (CR 613.1c). Printed Land type stays.
                effects.Register(new MutavaultAnimateEffect(land));

                // Layer 7b — set base P/T 2/2 (CR 613.7b).
                effects.Register(new MutavaultBecomesPTEffect(land, 2, 2));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }

    /// <summary>
    /// Construct a Mutavault token (CR 111) and put it onto the battlefield
    /// under <paramref name="controller"/>. The token is a Land with the
    /// same ability shape as the printed card (<c>{T}: Add {C}</c> +
    /// <c>{1}: become a 2/2 every-creature-type creature until EOT, it's
    /// still a land</c>), stamped with <see cref="Permanent.IsToken"/> so
    /// SBA 704.5d removes it from any zone other than the battlefield.
    ///
    /// Created by spells / abilities that print "create a Mutavault
    /// token" (Mutable Explorer's ETB trigger is the in-Modern source).
    /// </summary>
    /// <param name="controller">Initial controller / owner of the token.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 / 7b
    /// registration of the animate ability. May be null — the ability
    /// still resolves but no continuous effect is recorded
    /// (shape-only path).</param>
    /// <param name="zones">When supplied, the token enters via
    /// <see cref="ZoneService.MoveCardTo"/> so CardMovedEvent fires for
    /// downstream triggers / log subscribers. Without it the token is
    /// placed directly into <c>controller.Zones.Battlefield</c>.</param>
    /// <param name="tapped">When true, the token is tapped on the way in
    /// (Mutable Explorer's oracle: "create a tapped Mutavault token").
    /// </param>
    public static Land CreateAsToken(
        Player controller,
        ContinuousEffectsService? effects = null,
        ZoneService? zones = null,
        bool tapped = false)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        var token = Create(controller, effects);
        token.MarkAsToken();

        // Tokens enter the battlefield directly (CR 111.6) — sentinel
        // "from Library" pattern mirrors TokenFactory.CreateOnBattlefield
        // so ZoneService.MoveCardTo's from-zone check passes and
        // CardMovedEvent fires for downstream subscribers.
        token.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(token);

        if (zones != null)
        {
            zones.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(token);
            token.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(token);
        }

        // CR 614.1c — "create a tapped …": tap after the move so the
        // CardMovedEvent fired untapped; the resulting permanent state
        // is tapped, which is what matters for subsequent SBAs / mana
        // ability legality (Land.CanTapForMana returns false).
        if (tapped)
        {
            token.Tap();
        }

        return token;
    }
}

/// <summary>
/// Mutavault — activated-ability resolution: Layer 4 type-adding effect.
/// Adds <see cref="CardType.Creature"/> and every creature subtype the
/// engine currently models to the target permanent's effective
/// characteristics. Expires at end of turn (CR 514.2).
///
/// CR 613.1c — types and subtypes are added on top of printed values;
/// Mutavault's printed <see cref="CardType.Land"/> remains intact, which
/// matches the oracle's "It's still a land" rider.
/// </summary>
public sealed class MutavaultAnimateEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public MutavaultAnimateEffect(Permanent target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>The permanent being animated.</summary>
    public Permanent Target => _target;

    /// <summary>
    /// The set of creature subtypes the engine currently models. Used to
    /// approximate "every creature type" — see the
    /// <see cref="MutavaultFactory"/> class xmldoc for the v1 simplification.
    /// The list is the source of truth: when <see cref="CardSubtype"/>
    /// grows new creature subtypes, append them here.
    /// </summary>
    public static readonly IReadOnlyList<CardSubtype> EveryCreatureType = new[]
    {
        CardSubtype.Human, CardSubtype.Dryad, CardSubtype.Phyrexian, CardSubtype.Elf,
        CardSubtype.Goblin, CardSubtype.Dragon, CardSubtype.Angel, CardSubtype.Demon,
        CardSubtype.Zombie, CardSubtype.Beast, CardSubtype.Bird, CardSubtype.Cat,
        CardSubtype.Dog, CardSubtype.Elemental, CardSubtype.Bear, CardSubtype.Insect,
        CardSubtype.Spirit, CardSubtype.Warrior, CardSubtype.Wizard, CardSubtype.Cleric,
        CardSubtype.Rogue, CardSubtype.Knight, CardSubtype.Soldier, CardSubtype.Shaman,
        CardSubtype.Halfling, CardSubtype.Citizen, CardSubtype.Orc, CardSubtype.Archer,
        CardSubtype.Army, CardSubtype.Advisor, CardSubtype.Incarnation, CardSubtype.Lhurgoyf,
        CardSubtype.Kor, CardSubtype.Artificer, CardSubtype.Ooze, CardSubtype.Avatar,
        CardSubtype.Wurm, CardSubtype.Nightmare, CardSubtype.Rhino, CardSubtype.Giant,
        CardSubtype.Dauthi, CardSubtype.Monkey, CardSubtype.Pirate, CardSubtype.Scout,
        CardSubtype.Illusion, CardSubtype.Nymph,
        CardSubtype.Eldrazi, CardSubtype.Spawn, CardSubtype.Scion,
    };

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
        foreach (var st in EveryCreatureType)
        {
            chars.Subtypes.Add(st);
        }
    }
}

/// <summary>
/// Mutavault — activated-ability resolution: Layer 7b set-base P/T effect
/// recording the 2/2 the manland turns into. Mirrors the
/// <c>KarnAnimatedShimPTEffect</c> pattern: Mutavault is a
/// <see cref="Land"/> runtime instance, so
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a
/// plain <see cref="PermanentCharacteristics"/> with no P/T fields. The
/// effect is still registered for layer-system correctness and exposes
/// <see cref="NewPower"/> / <see cref="NewToughness"/> for inspection
/// until <see cref="ContinuousEffectsService.Compute(Permanent)"/> can
/// upgrade the chars row when Layer 4 grants Creature type.
/// </summary>
public sealed class MutavaultBecomesPTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>The base power the target becomes (CR 613.7b).</summary>
    public int NewPower { get; }

    /// <summary>The base toughness the target becomes (CR 613.7b).</summary>
    public int NewToughness { get; }

    public MutavaultBecomesPTEffect(Permanent target, int power, int toughness)
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

    public override void Apply(PermanentCharacteristics chars)
    {
        // Layer 7b on a non-Creature row is observationally a no-op in
        // the current pipeline. See class xmldoc.
    }
}
