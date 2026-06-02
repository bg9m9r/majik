using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Curiosity (Tempest, {U}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant creature
///    Whenever enchanted creature deals damage to an opponent, you may
///    draw a card."
///
/// ## Shape source
/// Card identity (name, {U}, Enchantment — Aura, blue) is loaded from
/// <c>Majik.Core/CardData/Cards/curiosity.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The granted trigger is attached in code
/// below — the JSON ability schema expresses neither a damage-to-an-opponent
/// trigger nor an optional draw, so it is hand-rolled here (same posture as
/// <see cref="CombatResearchFactory"/>, the closest analogue).
///
/// ## Implemented (v1)
/// - Enchantment — Aura at {U}; ETB-attach plumbing via the standard
///   <see cref="AuraSpellDefinitionBuilder"/> path (<see cref="BuildSpellDefinition"/>;
///   "Enchant creature" — CR 702.5b).
/// - <b>Granted trigger (CR 603.1)</b>: "Whenever enchanted creature deals
///   damage to an opponent, you may draw a card." Unlike Combat Research's
///   trigger this fires on <b>any</b> damage — combat, spell, or ability — so
///   it binds the parent <see cref="DamageDealtEvent"/> rather than
///   <see cref="CombatDamageDealtEvent"/>. The condition matches when:
///     (1) the damage source is the currently-enchanted creature
///         (<see cref="Permanent.AttachedTo"/>, read dynamically so a
///         control-change of the creature redirects the trigger), AND
///     (2) the damage target is a <b>player</b> who is an <b>opponent</b> of
///         the enchanted creature's controller — i.e. a non-null
///         <see cref="DamageDealtEvent.TargetPlayer"/> who is not that
///         controller (CR 109.1 / "opponent" = any other player; same opponent
///         check shape <see cref="AngrathsMaraudersFactory"/> uses).
///   On resolution it draws one card for the enchanted creature's controller
///   (CR 603.3c — the granted ability's controller is the controller of the
///   permanent it's on when it triggers).
/// - <b>"You may" (CR 603.5 / 601.3e modal-of-one optional)</b>: the draw is
///   optional. The <c>mayDraw</c> closure supplied at construction time models
///   the controller's yes/no choice on resolution; it defaults to drawing
///   (the common engine-wide degrade-to-yes posture for unattended/test runs).
///   Returning false skips the draw entirely.
///
/// ## Deferred (v1 gaps)
/// - None. Curiosity is fully expressible: the only departures from Combat
///   Research are the broader (any-damage) trigger event and the optional
///   draw, both of which the engine already supports.
/// </summary>
[CardName("Curiosity")]
public static class CuriosityFactory
{
    public const string CardName = "Curiosity";

    public static readonly IReadOnlyList<string> OracleText = new[]
    {
        "Enchant creature",
        "Whenever enchanted creature deals damage to an opponent, you may draw a card.",
    };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("curiosity");

    /// <summary>
    /// Construct Curiosity with the granted damage trigger attached. The
    /// optional draw defaults to drawing (CR 603.5 — degrade-to-yes for
    /// unattended runs). Suitable for shape / dispatcher / trigger-gating tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, mayDraw: null);

    /// <summary>
    /// Construct Curiosity. <paramref name="mayDraw"/> models the controller's
    /// "you may draw a card" yes/no choice (CR 603.5); when null it defaults to
    /// drawing. The granted damage-to-an-opponent trigger is always attached to
    /// the aura's <see cref="Card.Abilities"/>.
    /// </summary>
    public static Enchantment Create(Player owner, Func<bool>? mayDraw)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Granted trigger — "Whenever enchanted creature deals damage to an
        // opponent, you may draw a card." (CR 603.1)
        //
        // Binds the parent DamageDealtEvent (combat / spell / ability — CR
        // 119.1) rather than CombatDamageDealtEvent: the printed text says
        // "deals damage", not "combat damage". Matches when the source is the
        // enchanted creature AND the target is an opponent (a non-null
        // TargetPlayer who is not the enchanted creature's controller —
        // CR 109.1).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: enchanted creature dealt damage to an opponent — you may draw a card",
            () =>
            {
                // CR 603.5 — optional draw. Default to drawing when no choice
                // closure is wired (unattended / test runs).
                if (mayDraw != null && !mayDraw()) return;

                // CR 603.3c — the granted ability's controller is the
                // controller of the enchanted creature. Fall back to the
                // aura's controller if (somehow) detached at resolution.
                var drawFor = card.AttachedTo?.Controller ?? card.Controller ?? owner;
                DrawOne(drawFor);
            });

        var damageTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<DamageDealtEvent>((e, _) =>
            {
                // (2) target is an opponent player (CR 109.1).
                var targetPlayer = e.TargetPlayer;
                if (targetPlayer == null) return false;

                var enchanted = card.AttachedTo;
                if (enchanted == null) return false;

                // (1) source is the currently-enchanted creature.
                if (!ReferenceEquals(e.SourceCard, enchanted)) return false;

                // "opponent" = any player other than the enchanted creature's
                // controller (fall back to the aura's controller).
                var controller = enchanted.Controller ?? card.Controller ?? owner;
                return !ReferenceEquals(targetPlayer, controller);
            }),
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(damageTrigger);

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Curiosity. The
    /// printed "Enchant creature" line (CR 702.5b) makes any creature a legal
    /// target. Filters the supplied battlefield to creatures. CR 303.4f — on
    /// resolve, the aura enters the battlefield already attached to the chosen
    /// target.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target creature",
            battlefield: battlefield,
            predicate: p => p != null && p.HasType(CardType.Creature));
    }

    /// <summary>
    /// Draw a single card for <paramref name="player"/> via raw library → hand
    /// zone moves. Empty-library halts the draw and stamps
    /// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> so the SBA loop
    /// notes the loss condition (CR 704.5b / 120.3). Mirrors
    /// <see cref="CombatResearchFactory"/>'s simple-draw shape.
    /// </summary>
    private static void DrawOne(Player player)
    {
        var top = player.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            player.MarkTriedToDrawFromEmptyLibrary();
            return;
        }
        player.Zones.Library.RemoveCard(top);
        player.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
