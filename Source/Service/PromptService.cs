using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimTalk.API;
using RimTalk.Data;
using RimTalk.Util;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Service;

/// <summary>
/// All public methods in this class are designed to be patchable with Harmony.
/// Use Prefix to replace functionality, Postfix to extend it.
/// </summary>
public static class PromptService
{
    public enum InfoLevel { Short, Normal, Full }

    public static string BuildContext(List<Pawn> pawns)
    {
        var context = new StringBuilder();
    
        for (int i = 0; i < pawns.Count; i++)
        {
            var pawn = pawns[i];
            if (pawn.IsPlayer()) continue;
            InfoLevel infoLevel = Settings.Get().Context.EnableContextOptimization 
                                  || i != 0 ? InfoLevel.Normal : InfoLevel.Full;
            var pawnContext = CreatePawnContext(pawn, infoLevel);
            pawnContext = CommonUtil.StripFormattingTags(pawnContext);

            Cache.Get(pawn).Context = pawnContext;
            context.AppendLine($"[P{i + 1}]").AppendLine(pawnContext);
        }

        return context.ToString().TrimEnd();
    }

    /// <summary>Creates the basic pawn backstory section.</summary>
    public static string CreatePawnBackstory(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var sb = new StringBuilder();
        var name = pawn.LabelShort;
        
        // Mark pawns who cannot currently speak
        name += ContextHelper.GetState(pawn);
        
        var title = pawn.story?.title == null ? "" : $"({pawn.story.title})";
        var genderAndAge = Regex.Replace(pawn.MainDesc(false), @"\(\d+\)", "").Trim();
        sb.AppendLine($"{name} {title} ({genderAndAge})");

        var role = pawn.GetRole(true);
        if (role != null)
            sb.AppendLine($"Role: {role}");

        var faction = pawn.Faction;
        if (faction != null && !pawn.IsPlayer())
            sb.AppendLine($"Faction: {faction.Name}");
        
        AppendWithHook(sb, pawn, ContextCategories.Pawn.RecentLogs, ContextBuilder.GetRecentLogsContext(pawn, infoLevel));

        // Each section applies hooks via AppendWithHook
        AppendWithHook(sb, pawn, ContextCategories.Pawn.Race, ContextBuilder.GetRaceContext(pawn, infoLevel));
        
        if (infoLevel != InfoLevel.Short && !pawn.IsVisitor() && !pawn.IsEnemy())
            AppendWithHook(sb, pawn, ContextCategories.Pawn.Genes, ContextBuilder.GetNotableGenesContext(pawn, infoLevel));
        
        AppendWithHook(sb, pawn, ContextCategories.Pawn.Ideology, ContextBuilder.GetIdeologyContext(pawn, infoLevel));

        // Stop here for invaders and visitors
        if ((pawn.IsEnemy() || pawn.IsVisitor()) && !pawn.IsQuestLodger())
            return sb.ToString();

        AppendWithHook(sb, pawn, ContextCategories.Pawn.Backstory, ContextBuilder.GetBackstoryContext(pawn, infoLevel));
        AppendWithHook(sb, pawn, ContextCategories.Pawn.Traits, ContextBuilder.GetTraitsContext(pawn, infoLevel));
        
        if (infoLevel != InfoLevel.Short)
            AppendWithHook(sb, pawn, ContextCategories.Pawn.Skills, ContextBuilder.GetSkillsContext(pawn, infoLevel));

        return sb.ToString();
    }

    /// <summary>Creates the full pawn context.</summary>
    public static string CreatePawnContext(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var sb = new StringBuilder();
        sb.Append(CreatePawnBackstory(pawn, infoLevel));

        // Each section applies hooks via AppendWithHook
        AppendWithHook(sb, pawn, ContextCategories.Pawn.Health, ContextBuilder.GetHealthContext(pawn, infoLevel));

        var personality = Cache.Get(pawn)?.Personality;
        if (personality != null)
            sb.AppendLine($"角色描述: {personality}");

        // Stop here for invaders
        if (pawn.IsEnemy())
            return sb.ToString();

        AppendWithHook(sb, pawn, ContextCategories.Pawn.Mood, ContextBuilder.GetMoodContext(pawn, infoLevel));
        AppendWithHook(sb, pawn, ContextCategories.Pawn.Thoughts, ContextBuilder.GetThoughtsContext(pawn, infoLevel));
        AppendWithHook(sb, pawn, ContextCategories.Pawn.CaptiveStatus, ContextBuilder.GetPrisonerSlaveContext(pawn, infoLevel));
        
        // Visitor activity
        if (pawn.IsVisitor())
        {
            var lord = pawn.GetLord() ?? pawn.CurJob?.lord;
            if (lord?.LordJob != null)
            {
                var cleanName = lord.LordJob.GetType().Name.Replace("LordJob_", "");
                sb.AppendLine($"Activity: {cleanName}");
            }
        }

        AppendWithHook(sb, pawn, ContextCategories.Pawn.Social, ContextBuilder.GetRelationsContext(pawn, infoLevel));
        
        if (infoLevel != InfoLevel.Short)
            AppendWithHook(sb, pawn, ContextCategories.Pawn.Equipment, ContextBuilder.GetEquipmentContext(pawn, infoLevel));

        return sb.ToString();
    }

    /// <summary>Decorates the prompt with dialogue type, time, weather, location, and environment.</summary>
    public static void DecoratePrompt(TalkRequest talkRequest, List<Pawn> pawns, string status)
    {
        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        var gameData = CommonUtil.GetInGameData();
        var mainPawn = pawns[0];
        var shortName = $"{mainPawn.LabelShort}";

        // Dialogue type
        ContextBuilder.BuildDialogueType(sb, talkRequest, pawns, shortName, mainPawn);
        sb.Append($"\n{status}");

        // Time and weather (apply environment hooks with injections)
        if (contextSettings.IncludeTime)
            sb.Append($"\n现在时间: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Time, gameData.Hour12HString)}");
        if (contextSettings.IncludeDate)
            sb.Append($"\n现在日期: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Date, gameData.DateString)}");
        if (contextSettings.IncludeSeason)
            sb.Append($"\n现在季节: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Season, gameData.SeasonString)}");
        if (contextSettings.IncludeWeather)
            sb.Append($"\n现在天气: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Weather, gameData.WeatherString)}");
        
        // Temperature
        var outdoorTemp = mainPawn.Map.mapTemperature.OutdoorTemp;
        sb.Append($"\n室外温度: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Temperature, $"{outdoorTemp:F1}°C")}");

        // Location
        ContextBuilder.BuildLocationContext(sb, contextSettings, mainPawn);

        // Environment
        ContextBuilder.BuildEnvironmentContext(sb, contextSettings, mainPawn);

        if (contextSettings.IncludeWealth)
            sb.Append($"\nWealth: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Wealth, Describer.Wealth(mainPawn.Map.wealthWatcher.WealthTotal))}");

        // Player Caravans information
        var caravansInfo = BuildPlayerCaravansInfo();
        if (!string.IsNullOrEmpty(caravansInfo))
            sb.Append($"\n{caravansInfo}");

        var otherPawnInfo = BuildOtherPawnInfo();
        if (!string.IsNullOrEmpty(otherPawnInfo))
            sb.Append($"\n{otherPawnInfo}");

        if (AIService.IsFirstInstruction())
            sb.Append($"\nin {Constant.Lang}");

        talkRequest.Prompt = sb.ToString();
    }

    /// <summary>构建所有玩家远行队的信息。</summary>
    private static string BuildPlayerCaravansInfo()
    {
        var caravans = Find.WorldObjects.Caravans?.Where(c => c.IsPlayerControlled).ToList();
        if (caravans == null || caravans.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("\nCaravans of Player Faction:{");

        for (int i = 0; i < caravans.Count; i++)
        {
            var caravan = caravans[i];
            
            // 找到身价最高的殖民者作为远行队名称
            var colonists = caravan.PawnsListForReading.Where(p => p.IsColonist).ToList();
            Pawn leader = colonists.OrderByDescending(p => p.MarketValue).FirstOrDefault();
            string caravanName = leader != null ? $"{leader.LabelShort}的远行队" : $"Caravan {i + 1}";
            
            sb.AppendLine($"{caravanName}:{{");

            // 1. 成员及其身份
            var members = new List<string>();
            foreach (var pawn in caravan.PawnsListForReading)
            {
                string identity = "未知";
                if (pawn.IsColonist)
                    identity = "殖民者";
                else if (pawn.IsPrisoner)
                    identity = "囚犯";
                else if (pawn.IsSlaveOfColony)
                    identity = "奴隶";
                else if (pawn.RaceProps.Animal)
                    identity = "动物";
                
                members.Add($"{pawn.LabelShort}（{identity}）");
            }
            sb.AppendLine($"  Members: [{string.Join(", ", members)}],");

            // 2. 目的地
            if (caravan.pather?.Destination != null && caravan.pather.Destination >= 0)
            {
                int destTile = caravan.pather.Destination;
                var destObject = Find.WorldObjects.AllWorldObjects.FirstOrDefault(wo => wo.Tile == destTile);
                string destName = destObject != null ? destObject.Label : $"Tile {destTile}";
                sb.AppendLine($"  Destination: {destName},");

                // 3. 预计抵达时间
                if (caravan.pather.Moving)
                {
                    int ticksToArrive = CaravanArrivalTimeEstimator.EstimatedTicksToArrive(caravan, true);
                    float daysToArrive = ticksToArrive / 60000f;
                    sb.AppendLine($"  Estimated arrival: {daysToArrive:F1} days,");
                }
            }
            else
            {
                sb.AppendLine("  Destination: None,");
            }

            // 4. 剩余食物天数
            try
            {
                var foodData = caravan.DaysWorthOfFood;
                if (foodData.days > 0)
                {
                    sb.AppendLine($"  Food remaining: {foodData.days:F1} days}}");
                }
                else
                {
                    sb.AppendLine("  Food remaining: Ran out}");
                }
            }
            catch
            {
                sb.AppendLine("  Food remaining: Unknown}");
            }
        }
        sb.AppendLine("}");
        return sb.ToString().TrimEnd();
    }

    private static string GetPawnRaceLabel(Pawn pawn)
    {
        return ModsConfig.BiotechActive && pawn.genes?.Xenotype != null
            ? $"{pawn.def.LabelCap.RawText} - {pawn.genes.XenotypeLabel}"
            : pawn.def.LabelCap.RawText;
    }

    private static void AddPawnToCategory(Dictionary<string, Dictionary<string, Dictionary<string, int>>> category, string factionName, string status, Pawn pawn)
    {
        if (!category.ContainsKey(factionName))
            category[factionName] = new Dictionary<string, Dictionary<string, int>>();

        if (!category[factionName].ContainsKey(status))
            category[factionName][status] = new Dictionary<string, int>();

        var raceLabel = GetPawnRaceLabel(pawn);
        if (!category[factionName][status].ContainsKey(raceLabel))
            category[factionName][status][raceLabel] = 0;
        category[factionName][status][raceLabel]++;
    }

    private static string BuildOtherPawnInfo()
    {
        var map = Find.CurrentMap;
        if (map == null)
            return string.Empty;

        // faction -> (status -> (race -> count))
        var colonists = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
        var prisoners = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
        var slaves = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
        var enemies = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
        var visitors = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();

        foreach (var pawn in map.mapPawns.AllPawnsSpawned)
        {
            if (pawn == null || (!pawn.RaceProps.Humanlike && !pawn.RaceProps.ToolUser))
                continue;

            var factionName = pawn.Faction?.Name ?? "无势力";
            var status = pawn.Downed ? "倒地无法行动" : "可行动";

            if (pawn.IsColonist && !pawn.IsPrisoner && !pawn.IsSlaveOfColony)
                AddPawnToCategory(colonists, factionName, status, pawn);
            else if (pawn.IsPrisoner)
                AddPawnToCategory(prisoners, factionName, status, pawn);
            else if (pawn.IsSlaveOfColony)
                AddPawnToCategory(slaves, factionName, status, pawn);
            else if (pawn.HostileTo(Faction.OfPlayer))
                AddPawnToCategory(enemies, factionName, status, pawn);
            else if (pawn.Faction != Faction.OfPlayer)
                AddPawnToCategory(visitors, factionName, status, pawn);
        }

        // 扫描地图上的尸体
        foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
        {
            if (thing is not Corpse corpse)
                continue;

            var pawn = corpse.InnerPawn;
            if (pawn == null || (!pawn.RaceProps.Humanlike && !pawn.RaceProps.ToolUser))
                continue;

            var factionName = pawn.Faction?.Name ?? "无势力";
            const string status = "尸体";
            if (pawn.IsColonist && !pawn.IsPrisoner && !pawn.IsSlaveOfColony)
                AddPawnToCategory(colonists, factionName, status, pawn);
            else if (pawn.IsPrisoner)
                AddPawnToCategory(prisoners, factionName, status, pawn);
            else if (pawn.IsSlaveOfColony)
                AddPawnToCategory(slaves, factionName, status, pawn);
            else if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
                AddPawnToCategory(enemies, factionName, status, pawn);
            else if (pawn.Faction != Faction.OfPlayer)
                AddPawnToCategory(visitors, factionName, status, pawn);
        }

        if (prisoners.Count == 0 && slaves.Count == 0 && enemies.Count == 0 && visitors.Count == 0 && colonists.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("当前地图角色计数:{");

        var sections = new List<(string title, Dictionary<string, Dictionary<string, Dictionary<string, int>>> data)>();
        if (colonists.Count > 0) sections.Add(("殖民者", colonists));
        if (prisoners.Count > 0) sections.Add(("囚犯", prisoners));
        if (slaves.Count > 0) sections.Add(("奴隶", slaves));
        if (enemies.Count > 0) sections.Add(("敌人", enemies));
        if (visitors.Count > 0) sections.Add(("访客及住宿访客", visitors));
        for (int i = 0; i < sections.Count; i++)
        {
            var (title, data) = sections[i];
            sb.AppendLine($"  {title}:{{");
            foreach (var factionKvp in data)
            {
                int totalCount = factionKvp.Value.Values.SelectMany(r => r.Values).Sum();
                sb.AppendLine($"    {factionKvp.Key}({totalCount}人):{{");
                foreach (var statusKvp in factionKvp.Value)
                {
                    int statusCount = statusKvp.Value.Values.Sum();
                    sb.AppendLine($"        {statusKvp.Key}({statusCount}人):{{");
                    foreach (var race in statusKvp.Value)
                        sb.AppendLine($"            {race.Key}: {race.Value}人,");
                    sb.AppendLine("        },");
                }
                sb.AppendLine("    },");
            }
            var ending = i == sections.Count - 1 ? "  }" : "  },";
            sb.AppendLine(ending);
        }

        sb.AppendLine("}");
        return sb.ToString().TrimEnd();
    }
    
    /// <summary>
    /// Appends text to StringBuilder if not empty, with optional hook application.
    /// </summary>
    private static void AppendIfNotEmpty(StringBuilder sb, string text)
    {
        if (!string.IsNullOrEmpty(text))
            sb.AppendLine(text);
    }
    
    /// <summary>
    /// Appends pawn context text with hook and injection application.
    /// </summary>
    private static void AppendWithHook(StringBuilder sb, Pawn pawn, ContextCategory category, string text)
    {
        // Render Before injections
        if (ContextHookRegistry.HasAnyInjections)
            foreach (var (_, pos, _, provider) in ContextHookRegistry.GetInjectedSectionsAt(category))
                if (pos == ContextHookRegistry.InjectPosition.Before && provider is Func<Pawn, string> p)
                    AppendIfNotEmpty(sb, p(pawn));
        
        // Apply hooks (always call to allow Override hooks on empty categories)
        var hooked = ContextHookRegistry.ApplyPawnHooks(category, pawn, text ?? "");
        AppendIfNotEmpty(sb, hooked);
        
        // Render After injections
        if (ContextHookRegistry.HasAnyInjections)
            foreach (var (_, pos, _, provider) in ContextHookRegistry.GetInjectedSectionsAt(category))
                if (pos == ContextHookRegistry.InjectPosition.After && provider is Func<Pawn, string> p)
                    AppendIfNotEmpty(sb, p(pawn));
    }
    
    /// <summary>
    /// Appends environment context text with hook and injection application.
    /// </summary>
    private static string ApplyEnvironmentWithHook(Map map, ContextCategory category, string text)
    {
        var sb = new StringBuilder();
        
        // Render Before injections
        if (ContextHookRegistry.HasAnyInjections)
            foreach (var (_, pos, _, provider) in ContextHookRegistry.GetInjectedSectionsAt(category))
                if (pos == ContextHookRegistry.InjectPosition.Before && provider is Func<Map, string> p)
                    AppendIfNotEmpty(sb, p(map));
        
        // Apply hooks
        var hooked = ContextHookRegistry.ApplyEnvironmentHooks(category, map, text ?? "");
        AppendIfNotEmpty(sb, hooked);
        
        // Render After injections
        if (ContextHookRegistry.HasAnyInjections)
            foreach (var (_, pos, _, provider) in ContextHookRegistry.GetInjectedSectionsAt(category))
                if (pos == ContextHookRegistry.InjectPosition.After && provider is Func<Map, string> p)
                    AppendIfNotEmpty(sb, p(map));
        
        return sb.ToString().TrimEnd();
    }
}
