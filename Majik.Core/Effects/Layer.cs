namespace Majik.Core.Effects;

/// <summary>
/// CR 613 — order in which continuous effects apply. Lower number = earlier.
/// MVP supports layers 6 and 7c; other layers exist as enum values so
/// effect authors can register against them once the service is expanded.
/// </summary>
public enum Layer
{
    /// <summary>1 — copy effects.</summary>
    Copy = 1,
    /// <summary>2 — control-changing effects.</summary>
    Control = 2,
    /// <summary>3 — text-changing effects.</summary>
    Text = 3,
    /// <summary>4 — type-changing effects.</summary>
    Type = 4,
    /// <summary>5 — color-changing effects.</summary>
    Color = 5,
    /// <summary>6 — ability-adding/removing effects.</summary>
    Abilities = 6,
    /// <summary>7a — characteristic-defining P/T.</summary>
    PT_Cda = 71,
    /// <summary>7b — "becomes a N/N" sets base P/T.</summary>
    PT_SetBase = 72,
    /// <summary>7c — P/T modifications (+N/+N).</summary>
    PT_Modify = 73,
    /// <summary>7d — P/T switching.</summary>
    PT_Switch = 74,
}
