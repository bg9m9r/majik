using System;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Beastie Beatdown (Bloomburrow, {R}{G}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-24):
///   "Choose target creature you control and target creature an opponent
///    controls.
///    Delirium — If there are four or more card types among cards in your
///    graveyard, put two +1/+1 counters on the creature you control.
///    The creature you control deals damage equal to its power to the creature
///    an opponent controls."
///
/// ## Shape
/// Base card shape (name, Sorcery type, {R}{G}, red+green colour identity) is
/// materialised from the embedded JSON definition (<c>beastie-beatdown.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The resolve-time
/// <see cref="SpellDefinition"/> (two creature targets + the
/// delirium-then-one-sided-damage body) is built on demand via
/// <see cref="BuildDefinition"/>.
///
/// ## Two targets (CR 115.1a / 601.2c)
/// Two ordered 1..1 creature target requests: the FIGHTER ("target creature you
/// control") and the VICTIM ("target creature an opponent controls"). Like the
/// fight cards (<see cref="KhalniAmbushFactory"/> /
/// <see cref="PreyUponFactory"/>), the control-scoping legality is the cast
/// flow's concern; the resolved effect re-checks only that both are still
/// creatures on the battlefield (CR 608.2b).
///
/// ## Delirium — two +1/+1 counters first (CR 702.105 + CR 122)
/// "Delirium — If there are four or more card types among cards in your
/// graveyard, put two +1/+1 counters on the creature you control." Evaluated at
/// RESOLUTION against the CONTROLLER's graveyard via
/// <see cref="UnholyHeatFactory.IsDeliriumActive"/> (which counts distinct
/// <see cref="Cards.Types.CardType"/> values, reused by Tarmogoyf / Unholy Heat
/// / Shifting Woodland). When active, two <see cref="CounterType.PlusOnePlusOne"/>
/// counters are placed on the controlled creature via
/// <see cref="Fx.PlaceCounter"/> BEFORE the damage step — so the boosted power
/// is what deals damage (CR 122.1 / the printed order: counters, then damage).
///
/// ## One-sided damage (NOT a fight — CR 120 / 701.13)
/// "The creature you control deals damage equal to its power to the creature an
/// opponent controls." Unlike Prey Upon / Khalni Ambush this is NOT a fight: only
/// the controlled creature deals damage; the opponent's creature deals none back.
/// The controlled creature's CURRENT power (read AFTER the delirium counters
/// resolve) is dealt as non-combat damage to the opponent's creature via
/// <see cref="Creature.TakeDamage"/> — honouring deathtouch / lifelink (CR 702.2b
/// / 702.15a). Lethal-damage SBAs run afterwards (CR 704).
/// </summary>
[CardName("Beastie Beatdown")]
public static class BeastieBeatdownFactory
{
    public const string CardName = "Beastie Beatdown";
    public const string Slug = "beastie-beatdown";
    public const string PrintedManaCost = "{R}{G}";

    /// <summary>The number of +1/+1 counters the delirium clause places.</summary>
    public const int DeliriumCounters = 2;

    /// <summary>
    /// Construct Beastie Beatdown as a <see cref="Sorcery"/> with owner /
    /// controller wired (base shape from the embedded JSON). Suitable for
    /// identity / shape / dispatcher tests. The resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildDefinition"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var sorcery = (Sorcery)CardDefinitionFactory.Build(definition, owner);
        return sorcery;
    }

    /// <summary>
    /// Build the resolve-time "delirium then one-sided power-damage"
    /// <see cref="SpellDefinition"/>.
    ///
    /// Two ordered 1..1 creature target requests — the controlled creature
    /// (slot 0) and the opponent's creature (slot 1). At resolution: the
    /// delirium counters are placed on the controlled creature (if active),
    /// then it deals damage equal to its (now-boosted) power to the opponent's
    /// creature.
    /// </summary>
    /// <param name="resolver">Maps each agent-supplied raw target token to the
    /// live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    public static SpellDefinition BuildDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
                new TargetRequest(
                    Description: "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            },
            EffectFactory: chosen =>
            {
                object? rawMine = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? chosen.Targets[0][0]
                    : null;
                object? rawTheirs = chosen.Targets.Count > 1 && chosen.Targets[1].Count > 0
                    ? chosen.Targets[1][0]
                    : null;

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: delirium counters + one-sided power damage",
                        () => Resolve(rawMine, rawTheirs, resolver)),
                };
            });
    }

    /// <summary>
    /// CR 702.105 (Delirium) + CR 122 (counters) + CR 120 (damage). Places two
    /// +1/+1 counters on the controlled creature when delirium is active (read
    /// off the CONTROLLER's graveyard), then deals damage equal to its CURRENT
    /// power to the opponent's creature. The counters are placed BEFORE the
    /// power is read so the boost feeds the damage. A target that is no longer a
    /// battlefield creature at resolution causes that step to no-op (CR 608.2b).
    /// </summary>
    private static void Resolve(
        object? rawMine,
        object? rawTheirs,
        Func<object, object> resolver)
    {
        if (rawMine == null || rawTheirs == null) return;

        var mineObj = resolver(rawMine);
        var theirsObj = resolver(rawTheirs);

        // CR 608.2b — the controlled creature must still be a creature on the
        // battlefield. If it is gone the whole spell does nothing (the delirium
        // counters have nowhere to go and there is no source for the damage).
        if (mineObj is not Creature mine || mine.Zone != ZoneType.Battlefield) return;

        // CR 702.105 Delirium — four or more card types among cards in the
        // CONTROLLER's graveyard. The controller is the controller of the
        // "creature you control" (the spell's caster controls both the spell
        // and that creature; CR 109.5 / 113.7a). Place two +1/+1 counters FIRST
        // so the boosted power is what deals damage.
        var controller = mine.Controller;
        if (controller != null && UnholyHeatFactory.IsDeliriumActive(controller))
        {
            // CR 122.1 — put two +1/+1 counters on the creature you control.
            Fx.PlaceCounter(mine, CounterType.PlusOnePlusOne, DeliriumCounters);
        }

        // CR 608.2b — the opponent's creature must still be a creature on the
        // battlefield to receive damage. If it is gone, the counters still
        // applied above but no damage is dealt (clean no-op for the damage half).
        if (theirsObj is not Creature theirs || theirs.Zone != ZoneType.Battlefield) return;

        // CR 120 — "The creature you control deals damage equal to its power to
        // the creature an opponent controls." One-sided (NOT a fight): only the
        // controlled creature deals damage. Power is read AFTER the delirium
        // counters resolve (CR 122 modifies P/T via the layer system, layer 7).
        var power = mine.Power;
        if (power > 0) theirs.TakeDamage(power);
    }
}
