using RimTalk.Service;
using RimWorld;
using Verse;

namespace RimTalk.Patch;

/// <summary>
/// Patch to initialize/close ChromaDB when loading/unloading saves.
/// Ensures each save has its own ChromaDB collection for conversation storage.
/// </summary>
[HarmonyLib.HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
public static class GameLoadPatch
{
    [HarmonyLib.HarmonyPostfix]
    public static void Postfix()
    {
        /*if (Find.World == null)*/ return;

        // Initialize ChromaDB for this save using world name as identifier
        string saveId = Find.World.info.name ?? "default";
        ChromaService.InitializeForSave(saveId);
    }
}

/// <summary>
/// Patch to close ChromaDB when exiting to main menu.
/// </summary>
[HarmonyLib.HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
public static class SceneExitPatch
{
    [HarmonyLib.HarmonyPostfix]
    public static void Postfix()
    {
        //ChromaService.CloseSave();
    }
}
