using System.Linq;
using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Patch;
using RimTalk.Service;
using Verse;

namespace RimTalk;

public enum ButtonDisplayMode
{
    Tab,
    Toggle,
    None
}

public class RimTalk : GameComponent
{
    public RimTalk(Game game)
    {
    }

    public override void StartedNewGame()
    {
        base.StartedNewGame();
        Reset();
        
        // Initialize ChromaDB for new game
        if (Find.World != null)
        {
            string saveId = Find.World.info.name ?? "default";
           // ChromaService.InitializeForSave(saveId);
        }
    }

    public override void LoadedGame()
    {
        base.LoadedGame();
        Reset();
        
        // Initialize ChromaDB for loaded game
        if (Find.World != null)
        {
            string saveId = Find.World.info.name ?? "default";
            //ChromaService.InitializeForSave(saveId);
        }
    }

    public static void Reset(bool soft = false)
    {
        var settings = Settings.Get();
        if (settings != null)
        {
            settings.CurrentCloudConfigIndex = 0;
        }

        AIErrorHandler.ResetQuotaWarning();
        TickManagerPatch.Reset();
        AIClientFactory.Clear();
        AIService.Clear();
        TalkHistory.Clear();
        //ChromaService.UpdateBackground(Constant.Context);
        PatchThoughtHandlerGetDistinctMoodThoughtGroups.Clear();
        Cache.GetAll().ToList().ForEach(pawnState => pawnState.IgnoreAllTalkResponses());

        if (soft) return;

        Counter.Tick = 0;
        Cache.Clear();
        Stats.Reset();
        TalkRequestPool.Clear();
        ApiHistory.Clear();
    }
}