using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vexing Shusher (Shadowmoor, {R/G}{R/G}).
///
/// Creature — Goblin Shaman 2/2. Oracle text (Scryfall, verified):
///   "This spell can't be countered.
///    {R/G}: Target spell can't be countered."
///
/// Hand-rolled factory (not JSON-driven): the JSON
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>
/// pipeline supports neither the cast-uncounterable self marker nor a
/// targeted "grant can't-be-countered" activated effect, so the behaviour
/// is composed directly here. Shape mirrors
/// <see cref="EmrakulTheAeonsTornFactory"/> (Uncounterable self marker) and
/// <see cref="MistriseVillageFactory"/> (uncounterable activated ability).
///
/// ## Implemented (v1)
/// - 2/2 Creature — Goblin Shaman at {R/G}{R/G}. The hybrid cost is passed
///   verbatim; <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> handles
///   the {R/G} pips (CR 107.4e).
/// - <b>Cast-uncounterable self marker (CR 701.5b)</b>: "This spell can't be
///   countered." Wired as a <see cref="KeywordAbility"/>("Uncounterable")
///   marker that <see cref="Majik.Core.Game.SpellCastFlow"/> reads at cast
///   time to stamp <see cref="Spell.CannotBeCountered"/> on the resolving
///   Vexing Shusher spell. <see cref="OracleSpellBinder.RemoveFromStack"/>
///   then short-circuits any counter-effect pop. Same wiring as
///   <see cref="EmrakulTheAeonsTornFactory"/>.
/// - <b>{R/G}: Target spell can't be countered (CR 701.5b)</b>: an
///   <see cref="ActivatedAbility"/> with a single {R/G}
///   <see cref="ManaCostCost"/> and one "target spell"
///   <see cref="TargetRequest"/> (Min = Max = 1). Resolution reads
///   <see cref="ActivatedAbility.ChosenTargets"/>, and — when the chosen
///   target is a live <see cref="Spell"/> still on the stack — stamps its
///   <see cref="Spell.CannotBeCountered"/> flag. The same downstream gate
///   (<see cref="OracleSpellBinder.RemoveFromStack"/> + every counter
///   primitive) that honours the self-cast marker honours this flag, so the
///   targeted spell becomes uncounterable. CR 608.2b — legality is
///   re-checked at resolution (the spell must still be on the supplied
///   stack); an absent / non-spell target is a clean no-op.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both abilities are attached;
///   the activated ability's resolution becomes a no-op without a live
///   <see cref="Majik.Core.Stack.Stack"/> (the chosen-target legality recheck
///   is skipped). Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, Majik.Core.Stack.Stack)"/> — fully wired. The
///   activated ability's resolution re-checks that the chosen spell is still
///   on the live stack before stamping it (CR 608.2b).
/// </summary>
[CardName("Vexing Shusher")]
public static class VexingShusherFactory
{
    public const string CardName = "Vexing Shusher";
    public const string PrintedManaCost = "{R/G}{R/G}";
    public const string ActivationCost = "{R/G}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Vexing Shusher with no live stack wiring. Both abilities
    /// are attached; the activated ability's resolution is a no-op (it can't
    /// re-check the chosen spell's stack membership without a live stack).
    /// Suitable for card-shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, stack: null);

    /// <summary>
    /// Construct Vexing Shusher with an optional live stack.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="stack">Live stack. When supplied, the activated
    /// ability's resolution re-checks (CR 608.2b) that the chosen spell is
    /// still on the stack before stamping
    /// <see cref="Spell.CannotBeCountered"/>. When null the stamp is applied
    /// directly to the chosen <see cref="Spell"/> reference (the recheck is
    /// skipped) — the unit-test posture.</param>
    public static Creature Create(Player owner, Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 701.5b — "This spell can't be countered." Read at cast time by
        // SpellCastFlow to stamp Spell.CannotBeCountered on the resolving
        // Vexing Shusher spell. Marker form keeps the surface symmetric with
        // the rest of the uncounterable pipeline (Emrakul, the Aeons Torn).
        card.AddAbility(new KeywordAbility("Uncounterable", card, owner));

        // ----------------------------------------------------------------
        // {R/G}: Target spell can't be countered. CR 602 — activated
        // ability; CR 701.5b — grants the targeted spell the
        // can't-be-countered property. The effect reads the chosen target
        // off the live ResolutionContext and stamps Spell.CannotBeCountered,
        // which every counter primitive (Fx.Counter + counter templates) and
        // OracleSpellBinder.RemoveFromStack already honour.
        //
        // RE-SOURCE-SAFE (agatha-bespoke-factory-resolutioncontext-source-
        // migration): the effect captures NO source permanent — it reads its
        // chosen spell off the live ResolutionContext.ChosenTargets and
        // re-checks legality against the (game-global) stack. Nothing is
        // sourced from `card` / the exiled Vexing Shusher, so the ability is
        // marked RebindSafe and Agatha's Soul Cauldron's group-grant re-homes
        // the REAL "{R/G}: target spell can't be countered" ability onto a
        // counter-bearing bearer via ActivatedAbility.RebindTo (CR 707.2 /
        // 613.1f). The oracle-rebuild fallback cannot reconstruct this
        // grant-uncounterable shape, so RebindTo is the only sound re-home.
        // The captured `stack` is shared game state (identical for any
        // bearer), not a source capture, so it does not break re-source
        // soundness.
        // ----------------------------------------------------------------
        var grantEffect = new Effect(
            $"{CardName}: target spell can't be countered",
            ctx =>
            {
                if (ctx.ChosenTargets.Count == 0 || ctx.ChosenTargets[0].Count == 0)
                {
                    return ValueTask.CompletedTask;
                }

                // CR 608.2b — re-check legality at resolution: the target
                // must still be a spell on the stack.
                if (ctx.ChosenTargets[0][0] is not Spell spell) return ValueTask.CompletedTask;

                var liveStack = ctx.Game?.Stack ?? stack;
                if (liveStack != null && !liveStack.GetAll().Contains(spell))
                {
                    return ValueTask.CompletedTask;
                }

                spell.CannotBeCountered = true;
                return ValueTask.CompletedTask;
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivationCost) },
            effects: new IEffect[] { grantEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target spell",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            rebindSafe: true);

        card.AddAbility(ability);

        return card;
    }
}
