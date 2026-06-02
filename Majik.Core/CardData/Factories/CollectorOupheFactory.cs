using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Collector Ouphe (Modern Horizons, {1}{G}).
///
/// Creature — Ouphe 2/2.
/// Oracle text:
///   "Activated abilities of artifacts can't be activated."
///
/// ## Functional reprint of Stony Silence (on a creature body)
/// Collector Ouphe imposes the identical printed static as
/// <see cref="StonySilenceFactory"/> — a symmetric, global suppression of
/// the activated abilities of every artifact on the battlefield (CR 602.5c).
/// The only difference is the chassis: an Ouphe creature instead of an
/// enchantment. The suppression itself is reused verbatim via
/// <see cref="StonySilenceStaticEffect"/> (a <see cref="Permanent"/>-sourced
/// lifecycle binder — a <see cref="Creature"/> is a <see cref="Permanent"/>).
///
/// ## CR 605.1a mana-ability exemption
/// Collector Ouphe's printed text omits Stony Silence's explicit "unless
/// they're mana abilities" clause, but the behaviour is identical: under
/// CR 605.1a the term "activated abilities" in rules / card text excludes
/// mana abilities, so {T}: Add … abilities of artifacts (Mox, Sol Ring, …)
/// are NOT suppressed by Collector Ouphe either. That exemption is enforced
/// centrally by
/// <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(Abilities.IActivatedAbility)"/>
/// before the predicate runs, and mana abilities additionally route through
/// <see cref="Majik.Core.Services.ManaAbilityActivator"/> which bypasses
/// <see cref="Majik.Core.Rules.ActionValidator"/> entirely.
///
/// ## Wiring
/// Loads <c>Majik.Core/CardData/Cards/collector-ouphe.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime creature; the
/// printed static is wired here after the build (the same pattern
/// <see cref="OrnithopterOfParadiseFactory"/> uses to attach its keyword
/// marker), because the JSON definition schema does not yet carry global
/// static-effect markers.
/// </summary>
[CardName("Collector Ouphe")]
public static class CollectorOupheFactory
{
    public const string CardName = "Collector Ouphe";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("collector-ouphe");

    /// <summary>
    /// Construct a Collector Ouphe with no live wiring. The printed static
    /// is not registered (no event bus). Suitable for card-shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct a Collector Ouphe whose printed static is wired against
    /// <paramref name="eventBus"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be
    /// null — the lifecycle will still sync once on Attach.</param>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        if (eventBus != null)
        {
            // CR 602.5c — reuse Stony Silence's global artifact-activated
            // suppression verbatim (functional reprint). A Creature is a
            // Permanent, so the Permanent-sourced binder applies unchanged.
            var lifecycle = new StonySilenceStaticEffect(source: card, eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }
}
