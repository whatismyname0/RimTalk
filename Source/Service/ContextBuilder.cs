using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
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
    private static readonly MethodInfo VisibleHediffsMethod = AccessTools.Method(typeof(HealthCardUtility), "VisibleHediffs");

    public static string GetRaceContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeRace || !ModsConfig.BiotechActive || pawn.genes?.Xenotype == null)
            return null;
        return $"Race: {pawn.genes.XenotypeLabel}";
    }

    public static string GetNotableGenesContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeNotableGenes || !ModsConfig.BiotechActive || pawn.genes?.GenesListForReading == null)
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
            return $"Notable Genes: {string.Join(", ", notableGenes)}";
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
                return $"Memes: {string.Join(", ", memes)}";
        }
        else
        {
            sb.Append($"Ideology: {ideo.name}");

            var memes = ideo.memes?
                .Where(m => m != null)
                .Select(m => m.LabelCap.Resolve())
                .Where(label => !string.IsNullOrEmpty(label));

            if (memes?.Any() == true)
                sb.Append($"\nMemes: {string.Join(", ", memes)}");

            return sb.ToString();
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
                return $"Background: {pawn.story.Adulthood.TitleCapFor(pawn.gender)}";
        }
        else
        {
            if (pawn.story?.Childhood != null)
                sb.Append(ContextHelper.FormatBackstory("Childhood", pawn.story.Childhood, pawn, infoLevel));

            if (pawn.story?.Adulthood != null)
            {
                if (sb.Length > 0) sb.Append("\n");
                sb.Append(ContextHelper.FormatBackstory("Adulthood", pawn.story.Adulthood, pawn, infoLevel));
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
            var separator = infoLevel == PromptService.InfoLevel.Full ? "\n" : ",";
            return $"Traits: {string.Join(separator, traits)}";
        }
        return null;
    }

    public static string GetSkillsContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeSkills)
            return null;

        var skills = pawn.skills?.skills?.Select(s => $"{s.def.label}: {s.Level}");
        if (skills?.Any() == true)
            return $"Skills: {string.Join(", ", skills)}";
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

        var healthInfo = string.Join(",", hediffs
            .GroupBy(h => h.def)
            .Select(g => $"{g.Key.label}({string.Join(",", g.Select(h => h.Part?.Label ?? ""))})"));

        if (!string.IsNullOrEmpty(healthInfo))
            return $"Health: {healthInfo}";
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
                ? "Critical: Downed (in pain/distress)"
                : pawn.InMentalState
                    ? $"Mood: {pawn.MentalState?.InspectLine} (in mental break)"
                    : $"Mood: {m.MoodString} ({(int)(m.CurLevelPercentage * 100)}%)";
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
        // For Short level, only include latest 3 thoughts
        var thoughtEntries = infoLevel == PromptService.InfoLevel.Short
            ? allThoughts.Take(3)
            : allThoughts;

        // Include cachedMoodOffsetOfGroup (if available) along with summed mood offsets
        var thoughtStrings = thoughtEntries.Select(kvp =>
        {
            var t = kvp.Key;
            var summedOffset = kvp.Value;
            var summedInt = (int)Math.Round(summedOffset);
            var cachedField = t.GetType().GetField("cachedMoodOffsetOfGroup", BindingFlags.Public | BindingFlags.Instance);
            if (cachedField != null)
            {
                var cachedVal = (float)cachedField.GetValue(t);
                var cachedInt = (int)Math.Round(cachedVal);
                return $"{ContextHelper.Sanitize(t.LabelCap)}({summedInt:+0;-0;0}/{cachedInt:+0;-0;0})";
            }
            return $"{ContextHelper.Sanitize(t.LabelCap)}({summedInt:+0;-0;0})";
        });

        if (thoughtStrings.Any())
            return $"Memory: {string.Join(", ", thoughtStrings)}";
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

        var equipment = new List<string>();
        if (pawn.equipment?.Primary != null)
            equipment.Add($"Weapon: {pawn.equipment.Primary.LabelCap}");

        var apparelLabels = pawn.apparel?.WornApparel?.Select(a => a.LabelCap);
        if (apparelLabels?.Any() == true)
            equipment.Add($"Apparel: {string.Join(", ", apparelLabels)}");

        if (equipment.Any())
            return $"Equipment: {string.Join(", ", equipment)}";
        return null;
    }

    public static void BuildDialogueType(StringBuilder sb, TalkRequest talkRequest, List<Pawn> pawns, string shortName, Pawn mainPawn)
    {
        if (talkRequest.TalkType == TalkType.User)
        {
            sb.Append($"{pawns[1].LabelShort}({pawns[1].GetRole()}) said to '{shortName}: {talkRequest.Prompt}'.");
            if (Settings.Get().PlayerDialogueMode == Settings.PlayerDialogueMode.Manual)
                sb.Append($"Generate dialogue starting after this. Do not generate any further lines for {pawns[1].LabelShort}");
            else if (Settings.Get().PlayerDialogueMode == Settings.PlayerDialogueMode.AIDriven)
                if (pawns[1].LabelShort == Settings.Get().PlayerName)
                    sb.Append($"从 {mainPawn.LabelShort} 开始续写对话, 但 {pawns[1].LabelShort} 不会参加对话.");
                else
                    sb.Append($"从 {mainPawn.LabelShort} 开始续写对话");
        }
        else
        {
            if (pawns.Count == 1)
            {
                sb.Append($"生成 {shortName} 连续的多句独白.");
            }
            else if (mainPawn.IsInCombat() || mainPawn.GetMapRole() == MapRole.Invading)
            {
                if (talkRequest.TalkType != TalkType.Urgent && !mainPawn.InMentalState)
                    talkRequest.Prompt = null;

                talkRequest.TalkType = TalkType.Urgent;
                sb.Append(mainPawn.IsSlave || mainPawn.IsPrisoner
                    ? $"生成从 {shortName} 开始的略微焦急紧张的对话"
                    : $"生成从 {shortName} 开始的急迫的对话 ({mainPawn.GetMapRole().ToString().ToLower()}/command)");
            }
            else
            {
                sb.Append($"生成从 {shortName} 开始的轮流对话.");
            }

            if (mainPawn.InMentalState)
                sb.Append("\n(这人崩溃了,发言不讲逻辑)");
            else if (mainPawn.Downed && !mainPawn.IsBaby())
                sb.Append("\n(这人倒地了,发言虚弱不清)");
            else
                sb.Append($"\n{talkRequest.Prompt}");
        }
    }

    public static void BuildLocationContext(StringBuilder sb, ContextSettings contextSettings, Pawn mainPawn)
    {
        if (contextSettings.IncludeLocationAndTemperature)
        {
            var locationStatus = ContextHelper.GetPawnLocationStatus(mainPawn);
            if (!string.IsNullOrEmpty(locationStatus))
            {
                var temperature = Mathf.RoundToInt(mainPawn.Position.GetTemperature(mainPawn.Map));
                var room = mainPawn.GetRoom();
                var roomRole = room is { PsychologicallyOutdoors: false } ? room.Role?.label ?? "Room" : "";

                sb.Append(string.IsNullOrEmpty(roomRole)
                    ? $"\nLocation: {locationStatus};{temperature}C"
                    : $"\nLocation: {locationStatus};{temperature}C;{roomRole}");
            }
        }
    }

    public static void BuildEnvironmentContext(StringBuilder sb, ContextSettings contextSettings, Pawn mainPawn)
    {
        if (contextSettings.IncludeTerrain)
        {
            var terrain = mainPawn.Position.GetTerrain(mainPawn.Map);
            if (terrain != null)
                sb.Append($"\nTerrain: {terrain.LabelCap}");
        }

        if (contextSettings.IncludeBeauty)
        {
            var nearbyCells = ContextHelper.GetNearbyCells(mainPawn);
            if (nearbyCells.Count > 0)
            {
                var beautySum = nearbyCells.Sum(c => BeautyUtility.CellBeauty(c, mainPawn.Map));
                sb.Append($"\nCellBeauty: {Describer.Beauty(beautySum / nearbyCells.Count)}");
            }
        }

        var pawnRoom = mainPawn.GetRoom();
        if (contextSettings.IncludeCleanliness && pawnRoom is { PsychologicallyOutdoors: false })
            sb.Append($"\nCleanliness: {Describer.Cleanliness(pawnRoom.GetStat(RoomStatDefOf.Cleanliness))}");

        if (contextSettings.IncludeSurroundings)
        {
            {
                var surroundingsText = ContextHelper.CollectNearbyContextText(mainPawn, 20);
                if (!string.IsNullOrEmpty(surroundingsText))
                {
                    sb.Append("\nSurroundings:\n");
                    sb.Append(surroundingsText);
                }
            }
        }
    }

    public static string GetRecentLogsContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeRecentLogs) return null;
        if (infoLevel == PromptService.InfoLevel.Short) return null;

        var entries = Find.PlayLog?.AllEntries;
        if (entries == null || entries.Count == 0) return null;

        // Last 2 in-game hours (2500 ticks per hour)
        int ticksWindow = 2500 * 2;
        int minTicks = GenTicks.TicksGame - ticksWindow;

        var ticksField = typeof(LogEntry).GetField("ticksAbs", BindingFlags.NonPublic | BindingFlags.Instance);

        var recentItems = new List<string>();

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry == null) continue;

            // Exclude RimTalk-produced logs:
            // - entries that are the special PlayLogEntry_RimTalkInteraction type
            // - entries that were converted and stored in RimTalkWorldComponent
            if (entry is PlayLogEntry_RimTalkInteraction) continue;
            if (entry is PlayLogEntry_Interaction interaction && InteractionTextPatch.IsRimTalkInteraction(interaction)) continue;

            int entryTicks = ticksField != null ? (int)ticksField.GetValue(entry) : -1;
            if (entryTicks < minTicks) continue;

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

            recentItems.Add($"{title}: {content}");
        }

        if (!recentItems.Any()) return null;
        recentItems = recentItems.TakeLast(3).ToList();
        recentItems[recentItems.Count - 1] = "正在进行:"+recentItems.Last();

        var sb = new StringBuilder();
        sb.Append("Actions:\n{");
        foreach (var item in recentItems)
        {
            sb.AppendLine().Append(item);
        }
        sb.Append("}");

        return sb.ToString();
    }
}