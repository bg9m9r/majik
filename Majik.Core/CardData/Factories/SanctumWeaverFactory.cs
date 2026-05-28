using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sanctum Weaver (Modern Horizons 2).
///
/// Enchantment Creature — Dryad  0/2. Mana cost: {1}{G}.
///
/// Oracle text:
///   "{T}: Add X mana of any one color, where X is the number of
///    enchantments you control."
///
/// Scryfall-confirmed: {1}{G} · Enchantment Creature — Dryad · 0/2 ·
///   "{T}: Add X mana of any one color, where X is the number of
///    enchantments you control."
///
/// ## v1 Representation
///
/// Enchantment Creatures are modeled as plain <see cref="Creature"/> in v1
/// (matching the <see cref="ReflectionOfKikiJikiFactory"/> and
/// <see cref="SythisHarvestsHandFactory"/> conventions). The
/// <see cref="CardType.Enchantment"/> card type is NOT added.
///
/// ## Mana Ability
///
/// Five parallel <see cref="ManaAbility"/> slots — one per WUBRG colour —
/// mirror the shape of <see cref="SpringleafDrumFactory"/>. The
/// controller picks the colour by activating the matching slot; no
/// separate colour-choice prompt is needed. The cost is bare {T}
/// (CR 605.1 — no additional mana component, so the CabalCoffers-style
/// inline-payment workaround does NOT apply here).
///
/// Each lambda samples <see cref="CountEnchantments"/> at activation time
/// (not factory-build time) so mid-game changes to the enchantment count
/// are reflected correctly (CR 605.1a).
///
/// ## Enchantment count (CR 109.2)
///
/// <see cref="CountEnchantments"/> counts permanents on the controller's
/// battlefield that have the <see cref="CardType.Enchantment"/> card type.
/// This includes Auras, pure Enchantments, and Enchantment Artifacts, but
/// NOT Enchantment Creatures that are modeled as plain Creatures in v1 —
/// see v1 gap below.
///
/// ## Zero-enchantment activation (CR 605.1c)
///
/// Activating a mana ability is always legal even when it produces zero
/// mana. With 0 enchantments the generator returns
/// <see cref="ManaCost.Zero"/> and the pool gains nothing. The creature
/// is still tapped.
///
/// ## Summoning Sickness (CR 302.6 / 605.3a)
///
/// Sanctum Weaver's mana ability includes {T}, so
/// <see cref="Abilities.SummoningSicknessTapGate"/> applies: the ability
/// cannot be activated the turn Sanctum Weaver enters the battlefield
/// (unless it has Haste). This is enforced automatically by
/// <see cref="ManaAbility.CanActivate"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Enchantment Creatures not counted</b>: v1 models Enchantment
///   Creatures (e.g. Sanctum Weaver itself, Reflection of Kiki-Jiki) as
///   plain <c>Creature</c> without <see cref="CardType.Enchantment"/>.
///   Therefore <see cref="CountEnchantments"/> does NOT count them.
///   Per the Magic rules (CR 205.2a) an Enchantment Creature has BOTH
///   the Creature AND Enchantment card types and would count for Sanctum
///   Weaver's ability. This gap is shared with any count-enchantments
///   ability in v1. Once v1 starts attaching <c>CardType.Enchantment</c>
///   to Enchantment Creatures, no change to this factory is needed —
///   <c>HasType(CardType.Enchantment)</c> will naturally pick them up.
/// - <b>Bot policy</b>: EV scoring for the {T} activation is inherited
///   from the generic "add mana" bot policy (<see cref="ManaAbility"/>
///   activation). Colour selection is determined by which slot the bot
///   picks; the existing WUBRG-slot pattern in
///   <see cref="SpringleafDrumFactory"/> is the precedent.
/// </summary>
[CardName("Sanctum Weaver")]
public static class SanctumWeaverFactory
{
    public const string CardName = "Sanctum Weaver";
    public const string ManaCostString = "{1}{G}";
    public const int Power = 0;
    public const int Toughness = 2;

    /// <summary>
    /// Construct a Sanctum Weaver owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // CR 205.2a — Enchantment Creature. v1 models as plain Creature
        // (no CardType.Enchantment added — see class xmldoc for v1 gap).
        var card = new Creature(
            name: CardName,
            manaCost: ManaCostString,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Dryad });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add X mana of any one color, where X is the number of
        // enchantments you control.
        //
        // Five colour-specific ManaAbility slots — WUBRG. The controller
        // picks the colour by choosing which slot to activate (same shape
        // as SpringleafDrumFactory). Each slot samples CountEnchantments
        // at activation time via a Func<ManaCost> lambda (CR 605.1a —
        // count is evaluated as the cost is paid, not when the ability
        // was declared, which is the same atomic step for mana abilities).
        //
        // canActivateCheck: source must not already be tapped (standard
        // {T} gate). Summoning sickness is handled by ManaAbility itself
        // (SummoningSicknessTapGate — CR 302.6 / 605.3a).
        // ----------------------------------------------------------------
        foreach (var pip in new[] { "W", "U", "B", "R", "G" })
        {
            card.AddAbility(BuildColorAbility(card, owner, pip));
        }

        return card;
    }

    /// <summary>
    /// Build one colour's <see cref="ManaAbility"/> slot. Exposed for
    /// tests that need to inspect or activate a specific colour.
    /// </summary>
    public static SanctumWeaverManaAbility BuildColorAbility(
        Creature source,
        Player controller,
        string colorPip)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorPip);

        return new SanctumWeaverManaAbility(source, controller, colorPip);
    }

    /// <summary>
    /// Count the number of enchantments <paramref name="controller"/>
    /// currently controls on the battlefield.
    ///
    /// Counts permanents that have <see cref="CardType.Enchantment"/> in
    /// their card types (CR 109.2 — includes Auras, pure Enchantments,
    /// Enchantment Artifacts). In v1, Enchantment Creatures are modeled
    /// as plain Creatures and are NOT counted — see class xmldoc for the
    /// v1 gap.
    ///
    /// Returns 0 for null input.
    /// </summary>
    public static int CountEnchantments(Player? controller)
    {
        if (controller == null) return 0;
        return controller.Zones.Battlefield.GetCards()
            .OfType<Card>()
            .Count(c => c.HasType(CardType.Enchantment));
    }

    /// <summary>
    /// Build a <see cref="ManaCost"/> representing <paramref name="n"/>
    /// pips of the given colour pip string (e.g. "G" → N × {G}).
    /// Returns <see cref="ManaCost.Zero"/> when <paramref name="n"/> is
    /// ≤ 0.
    /// </summary>
    internal static ManaCost BuildColorMana(string colorPip, int n)
    {
        if (n <= 0) return ManaCost.Zero;
        return ManaCost.Parse(string.Concat(Enumerable.Repeat($"{{{colorPip}}}", n)));
    }
}

/// <summary>
/// Sanctum Weaver's per-colour mana ability. Subclasses
/// <see cref="ManaAbility"/> so the colour pip and the source creature are
/// reachable from outside (agents / tests) — same shape as
/// <see cref="SpringleafDrumManaAbility"/>.
/// </summary>
public sealed class SanctumWeaverManaAbility : ManaAbility
{
    /// <summary>
    /// Colour pip this ability produces (one of W / U / B / R / G).
    /// </summary>
    public string ColorPip { get; }

    /// <summary>
    /// The Sanctum Weaver creature that is the source of this ability.
    /// </summary>
    public new Creature Source { get; }

    internal SanctumWeaverManaAbility(
        Creature source,
        Player controller,
        string colorPip)
        : base(
            source: source,
            controller: controller,
            manaGenerator: () =>
            {
                var n = SanctumWeaverFactory.CountEnchantments(controller);
                return SanctumWeaverFactory.BuildColorMana(colorPip, n);
            },
            canActivateCheck: () => !source.IsTapped)
    {
        Source = source;
        ColorPip = colorPip;
    }
}
