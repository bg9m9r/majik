using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hero of Bladehold (New Phyrexia, {2}{W}{W}).
///
/// Creature — Human Knight, 3/4. Oracle text:
///   "Battle cry (Whenever this creature attacks, each other attacking
///    creature gets +1/+0 until end of turn.)
///    Whenever this creature attacks, create two 1/1 white Soldier creature
///    tokens that are tapped and attacking."
///
/// ## Implemented (v1)
/// - 3/4 white Human Knight at {2}{W}{W}, owner / controller wired
///   (CR 105 — white from the {W} pips).
/// - <b>Battle cry (CR 702.92)</b> — an <see cref="Triggers.OnAttackSelf"/>
///   <see cref="TriggeredAbility"/> that, on resolution, registers a
///   <see cref="PumpUntilEndOfTurnEffect"/> of +1/+0 (CR 514.2 cleanup
///   expiry) on every OTHER attacking creature. The "each other attacking
///   creature" set is read from the supplied
///   <paramref name="attackingCreaturesSource"/> closure (same source-closure
///   shape as <see cref="GoblinPiledriverFactory"/> /
///   <see cref="GoblinRabblemasterFactory"/> — the engine doesn't yet expose
///   a global "currently attacking creatures" view from inside an effect
///   closure). The pump is registered on each target's own
///   <see cref="Creature.ActiveEffects"/> (each creature computes its P/T
///   from its own service — same posture as
///   <see cref="LegionLoyalistFactory"/>'s Battalion grant). Hero itself is
///   skipped (the keyword pumps each OTHER attacker, CR 702.92a). A
///   <see cref="KeywordAbility"/> marker is also attached so
///   <c>ICard.Abilities</c> reflects the printed Battle cry line and Scryfall
///   keyword parsing matches.
/// - <b>"Whenever this creature attacks, create two 1/1 white Soldier
///   creature tokens that are tapped and attacking" (CR 508.3g)</b> — a
///   second <see cref="Triggers.OnAttackSelf"/> trigger that creates two
///   1/1 white Soldier tokens via
///   <see cref="TokenFactory.CreateOnBattlefield"/> and splices each into
///   the in-progress combat as a token that is already tapped and attacking
///   the same defender as Hero, via
///   <see cref="CombatManager.AddTappedAndAttackingToken"/> (CR 508.3 —
///   enters tapped; CR 508.4 — attacking the same player / planeswalker).
///   Because the tokens are "put onto the battlefield attacking" rather than
///   "declared" as attackers, they do NOT re-trigger Hero's own attack
///   triggers (CR 508.3g). The two attack triggers are independent (the card
///   has two separate "whenever this attacks" abilities) — both fire off the
///   one <see cref="CreatureAttacksEvent"/>.
///
/// ## Ordering note (CR 603.3b)
/// The two attack triggers are independent objects; their controller orders
/// them on the stack. The printed Soldier tokens are created tapped and
/// attacking, but the comprehensive-rules interaction (do the new tokens get
/// the battle-cry +1/+0?) depends on order. In practice the battle-cry pump
/// reads the attacker snapshot at its own resolution; the two new Soldiers
/// are "put onto the battlefield attacking" by the token trigger and were
/// never "declared as attackers". To keep observably-correct semantics and
/// avoid double-pumping decisions that depend on stack order, the battle-cry
/// effect pumps every OTHER creature in the live attacker snapshot at its
/// resolution. Whether the Soldiers are pumped therefore tracks which trigger
/// resolves first — matching real MTG, where the active player chooses the
/// order. v1 does not force a particular order.
///
/// ## Source closure injection
/// Same shape as <see cref="GoblinPiledriverFactory"/> — when
/// <paramref name="attackingCreaturesSource"/> is null the battle-cry pump
/// is a no-op (the token rider still runs). When <paramref name="combat"/>
/// is null the tokens still enter the battlefield (untapped, not attacking)
/// — the "tapped and attacking" fidelity requires a live combat to splice
/// into (same no-combat fallback as <see cref="VoiceOfVictoryFactory"/>).
/// </summary>
[CardName("Hero of Bladehold")]
public static class HeroOfBladeholdFactory
{
    public const string CardName = "Hero of Bladehold";
    public const string PrintedManaCost = "{2}{W}{W}";
    public const int Power = 3;
    public const int Toughness = 4;

    /// <summary>Two 1/1 white Soldier tokens per attack.</summary>
    public const int TokenCount = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Hero of Bladehold with no live runtime wiring. Both attack
    /// triggers are attached to the card shape; battle cry is a no-op (no
    /// attackers source) and the token rider creates plain battlefield tokens
    /// (no combat splice). Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, combat: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct Hero of Bladehold with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, both attack triggers are
    /// registered so a <see cref="CreatureAttacksEvent"/> for Hero lands them
    /// on the stack automatically.</param>
    /// <param name="combat">When supplied, the Soldier tokens are spliced into
    /// the in-progress combat tapped and attacking
    /// (<see cref="CombatManager.AddTappedAndAttackingToken"/>).</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker creature list, called at battle-cry resolution. May be null —
    /// the battle-cry pump is then a no-op.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        CombatManager? combat,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.92 — Battle cry keyword marker so ICard.Abilities reflects
        // the printed line and Scryfall keyword parsing matches. The
        // functional pump is the trigger below.
        card.AddAbility(new KeywordAbility("Battle cry", card, owner));

        // CR 702.92a — "Whenever this creature attacks, each other attacking
        // creature gets +1/+0 until end of turn."
        var battleCryEffect = new Effect(
            $"{CardName}: Battle cry — each other attacking creature +1/+0 EOT",
            () =>
            {
                if (attackingCreaturesSource == null) return;
                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    // "each OTHER attacking creature" (CR 702.92a) — skip Hero.
                    if (ReferenceEquals(atk, card)) continue;
                    // Each creature computes P/T from its own service; without
                    // one the grant silently no-ops (same posture as
                    // LegionLoyalistFactory's Battalion grant).
                    if (atk.ActiveEffects == null) continue;
                    atk.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(atk, 1, 0));
                }
            });

        var battleCryTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { battleCryEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(battleCryTrigger);
        triggers?.RegisterTriggeredAbility(battleCryTrigger);

        // CR 508.3g — "Whenever this creature attacks, create two 1/1 white
        // Soldier creature tokens that are tapped and attacking."
        var tokenEffect = new Effect(
            $"{CardName}: create {TokenCount} tapped & attacking 1/1 white Soldiers",
            () => ResolveTokenRider(card, owner, combat));

        var tokenTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(tokenTrigger);
        triggers?.RegisterTriggeredAbility(tokenTrigger);

        return card;
    }

    /// <summary>
    /// CR 508.3g — create two 1/1 white Soldier tokens and splice each into
    /// the in-progress combat tapped and attacking the same defender as Hero.
    /// When no combat is live the tokens enter the battlefield untapped (the
    /// "tapped and attacking" fidelity requires a combat to splice into).
    /// </summary>
    private static void ResolveTokenRider(Creature source, Player owner, CombatManager? combat)
    {
        var controller = source.Controller ?? owner;

        // CR 111.4 — two 1/1 white Soldier creature tokens.
        var spec = new TokenFactory.TokenSpec(
            Name: "Soldier",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Soldier },
            Keywords: null,
            Colors: new[] { ManaColor.White });

        for (int i = 0; i < TokenCount; i++)
        {
            var token = TokenFactory.CreateOnBattlefield(spec, controller);

            // CR 508.3g — splice the token into the in-progress combat as a
            // tapped and attacking token. When no combat is live the token
            // stays on the battlefield untapped (no-combat fallback).
            combat?.AddTappedAndAttackingToken(token);
        }
    }
}
