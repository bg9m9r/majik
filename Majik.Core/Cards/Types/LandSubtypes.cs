namespace Majik.Core.Cards.Types;

/// <summary>
/// CR 205.3i — set of all land subtypes recognized by the engine. Used
/// to scope Layer 4 type-changing effects (Blood Moon, Spreading Seas,
/// Magus of the Moon, Conversion) that overwrite land types without
/// touching other subtype categories. Update this when adding new land
/// subtypes to <see cref="CardSubtype"/>.
/// </summary>
public static class LandSubtypes
{
    public static IReadOnlySet<CardSubtype> All { get; } = new HashSet<CardSubtype>
    {
        // Basic land subtypes (CR 205.3i, 305.6).
        CardSubtype.Plains,
        CardSubtype.Island,
        CardSubtype.Swamp,
        CardSubtype.Mountain,
        CardSubtype.Forest,
        CardSubtype.Wastes,

        // Nonbasic land subtypes currently enumerated in CardSubtype.
        CardSubtype.Desert,
        CardSubtype.Gate,
        CardSubtype.Lair,
        CardSubtype.Locus,
        CardSubtype.Mine,
        CardSubtype.PowerPlant,
        CardSubtype.Tower,
        CardSubtype.Urzas,
    };
}
