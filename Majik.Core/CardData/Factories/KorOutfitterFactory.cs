using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kor Outfitter (Zendikar, {W}{W}).
///
/// Creature — Kor Soldier 2/2. Oracle text:
///   "When this creature enters, you may attach target Equipment you control
///    to target creature you control."
///
/// ## Shape source
/// Card identity (name, {W}{W}, 2/2, Creature — Kor Soldier) is loaded from
/// <c>Majik.Core/CardData/Cards/kor-outfitter.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The single ETB triggered ability is
/// attached in code below — the JSON ability schema does not yet express an
/// attach-Equipment-to-a-creature effect, so the attach is hand-rolled here,
/// mirroring <see cref="SigardasAidFactory"/>'s ETB-attach rider and reusing
/// the same <see cref="Permanent.AttachTo"/> plumbing as the Equip factories
/// (Colossus Hammer / Batterskull).
///
/// ## Implemented (v1)
/// - 2/2 Kor Soldier (CR 205.3m) at {W}{W}.
/// - <b>ETB trigger (CR 603.6a)</b>: "you may attach target Equipment you
///   control to target creature you control." On resolution this finds an
///   Equipment the controller controls on the battlefield and attaches it to
///   a creature the controller controls (CR 701.3a attach; CR 301.5c — a
///   creature may have any number of Equipment attached, but moving an
///   Equipment from creature to creature is exactly what an attach effect
///   does). Re-checks legality at resolution per CR 603.7c: if no controlled
///   Equipment or no controlled creature is on the battlefield the ability
///   does nothing (one of its targets would be illegal → it would be
///   countered on resolution for having no legal targets; the no-op models
///   the same observable outcome).
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: the attach always fires when both a controlled
///   Equipment and a controlled creature are available. The optional decline
///   path needs an IPlayerAgent boolean prompt the engine doesn't yet thread
///   through triggered-ability resolution — same posture as
///   <see cref="SigardasAidFactory"/>.
/// - <b>Target Equipment / target creature prompts</b>: "target Equipment you
///   control" and "target creature you control" auto-pick the first
///   controller-side Equipment and the first controller-side creature (CR
///   701.3a target prompt deferred — same v1 simplification as Sigarda's Aid
///   and Stoneforge Mystic's attach step). The picker deliberately skips
///   attaching an Equipment to a creature it is already attached to so the
///   effect makes observable progress when an unequipped creature exists.
/// </summary>
[CardName("Kor Outfitter")]
public static class KorOutfitterFactory
{
    public const string CardName = "Kor Outfitter";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("kor-outfitter");

    /// <summary>
    /// Construct Kor Outfitter with its ETB trigger attached to the card shape
    /// but NOT registered with a <see cref="TriggerManager"/>. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Kor Outfitter with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so the relevant <c>CardMovedEvent</c> places it on the stack
    /// automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, you may attach target Equipment you
        //    control to target creature you control."
        // ----------------------------------------------------------------
        var attachEffect = new Effect(
            $"{CardName}: attach an Equipment you control to a creature you control",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 603.7c — re-check legality at resolution. "target
                // Equipment you control" → first Equipment the controller
                // controls on the battlefield (CR 701.3a target prompt
                // deferred — v1 deterministic pick, same as Sigarda's Aid).
                var battlefield = controller.Zones.Battlefield.GetCards().ToList();

                var equipment = battlefield
                    .OfType<Permanent>()
                    .FirstOrDefault(p =>
                        ReferenceEquals(p.Controller, controller)
                        && p.HasSubtype(CardSubtype.Equipment));
                if (equipment == null) return; // No legal Equipment target → no-op.

                // "target creature you control" — prefer a creature the chosen
                // Equipment is NOT already attached to so the attach makes
                // observable progress; fall back to the first controlled
                // creature otherwise.
                var creatures = battlefield
                    .OfType<Creature>()
                    .Where(c => ReferenceEquals(c.Controller, controller))
                    .ToList();
                if (creatures.Count == 0) return; // No legal creature target → no-op.

                var bearer = creatures
                    .FirstOrDefault(c => !ReferenceEquals(equipment.AttachedTo, c))
                    ?? creatures[0];

                equipment.AttachTo(bearer);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { attachEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
