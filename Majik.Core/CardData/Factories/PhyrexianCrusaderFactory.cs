using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phyrexian Crusader (Mirrodin Besieged, {1}{B}{B}).
///
/// Creature — Phyrexian Knight 2/2. Oracle text:
///   "First strike.
///    Protection from red and from white.
///    Infect (This creature deals damage to creatures in the form of
///    -1/-1 counters and to players in the form of poison counters.)"
///
/// ## Implemented (v1)
///
/// - 2/2 <see cref="Creature"/> at {1}{B}{B} with subtypes Phyrexian,
///   Knight.
/// - <b>First strike (CR 702.7)</b>: <see cref="KeywordAbility"/>
///   marker "First strike". Combat damage assignment for first-strike
///   creatures is read by the combat system through
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/>
///   (same wiring as <see cref="ThaliaGuardianOfThrabenFactory"/> /
///   <see cref="BorosReckonerFactory"/>).
/// - <b>Protection from red and from white (CR 702.16)</b>: two
///   separate <see cref="ProtectionAbility"/> instances (quality
///   "red" and quality "white"). <see cref="Majik.Core.Rules.Protection"/>
///   helpers read both colours via
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/>
///   (same shape as <see cref="SwordOfFireAndIceFactory"/>'s "red and
///   blue" pair — two independent qualities, not a single combined
///   string).
/// - <b>Infect (CR 702.90)</b>: <see cref="KeywordAbility"/> marker
///   "Infect". The combat-damage replacement is deferred at the
///   primitive level; the marker surfaces the keyword so a downstream
///   Infect primitive picks Phyrexian Crusader up without re-touching
///   the factory (same posture as <see cref="InkmothNexusFactory"/> /
///   <see cref="BlightedAgentFactory"/>).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Infect damage-replacement</b>: poison counter tracking on
///   <see cref="Player"/> + the layered combat replacement land in a
///   follow-up infrastructure PR. Phyrexian Crusader's keyword marker
///   becomes live behaviour for free at that point.
/// </summary>
[CardName("Phyrexian Crusader")]
public static class PhyrexianCrusaderFactory
{
    public const string CardName = "Phyrexian Crusader";
    public const string PrintedManaCost = "{1}{B}{B}";
    public const int Power = 2;
    public const int Toughness = 2;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[]
            {
                CardSubtype.Phyrexian,
                CardSubtype.Knight,
            });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.7 — First strike. Marker; the combat first-strike
        // damage step reads this via CombatAbilities.HasFirstStrike.
        card.AddAbility(new KeywordAbility("First strike", card, owner));

        // CR 702.16 — Protection from red and from white. Two separate
        // ProtectionAbility instances (one quality each) — mirrors the
        // Sword of Fire and Ice "red + blue" pair. Rules.Protection
        // reads each quality independently; combat / damage / target /
        // attach gates consult HasProtectionFromColor per colour.
        card.AddAbility(new ProtectionAbility("red"));
        card.AddAbility(new ProtectionAbility("white"));

        // CR 702.90 — Infect. Keyword marker; combat-damage replacement
        // is deferred (see class xmldoc).
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        return card;
    }
}
