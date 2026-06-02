using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Paradise Mantle (Fifth Dawn / Modern Horizons,
/// {0}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-02):
///   "Equipped creature has \"{T}: Add one mana of any color.\""
///   "Equip {1}"
///
/// The cheapest possible mana-fixer attachment — a {0} Equipment that turns
/// any creature it equips into a five-colour mana dork. Mechanically the
/// "grant a mana ability to the equipped creature" cousin of the keyword-
/// granting equipment cycle (<see cref="LavaspurBootsFactory"/>,
/// <see cref="SwordOfFireAndIceFactory"/>), built on the same
/// <see cref="GrantAbilityEffect"/> primitive — but here the granted ability
/// is a <see cref="Abilities.ManaAbility"/> rather than a keyword marker.
///
/// ## Why a hand-rolled C# factory (not a pure JSON CardDefinition)
///
/// The JSON <see cref="CardDefinitionFactory"/> path can express the artifact
/// shell + Equipment subtype (the shipped <c>paradise-mantle.json</c> mirrors
/// <c>lavaspur-boots.json</c>), but it has NO Equip activated ability and NO
/// attached ability-grant. So the shell is built from JSON and the Equip {1}
/// + the granted mana abilities are wired in C#, exactly as the rest of the
/// functioning equipment cycle is.
///
/// ## Implementation
///
/// - <b>Equipped creature has "{T}: Add one mana of any color"</b>
///   (CR 605.1 — mana abilities don't use the stack). "Add one mana of any
///   color" is modeled as FIVE <see cref="Abilities.ManaAbility"/> slots
///   (one per WUBRG), the same shape <see cref="BirdsOfParadiseFactory"/> /
///   <see cref="OrnithopterOfParadiseFactory"/> use — the mana picker
///   satisfies any single colour pip by selecting the matching slot. Each
///   slot is projected onto the live equipped creature by its own
///   <see cref="GrantAbilityEffect"/> (CR 613.1f, Layer 6 ability-adding):
///   the source is THIS mantle, the target selector reads
///   <see cref="Permanent.AttachedTo"/>, and the ability factory builds a
///   fresh <see cref="ManaAbility"/> whose source is the bearer (so the
///   default {T} self-tap taps the CREATURE, not the mantle — the creature
///   "has" the ability per the oracle text). Re-equipping transfers all five
///   grants; detach / LTB revoke them via the service's grant lifecycle
///   (CR 613.6e).
/// - <b>Equip {1}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the
///   Puresteel-Paladin zero-equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits service wiring
/// and produces the correct card shape only (factory-shape / dispatch tests);
/// the five mana grants are not registered against any
/// <see cref="ContinuousEffectsService"/> on that path. Use the two-arg
/// overload to wire the grants. Each grant gates on the mantle being on the
/// battlefield AND attached (the <see cref="GrantAbilityEffect"/> selector
/// returns null otherwise).
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (same gap as the rest of the
///   equipment cycle).
/// - <b>Colour prompt at activation</b> — covered by the five-slot shape: the
///   activator picks the colour by picking the matching granted ability slot,
///   no separate prompt needed (same as Springleaf Drum / Birds of Paradise).
/// </summary>
[CardName("Paradise Mantle")]
public static class ParadiseMantleFactory
{
    public const string CardName = "Paradise Mantle";
    public const string PrintedManaCost = "{0}";
    public const string EquipCost = "{1}";

    /// <summary>WUBRG — "any color" is modeled as one slot per colour.</summary>
    private static readonly string[] Colors = { "W", "U", "B", "R", "G" };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("paradise-mantle");

    /// <summary>
    /// Constructs Paradise Mantle with no live continuous-effects wiring (the
    /// shape / dispatcher path). The five granted mana abilities are NOT
    /// registered against any service.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Paradise Mantle. When <paramref name="continuousEffects"/>
    /// is supplied, the five "{T}: Add one mana of any color" grants (Layer 6,
    /// CR 613.1f) are registered against it; each gates on the mantle being on
    /// the battlefield AND attached to a battlefield permanent. When null,
    /// all five are skipped.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // CardDefinitionFactory.Build already owner/controller-sets the card.
        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // --------------------------------------------------------------
        // "Equipped creature has \"{T}: Add one mana of any color.\""
        // Five colour slots (WUBRG); each projected onto the live equipped
        // creature via its own GrantAbilityEffect (CR 613.1f, Layer 6).
        // The granted ManaAbility's source is the BEARER, so the default
        // {T} self-tap taps the creature — the creature "has" the ability
        // (CR 605.1). Selector reads AttachedTo, so re-equip transfers the
        // grant and detach / LTB revoke it (CR 613.6e).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            foreach (var pip in Colors)
            {
                var colorPip = pip;
                continuousEffects.Register(new GrantAbilityEffect(
                    source: card,
                    targetSelector: () => card.AttachedTo,
                    abilityFactory: bearer => new ParadiseMantleManaAbility(
                        source: bearer,
                        controller: bearer.Controller ?? owner,
                        colorPip: colorPip)));
            }
        }

        // --------------------------------------------------------------
        // Equip {1} — standard equipment-cycle Equip activated ability
        // (CR 702.6) via the shared primitive. Threads the Puresteel
        // zero-cost provider hook.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}

/// <summary>
/// One colour slot of the "{T}: Add one mana of any color" ability Paradise
/// Mantle grants its equipped creature. Subclasses <see cref="ManaAbility"/>
/// so the produced colour is inspectable from tests / agents (sibling shape
/// to <see cref="SpringleafDrumManaAbility"/>). The {T} self-tap of the
/// default <see cref="ManaAbility"/> ctor taps the bearer (its
/// <c>Source</c>), since the equipped creature is the one that "has" the
/// ability (CR 605.1).
/// </summary>
public sealed class ParadiseMantleManaAbility : ManaAbility
{
    /// <summary>Colour pip this slot produces (one of W / U / B / R / G).</summary>
    public string ColorPip { get; }

    internal ParadiseMantleManaAbility(Permanent source, Player controller, string colorPip)
        : base(source, controller, ManaCost.Parse(colorPip))
    {
        ColorPip = colorPip;
    }
}
