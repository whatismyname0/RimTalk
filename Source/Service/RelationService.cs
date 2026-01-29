using System;
using System.Linq;
using System.Text;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Service;

public static class RelationsService
{
    private const float FriendOpinionThreshold = 20f;
    private const float RivalOpinionThreshold = -20f;
    private const float LoveOpinionThreshold = 60f;
    private const float FuckOpinionThreshold = -60f;

    public static string GetRelationsString(Pawn pawn)
    {
        if (pawn?.relations == null) return "";

        StringBuilder relationsSb = new StringBuilder();
        var processedPawns = new System.Collections.Generic.HashSet<Pawn>();
        
        // Get all pawns from the social tab scope (similar to game's Social tab)
        var allSocialPawns = GetAllSocialPawns(pawn).ToList();
        
        // Calculate group attitudes
        var groupAttitudes = CalculateGroupAttitudes(pawn, allSocialPawns);
        
        // Add group attitude summaries
        foreach (var (groupType, attitudeType, isReverse) in groupAttitudes)
        {
            string attitudeText = GetAttitudeText(attitudeType);
            if (!string.IsNullOrEmpty(attitudeText))
            {
                string groupName = GetGroupName(groupType);
                if (isReverse)
                {
                    relationsSb.Append($"大多数{groupName}{attitudeText}我, ");
                }
                else
                {
                    relationsSb.Append($"我{attitudeText}大多数{groupName}, ");
                }
            }
        }
        
        foreach (var otherPawn in allSocialPawns)
        {
            if (otherPawn == null || otherPawn == pawn ||
                otherPawn.relations is { hidePawnRelations: true }) continue;
            if (!processedPawns.Add(otherPawn)) continue;

            try
            {
                float opinionValue = pawn.relations.OpinionOf(otherPawn);
                var labels = new System.Collections.Generic.List<string>();

                // Get all direct relations (not just the most important one)
                var directRelations = pawn.relations.DirectRelations
                    .Where(r => r.otherPawn == otherPawn && r.def != null && r.def.defName != "Acquaintance")
                    .Select(r => r.def.GetGenderSpecificLabelCap(otherPawn))
                    .ToList();
                
                if (directRelations.Any())
                {
                    labels.AddRange(directRelations.Select(r => $"{otherPawn.LabelShort}是我的"+r));
                }
                
                // Add opinion-based label
                string opinion = "";
                var attitudeType = GetAttitudeType(opinionValue);
                if (attitudeType != AttitudeType.None)
                {
                    opinion = GetOpinionLabel(attitudeType, otherPawn.LabelShort, false);
                }

                if (!string.IsNullOrEmpty(opinion))
                {
                    labels.Add(opinion);
                }

                // Add reverse opinion (how the other pawn views this pawn)
                float reverseOpinionValue = otherPawn.relations.OpinionOf(pawn);
                string reverseOpinion = "";
                var reverseAttitudeType = GetAttitudeType(reverseOpinionValue);
                if (reverseAttitudeType != AttitudeType.None)
                {
                    reverseOpinion = GetOpinionLabel(reverseAttitudeType, otherPawn.LabelShort, true);
                }

                if (string.IsNullOrEmpty(opinion) && !string.IsNullOrEmpty(reverseOpinion))
                    labels.Add($"我对{otherPawn.LabelShort}没有特别意见");
                if (!string.IsNullOrEmpty(reverseOpinion))
                    labels.Add(reverseOpinion);
                else if (!string.IsNullOrEmpty(opinion))
                {
                    labels.Add($"{otherPawn.LabelShort}对我没有特别意见");
                }

                // Check if this pawn should be skipped due to group attitude
                // Only check if there are no direct relations and only attitude labels exist
                if (directRelations.Count == 0 && labels.Count != 0 &&
                    ShouldSkipDueToGroupAttitude(otherPawn, attitudeType, reverseAttitudeType, groupAttitudes))
                {
                    continue;
                }

                if (labels.Any())
                {
                    string pawnName = otherPawn.LabelShort;
                    string deadMarker = otherPawn.Dead ? " (" + "Dead".Translate() + ")" : "";
                    string combinedLabels = string.Join(", ", labels);
                    relationsSb.Append($"{pawnName}{deadMarker}: [{combinedLabels}], ");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to get relation between {pawn} and {otherPawn}: {ex.Message}");
            }
        }

        if (relationsSb.Length > 0)
        {
            // Remove the trailing comma and space
            relationsSb.Length -= 2;
            return $"人际关系: {{ {relationsSb} }}";
        }

        return "";
    }

    private static System.Collections.Generic.IEnumerable<Pawn> GetAllSocialPawns(Pawn pawn)
    {
        if (pawn?.Map == null) yield break;

        // Get all pawns on the same map (colonists, prisoners, visitors, etc.)
        // This matches the scope shown in RimWorld's Social tab
        foreach (Pawn otherPawn in pawn.Map.mapPawns.AllPawnsSpawned)
        {
            // Include humanlike pawns and animals with vocal links
            if (otherPawn.RaceProps.Humanlike || otherPawn.HasVocalLink())
            {
                yield return otherPawn;
            }
        }

        // Also include pawns with direct relations that are not on the map
        // (like family members who left or died)
        foreach (var directRelation in pawn.relations.DirectRelations)
        {
            if (directRelation.otherPawn != null && 
                directRelation.def?.defName != "Acquaintance" &&
                !directRelation.otherPawn.Spawned)
            {
                yield return directRelation.otherPawn;
            }
        }
    }

    private enum AttitudeType
    {
        None,
        Hate,
        Love,
        Like,
        Dislike
    }

    private enum GroupType
    {
        Colonist,
        Prisoner,
        Slave
    }

    private static AttitudeType GetAttitudeType(float opinionValue)
    {
        if (opinionValue <= FuckOpinionThreshold)
            return AttitudeType.Hate;
        if (opinionValue >= LoveOpinionThreshold)
            return AttitudeType.Love;
        if (opinionValue >= FriendOpinionThreshold)
            return AttitudeType.Like;
        if (opinionValue <= RivalOpinionThreshold)
            return AttitudeType.Dislike;
        return AttitudeType.None;
    }

    private static string GetOpinionLabel(AttitudeType attitudeType, string pawnName, bool isReverse)
    {
        return attitudeType switch
        {
            AttitudeType.Hate => isReverse ? $"{pawnName}恨我" : $"我恨{pawnName}",
            AttitudeType.Love => isReverse ? $"{pawnName}喜爱我" : $"我喜爱{pawnName}",
            AttitudeType.Like => isReverse ? $"{pawnName}有点欣赏我" : $"我有点欣赏{pawnName}",
            AttitudeType.Dislike => isReverse ? $"{pawnName}有点厌恶我" : $"我有点厌恶{pawnName}",
            _ => ""
        };
    }

    private static string GetAttitudeText(AttitudeType attitudeType)
    {
        return attitudeType switch
        {
            AttitudeType.Hate => "恨",
            AttitudeType.Love => "喜爱",
            AttitudeType.Like => "有点欣赏",
            AttitudeType.Dislike => "有点厌恶",
            _ => ""
        };
    }

    private static string GetGroupName(GroupType groupType)
    {
        return groupType switch
        {
            GroupType.Colonist => "殖民者",
            GroupType.Prisoner => "囚犯",
            GroupType.Slave => "奴隶",
            _ => ""
        };
    }

    private static System.Collections.Generic.List<(GroupType, AttitudeType, bool)> 
        CalculateGroupAttitudes(Pawn pawn, System.Collections.Generic.List<Pawn> allPawns)
    {
        var results = new System.Collections.Generic.List<(GroupType, AttitudeType, bool)>();
        
        // Process each group type
        foreach (GroupType groupType in System.Enum.GetValues(typeof(GroupType)))
        {
            var groupPawns = allPawns.Where(p => p != pawn && GetPawnGroupType(p) == groupType).ToList();
            
            // Adjust count if pawn is in this group
            int totalCount = groupPawns.Count;
            
            // Need at least 6 pawns in the group
            if (totalCount < 6) continue;
            
            int threshold = (totalCount + 1) / 2; // More than half
            
            // Count attitudes from pawn to group
            var attitudeCountsToGroup = new System.Collections.Generic.Dictionary<AttitudeType, int>();
            foreach (var otherPawn in groupPawns)
            {
                if (otherPawn.relations?.hidePawnRelations == true) continue;
                float opinion = pawn.relations.OpinionOf(otherPawn);
                var attitudeType = GetAttitudeType(opinion);
                if (attitudeType != AttitudeType.None)
                {
                    if (!attitudeCountsToGroup.ContainsKey(attitudeType))
                        attitudeCountsToGroup[attitudeType] = 1;
                    attitudeCountsToGroup[attitudeType]++;
                }
            }
            
            // Count attitudes from group to pawn
            var attitudeCountsFromGroup = new System.Collections.Generic.Dictionary<AttitudeType, int>();
            foreach (var otherPawn in groupPawns)
            {
                if (otherPawn.relations?.hidePawnRelations == true) continue;
                float opinion = otherPawn.relations.OpinionOf(pawn);
                var attitudeType = GetAttitudeType(opinion);
                if (attitudeType != AttitudeType.None)
                {
                    if (!attitudeCountsFromGroup.ContainsKey(attitudeType))
                        attitudeCountsFromGroup[attitudeType] = 1;
                    attitudeCountsFromGroup[attitudeType]++;
                }
            }
            
            // Check if any attitude meets threshold (pawn to group)
            foreach (var kvp in attitudeCountsToGroup)
            {
                if (kvp.Value >= threshold)
                {
                    results.Add((groupType, kvp.Key, false));
                    break; // Only add one attitude per group
                }
            }
            
            // Check if any attitude meets threshold (group to pawn)
            foreach (var kvp in attitudeCountsFromGroup)
            {
                if (kvp.Value >= threshold)
                {
                    results.Add((groupType, kvp.Key, true));
                    break; // Only add one attitude per group
                }
            }
        }
        
        return results;
    }

    private static GroupType? GetPawnGroupType(Pawn pawn)
    {
        if (pawn.IsFreeColonist) return GroupType.Colonist;
        if (pawn.IsPrisoner) return GroupType.Prisoner;
        if (pawn.IsSlave) return GroupType.Slave;
        return null;
    }

    private static bool ShouldSkipDueToGroupAttitude(
        Pawn otherPawn,
        AttitudeType attitudeType, AttitudeType reverseAttitudeType,
        System.Collections.Generic.List<(GroupType, AttitudeType, bool)> groupAttitudes)
    {
        var otherPawnGroupType = GetPawnGroupType(otherPawn);
        if (otherPawnGroupType == null) return false;
        
        // Check if pawn's attitude to otherPawn (forward) matches a group attitude
        if (attitudeType != AttitudeType.None)
        {
            foreach (var (groupType, groupAttitude, isReverse) in groupAttitudes)
            {
                if (groupType == otherPawnGroupType && (groupAttitude == attitudeType || (groupAttitude == AttitudeType.Hate && attitudeType == AttitudeType.Dislike) || (groupAttitude == AttitudeType.Love && attitudeType == AttitudeType.Like)) && !isReverse)
                {
                    // Skip if this is the only label
                    if (reverseAttitudeType == AttitudeType.None)
                        return true;
                    // Also skip if the reverse attitude is also in group attitudes
                    foreach (var (groupType2, groupAttitude2, isReverse2) in groupAttitudes)
                    {
                        if (groupType2 == otherPawnGroupType && (groupAttitude2 == reverseAttitudeType || (groupAttitude2 == AttitudeType.Hate && reverseAttitudeType == AttitudeType.Dislike) || (groupAttitude2 == AttitudeType.Love && reverseAttitudeType == AttitudeType.Like)) && isReverse2)
                            return true;
                    }
                }
            }
        }
        
        // Check if otherPawn's attitude to pawn (reverse) matches a group attitude
        if (reverseAttitudeType != AttitudeType.None)
        {
            foreach (var (groupType, groupAttitude, isReverse) in groupAttitudes)
            {
                if (groupType == otherPawnGroupType && (groupAttitude == reverseAttitudeType || (groupAttitude == AttitudeType.Hate && reverseAttitudeType == AttitudeType.Dislike) || (groupAttitude == AttitudeType.Love && reverseAttitudeType == AttitudeType.Like)) && isReverse)
                {
                    // Skip if this is the only label
                    if (attitudeType == AttitudeType.None)
                        return true;
                }
            }
        }
        
        return false;
    }
}