using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimTalk.API;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using RimTalk.Patches;

namespace RimTalk.Service;

public static class ContextBuilder
{
    private static readonly MethodInfo VisibleHediffsMethod =
        AccessTools.Method(typeof(HealthCardUtility), "VisibleHediffs");

    private static string GetSkillLevelDescription(int level)
    {
        if (level <= 0) return "一窍不通";
        if (level <= 3) return "陌生";
        if (level <= 6) return "入门级";
        if (level <= 10) return "称职";
        if (level <= 14) return "十分熟练";
        if (level <= 17) return "专家级";
        if (level <= 19) return "闻名遐迩";
        return "举世罕见";
    }
    private static string GetPassionLevelDescription(Passion passion)
    {
        return passion switch
        {
            Passion.None => "",
            Passion.Minor => "、好奇",
            Passion.Major => "、狂热",
            _ => passion.ToString() switch
            {
                "VSE_Apathy" => "、厌烦",
                "VSE_Natural" => "、恃才",
                "VSE_Critical" => "、偏长",
                _ => ""
            }
        };
    }

    public static string GetRaceContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeRace || !ModsConfig.BiotechActive || pawn.genes?.Xenotype == null)
            return null;
        return $"种族: {(ModsConfig.BiotechActive && pawn.genes?.Xenotype != null
            ? $"{pawn.def.LabelCap.RawText} - {pawn.genes.XenotypeLabel}"
            : pawn.def.LabelCap.RawText)}";
    }

    public static string GetNotableGenesContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeNotableGenes || !ModsConfig.BiotechActive ||
            pawn.genes?.GenesListForReading == null)
            return null;

        var notableGenes = pawn.genes.GenesListForReading
            .Where(g => g.def.biostatMet != 0 || g.def.biostatCpx != 0)
            .Select(g => g.def.LabelCap);

        // For Short level, limit to top 3 most impactful genes
        if (infoLevel == PromptService.InfoLevel.Short)
        {
            notableGenes = pawn.genes.GenesListForReading
                .Where(g => g.def.biostatMet != 0 || g.def.biostatCpx != 0)
                .OrderByDescending(g => Mathf.Abs(g.def.biostatMet) + g.def.biostatCpx)
                .Take(3)
                .Select(g => g.def.LabelCap);
        }

        if (notableGenes.Any())
        {
            var genesArray = string.Join(",", notableGenes);
            return $"值得注意的基因:[{genesArray}]";
        }
        return null;
    }

    public static string GetIdeologyContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeIdeology || !ModsConfig.IdeologyActive || pawn.ideo?.Ideo == null)
            return null;

        var sb = new StringBuilder();
        var ideo = pawn.ideo.Ideo;

        // For Short level, skip ideology name and only show top 3 memes
        if (infoLevel == PromptService.InfoLevel.Short)
        {
            var memes = ideo.memes?
                .Where(m => m != null)
                .Take(3)
                .Select(m => m.LabelCap.Resolve())
                .Where(label => !string.IsNullOrEmpty(label));

            if (memes?.Any() == true)
            {
                var memesArray = string.Join(",", memes);
                return $"理念:[{memesArray}]";
            }
        }
        else
        {
            var memes = ideo.memes?
                .Where(m => m != null)
                .Select(m => m.LabelCap.Resolve())
                .Where(label => !string.IsNullOrEmpty(label));

            if (memes?.Any() == true)
            {
                var memesArray = string.Join(",", memes);
                return $"文化/意识形态: {ideo.name}, 理念:[{memesArray}]";
            }
            else
            {
                return $"文化/意识形态: {ideo.name}";
            }
        }

        return null;
    }

    public static string GetBackstoryContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeBackstory)
            return null;

        var sb = new StringBuilder();

        // For Short level, only include childhood title
        if (infoLevel == PromptService.InfoLevel.Short)
        {
            if (pawn.story?.Adulthood != null)
                return $"人物背景: {pawn.story.Adulthood.TitleCapFor(pawn.gender)}";
        }
        else
        {
            if (pawn.story?.Childhood != null)
                sb.Append(ContextHelper.FormatBackstory("童年经历", pawn.story.Childhood, pawn, infoLevel));

            if (pawn.story?.Adulthood != null)
            {
                if (sb.Length > 0) sb.Append("\n");
                sb.Append(ContextHelper.FormatBackstory("成年经历", pawn.story.Adulthood, pawn, infoLevel));
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    public static string GetTraitsContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeTraits)
            return null;

        var traits = new List<string>();
        foreach (var trait in pawn.story?.traits?.TraitsSorted ?? Enumerable.Empty<Trait>())
        {
            var degreeData = GenCollection.FirstOrDefault(trait.def.degreeDatas, d => d.degree == trait.Degree);
            if (degreeData != null)
            {
                // var traitText = infoLevel == PromptService.InfoLevel.Full
                //     ? $"{degreeData.label}:{ContextHelper.Sanitize(degreeData.description, pawn)}"
                //     : degreeData.label;
                // traits.Add(traitText);
                traits.Add(degreeData.label);
            }
        }

        // For Short level, limit to top 3 traits
        if (infoLevel == PromptService.InfoLevel.Short && traits.Count > 3)
            traits = traits.Take(3).ToList();

        if (traits.Any())
        {
            return $"人格特点: [{string.Join(",", traits)}]";
        }

        return null;
    }

    public static string GetSkillsContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeSkills)
            return null;

        var skills = pawn.skills?.skills;
        if (skills?.Any() == true)
        {
            var skillsJson = string.Join(";", skills.Select(s => $"{s.def.label}: {GetSkillLevelDescription(s.Level)}{GetPassionLevelDescription(s.passion)}"));
            return $"技能等级: {{ {skillsJson} }}";
        }
        return null;
    }

    public static string GetHealthContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeHealth)
            return null;

        var hediffs = (IEnumerable<Hediff>)VisibleHediffsMethod.Invoke(null, [pawn, false]);

        // For Short level, only show top 3 most recent/severe hediffs
        if (infoLevel == PromptService.InfoLevel.Short)
        {
            hediffs = hediffs
                .OrderByDescending(h => h.Visible ? 1 : 0)
                .ThenByDescending(h => h.Severity)
                .ThenByDescending(h => h.ageTicks)
                .Take(3);
        }

        var healthList = hediffs
            .GroupBy(h => h.def)
            .Select(g => new { 
                condition = g.Key.label, 
                parts = g.Select(h => h.Part?.Label ?? "").Where(p => !string.IsNullOrEmpty(p)).ToList() 
            })
            .ToList();

        if (healthList.Any())
        {
            var healthJson = string.Join(",", healthList.Select(h => 
            {
                if (h.parts.Any())
                {
                    var partsArray = string.Join(",", h.parts);
                    return $"{h.condition}: [{partsArray}]";
                }
                return $"{h.condition}";
            }));
            return $"健康状况: {{ {healthJson} }}";
        }
        return null;
    }

    public static string GetMoodContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeMood)
            return null;

        var m = pawn.needs?.mood;
        if (m?.MoodString != null)
        {
            string mood = pawn.Downed && !pawn.IsBaby()
                ? "紧急状况: 倒地!"
                : pawn.InMentalState
                    ? $"精神状况: {pawn.MentalState?.InspectLine} (精神崩溃中)"
                    : $"心情: {m.MoodString} ({(int)(m.CurLevelPercentage * 100)}%)";
            return mood;
        }

        return null;
    }

    public static string GetThoughtsContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeThoughts)
            return null;

        var allThoughts = ContextHelper.GetThoughts(pawn);
        // For Short level, take top 5 positive and top 5 negative mood impacts
        var thoughtEntries = allThoughts
                .OrderByDescending(kvp => kvp.Value)
                .Take(5)
                .Concat(allThoughts.OrderBy(kvp => kvp.Value).Take(5))
                .Distinct();

        // Include only current mood impact value
        var thoughtsList = thoughtEntries
            .Where(kvp => kvp.Key.VisibleInNeedsTab)
            .Select(kvp =>
            {
                var t = kvp.Key;
                var summedOffset = kvp.Value;
                var summedInt = (int)Math.Round(summedOffset);
                return new { thought = CommonUtil.Sanitize(t.LabelCap), impact = summedInt };
            }).ToList();

        if (thoughtsList.Any())
        {
            var thoughtsJson = string.Join(",", thoughtsList.Select(t => 
                $"{t.thought}: {(t.impact>0?"+": "")}{t.impact}"));
            return $"想法: {{ {thoughtsJson} }}";
        }
        return null;
    }

    public static string GetPrisonerSlaveContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludePrisonerSlaveStatus || (!pawn.IsSlave && !pawn.IsPrisoner))
            return null;

        return pawn.GetPrisonerSlaveStatus();
    }

    public static string GetRelationsContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeRelations)
            return null;

        return RelationsService.GetRelationsString(pawn);
    }

    public static string GetEquipmentContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeEquipment)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("装备:{" );
        
        var hasAny = false;
        if (pawn.equipment?.Primary != null)
        {
            sb.AppendLine($"武器: {pawn.equipment.Primary.LabelCap}");
            hasAny = true;
        }

        var wornApparel = pawn.apparel?.WornApparel;
        var apparelLabels = wornApparel?.Select(a => a.LabelCap);
        if (apparelLabels?.Any() == true)
        {
            if (hasAny) sb.Append(",\n");
            var apparelArray = string.Join(",", apparelLabels);
            sb.Append($"衣着: [{apparelArray}]");
            hasAny = true;
        }

        // Check if no clothing in OnSkin and Shell layers
        if (pawn.apparel != null)
        {
            var hasOnSkinOrShell = wornApparel?.Any(a => 
                a.def.apparel.layers.Contains(ApparelLayerDefOf.OnSkin) || 
                a.def.apparel.layers.Contains(ApparelLayerDefOf.Shell)) == true;
            
            if (!hasOnSkinOrShell)
            {
                if (hasAny) sb.Append(",\n");
                sb.Append("未穿衣服");
                hasAny = true;
            }
        }

        sb.Append("}");
        
        return hasAny ? sb.ToString() : null;
    }

    public static void BuildDialogueType(StringBuilder sb, TalkRequest talkRequest, List<Pawn> pawns, string shortName, Pawn mainPawn)
    {
        if (sb == null || talkRequest == null || pawns == null || mainPawn == null)
            return;

        if (string.IsNullOrEmpty(shortName))
            shortName = mainPawn.LabelShort ?? "Unknown";

        var settings = Settings.Get();
        if (settings == null)
            return;

        if (talkRequest.TalkType.IsFromUser())
        {
            // "." shortcut: prompt is null/empty and no recipient → treat as regular conversation
            if (string.IsNullOrEmpty(talkRequest.Prompt) && talkRequest.Recipient == null)
            {
                if (pawns.Count == 1)
                    sb.Append($"生成 {shortName} 连续的多次独白发言, \"name\"字段应该全为 \"{shortName}\".");
                else
                    sb.Append($"从 {shortName} 开始与他人的多次轮流发言.");

                if (mainPawn.InMentalState)
                    sb.Append("\n(这人崩溃了,发言不讲逻辑)");
                else if (mainPawn.Downed && !mainPawn.IsBaby())
                    sb.Append("\n(这人倒地了,发言虚弱不清)");
                return;
            }

            if (pawns.Count < 2)
            {
                Log.Warning("[RimTalk] BuildDialogueType: pawns list must have at least 2 elements for user dialogue");
                return;
            }

            var speakerPawn = pawns[1];
            if (speakerPawn == null)
            {
                Log.Warning("[RimTalk] BuildDialogueType: pawns[1] is null");
                return;
            }

            var speakerLabel = speakerPawn.LabelShort ?? "Unknown";
            var speakerRole = speakerPawn.GetRole() ?? "Unknown";
            var prompt = talkRequest.Prompt ?? "";
            
            sb.Append($"{speakerLabel}({speakerRole}) said to {shortName}: '{prompt}'.");
            
            if (settings.PlayerDialogueMode == Settings.PlayerDialogueMode.Manual)
                sb.Append($"Generate dialogue starting after this. Do not generate any further lines for {speakerLabel}");
            else if (settings.PlayerDialogueMode == Settings.PlayerDialogueMode.AIDriven)
            {
                var playerName = settings.PlayerName ?? "";
                if (speakerLabel == playerName)
                {
                    var mainPawnLabel = mainPawn.LabelShort ?? "Unknown";
                    if (pawns.Count == 2)
                        sb.Append($"从 {mainPawnLabel} 开始续写多次连续的独白发言以回应 {speakerLabel}.");
                    else
                        sb.Append($"从 {mainPawnLabel} 开始续写多次轮流发言, 但 {speakerLabel} 不会参加对话.");
                }
                else
                {
                    var mainPawnLabel = mainPawn.LabelShort ?? "Unknown";
                    sb.Append($"从 {mainPawnLabel} 开始续写多次轮流发言");
                }
            }
        }
        else
        {
            if (pawns.Count == 1)
            {
                var prompt = talkRequest.Prompt ?? "";
                if (prompt.StartsWith("[群体讨论]"))
                    sb.Append($"生成 {shortName} 与其他人群体讨论以下内容的多次轮流发言.");
                else
                    sb.Append($"生成 {shortName} 连续的多次独白发言, \"name\"字段应该全为 \"{shortName}\".");
            }
            else if (mainPawn.IsInCombat() || mainPawn.GetMapRole() == MapRole.Invading)
            {
                if (talkRequest.TalkType != TalkType.Urgent && !mainPawn.InMentalState)
                    talkRequest.Prompt = null;

                talkRequest.TalkType = TalkType.Urgent;
                var mapRoleStr = mainPawn.GetMapRole().ToString()?.ToLower() ?? "unknown";
                sb.Append(mainPawn.IsSlave || mainPawn.IsPrisoner
                    ? $"从 {shortName} 开始与他人略微焦急紧张的多次轮流发言"
                    : $"从 {shortName} 开始与他人急迫的多次轮流发言 ({mapRoleStr}/command)");
            }
            else
            {
                sb.Append($"从 {shortName} 开始与他人的多次轮流发言.");
            }

            if (mainPawn.InMentalState)
                sb.Append("\n(这人崩溃了,发言不讲逻辑)");
            else if (mainPawn.Downed && !mainPawn.IsBaby())
                sb.Append("\n(这人倒地了,发言虚弱不清)");
            else if (!string.IsNullOrEmpty(talkRequest.Prompt))
                sb.Append($"\n{talkRequest.Prompt}");
        }
    }

    public static void BuildLocationContext(StringBuilder sb, ContextSettings contextSettings, Pawn mainPawn)
    {
        if (!contextSettings.IncludeLocationAndTemperature) return;
        
        var locationStatus = ContextHelper.GetPawnLocationStatus(mainPawn);
        if (string.IsNullOrEmpty(locationStatus)) return;
        
        var temperature = Mathf.RoundToInt(mainPawn.Position.GetTemperature(mainPawn.Map));
        var room = mainPawn.GetRoom();
        var roomRole = room is { PsychologicallyOutdoors: false } ? room.Role?.label ?? "Room" : "";

        var locationInfo = string.IsNullOrEmpty(roomRole)
            ? $"{locationStatus};{temperature}C"
            : $"{locationStatus};{temperature}C;{roomRole}";
        
        // Apply pawn hooks (location is now a pawn property)
        locationInfo = ContextHookRegistry.ApplyPawnHooks(
            ContextCategories.Pawn.Location, mainPawn, locationInfo);
        sb.Append($"\n对话地点: {locationInfo}");
    }

    public static void BuildEnvironmentContext(StringBuilder sb, ContextSettings contextSettings, Pawn mainPawn)
    {
        if (contextSettings.IncludeTerrain)
        {
            var terrain = mainPawn.Position.GetTerrain(mainPawn.Map);
            if (terrain != null)
            {
                var value = ContextHookRegistry.ApplyPawnHooks(
                    ContextCategories.Pawn.Terrain, mainPawn, terrain.LabelCap);
                sb.Append($"\n地面材质: {value}");
            }
        }

        if (contextSettings.IncludeBeauty)
        {
            var nearbyCells = ContextHelper.GetNearbyCells(mainPawn);
            if (nearbyCells.Count > 0)
            {
                var beautySum = nearbyCells.Sum(c => BeautyUtility.CellBeauty(c, mainPawn.Map));
                var value = ContextHookRegistry.ApplyPawnHooks(
                    ContextCategories.Pawn.Beauty, mainPawn, Describer.Beauty(beautySum / nearbyCells.Count));
                sb.Append($"\n美观度: {value}");
            }
        }

        var pawnRoom = mainPawn.GetRoom();
        if (contextSettings.IncludeCleanliness && pawnRoom is { PsychologicallyOutdoors: false })
        {
            var value = ContextHookRegistry.ApplyPawnHooks(
                ContextCategories.Pawn.Cleanliness, mainPawn,
                Describer.Cleanliness(pawnRoom.GetStat(RoomStatDefOf.Cleanliness)));
            sb.Append($"\n清洁度: {value}");
        }

        if (contextSettings.IncludeSurroundings)
        {
            var surroundingsText = ContextHelper.CollectNearbyContextText(mainPawn);
            if (!string.IsNullOrEmpty(surroundingsText))
            {
                var value = ContextHookRegistry.ApplyPawnHooks(
                    ContextCategories.Pawn.Surroundings, mainPawn, surroundingsText);
                sb.Append("\n周围物体:\n");
                sb.Append(value);
            }
        }
    }

    private static string FormatTicksAgo(int ticksAgo)
    {
        const int ticksPerHour = 2500;
        const int ticksPerDay = 60000;
        const int ticksPerQuadrum = 900000;
        const int ticksPerYear = 3600000;

        if (ticksAgo >= ticksPerYear)
            return $"{ticksAgo / ticksPerYear}年前";
        if (ticksAgo >= ticksPerQuadrum)
            return $"{ticksAgo / ticksPerQuadrum}象前";
        if (ticksAgo >= ticksPerDay)
            return $"{ticksAgo / ticksPerDay}日前";
        if (ticksAgo >= ticksPerHour)
            return $"{ticksAgo / ticksPerHour}时前";

        int seconds = ticksAgo * 3600 / ticksPerHour;
        return $"{Math.Max(1, seconds)}秒前";
    }

    public static string GetRecentLogsContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeRecentLogs) return null;
        if (infoLevel == PromptService.InfoLevel.Short) return null;

        // Time windows: 2 hours and 16 hours (2500 ticks per hour)
        int twoHoursTicks = 2500 * 2;
        int sixteenHoursTicks = 2500 * 16;
        int currentTick = Find.TickManager.TicksAbs;

        var ticksField = typeof(LogEntry).GetField("ticksAbs", BindingFlags.NonPublic | BindingFlags.Instance);
        if (ticksField == null)
        {
            Log.Warning("[RimTalk] Failed to reflect ticksAbs field from LogEntry");
            return null;
        }

        var recentItems = new List<(string content, int ticksAgo)>();

        // Process PlayLog entries (general game events)
        var playLogEntries = Find.PlayLog?.AllEntries;
        if (playLogEntries != null)
        {
            foreach (var entry in playLogEntries)
            {
                if (entry == null) continue;

                int entryTicks = (int)ticksField.GetValue(entry);
                int ticksAgo = currentTick - entryTicks;
                
                // Only include logs within 16 hours
                if (ticksAgo < 0 || ticksAgo > sixteenHoursTicks) continue;

                // Check if this entry concerns the pawn
                var concerns = entry.GetConcerns();
                if (!concerns.OfType<Pawn>().Contains(pawn)) continue;

                // Title: interaction label if available
                string title = entry.GetType().Name;
                var intDefField = entry.GetType().GetField("intDef", BindingFlags.NonPublic | BindingFlags.Instance);
                if (intDefField != null)
                {
                    var intDef = intDefField.GetValue(entry) as InteractionDef;
                    if (intDef != null) title = intDef.label;
                }

                string content;
                try
                {
                    content = entry.ToGameStringFromPOV(pawn).StripTags();
                }
                catch
                {
                    content = entry.ToString();
                }

                recentItems.Add(($"{title}: {content}", ticksAgo));
            }
        }

        // Process BattleLog entries (combat events)
        var battleLogEntries = Find.BattleLog?.Battles;
        if (battleLogEntries != null)
        {
            foreach (var battle in battleLogEntries)
            {
                if (battle?.Entries == null) continue;

                foreach (var entry in battle.Entries)
                {
                    if (entry == null) continue;

                    int entryTicks = (int)ticksField.GetValue(entry);
                    int ticksAgo = currentTick - entryTicks;
                    
                    // Only include logs within 16 hours
                    if (ticksAgo < 0 || ticksAgo > sixteenHoursTicks) continue;

                    // Check if this entry concerns the pawn
                    var concerns = entry.GetConcerns();
                    if (!concerns.OfType<Pawn>().Contains(pawn)) continue;

                    string content;
                    try
                    {
                        content = entry.ToGameStringFromPOV(pawn).StripTags();
                    }
                    catch
                    {
                        content = entry.ToString();
                    }

                    recentItems.Add(($"{content}", ticksAgo));
                }
            }
        }

        // Sort by time (most recent first)
        recentItems.Reverse();
        recentItems = recentItems.OrderByDescending(item => item.ticksAgo).ToList();

        // If infoLevel is Normal or Short, limit to 8 most recent items
        recentItems = recentItems.TakeLast(8).ToList();

        // Collect 5 most recent non-RimTalk entries (independently counted, non-duplicate)
        var existingContents = new HashSet<string>(recentItems.Select(r => r.content));
        var extraItems = new List<(string content, int ticksAgo)>();

        if (playLogEntries != null)
        {
            foreach (var entry in playLogEntries)
            {
                if (entry == null) continue;

                int entryTicks = (int)ticksField.GetValue(entry);
                int ticksAgo = currentTick - entryTicks;
                if (ticksAgo < 0) continue;

                var concerns = entry.GetConcerns();
                if (!concerns.OfType<Pawn>().Contains(pawn)) continue;

                string title = entry.GetType().Name;
                var intDefField2 = entry.GetType().GetField("intDef", BindingFlags.NonPublic | BindingFlags.Instance);
                if (intDefField2 != null)
                {
                    var intDef = intDefField2.GetValue(entry) as InteractionDef;
                    if (intDef != null) title = intDef.label;
                }

                string content;
                try { content = entry.ToGameStringFromPOV(pawn).StripTags(); }
                catch { content = entry.ToString(); }

                var fullContent = $"{title}: {content}";

                // Skip RimTalk entries
                if (fullContent.Contains("(最近的对话)")) continue;
                // Skip duplicates with existing entries
                if (existingContents.Contains(fullContent)) continue;

                extraItems.Add((fullContent, ticksAgo));
                existingContents.Add(fullContent);
            }
        }

        if (battleLogEntries != null)
        {
            foreach (var battle in battleLogEntries)
            {
                if (battle?.Entries == null) continue;

                foreach (var entry in battle.Entries)
                {
                    if (entry == null) continue;

                    int entryTicks = (int)ticksField.GetValue(entry);
                    int ticksAgo = currentTick - entryTicks;
                    if (ticksAgo < 0) continue;

                    var concerns = entry.GetConcerns();
                    if (!concerns.OfType<Pawn>().Contains(pawn)) continue;

                    string content;
                    try { content = entry.ToGameStringFromPOV(pawn).StripTags(); }
                    catch { content = entry.ToString(); }

                    if (content.Contains("(最近的对话)")) continue;
                    if (existingContents.Contains(content)) continue;

                    extraItems.Add((content, ticksAgo));
                    existingContents.Add(content);
                }
            }
        }

        // Take 5 most recent, then sort oldest-first for display
        extraItems = extraItems.OrderBy(e => e.ticksAgo).Take(5).OrderByDescending(e => e.ticksAgo).ToList();

        if (!recentItems.Any() && !extraItems.Any()) return null;

        var sb = new StringBuilder();
        sb.Append("Recent Actions (chronological order, oldest to newest):\n{");

        // Prepend 5 non-RimTalk entries with elapsed time prefix
        foreach (var item in extraItems)
        {
            sb.AppendLine().Append($"({FormatTicksAgo(item.ticksAgo)}) {item.content}");
        }

        for (int i = 0; i < recentItems.Count; i++)
        {
            var item = recentItems[i];
            string prefix;
            
            if (i == recentItems.Count-1 && item.ticksAgo <= 500)
            {
                // Most recent log
                prefix = "Currently:";
            }
            else if (item.ticksAgo <= twoHoursTicks)
            {
                // Within 2 hours
                prefix = "Recently:";
            }
            else
            {
                // Within 16 hours
                prefix = "Earlier:";
            }

            sb.AppendLine().Append(prefix + item.content);
        }
        
        sb.Append("}");

        return sb.ToString();
    }

    public static string GetMostRecentLogContext(Pawn pawn)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeRecentLogs) return null;

        int TickThreshold = 500;
        int currentTick = Find.TickManager.TicksAbs;

        var ticksField = typeof(LogEntry).GetField("ticksAbs", BindingFlags.NonPublic | BindingFlags.Instance);
        if (ticksField == null)
        {
            Log.Warning("[RimTalk] Failed to reflect ticksAbs field from LogEntry");
            return null;
        }

        Tuple<string, int> recentItem = null;

        // Process PlayLog entries (general game events)
        var playLogEntries = Find.PlayLog?.AllEntries;
        if (playLogEntries != null)
        {
            foreach (var entry in playLogEntries)
            {
                if (entry == null) continue;

                // Exclude RimTalk-produced logs
                if (entry is PlayLogEntry_RimTalkInteraction) continue;
                if (entry is PlayLogEntry_Interaction interaction && InteractionTextPatch.IsRimTalkInteraction(interaction)) continue;

                int entryTicks = (int)ticksField.GetValue(entry);
                int ticksAgo = currentTick - entryTicks;
                
                // Only include logs within 16 hours
                if (ticksAgo < 0 || ticksAgo > TickThreshold) continue;
                if (recentItem != null && ticksAgo > recentItem.Item2) continue;

                // Check if this entry concerns the pawn
                var concerns = entry.GetConcerns();
                if (!concerns.OfType<Pawn>().Contains(pawn)) continue;

                // Title: interaction label if available
                string title = entry.GetType().Name;
                var intDefField = entry.GetType().GetField("intDef", BindingFlags.NonPublic | BindingFlags.Instance);
                if (intDefField != null)
                {
                    var intDef = intDefField.GetValue(entry) as InteractionDef;
                    if (intDef != null) title = intDef.label;
                }

                string content;
                try
                {
                    content = entry.ToGameStringFromPOV(pawn).StripTags();
                }
                catch
                {
                    content = entry.ToString();
                }

                recentItem = Tuple.Create($"{title}: {content}", ticksAgo);
            }
        }

        // Process BattleLog entries (combat events)
        var battleLogEntries = Find.BattleLog?.Battles;
        if (battleLogEntries != null)
        {
            foreach (var battle in battleLogEntries)
            {
                if (battle?.Entries == null) continue;

                foreach (var entry in battle.Entries)
                {
                    if (entry == null) continue;

                    int entryTicks = (int)ticksField.GetValue(entry);
                    int ticksAgo = currentTick - entryTicks;
                    
                    if (ticksAgo < 0 || ticksAgo > TickThreshold) continue;
                    if (recentItem != null && ticksAgo > recentItem.Item2) continue;

                    // Check if this entry concerns the pawn
                    var concerns = entry.GetConcerns();
                    if (!concerns.OfType<Pawn>().Contains(pawn)) continue;

                    string content;
                    try
                    {
                        content = entry.ToGameStringFromPOV(pawn).StripTags();
                    }
                    catch
                    {
                        content = entry.ToString();
                    }

                    recentItem = Tuple.Create($"{content}", ticksAgo);
                }
            }
        }

        if (recentItem == null) return "";

        return recentItem.Item1;
    }
}