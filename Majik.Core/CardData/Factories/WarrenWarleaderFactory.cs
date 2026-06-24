using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Warren Warleader (Bloomburrow, {2}{W}{W}).
///
/// Creature — Rabbit Knight, 4/4. Oracle text (Scryfall, verified 2026-06-24):
///   "Offspring {2} (You may pay an additional {2} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    Whenever you attack, choose one —
///    • Create a 1/1 white Rabbit creature token that's tapped and attacking.
///    • Attacking creatures you control get +1/+1 until end of turn."
///
/// The base shape (name, Creature, Rabbit + Knight subtypes, {2}{W}{W}, 4/4) is
/// materialised from the embedded JSON definition (<c>warren-warleader.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Offspring and the modal attack
/// trigger are layered on here — neither is expressible in the current JSON
/// <c>AbilityDefinition</c> schema (same posture as
/// <see cref="ManifoldMouseFactory"/> + <see cref="AdelineResplendentCatharFactory"/>).
///
/// ## Offspring {2} (CR 702.169)
///
/// Wired through the generic Offspring keyword subsystem:
/// <see cref="OffspringAdditionalCost"/> (the optional additional cast cost,
/// CR 702.169a — drains {2} and stamps <see cref="Card.WasOffspringPaid"/>) +
/// <see cref="OffspringAbility.Attach"/> (the ETB trigger, CR 702.169b — when
/// this creature enters, if its Offspring cost was paid, create a 1/1 token copy
/// of it). The caller layers <see cref="BuildOffspringCost"/> onto the cast via
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c> when the
/// caster chooses to pay; declining simply omits it.
///
/// ## "Whenever you attack, choose one —" (CR 508.1 / 700.2)
///
/// A <see cref="TriggeredAbility"/> scoped to <see cref="AttackersDeclaredEvent"/>
/// where the attacking player is Warren Warleader's controller ("Whenever you
/// attack", CR 508.1 / 109.5 — the controller-scoped attack trigger, same gate
/// as <see cref="AdelineResplendentCatharFactory"/> /
/// <see cref="MostValuableSlayerFactory"/>). The triggering
/// <see cref="Majik.Core.Combat.Combat"/> is captured off the matched event
/// (CR 603.2). On resolution the "choose one" modal pick (CR 700.2) is resolved
/// through the resolving agent (<see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseModeAsync"/>),
/// defaulting to mode 0 (the token) when no agent / game context is wired (shape
/// / direct-call tests — same default posture as
/// <see cref="ManifoldMouseFactory"/>):
/// <list type="bullet">
///   <item><b>Mode 0 — "Create a 1/1 white Rabbit creature token that's tapped
///   and attacking." (CR 508.3 / 508.4)</b>: a 1/1 white Rabbit token is created
///   via <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111.4) and spliced
///   into the in-progress combat tapped and attacking the same defender, via
///   <see cref="CombatManager.AddTappedAndAttackingToken"/> (CR 508.3 — enters
///   tapped; CR 508.4 — attacking the defending player). Because the token is
///   put onto the battlefield attacking rather than declared, it does NOT
///   re-trigger attack triggers (CR 508.3g). Same token-rider shape as Adeline /
///   Kari Zev. When no combat is live the token enters untapped (no-combat
///   fallback).</item>
///   <item><b>Mode 1 — "Attacking creatures you control get +1/+1 until end of
///   turn." (CR 613.7c Layer 7c / CR 514.2)</b>: every attacking creature the
///   controller controls (read from the captured combat) gets a
///   <see cref="PumpUntilEndOfTurnEffect"/> of +1/+1, registered on each
///   creature's own <see cref="Permanent.ActiveEffects"/> (same per-attacker pump
///   machinery as <see cref="HonoredCropCaptainFactory"/>, but +1/+1 on ALL the
///   controller's attackers — Warren Warleader included if it is attacking). Each
///   attacker without a live effects service silently no-ops.</item>
/// </list>
/// </summary>
[CardName("Warren Warleader")]
public static class WarrenWarleaderFactory
{
    public const string CardName = "Warren Warleader";
    public const string Slug = "warren-warleader";
    public const string OffspringCostText = "{2}";

    public const string ModeToken = "Create a 1/1 white Rabbit creature token that's tapped and attacking";
    public const string ModePump = "Attacking creatures you control get +1/+1 until end of turn";

    /// <summary>Rabbit token — 1/1 white.</summary>
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>+1/+1 to each attacking creature you control (mode 1).</summary>
    public const int PumpPower = 1;
    public const int PumpToughness = 1;

    /// <summary>CR 702.169 — the Offspring additional cost ({2}). Exposed so
    /// callers build the cost without hard-coding the value.</summary>
    public static ManaCost OffspringCost => ManaCost.Parse(OffspringCostText);

    /// <summary>
    /// Construct Warren Warleader with no live runtime wiring (the dispatcher /
    /// shape path). Offspring + the modal attack trigger are attached for shape
    /// observability; without a combat the token mode creates a plain battlefield
    /// token and without an agent the modal pick defaults to mode 0. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, combat: null);

    /// <summary>
    /// Construct Warren Warleader with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the Offspring ETB trigger and the
    /// modal attack trigger are registered so the centralised event pump queues
    /// them automatically in a real match. May be null — both are still attached
    /// to the card shape.</param>
    /// <param name="combat">When supplied, the mode-0 Rabbit token is spliced
    /// into the in-progress combat tapped and attacking
    /// (<see cref="CombatManager.AddTappedAndAttackingToken"/>). May be null —
    /// the token then enters the battlefield untapped (no-combat fallback).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        CombatManager? combat)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Rabbit +
        // Knight, {2}{W}{W}, 4/4). The JSON carries no abilities — Offspring and
        // the modal attack trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Offspring {2} ETB token-copy (CR 702.169b).
        OffspringAbility.Attach(card, triggers);

        // CR 702.169 — expose the keyword marker so the keyword scan surface is
        // uniform. The "{cost}" rider is carried by the OffspringAdditionalCost
        // the caller layers onto the cast.
        card.AddAbility(new KeywordAbility("Offspring", card, owner, arg: 2));

        AddModalAttackTrigger(card, owner, triggers, combat);

        return card;
    }

    /// <summary>Build the Offspring {2} additional cost for this spell. Layer it
    /// onto the cast via SpellCastFlow's <c>additionalCosts</c> when the caster
    /// chooses to pay Offspring; omit it to decline.</summary>
    public static IAdditionalCost BuildOffspringCost(ICard card) =>
        new OffspringAdditionalCost(card, OffspringCost);

    // -----------------------------------------------------------------------
    // "Whenever you attack, choose one —
    //   • Create a 1/1 white Rabbit creature token that's tapped and attacking.
    //   • Attacking creatures you control get +1/+1 until end of turn."
    // (CR 508.1 attack trigger; CR 700.2 modal choice.)
    // -----------------------------------------------------------------------
    private static void AddModalAttackTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers,
        CombatManager? combat)
    {
        // Capture the combat from the triggering event so the resolve body can
        // read the declared attackers (CR 603.2 — a triggered ability is
        // associated with the specific event that triggered it).
        Majik.Core.Combat.Combat? capturedCombat = null;

        var condition = new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
        {
            // "Whenever you attack" — only when this card's controller is the
            // attacking player (CR 508.1 / 109.5).
            if (!ReferenceEquals(e.Combat.AttackingPlayer, card.Controller ?? owner))
                return false;
            capturedCombat = e.Combat;
            return true;
        });

        var effect = new Effect(
            $"{CardName}: on attack, choose one — Rabbit token tapped & attacking, or attacking creatures you control get +1/+1",
            async ctx =>
            {
                var liveCombat = capturedCombat;
                capturedCombat = null;

                // CR 700.2 — "choose one". Resolve the modal pick through the
                // resolving agent; default to mode 0 (the token) when no agent /
                // game context is wired (shape / direct-call tests).
                var mode = 0;
                if (ctx.Agent != null && ctx.Game != null)
                {
                    var modes = new[] { ModeToken, ModePump };
                    mode = await ctx.Agent
                        .ChooseModeAsync(ctx.Game, modes, modeIntents: null, ctx.Ct)
                        .ConfigureAwait(false);
                }

                var controller = card.Controller ?? owner;
                if (mode == 1)
                {
                    ResolvePumpMode(controller, liveCombat);
                }
                else
                {
                    ResolveTokenMode(controller, combat);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    /// <summary>
    /// CR 508.3 / 508.4 — mode 0: create a 1/1 white Rabbit token spliced into
    /// the in-progress combat tapped and attacking the same defender. When no
    /// combat is live the token stays on the battlefield untapped (no-combat
    /// fallback, same as Adeline / Hero of Bladehold).
    /// </summary>
    private static void ResolveTokenMode(Player controller, CombatManager? combat)
    {
        // CR 111.4 — 1/1 white Rabbit creature token.
        var spec = new TokenFactory.TokenSpec(
            Name: "Rabbit",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Rabbit },
            Keywords: null,
            Colors: new[] { ManaColor.White });

        var token = TokenFactory.CreateOnBattlefield(spec, controller);

        // CR 508.3 — enters tapped; CR 508.4 — attacking the same defender as the
        // combat. When no combat is live the token stays untapped.
        combat?.AddTappedAndAttackingToken(token);
    }

    /// <summary>
    /// CR 613.7c (Layer 7c) / CR 514.2 — mode 1: every attacking creature the
    /// controller controls gets +1/+1 until end of turn. The attacker set is read
    /// from the captured combat (CR 603.2). Each creature pumps on its own
    /// ActiveEffects layers service; an attacker without one silently no-ops
    /// (same posture as Honored Crop-Captain's pump).
    /// </summary>
    private static void ResolvePumpMode(Player controller, Majik.Core.Combat.Combat? combat)
    {
        if (combat == null) return;

        foreach (var atk in combat.Attackers)
        {
            // CR 508 — Attacker.Creature is Permanent-typed (animated manlands
            // may attack); "attacking creatures you control" pumps the real
            // attacking CREATURES the controller controls.
            if (atk?.Creature is not Creature creature) continue;
            if (!ReferenceEquals(creature.Controller, controller)) continue;
            if (creature.ActiveEffects == null) continue;

            creature.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));
        }
    }
}
