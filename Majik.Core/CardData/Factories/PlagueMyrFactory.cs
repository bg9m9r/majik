using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Plague Myr (Mirrodin Besieged, {2}).
///
/// Artifact Creature — Phyrexian Myr 1/1. Oracle text:
///   "Infect (This creature deals damage to creatures in the form of
///    -1/-1 counters and to players in the form of poison counters.)
///    {T}: Add {C}."
///
/// ## Implemented (v1)
///
/// - 1/1 <b>Artifact Creature</b> — Phyrexian Myr at {2}. The base
///   <see cref="Creature"/> constructor only registers
///   <see cref="CardType.Creature"/>; the Artifact type is additively
///   flagged via <c>AddCardType(CardType.Artifact)</c> (mirrors
///   <see cref="MyrEnforcerFactory"/> / <see cref="PhyrexianWalkerFactory"/>'s
///   Artifact Creature shape).
/// - <b>Infect (CR 702.90)</b>: wired as a <see cref="KeywordAbility"/>
///   marker. The combat-damage replacement (-1/-1 counters to creatures
///   + poison counters to players) is deferred at the primitive level;
///   the marker surfaces the keyword so a downstream Infect primitive
///   picks Plague Myr up for free (same posture as
///   <see cref="InkmothNexusFactory"/> / <see cref="BlightedAgentFactory"/> /
///   <see cref="PhyrexianCrusaderFactory"/>).
/// - <b>{T}: Add {C}. (CR 605.1)</b>: <see cref="ManaAbility"/>
///   generating one colourless ({C} is bucketed as +1 generic in
///   <see cref="ValueObjects.ManaCost.Parse"/>). Gated by a
///   <c>!IsTapped</c> <c>canActivateCheck</c> so a duplicate activation
///   doesn't double-tap (mirrors <see cref="LlanowarElvesFactory"/>'s
///   gate). Summoning sickness (CR 302.1) is enforced by the engine at
///   activation time — not baked here.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Infect damage-replacement</b>: poison counter tracking on
///   <see cref="Player"/> + the layered combat replacement land in a
///   follow-up infrastructure PR. Plague Myr's keyword marker becomes
///   live behaviour for free at that point.
/// </summary>
[CardName("Plague Myr")]
public static class PlagueMyrFactory
{
    public const string CardName = "Plague Myr";
    public const string PrintedManaCost = "{2}";
    public const int Power = 1;
    public const int Toughness = 1;

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
                CardSubtype.Myr,
            });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups see both types (mirrors
        // Myr Enforcer / Phyrexian Walker).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.90 — Infect. Keyword marker; combat-damage replacement
        // is deferred (see class xmldoc).
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        // CR 605.1 — {T}: Add {C}. Mana ability (no stack). Tap the myr
        // when activated; gate on !IsTapped to prevent duplicate
        // activations. Mirrors Llanowar Elves' single-colour shape.
        // {C} is bucketed as +1 generic in ManaCost.Parse today (same
        // convention used by Inkmoth Nexus' {T}: Add {C}).
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{C}"),
            canActivateCheck: () => !card.IsTapped));

        return card;
    }
}
