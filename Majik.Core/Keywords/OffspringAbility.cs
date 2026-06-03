using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.169 — Offspring. "Offspring {cost}" is a keyword on a creature card
/// representing two abilities (CR 702.169a):
///
/// <list type="number">
/// <item>A static ability of the creature spell: "You may pay an additional
/// {cost} as you cast this spell." — modelled by
/// <see cref="Majik.Core.Costs.OffspringAdditionalCost"/>, which stamps
/// <see cref="Card.WasOffspringPaid"/> when the optional cost is paid.</item>
/// <item>A triggered ability of the permanent the spell becomes (CR 702.169b):
/// "When this permanent enters, if its Offspring cost was paid, its controller
/// creates a token that's a copy of it, except it's 1/1." — built here.</item>
/// </list>
///
/// <para>This helper attaches the ETB triggered ability to a creature factory.
/// On resolution the trigger checks the cast-time <see cref="Card.WasOffspringPaid"/>
/// sentinel; if set, it mints one token that copies the creature's copiable
/// characteristics (name, subtypes, keyword abilities, colours — CR 707.2 /
/// 706.2) with power and toughness OVERRIDDEN to 1/1 (CR 702.169b — the copy is
/// 1/1 regardless of the original's printed P/T). The token enters under the
/// creature's controller. Whether or not the cost was paid, the trigger then
/// clears the sentinel (CR 400.7) so a later blink / re-cast of the same card
/// does not re-read a stale payment.</para>
///
/// <para>The token-copy reuses the same <see cref="TokenFactory.TokenSpec"/>
/// from-source snapshot the "create a token that's a copy of target creature"
/// family uses (Cackling Counterpart etc.), with the P/T fields forced to 1.
/// Like that family, only the source's printed keyword abilities are mirrored
/// onto the token (not arbitrary triggered/activated abilities) — the v1 copy
/// boundary shared with <see cref="Majik.Core.Effects.CopyEffect"/>.</para>
/// </summary>
public static class OffspringAbility
{
    /// <summary>The 1/1 power/toughness of an Offspring token copy (CR 702.169b).</summary>
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Build and attach the Offspring ETB triggered ability (CR 702.169b) to
    /// <paramref name="creature"/>. The trigger fires when the creature enters
    /// the battlefield; if its Offspring cost was paid at cast time
    /// (<see cref="Card.WasOffspringPaid"/>), the creature's controller creates
    /// a 1/1 token copy of it. Registers the trigger on
    /// <paramref name="triggers"/> when supplied so the centralised ETB event
    /// queues it automatically in a real match; otherwise the ability is
    /// attached for shape / direct-call observability.
    /// </summary>
    public static TriggeredAbility Attach(Creature creature, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var etbEffect = new Effect(
            $"{creature.Name}: Offspring — create a 1/1 token copy if its cost was paid",
            () =>
            {
                // CR 603.6b — the ETB ability fires after the creature has
                // entered; if it has already left, do nothing but still clear.
                if (creature.Zone != ZoneType.Battlefield)
                {
                    creature.ClearWasOffspringPaid();
                    return;
                }

                if (creature.WasOffspringPaid)
                {
                    CreateOneOneTokenCopy(creature);
                }

                // CR 400.7 — consume the cast-time payment so a later blink /
                // re-cast of this card doesn't re-read a stale Offspring flag.
                creature.ClearWasOffspringPaid();
            });

        var trigger = new TriggeredAbility(
            source: creature,
            controller: creature.Controller ?? creature.Owner!,
            condition: Triggers.OnEnterBattlefieldSelf(creature),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        creature.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
        return trigger;
    }

    /// <summary>
    /// CR 702.169b / CR 707.2 — mint a single 1/1 token that copies
    /// <paramref name="source"/>'s copiable characteristics, except power and
    /// toughness are 1/1. The token enters under the source's controller
    /// (CR 707.2 — a copy token's controller is the controller of the effect
    /// creating it; here the Offspring ability's controller == the source's
    /// controller). Uses the live <see cref="ZoneService"/> from the registry
    /// when available so CardMovedEvent fires (Soul Warden etc.).
    /// </summary>
    private static void CreateOneOneTokenCopy(Creature source)
    {
        var controller = source.Controller ?? source.Owner;
        if (controller == null) return;

        var keywords = source.Abilities
            .OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // CR 706.2 — copy the source's colour identity alongside its other
        // copiable values.
        var colours = CardColors.GetColors(source).ToList();

        // CR 702.169b — copy name + subtypes + keyword abilities + colour, but
        // FORCE the power/toughness to 1/1 (the defining override of Offspring;
        // the printed P/T is replaced, not copied).
        var spec = new TokenFactory.TokenSpec(
            Name: source.Name,
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: source.Subtypes.ToArray(),
            Keywords: keywords,
            Colors: colours);

        TokenFactory.CreateOnBattlefield(spec, controller, ZoneServiceRegistry.Get(controller));
    }
}
