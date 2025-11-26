using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimTalk.Service;

public static class PromptService
{
    public enum InfoLevel
    {
        Short,
        Normal,
        Full
    }

    public static string BuildContext(List<Pawn> pawns)
    {
        StringBuilder context = new StringBuilder();
        context.AppendLine(Constant.Instruction).AppendLine();

        int count = 0;
        foreach (Pawn pawn in pawns)
        {
            // Main pawn gets more detail, others get basic info
            InfoLevel infoLevel = pawn == pawns[0] ? InfoLevel.Full : InfoLevel.Normal;
            string pawnContext = CreatePawnContext(pawn, infoLevel);
            Cache.Get(pawn).Context = pawnContext;
            count++;
            context.AppendLine();
            context.AppendLine($"[Person {count} START]");
            context.AppendLine(pawnContext);
            context.AppendLine($"[Person {count} END]");
        }

        return context.ToString();
    }

    public static string CreatePawnBackstory(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        StringBuilder sb = new StringBuilder();

        var name = pawn.LabelShort;
        var title = pawn.story.title == null ? "" : $"({pawn.story.title})";
        var genderAndAge = Regex.Replace(pawn.MainDesc(false), @"\(\d+\)", "");
        sb.AppendLine($"{name} {title} ({genderAndAge})");

        var role = $"Role: {pawn.GetRole(true)}";
        sb.AppendLine(role);

        if (ModsConfig.BiotechActive && pawn.genes?.Xenotype != null)
        {
            var xenotypeInfo = $"Race: {pawn.genes.Xenotype.LabelCap}";
            // if (!pawn.genes.Xenotype.descriptionShort.NullOrEmpty())
                // xenotypeInfo += $" - {pawn.genes.Xenotype.descriptionShort}";
            sb.AppendLine(xenotypeInfo);
            sb.AppendLine(pawn.genes.Xenotype.description);
        }

        if (infoLevel != InfoLevel.Short && !pawn.IsVisitor() && !pawn.IsEnemy())
        {
            if (ModsConfig.BiotechActive && pawn.genes?.GenesListForReading != null)
            {
                var notableGenes = pawn.genes.GenesListForReading
                    .Where(g => g.def.biostatMet != 0 || g.def.biostatCpx != 0)
                    .Select(g => g.def.LabelCap);

                if (notableGenes.Any())
                {
                    sb.AppendLine($"Notable Genes: {string.Join(", ", notableGenes)}");
                }
            }
        }

        // Add Ideology information
        if (ModsConfig.IdeologyActive && pawn.ideo?.Ideo != null)
        {
            var ideo = pawn.ideo.Ideo;

            var ideologyInfo = $"Ideology: {ideo.name}";
            sb.AppendLine(ideologyInfo);

            var memes = ideo?.memes?
                .Where(m => m != null)
                .Select(m => m.LabelCap.Resolve())
                .Where(label => !string.IsNullOrEmpty(label))
                .ToList();
            var descs = ideo?.memes?
                .Where(m => m != null)
                .Select(m => m.description)
                .Where(description => !string.IsNullOrEmpty(description))
                .ToList();

            if (memes != null && memes.Any())
            {
                StringBuilder strMemes = new();
                strMemes.Append("Memes: \n");
                for (int i=0;i<memes.Count;i++)
                {
                    strMemes.Append(i!=0?"\n":"");
                    strMemes.Append($"{memes[i]}:{descs[i]}\n");
                }
                sb.Append(strMemes);
            }
        }

        //// INVADER AND VISITOR STOP
        if (pawn.IsEnemy() || pawn.IsVisitor())
            return sb.ToString();

        if (pawn.story.Childhood != null)
        {
            var childHood =
                $"Childhood: {pawn.story.Childhood.title}({pawn.story.Childhood.titleShort})";
            if (infoLevel == InfoLevel.Full) childHood += $":{Sanitize(pawn.story.Childhood.description, pawn)}";
            sb.AppendLine(childHood);
        }

        if (pawn.story.Adulthood != null)
        {
            var adulthood =
                $"Adulthood: {pawn.story.Adulthood.title}({pawn.story.Adulthood.titleShort})";
            if (infoLevel == InfoLevel.Full) adulthood += $":{Sanitize(pawn.story.Adulthood.description, pawn)}";
            sb.AppendLine(adulthood);
        }

        var traits = "Traits: ";
        foreach (Trait trait in pawn.story.traits.TraitsSorted)
        {
            foreach (TraitDegreeData degreeData in trait.def.degreeDatas)
            {
                if (degreeData.degree == trait.Degree)
                {
                    traits += degreeData.label + (infoLevel == InfoLevel.Full
                        ? $":{Sanitize(degreeData.description, pawn)}\n"
                        : ",");
                    break;
                }
            }
        }

        sb.AppendLine(traits);

        if (infoLevel != InfoLevel.Short)
        {
            var skills = "Skills: ";
            foreach (SkillRecord skillRecord in pawn.skills.skills)
            {
                skills += $"{skillRecord.def.label}: {skillRecord.Level}({TranslatePassionToString(skillRecord.passion)}), ";
            }

            sb.AppendLine(skills);
        }

        return sb.ToString();
    }
    public static string CreatePawnBackstory_ToolUser(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        StringBuilder sb = new StringBuilder();

        var name = pawn.LabelShort;
        var type = pawn.def.label;

        sb.AppendLine($"{name}");

        var role = $"Role: {pawn.GetRole(true)}";
        sb.AppendLine(role);

        sb.AppendLine($"{name} 是一个 {type} 型号的机器人.");

        return sb.ToString();
    }

    public static string CreatePawnContext(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        StringBuilder sb = new StringBuilder();

        if (pawn.RaceProps.Humanlike)
            sb.Append(CreatePawnBackstory(pawn, infoLevel));
        
        else if (pawn.RaceProps.ToolUser)
            sb.Append(CreatePawnBackstory_ToolUser(pawn, infoLevel));

        // add Health
        var method = AccessTools.Method(typeof(HealthCardUtility), "VisibleHediffs");
        IEnumerable<Hediff> hediffs = (IEnumerable<Hediff>)method.Invoke(null, new object[] { pawn, false });

        var hediffDict = hediffs
            .GroupBy(hediff => hediff.def)
            .ToDictionary(
                group => group.Key,
                group => string.Join(",",
                    group.Select(hediff => hediff.Part?.Label ?? ""))); // Values are concatenated body parts

        var healthInfo = string.Join(",", hediffDict.Select(kvp => $"{kvp.Key.label}({kvp.Value})"));

        if (healthInfo != "")
            sb.AppendLine($"Health: {healthInfo}");

        var personality = Cache.Get(pawn).Personality;
        if (personality != null && pawn.RaceProps.Humanlike)
            sb.AppendLine($"Personality: {personality}");

        //// INVADER STOP
        if (pawn.IsEnemy())
            return sb.ToString();

        var m = pawn.needs?.mood;
        var mood = pawn.Downed
            ? "危急: 倒地"
            : pawn.InMentalState
                ? $"Mood: {pawn.MentalState?.InspectLine} (精神已崩溃)"
                : $"Mood: {m?.MoodString ?? "N/A"} ({(int)((m?.CurLevelPercentage ?? 0) * 100)}%)";
        if (pawn.RaceProps.Humanlike)sb.AppendLine(mood);

        var thoughts = "Memory: ";
        foreach (Thought thought in GetThoughts(pawn).Keys)
        {
            thoughts += $"{Sanitize(thought.LabelCap)}, ";
        }

        if (pawn.RaceProps.Humanlike)sb.AppendLine(thoughts);

        if (pawn.IsSlave || pawn.IsPrisoner)
            sb.AppendLine(pawn.GetPrisonerSlaveStatus());

        //// VISITOR STOP
        if (pawn.IsVisitor())
        {
            Lord lord = pawn.GetLord() ?? pawn.CurJob?.lord;
            if (lord?.LordJob != null)
            {
                string fullTypeName = lord.LordJob.GetType().Name;
                string cleanName = fullTypeName.Replace("LordJob_", "");
                sb.AppendLine($"Activity: {cleanName}");
            }
        }

        sb.AppendLine(RelationsService.GetRelationsString(pawn));

        if (infoLevel != InfoLevel.Short)
        {
            var equipment = "Equipment: ";
            if (pawn.equipment?.Primary != null)
                equipment += $"Weapon: {pawn.equipment.Primary.LabelCap}, ";

            var wornApparel = pawn.apparel?.WornApparel;
            var apparelLabels =
                wornApparel != null ? wornApparel.Select(a => a.LabelCap) : Enumerable.Empty<string>();

            if (apparelLabels.Any())
            {
                equipment += $"Apparel: {string.Join(", ", apparelLabels)}";
            }

            if (equipment != "Equipment: ")
                sb.AppendLine(equipment);
        }

        return sb.ToString();
    }

    public static void DecoratePrompt(TalkRequest talkRequest, List<Pawn> pawns, string status)
    {
        if (pawns == null)
            throw new System.ArgumentNullException(nameof(pawns));
        if (pawns.Count == 0)
            throw new System.ArgumentException("pawns must contain at least one Pawn", nameof(pawns));

        var sb = new StringBuilder();
        CommonUtil.InGameData gameData = CommonUtil.GetInGameData();
        
        string shortName = $"{pawns[0].LabelShort}({pawns[0].GetRole(true)})";

        // Add the conversation part
        if (talkRequest.TalkType == TalkType.User)
        {
            if (talkRequest.Initiator == talkRequest.Recipient)
            {
                sb.Append("一个来自世界之外的——超凡智能,一个同情殖民地的超维度观测者——对 "+shortName+$" 说: {talkRequest.Prompt}");
            }
            else
            {
                if (pawns.Count < 2)
                    throw new System.ArgumentException("pawns must contain at least two Pawns for a two-party user talkRequest", nameof(pawns));

                sb.Append($"{pawns[1].LabelShort}({pawns[1].GetRole()}) 对 "+shortName+$" 说:{talkRequest.Prompt}'. 从 {pawns[0].LabelShort} 开始生成几个回合的对话, ");
            }
        }
        else
        {
            if (pawns.Count == 1) 
                
                sb.Append($"{shortName} 的一段独白");
            else if (pawns[0].IsInCombat() || pawns[0].GetMapRole() == MapRole.Invading)
            {
                if (talkRequest.TalkType != TalkType.Urgent && !pawns[0].InMentalState)
                {
                    talkRequest.Prompt = null;
                }
                talkRequest.TalkType = TalkType.Urgent;
                if (pawns[0].IsSlave || pawns[0].IsPrisoner)
                    sb.Append($"{shortName} 发起短对话 (着急)");
                else 
                    sb.Append($"{shortName} 发起短对话,语气急迫 ({pawns[0].GetMapRole().ToString().ToLower()}/命令)");
            }
            else
            {
                sb.Append($"{shortName} 发起对话,轮流发言");
            }

            if (pawns[0].InMentalState)
                sb.Append($"\n疯疯癫癫,略带戏剧性 (精神崩溃)");
            else if (pawns[0].Downed)
                sb.Append($"\n(疼痛倒地,简短勉强的对话)");
            else
                sb.Append($"\n{talkRequest.Prompt}");
        }
        

        // add pawn status
        sb.Append($"\n{status}");

        string locationStatus = GetPawnLocationStatus(pawns[0]);

        Room room = pawns[0].GetRoom();
        float temperature = room != null ? room.Temperature : pawns[0].Map.mapTemperature.OutdoorTemp;

        if (!string.IsNullOrEmpty(locationStatus))
            sb.Append($"\n地点: {locationStatus}");

        // add time
        sb.Append($"\n时间: {gameData.Hour12HString}");

        // add date
        sb.Append($"\n日期: {gameData.DateString}");

        // add season
        sb.Append($"\n季节: {gameData.SeasonString}");

        // add weather
        sb.Append($"\n天气: {gameData.WeatherString}");

        List<(string,string)> conditions = gameData.ConditionStrings;

        sb.Append($"\n特殊气象状态:");

        bool first = true;
        foreach (var condition in conditions)
        {
            if (!first)
                sb.Append(", ");
            else first = false;
            sb.Append($"{condition.Item1}");
        }
        if (first)
            sb.Append(" 无");

        sb.Append($"\n当前地点温度: {temperature}");

        // add language assurance
        if (AIService.IsFirstInstruction())
            sb.Append($"\n用 {Constant.Lang} 语言回复");

        talkRequest.Prompt = sb.ToString();
    }

    public static string GetPawnLocationStatus(Pawn pawn)
    {
        if (pawn == null || pawn.Map == null || pawn.Position == IntVec3.Invalid)
            return null;

        Room room = pawn.GetRoom();
        if (room != null && !room.PsychologicallyOutdoors)
            return room?.GetRoomRoleLabel().Translate();
        return "Outdoors".Translate();
    }
    
    public static Dictionary<Thought, float> GetThoughts(Pawn pawn)
    {
        var thoughts = new List<Thought>();
        pawn?.needs?.mood?.thoughts?.GetAllMoodThoughts(thoughts);

        return thoughts
            .GroupBy(t => t.def.defName)
            .ToDictionary(g => g.First(), g => g.Sum(t => t.MoodOffset()));
    }

    private static string Sanitize(string text, Pawn pawn = null)
    {
        if (pawn != null)
            text = text.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true).Resolve();
        return text.StripTags().RemoveLineBreaks();
    }

    private static string TranslatePassionToString(Passion passion)
    {
        return passion switch
        {
            Passion.None => "无",
            Passion.Minor => "好奇",
            Passion.Major => "狂热",
            (Passion)3 => "乏味",
            (Passion)4 => "恃才",
            (Passion)5 => "偏长",
            _ => "未知"
        };
    }
}