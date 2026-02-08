using System;
using System.Reflection;
using Verse;

namespace RimTalk.Compatibility;

/// <summary>
/// Provides reflection-based compatibility with the Hospitality mod.
/// Detects whether a pawn is a Hospitality guest (住宿访客) without a hard dependency.
/// </summary>
public static class HospitalityCompatibility
{
    private static bool _initialized;
    private static bool _hospitalityActive;

    // Hospitality.Utilities.GuestUtility.IsGuest(Pawn)
    private static MethodInfo _isGuestMethod;

    private static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.FullName.StartsWith("Hospitality,")) continue;

                var guestUtilityType = assembly.GetType("Hospitality.Utilities.GuestUtility");
                if (guestUtilityType == null) continue;

                _isGuestMethod = guestUtilityType.GetMethod("IsGuest",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Pawn) },
                    null);

                if (_isGuestMethod != null)
                {
                    _hospitalityActive = true;
                }

                break;
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Returns true if the Hospitality mod is loaded and active.
    /// </summary>
    public static bool IsHospitalityActive
    {
        get
        {
            Initialize();
            return _hospitalityActive;
        }
    }

    /// <summary>
    /// Checks whether the given pawn is a Hospitality guest (住宿访客).
    /// Returns false if Hospitality is not loaded or the pawn is not a guest.
    /// </summary>
    public static bool IsHospitalityGuest(Pawn pawn)
    {
        Initialize();

        if (!_hospitalityActive || _isGuestMethod == null || pawn == null)
            return false;

        try
        {
            return (bool)_isGuestMethod.Invoke(null, new object[] { pawn });
        }
        catch
        {
            return false;
        }
    }
}
