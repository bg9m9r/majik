using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Curiosity (Odyssey, {U}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant creature"
///   "Whenever enchanted creature deals damage to a player, you may
///    draw a card."
///
/// ## Implementation
///
/// - Aura subtype + <c>{U}</c> cost; ETB-attach plumbing via the standard
///   <see cref="AuraSpellDefinitionBuilder"/> path
///   (<see cref="BuildSpellDefinition"/>).
/// - <b>Damage-to-a-player trigger (CR 603.1)</b> — wired over
///   <see cref="DamageDealtEvent"/> (the parent, NOT
///   <see cref="CombatDamageDealtEvent"/>) — printed text reads "deals
///   damage to a player" with no combat qualifier, so any damage type
///   (combat, ability, spell-via-creature like Hammer of Bogardan-style
///   pings) qualifies. Gated on:
///     1. <see cref="DamageDealtEvent.TargetPlayer"/> non-null (printed
///        "to a player"); AND
///     2. <see cref="DamageDealtEvent.SourceCard"/> referentially equal
///        to the aura's current <see cref="Permanent.AttachedTo"/>
///        (printed "enchanted creature" — the live attachment, not a
///        snapshot at cast time).
///   On resolution, draws one card for the aura's controller via the
///   raw library→hand zone move. The printed "you may" rider (CR 605.1)
///   is collapsed to deterministic "always draw" in v1 — the engine
///   has no <c>ConfirmRequest</c> primitive yet, and "draw a card" is a
///   strict positive (the only downside is decking on an empty library,
///   which the SBA path already handles via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>). Matches the
///   "may"-defer posture used by <see cref="PestermiteFactory"/>'s ETB
///   trigger.
///
/// ## Lifecycle
///
/// Single-arg <see cref="Create(Player)"/> attaches the trigger to the
/// card but doesn't register it with a <see cref="TriggerManager"/> —
/// shape-only path for factory-dispatch / identity tests. Use
/// <see cref="Create(Player, TriggerManager?)"/> for runtime wiring.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real "you may" prompt</b> — v1 always draws. Once the engine
///   grows a confirm-request primitive, the trigger's effect should
///   gate on an agent decision before resolving the draw.
/// </summary>
[CardName("Curiosity")]
public static class CuriosityFactory
{
    public const string CardName = "Curiosity";
    public const string PrintedManaCost = "{U}";

    /// <summary>Printed oracle text — source of truth for the single-noun
    /// "Enchant creature" clause routed through
    /// <see cref="AuraEnchantClauseParser"/>.</summary>
    public const string OracleText =
        "Enchant creature\n" +
        "Whenever enchanted creature deals damage to a player, you may " +
        "draw a card.";

    /// <summary>
    /// Constructs a Curiosity with the damage trigger attached to the
    /// card but not registered against any <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Constructs a Curiosity. When <paramref name="triggers"/> is
    /// supplied, the damage-to-a-player trigger is registered so a
    /// <see cref="DamageDealtEvent"/> from the enchanted creature
    /// (targeting a player) automatically queues the ability.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura });
        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Damage-to-a-player trigger — CR 603.1.
        //   "Whenever enchanted creature deals damage to a player, you
        //    may draw a card."
        // Binds to the parent DamageDealtEvent so non-combat damage
        // (ability/spell sources flowing through the enchanted creature)
        // qualifies too. Gates on TargetPlayer != null AND
        // SourceCard == card.AttachedTo at evaluation time.
        // --------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card",
            () =>
            {
                var ctrl = card.Controller ?? card.Owner ?? owner;
                DrawOne(ctrl);
            });

        var damageTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<DamageDealtEvent>((e, _) =>
            {
                if (e.TargetPlayer == null) return false;
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                return ReferenceEquals(e.SourceCard, equipped);
            }),
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(damageTrigger);
        triggers?.RegisterTriggeredAbility(damageTrigger);

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Curiosity.
    /// The printed "Enchant creature" clause routes through
    /// <see cref="AuraSpellDefinitionBuilder.ForAuraFromOracle"/>, which
    /// parses the noun and filters the battlefield to creatures.
    /// CR 303.4f — on resolve, the aura enters the battlefield already
    /// attached to the chosen creature.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAuraFromOracle(
            aura, OracleText, battlefield);
    }

    /// <summary>
    /// Draw a single card for <paramref name="player"/> via raw library →
    /// hand zone moves. Empty-library halts the draw and stamps
    /// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> so the SBA
    /// loop notes the loss condition (CR 704.5b / 120.3). Mirrors the
    /// simple-draw shape used by <see cref="SwordOfFireAndIceFactory"/>.
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
