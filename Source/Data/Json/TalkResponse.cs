#nullable enable
using System;
using System.Runtime.Serialization;
using RimTalk.Source.Data;
using Verse;

namespace RimTalk.Data;

[DataContract]
public class TalkResponse(TalkType talkType, string name, string text, string thinking) : IJsonData
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public TalkType TalkType { get; set; } = talkType;
    
    [DataMember(Name = "name")] 
    public string Name { get; set; } = name;

    [DataMember(Name = "text")] 
    public string Text { get; set; } = text;

    [DataMember(Name = "thinking")] 
    public string Thinking { get; set; } = thinking;

    [DataMember(Name = "act", EmitDefaultValue = false)]
    public string? InteractionRaw { get; set; }

    [DataMember(Name = "target", EmitDefaultValue = false)]
    public string? TargetName { get; set; }
    
    public Guid ParentTalkId { get; set; }
    
    public bool IsReply()
    {
        return ParentTalkId != Guid.Empty;
    }
    
    public string GetText()
    {
        return (Thinking.Length==0)?"":((Thinking[0]=='('&&Thinking[Thinking.Length - 1]==')')?"":"(")+Thinking+((Thinking[0]=='('&&Thinking[Thinking.Length - 1]==')')?"\n":")\n")+Text;
    }
    
    public string GetThinking()
    {
        return Thinking;
    }
    
    public InteractionType GetInteractionType()
    {
        if (string.IsNullOrWhiteSpace(InteractionRaw)) 
            return InteractionType.None;

        return Enum.TryParse(InteractionRaw, true, out InteractionType result) ? result : InteractionType.None;
    }
    public Pawn? GetTarget()
    {
        return TargetName != null ? Cache.GetByName(TargetName)?.Pawn : null;
    }

    public override string ToString()
    {
        return $"Type: {TalkType} | Name: {Name} | Text: \"{Text}\" | " +
               $"Int: {InteractionRaw} | Target: {TargetName}";
    }
}