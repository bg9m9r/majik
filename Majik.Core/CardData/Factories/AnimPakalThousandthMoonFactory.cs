using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Anim Pakal, Thousandth Moon (The Lost Caverns of
/// Ixalan, {1}{R}{W}). Legendary Creature — Human Soldier, 1/2. Oracle text
/// (verified against Scryfall):
///   "Whenever you attack with one or more non-Gnome creatures, put a +1/+1
///    counter on Anim Pakal, then create X 1/1 colorless Gnome artifact
///    creature tokens that are tapped and attacking, where X is the number of
///    +1/+1 counters on Anim Pakal."
///
/// The base shape (name, Legendary supertype, Creature, Human + Soldier
/// subtypes, {1}{R}{W}, 1/2) is materialised from the embedded JSON definition
/// (<c>anim-pakal-thousandth-moon.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The attack trigger is layered on
/// here — the JSON <c>AbilityDefinition</c> schema doesn't express attack
/// triggers (same posture as <see cref="AdelineResplendentCatharFactory"/> /
/// <see cref="HanweirGarrisonFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>"Whenever you attack with one or more non-Gnome creatures, put a +1/+1
///   counter on Anim Pakal, then create X 1/1 colorless Gnome artifact creature
///   tokens that are tapped and attacking, where X is the number of +1/+1
///   counters on Anim Pakal" (CR 508.1 / 508.3g)</b> — a
///   <see cref="TriggeredAbility"/> scoped to <see cref="AttackersDeclaredEvent"/>
///   gated on (a) the attacking player being Anim Pakal's controller ("Whenever
///   you attack", CR 508.1 / 109.5) AND (b) at least one declared attacker being
///   a NON-Gnome creature ("with one or more non-Gnome creatures"). Note Anim
///   Pakal herself is a Human Soldier (non-Gnome), so attacking with her alone
///   satisfies the gate; an attack composed solely of Gnome tokens does not.
///   The whole-combat attack trigger fires once per combat (CR 508.4a — a single
///   "whenever you attack" event), NOT once per attacker.
///
///   On resolution (in printed order):
///   1. <b>"put a +1/+1 counter on Anim Pakal"</b> via
///      <see cref="CountersService.Add"/> (CR 122 / 121.6 — through the
///      replacement bus so Hardened Scales / Doubling Season can rewrite the
///      count, and publishing <see cref="CounterAddedEvent"/>). Done FIRST so
///      the counter is already present when X is read (CR 608.2 — resolve in
///      order), making the first attack mint one Gnome.
///   2. <b>"create X 1/1 colorless Gnome artifact creature tokens that are
///      tapped and attacking, where X is the number of +1/+1 counters on Anim
///      Pakal"</b> — X is read live from Anim Pakal's
///      <see cref="Permanent.Counters"/> AFTER the placement; each token is a
///      1/1 colourless <see cref="CardSubtype.Gnome"/> creature additively
///      stamped <see cref="CardType.Artifact"/> (CR 111.1 — a Gnome artifact
///      creature token, same artifact-creature-token shape as
///      <see cref="PiaAndKiranNalaarFactory"/>'s Thopters) created via
///      <see cref="TokenFactory.CreateOnBattlefield"/> and spliced into the
///      in-progress combat tapped and attacking the same defender as the combat
///      via <see cref="CombatManager.AddTappedAndAttackingToken"/> (CR 508.3 —
///      enters tapped; CR 508.4 — attacking the defending player). Because the
///      Gnomes are "put onto the battlefield attacking" rather than declared,
///      they do NOT re-trigger this attack trigger (CR 508.3g) and — being
///      Gnomes — would not satisfy the non-Gnome gate even if they did.
///
/// ## No-combat fallback
/// Same posture as <see cref="HanweirGarrisonFactory"/> /
/// <see cref="AdelineResplendentCatharFactory"/>: when <paramref name="combat"/>
/// is null (shape / dispatcher tests) the +1/+1 counter is still placed and the
/// Gnome tokens still enter the battlefield, just untapped and not attacking —
/// the "tapped and attacking" fidelity requires a live combat to splice into.
/// </summary>
[CardName("Anim Pakal, Thousandth Moon")]
public static class AnimPakalThousandthMoonFactory
{
    public const string CardName = "Anim Pakal, Thousandth Moon";
    public const string Slug = "anim-pakal-thousandth-moon";

    /// <summary>Gnome artifact creature token — 1/1 colourless.</summary>
    public const string TokenName = "Gnome";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Anim Pakal with no live runtime wiring (the dispatcher / shape
    /// path). The attack trigger is attached for shape observability but mints
    /// no tokens and places no counter (no event bus / combat). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, combat: null, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Anim Pakal with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the attack trigger is registered so
    /// an <see cref="AttackersDeclaredEvent"/> by the controller lands it on the
    /// stack automatically.</param>
    /// <param name="combat">When supplied, the Gnome tokens are spliced into the
    /// in-progress combat tapped and attacking
    /// (<see cref="CombatManager.AddTappedAndAttackingToken"/>).</param>
    /// <param name="eventBus">Event bus the +1/+1 counter placement publishes
    /// <see cref="CounterAddedEvent"/> on (CR 603.6). May be null.</param>
    /// <param name="replacements">Replacement bus the counter placement routes
    /// through so counter-doublers (Hardened Scales, Doubling Season) can rewrite
    /// the +1/+1 count (CR 616). May be null.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        CombatManager? combat,
        IEventBus? eventBus,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Human + Soldier, {1}{R}{W}, 1/2). No abilities in the JSON —
        // the attack trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        AddAttackTrigger(card, owner, triggers, combat, eventBus, replacements);

        return card;
    }

    /// <summary>
    /// CR 205.3 — "one or more non-Gnome creatures". True when at least one of
    /// the combat's declared attackers is a creature that is NOT a Gnome. Pure
    /// helper exposed for tests; mirrors the gate baked into the live trigger.
    /// </summary>
    public static bool AttackIncludesNonGnomeCreature(Combat.Combat combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        return combat.Attackers.Any(a =>
            a.Creature.HasType(CardType.Creature) &&
            !a.Creature.HasSubtype(CardSubtype.Gnome));
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "Whenever you attack with one or more non-Gnome
    // creatures, put a +1/+1 counter on Anim Pakal, then create X 1/1 colorless
    // Gnome artifact creature tokens that are tapped and attacking, where X is
    // the number of +1/+1 counters on Anim Pakal." (CR 508.1 / 508.3g.)
    // -----------------------------------------------------------------------
    private static void AddAttackTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers,
        CombatManager? combat,
        IEventBus? eventBus,
        ReplacementBus? replacements)
    {
        var condition = new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
            // "Whenever you attack" — only when Anim Pakal's controller is the
            // attacking player (CR 508.1 / 109.5) ...
            ReferenceEquals(e.Combat.AttackingPlayer, card.Controller ?? owner) &&
            // ... "with one or more non-Gnome creatures" (CR 205.3 subtype gate).
            AttackIncludesNonGnomeCreature(e.Combat));

        var effect = new Effect(
            $"{CardName}: +1/+1 counter, then create X tapped & attacking 1/1 Gnome artifact creatures",
            () => ResolveAttackTrigger(card, owner, combat, eventBus, replacements));

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

    private static void ResolveAttackTrigger(
        Creature card,
        Player owner,
        CombatManager? combat,
        IEventBus? eventBus,
        ReplacementBus? replacements)
    {
        var controller = card.Controller ?? owner;

        // CR 608.2 — resolve in printed order. (1) "put a +1/+1 counter on Anim
        // Pakal" FIRST, routed through the replacement bus (Hardened Scales etc.)
        // and publishing CounterAddedEvent (CR 122 / 603.6). On the first attack
        // this brings the count from 0 -> 1, so X reads 1 below.
        CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements, eventBus);

        // (2) "where X is the number of +1/+1 counters on Anim Pakal" — read the
        // LIVE count AFTER the placement (CR 608.2 sequential resolution).
        int x = card.Counters.Count(CounterType.PlusOnePlusOne);
        if (x <= 0) return;

        // CR 111.4 — 1/1 colourless Gnome artifact creature token.
        var spec = new TokenFactory.TokenSpec(
            Name: TokenName,
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Gnome },
            Keywords: null,
            Colors: Array.Empty<ManaColor>());

        for (int i = 0; i < x; i++)
        {
            var token = TokenFactory.CreateOnBattlefield(spec, controller);

            // CR 111.1 — additively stamp Artifact so the token reports
            // Artifact + Creature — Gnome (same artifact-creature-token shape as
            // Pia and Kiran Nalaar's Thopters).
            token.AddCardType(CardType.Artifact);

            // CR 508.3g — splice the token into the in-progress combat tapped and
            // attacking the same defender as Anim Pakal's combat. When no combat
            // is live the token stays on the battlefield untapped (no-combat
            // fallback). Gnome tokens never re-trigger this ability (CR 508.3g),
            // and being Gnomes would fail the non-Gnome gate regardless.
            combat?.AddTappedAndAttackingToken(token);
        }
    }
}
