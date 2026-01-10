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
                string label = null;

                // Check if there's an important relation (not acquaintance)
                PawnRelationDef mostImportantRelation = pawn.GetMostImportantRelation(otherPawn);
                if (mostImportantRelation != null && mostImportantRelation.defName != "Acquaintance")
                {
                    label = mostImportantRelation.GetGenderSpecificLabelCap(otherPawn);
                }
                
                // If no important relation, check friend/rival based on opinion
                if (string.IsNullOrEmpty(label))
                {
                    if (opinionValue >= FriendOpinionThreshold)
                    {
                        label = "Friend".Translate();
                    }
                    else if (opinionValue <= RivalOpinionThreshold)
                    {
                        label = "Rival".Translate();
                    }
                }

                if (!string.IsNullOrEmpty(label))
                {
                    string pawnName = otherPawn.LabelShort;
                    string deadMarker = otherPawn.Dead ? " (" + "Dead".Translate() + ")" : "";
                    relationsSb.Append($"{pawnName}{deadMarker}: {label}, ");
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
            return $"Relations: {{ {relationsSb} }}";
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