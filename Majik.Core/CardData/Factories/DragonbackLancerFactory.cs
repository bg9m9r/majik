using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dragonback Lancer (Tarkir: Dragonstorm, {3}{W}).
///
/// Creature — Human Soldier, 3/3. Oracle text (verified against Scryfall):
///   "Flying
///    Mobilize 1 (Whenever this creature attacks, create a tapped and attacking
///    1/1 red Warrior creature token. Sacrifice it at the beginning of the next
///    end step.)"
///
/// The base shape (name, Creature, Human Soldier subtypes, {3}{W}, 3/3, and the
/// printed <b>Flying</b> keyword line — CR 702.9) is materialised from the
/// embedded JSON definition (<c>dragonback-lancer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Flying is carried declaratively as
/// a <c>keywords</c> entry in the JSON (stamped as a
/// <see cref="KeywordAbility"/> marker), so the combat subsystem reads it
/// without bespoke wiring.
///
/// ## Implemented (v1)
/// - 3/3 white Human Soldier at {3}{W} with Flying, owner / controller wired
///   (CR 105 — white from the {W} pip).
/// - <b>Mobilize 1 (CR 702.170)</b> — "Whenever this creature attacks, create a
///   tapped and attacking 1/1 red Warrior creature token. Sacrifice it at the
///   beginning of the next end step." Delegated to the shared reusable mechanic
///   <see cref="Majik.Core.Keywords.MobilizeHelper.AttachTo"/> (the same rail
///   <see cref="VoiceOfVictoryFactory"/> uses, with N = 1): an
///   <see cref="Triggers.OnAttackSelf"/> <see cref="TriggeredAbility"/> that,
///   on resolution, mints one 1/1 red Warrior token, splices it into the live
///   combat tapped-and-attacking
///   (<see cref="CombatManager.AddTappedAndAttackingToken"/> — CR 508.3 enters
///   tapped, CR 508.4 attacking the same defender), and registers a delayed
///   end-step sacrifice (CR 603.7 / 500.4 / 701.16). Because the token is "put
///   onto the battlefield attacking" rather than "declared" (CR 508.3g), it
///   does NOT re-trigger Mobilize or other "whenever a creature attacks"
///   abilities.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The Mobilize trigger is attached
///   to the card shape; its resolution mints plain battlefield tokens (no combat
///   splice, no delayed sacrifice — no services supplied). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, CombatManager?)"/> — fully wired:
///   the Mobilize trigger is registered against the <see cref="TriggerManager"/>
///   and the token is spliced into the supplied <see cref="CombatManager"/>'s
///   in-progress combat, with the end-step sacrifice registered.
/// </summary>
[CardName("Dragonback Lancer")]
public static class DragonbackLancerFactory
{
    public const string CardName = "Dragonback Lancer";
    public const string Slug = "dragonback-lancer";

    /// <summary>Mobilize 1 — one 1/1 red Warrior token per attack.</summary>
    public const int MobilizeCount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Dragonback Lancer with no live runtime wiring. The Mobilize
    /// trigger is attached to the card shape; its resolution creates a plain
    /// battlefield token (no combat splice, no delayed sacrifice). Suitable for
    /// dispatcher / shape tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, combat: null);

    /// <summary>
    /// Construct Dragonback Lancer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the Mobilize attack trigger is
    /// registered so a <see cref="CreatureAttacksEvent"/> for Dragonback Lancer
    /// lands it on the stack automatically, and the delayed end-step sacrifice
    /// is registered.</param>
    /// <param name="combat">When supplied, the Mobilize token is spliced into
    /// the in-progress combat tapped and attacking
    /// (<see cref="CombatManager.AddTappedAndAttackingToken"/>).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        CombatManager? combat)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human
        // Soldier, {3}{W}, 3/3, Flying). Mobilize is layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------------
        // Mobilize 1 (CR 702.170): "Whenever this creature attacks, create a
        // tapped and attacking 1/1 red Warrior creature token. Sacrifice it at
        // the beginning of the next end step." Delegated to the shared reusable
        // mechanic in Majik.Core/Keywords/MobilizeHelper.cs.
        // --------------------------------------------------------------------
        Majik.Core.Keywords.MobilizeHelper.AttachTo(card, MobilizeCount, triggers, combat);

        return card;
    }
}
