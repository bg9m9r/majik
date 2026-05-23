using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Oko, Thief of Crowns (Throne of Eldraine, {1}{G}{U}).
///
/// Legendary Planeswalker — Oko, starting loyalty 4.
/// Oracle text:
///   "+2: Create a Food token.
///    +1: Target artifact or creature loses all abilities and becomes a green
///         Elk creature with base power and toughness 3/3.
///    -5: Exchange control of target artifact or creature you don't control
///         and target creature you control. Then those creatures' controllers
///         each remove all counters from the creature they control."
///
/// ## Implemented (v1)
/// - Legendary Planeswalker with loyalty 4, Oko subtype, mana cost {1}{G}{U}.
/// - <b>+2</b>: spawns a Food token via <see cref="TokenFactory.CreateFood"/>
///   on the controller's battlefield. Same token shape used by the
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Tokens.CreateFoodTokensTemplate"/>
///   pipeline — {2}, {T}, Sacrifice: gain 3 life (CR 111.10).
/// - <b>+1</b>: deterministic auto-pick — first artifact OR creature on the
///   battlefield via <paramref name="battlefieldResolver"/>. Registers three
///   continuous effects on <paramref name="effects"/>:
///   <list type="bullet">
///     <item>A Layer 4 <see cref="OkoBecomesElkTypeEffect"/> that adds
///           <see cref="CardType.Creature"/> + sets subtype to
///           <see cref="CardSubtype.Elk"/> (printed subtypes within the
///           creature-subtype category are dropped per CR 613.1d, mirroring
///           <see cref="SetSubtypesEffect"/> on creature subtypes).</item>
///     <item>A Layer 6 <see cref="LoseAllAbilitiesEffect"/> scoped to the
///           target — strips all keyword abilities (CR 613.6). Non-keyword
///           abilities on the underlying card are NOT removed in v1; the
///           engine's only in-characteristics ability surface is
///           <c>chars.Keywords</c>. The structural strip is registered for
///           layer-correctness regardless.</item>
///     <item>A Layer 7b <see cref="BecomesPTEffect"/> (or the
///           <see cref="KarnAnimatedShimPTEffect"/> shim when the target is
///           not a <see cref="Creature"/> C# instance) carrying 3/3.</item>
///   </list>
///   Effects do not auto-expire — Oko's +1 is "for as long as Oko is on the
///   battlefield" (no duration printed). The effects' <c>IsActive()</c>
///   gating on the target's battlefield zone naturally lifts the rider when
///   the affected permanent leaves.
/// - <b>-5</b>: exchange-control mechanic (CR 702.X / 611). Deterministic
///   auto-pick: first artifact/creature controlled by anyone other than
///   Oko's controller, and first creature controlled by Oko's controller.
///   Swaps <see cref="Permanent.Controller"/> on both and moves them between
///   the two players' battlefield zones so zone-snapshots see the new
///   controller. Counter-removal half is wired but no-ops because the v1
///   engine has no counter-tracking surface on <see cref="Permanent"/>; the
///   intent is documented for future expansion.
///
/// ## Deferred (v1 gaps)
/// - <b>Colour-set to green</b>: no <c>SetColorsEffect</c> exists in the
///   engine yet — Layer 5 colour-changing is absent. The +1 stamps Elk +
///   Creature + 3/3 but the target's printed colours pass through
///   unchanged. Documented gap; same shape as the
///   <see cref="KarnAnimateArtifactEffect"/> "stamp Creature without
///   colour" v1 simplification.
/// - <b>Targeting prompts</b>: <see cref="LoyaltyAbility"/> does not yet
///   declare <see cref="Majik.Core.Targeting.TargetRequest"/>s. +1 and -5
///   auto-pick targets deterministically rather than via the agent.
/// - <b>Counter removal on -5</b>: CR 611 counter-tracking is not modelled
///   on <see cref="Permanent"/>; the -5 documents the intent in its
///   effect description but no runtime state changes.
/// - <b>"Up to" / "may" softening on the +1's target</b>: the printed +1
///   requires a target. The single-arg dispatcher path passes no resolver
///   so the +1 no-ops cleanly (legal for tests / shape).
/// </summary>
public static class OkoThiefOfCrownsFactory
{
    public const string CardName = "Oko, Thief of Crowns";
    public const string Cost = "{1}{G}{U}";

    /// <summary>
    /// Construct an Oko with no live continuous-effects service or board
    /// resolvers. The +2 still spawns a Food token (the
    /// <see cref="TokenFactory"/> uses the controller's zones directly).
    /// The +1 and -5 ability bodies are attached so loyalty changes still
    /// apply (CR 606.3); their effect bodies are gated on the resolvers /
    /// services being non-null, so they no-op here.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, effects: null, battlefieldResolver: null, allPlayersResolver: null);

    /// <summary>
    /// Construct a fully-wired Oko.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for the +1's Layer
    /// 4 / 6 / 7b registration. May be null — the +1 still picks a target
    /// and the loyalty change applies, but no continuous effect is
    /// registered.</param>
    /// <param name="battlefieldResolver">Returns the live battlefield
    /// snapshot at activation time. Used by the +1 (to find a valid target)
    /// and indirectly by the -5 via <paramref name="allPlayersResolver"/>.
    /// May be null — the +1 no-ops (legal — target picker).</param>
    /// <param name="allPlayersResolver">Returns the player list. Used by
    /// the -5 to find a non-controller's permanent + a controller's
    /// creature for the exchange. May be null — the -5 no-ops while
    /// loyalty still decrements per CR 606.3.</param>
    public static Planeswalker Create(
        Player owner,
        ContinuousEffectsService? effects,
        Func<IReadOnlyList<Permanent>>? battlefieldResolver,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var oko = new Planeswalker(
            name: CardName,
            manaCost: Cost,
            startingLoyalty: 4,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Oko });

        oko.SetOwner(owner);
        oko.SetController(owner);

        // -- +2: Create a Food token. ---------------------------------------
        oko.AddAbility(new LoyaltyAbility(oko, +2, () =>
        {
            // CR 111.10 — Food token (colourless artifact with the printed
            // gain-3-life activated ability). TokenFactory.CreateFood
            // handles ETB zone routing. Use the current controller (falling
            // back to owner if controller has been cleared post-LTB).
            var controller = oko.Controller ?? owner;
            TokenFactory.CreateFood(controller);
        }));

        // -- +1: Target artifact/creature loses abilities + becomes 3/3 Elk
        oko.AddAbility(new LoyaltyAbility(oko, +1, () =>
        {
            var target = PickElkTarget(battlefieldResolver);
            if (target == null) return; // no resolver / no valid pick — no-op
            if (effects == null) return; // shape-only path

            // Layer 4 — add Creature type + set creature subtype to Elk.
            effects.Register(new OkoBecomesElkTypeEffect(source: oko, target: target));

            // Layer 6 — strip all abilities (CR 613.6). Scoped to the
            // single target. Pool is supplied as a single-element list
            // for predicate matching; the underlying effect filters by
            // battlefield zone + predicate.
            if (target is Creature targetCreature)
            {
                effects.Register(new LoseAllAbilitiesEffect(
                    source: oko,
                    pool: new[] { targetCreature },
                    predicate: c => ReferenceEquals(c, targetCreature)));

                // Layer 7b — set base P/T to 3/3.
                effects.Register(new BecomesPTEffect(targetCreature, 3, 3));
            }
            else
            {
                // Non-creature target (e.g. an artifact). The Layer 6
                // strip's pool only accepts Creature; the layer system
                // already gates ability-strip on creature characteristics.
                // We still register the Layer 7b shim so future Compute
                // upgrades (Permanent-to-Creature row promotion under
                // Layer 4) can surface the 3/3.
                effects.Register(new KarnAnimatedShimPTEffect(target, 3, 3));
            }
        }));

        // -- -5: Exchange control of target opponent-permanent and target
        //        creature you control. -----------------------------------
        oko.AddAbility(new LoyaltyAbility(oko, -5, () =>
        {
            if (allPlayersResolver == null) return;
            var players = allPlayersResolver.Invoke();
            if (players == null) return;

            // First non-controller's artifact-or-creature, first
            // controller's creature.
            Permanent? theirs = null;
            Creature? mine = null;

            var myController = oko.Controller ?? owner;

            // Mine: first creature on Oko's controller's battlefield.
            foreach (var card in myController.Zones.Battlefield.GetCards())
            {
                if (card is Creature c)
                {
                    mine = c;
                    break;
                }
            }
            if (mine == null) return;

            // Theirs: first artifact-or-creature on any other player's
            // battlefield.
            foreach (var p in players)
            {
                if (ReferenceEquals(p, myController)) continue;
                foreach (var card in p.Zones.Battlefield.GetCards())
                {
                    if (card is Permanent perm
                        && (perm.HasType(CardType.Artifact) || perm.HasType(CardType.Creature)))
                    {
                        theirs = perm;
                        break;
                    }
                }
                if (theirs != null) break;
            }
            if (theirs == null) return;

            // Exchange control — move each permanent between the two
            // players' battlefield zones and swap their Controller refs.
            var theirController = theirs.Controller ?? throw new InvalidOperationException(
                "Exchange target is missing a controller — the resolver returned an off-battlefield permanent.");

            myController.Zones.Battlefield.RemoveCard(mine);
            theirController.Zones.Battlefield.RemoveCard(theirs);

            theirController.Zones.Battlefield.AddCard(mine);
            myController.Zones.Battlefield.AddCard(theirs);

            mine.SetController(theirController);
            theirs.SetController(myController);

            // Counter removal half — documented intent. CR 611 counter-
            // tracking isn't modelled on Permanent in v1, so this is a
            // structural no-op. Once counters land, scan each swapped
            // permanent's counter list and remove every counter.
        }));

        return oko;
    }

    /// <summary>
    /// Deterministic v1 target picker for the +1 ability: first
    /// artifact-or-creature permanent on the supplied battlefield
    /// snapshot. Matches the same shape as
    /// <see cref="KarnTheGreatCreatorFactory"/>'s +1 picker.
    /// </summary>
    private static Permanent? PickElkTarget(
        Func<IReadOnlyList<Permanent>>? resolver)
    {
        if (resolver == null) return null;
        var board = resolver.Invoke();
        if (board == null) return null;
        foreach (var perm in board)
        {
            // CR 109.1 — "artifact or creature".
            if (perm.HasType(CardType.Artifact) || perm.HasType(CardType.Creature))
            {
                return perm;
            }
        }
        return null;
    }
}

/// <summary>
/// Layer 4 type/subtype effect for Oko's +1 — "becomes a green Elk creature".
/// Adds <see cref="CardType.Creature"/> and rewrites the target's creature-
/// subtype set to <c>{ Elk }</c>. Operates at the
/// <see cref="PermanentCharacteristics"/> level so non-Creature artifact
/// targets also see the type-add (mirroring
/// <see cref="KarnAnimateArtifactEffect"/>); when the target is a Creature
/// the same effect overrides subtypes on the
/// <see cref="CreatureCharacteristics"/> row.
///
/// Colour-set to green is NOT modelled in v1 — no Layer 5 colour-changing
/// primitive exists yet (see <see cref="OkoThiefOfCrownsFactory"/> xmldoc).
/// </summary>
public sealed class OkoBecomesElkTypeEffect : ContinuousEffect
{
    private readonly Permanent _target;
    private readonly Permanent _source;

    /// <param name="source">Oko himself — the planeswalker generating the
    /// continuous effect. CR 613.1g; the layer service uses Source to
    /// suppress effects whose source has had its abilities stripped (CR
    /// 613.8). Sourcing on the target rather than Oko would let a same-
    /// turn Humility / Dress Down on the now-Elk creature silently kill
    /// Oko's own +1 rider via the Layer 6 dependency filter — Oko keeps
    /// his abilities, so anchoring Source on the planeswalker is the
    /// correct attribution.</param>
    public OkoBecomesElkTypeEffect(Permanent source, Permanent target)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public Permanent Target => _target;

    public override Layer Layer => Layer.Type;
    public override Permanent? Source => _source;
    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield
        && _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);
    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        // Creature row — set creature subtype to Elk (replace any
        // existing creature subtypes; CR 613.1d "becomes a green Elk
        // creature" overwrites the subtype slot rather than appending).
        // Non-creature subtypes (Land subtypes, Artifact subtypes like
        // Equipment) are untouched — Oko's +1 strips creature identity,
        // not all subtyping.
        chars.Subtypes.RemoveWhere(IsCreatureSubtype);
        chars.Subtypes.Add(CardSubtype.Elk);
        // Also stamp Creature on the type set so a Compute pass over the
        // creature row continues to report Creature.
        if (!chars.Types.Contains(CardType.Creature))
        {
            chars.Types.Add(CardType.Creature);
        }
    }

    public override void Apply(PermanentCharacteristics chars)
    {
        // Permanent row (non-creature target) — stamp Creature type so the
        // layer system would, in a future Compute-promotion world, route
        // the permanent through the Creature pipeline. Subtype rewrite
        // happens on the Creature row (Apply(CreatureCharacteristics));
        // for non-Creature permanents we still drop the Elk marker on the
        // subtypes set so consumers reading Subtypes-only see the rider.
        if (!chars.Types.Contains(CardType.Creature))
        {
            chars.Types.Add(CardType.Creature);
        }
        chars.Subtypes.RemoveWhere(IsCreatureSubtype);
        chars.Subtypes.Add(CardSubtype.Elk);
    }

    // Predicate identifying creature-subtype enum members. The enum has
    // no category metadata, so we hard-code the known creature-subtype
    // values. Land / Artifact / Enchantment / Planeswalker subtypes are
    // left untouched. Conservative: when in doubt we treat a subtype as
    // non-creature, so this predicate intentionally accepts the
    // well-known creature tribes (Elf / Goblin / Bear / etc.) and any
    // other CardSubtype value above the explicit non-creature ranges.
    private static bool IsCreatureSubtype(CardSubtype st) => st switch
    {
        // Land subtypes
        CardSubtype.Forest or CardSubtype.Island or CardSubtype.Mountain
            or CardSubtype.Plains or CardSubtype.Swamp or CardSubtype.Wastes
            or CardSubtype.Desert or CardSubtype.Gate or CardSubtype.Lair
            or CardSubtype.Locus or CardSubtype.Mine or CardSubtype.PowerPlant
            or CardSubtype.Tower or CardSubtype.Urzas => false,
        // Enchantment subtypes
        CardSubtype.Aura or CardSubtype.Saga or CardSubtype.Shrine => false,
        // Artifact subtypes
        CardSubtype.Equipment or CardSubtype.Vehicle or CardSubtype.Food
            or CardSubtype.Treasure or CardSubtype.Clue
            or CardSubtype.Construct or CardSubtype.Blood
            or CardSubtype.Powerstone => false,
        // Planeswalker subtypes
        CardSubtype.Ajani or CardSubtype.Ashiok or CardSubtype.Chandra
            or CardSubtype.Grist or CardSubtype.Jace or CardSubtype.Liliana
            or CardSubtype.Garruk or CardSubtype.Nissa or CardSubtype.Teferi
            or CardSubtype.Karn or CardSubtype.Ugin or CardSubtype.Bolas
            or CardSubtype.Wrenn or CardSubtype.Oko => false,
        // Everything else is treated as creature subtype.
        _ => true,
    };
}
