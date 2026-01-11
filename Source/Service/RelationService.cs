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
        var allSocialPawns = GetAllSocialPawns(pawn);
        
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
                    labels.AddRange(directRelations);
                }
                
                // Add opinion-based label
                string opinion = "";
                if (opinionValue <= FuckOpinionThreshold)
                {
                    opinion = "仇恨";
                }
                else if (opinionValue >= LoveOpinionThreshold)
                {
                    opinion = "喜爱";
                }
                else if (opinionValue >= FriendOpinionThreshold)
                {
                    opinion = "欣赏";
                }
                else if (opinionValue <= RivalOpinionThreshold)
                {
                    opinion = "厌恶";
                }

                if (!string.IsNullOrEmpty(opinion))
                {
                    labels.Add(opinion);
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
            return $"对他人的看法或他人相对于自己的身份: {{ {relationsSb} }}";
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
}