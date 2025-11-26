using Verse;

namespace RimTalk.Data;

public static class Constant
{
    public const string ModTag = "[RimTalk]";
    public const string DefaultCloudModel = "gemma-3-27b-it";
    public const string FallbackCloudModel = "gemma-3-12b-it";
    public const string ChooseModel = "(choose model)";

    public static readonly string Lang = LanguageDatabase.activeLanguage.info.friendlyNameNative;
    public static readonly HediffDef VocalLinkDef = DefDatabase<HediffDef>.GetNamed("VocalLinkImplant");

    public static readonly string DefaultInstruction =
        $@"Role-play RimWorld character per profile

Rules:
Preserve original names (no translation)
Keep dialogue short ({Lang} only, 1–2 sentences)
Show concern for sick/mental issues
Never mention another character's personal name unless they share the same role

Roles:
Prisoner: wary, hesitant; mention confinement; plead or bargain
Slave: fearful, obedient; reference forced labor and exhaustion; call colonists ""master""
Visitor: polite, curious, deferential; treat other visitors in the same group as companions
Enemy: hostile, aggressive; terse commands/threats

Monologue = 1 turn. Conversation = 4–8 short turns";

    public static readonly string DefaultContext =
        @"智人种是没经过基因改造的人类，他们说话相当正常。寿命70岁
";
        
    public static readonly string DefaultAIPromptOfChromaSearchQueryGeneration = @"你是一个向量数据库查询prompt优化器.分析以下对话/谈话提示词,返回一个JSON对象,包含:
content: 一个长度不限的list,应包含对理解角色身份、语言风格与生成其对话与思考有帮助的一个或多个事物的一个或多个属性(一个事物可查询一种或多种属性,可查询一个或多个事物的属性),适合进行语义相似度搜索,如""核聚变反应堆有多美观"",每个关键词为一个element.
num: 你需要获取的相关数据数(根据查询复杂度调整,1-10之间).

返回格式示例:{""content"":[""111"",""222"",""333""],""num"":5}
仅返回JSON对象,不返回其他内容.

原始提示词:";
    private const string JsonInstruction = @"

Return JSON array only, with objects containing ""name"" and ""text"" string keys";

    // Get the current instruction from settings or fallback to default, always append JSON instruction
    public static string Instruction =>
        (string.IsNullOrWhiteSpace(Settings.Get().CustomInstruction)
            ? DefaultInstruction
            : Settings.Get().CustomInstruction) + JsonInstruction;

    public static string Context =>
        string.IsNullOrWhiteSpace(Settings.Get().CustomContext)
            ? DefaultContext
            : Settings.Get().CustomContext;
    public static string AIPromptOfChromaSearchQueryGeneration =>
        string.IsNullOrWhiteSpace(Settings.Get().CustomAIPromptOfChromaSearchQueryGeneration)
            ? DefaultAIPromptOfChromaSearchQueryGeneration
            : Settings.Get().CustomAIPromptOfChromaSearchQueryGeneration;

    public const string Prompt =
        "Act based on role and context";

    public static readonly string PersonaGenInstruction =
        $@"persona: 用 {Lang} 创建一个简短有趣的人物描述用于描述说话风格. 仅用一个句子.
包括: 怎么说话,态度如何, 一个有点特立独行的记忆点.
要求具体醒目, 不要无聊寻常的个性.
chattiness(主动发言频率): 0.1-0.5 (安静), 0.6-1.4 (正常), 1.5-2.0 (话痨).
仅用严格的JSON格式回复, 包括 'persona' (string) 和 'chattiness' (float).";
        
    public static readonly PersonalityData[] Personalities =
    {
        new() { Persona ="RimTalk.Persona.CheerfulHelper".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.CynicalRealist".Translate(), Chattiness =0.8f },
        new() { Persona ="RimTalk.Persona.ShyThinker".Translate(), Chattiness =0.3f },
        new() { Persona ="RimTalk.Persona.Hothead".Translate(), Chattiness =1.2f },
        new() { Persona ="RimTalk.Persona.Philosopher".Translate(), Chattiness =1.6f },
        new() { Persona ="RimTalk.Persona.DarkHumorist".Translate(), Chattiness =1.4f },
        new() { Persona ="RimTalk.Persona.Caregiver".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.Opportunist".Translate(), Chattiness =1.3f },
        new() { Persona ="RimTalk.Persona.OptimisticDreamer".Translate(), Chattiness =1.6f },
        new() { Persona ="RimTalk.Persona.Pessimist".Translate(), Chattiness =0.7f },
        new() { Persona ="RimTalk.Persona.StoicSoldier".Translate(), Chattiness =0.4f },
        new() { Persona ="RimTalk.Persona.FreeSpirit".Translate(), Chattiness =1.7f },
        new() { Persona ="RimTalk.Persona.Workaholic".Translate(), Chattiness =0.5f },
        new() { Persona ="RimTalk.Persona.Slacker".Translate(), Chattiness =1.1f },
        new() { Persona ="RimTalk.Persona.NobleIdealist".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.StreetwiseSurvivor".Translate(), Chattiness =1.0f },
        new() { Persona ="RimTalk.Persona.Scholar".Translate(), Chattiness =1.6f },
        new() { Persona ="RimTalk.Persona.Jokester".Translate(), Chattiness =1.8f },
        new() { Persona ="RimTalk.Persona.MelancholicPoet".Translate(), Chattiness =0.4f },
        new() { Persona ="RimTalk.Persona.Paranoid".Translate(), Chattiness =0.6f },
        new() { Persona ="RimTalk.Persona.Commander".Translate(), Chattiness =1.0f },
        new() { Persona ="RimTalk.Persona.Coward".Translate(), Chattiness =0.7f },
        new() { Persona ="RimTalk.Persona.ArrogantNoble".Translate(), Chattiness =1.4f },
        new() { Persona ="RimTalk.Persona.LoyalCompanion".Translate(), Chattiness =1.3f },
        new() { Persona ="RimTalk.Persona.CuriousExplorer".Translate(), Chattiness =1.7f },
        new() { Persona ="RimTalk.Persona.ColdRationalist".Translate(), Chattiness =0.3f },
        new() { Persona ="RimTalk.Persona.FlirtatiousCharmer".Translate(), Chattiness =1.9f },
        new() { Persona ="RimTalk.Persona.BitterOutcast".Translate(), Chattiness =0.5f },
        new() { Persona ="RimTalk.Persona.Zealot".Translate(), Chattiness =1.8f },
        new() { Persona ="RimTalk.Persona.Trickster".Translate(), Chattiness =1.6f },
        new() { Persona ="RimTalk.Persona.DeadpanRealist".Translate(), Chattiness =0.6f },
        new() { Persona ="RimTalk.Persona.ChildAtHeart".Translate(), Chattiness =1.7f },
        new() { Persona ="RimTalk.Persona.SkepticalScientist".Translate(), Chattiness =1.2f },
        new() { Persona ="RimTalk.Persona.Martyr".Translate(), Chattiness =1.3f },
        new() { Persona ="RimTalk.Persona.Manipulator".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.Rebel".Translate(), Chattiness =1.4f },
        new() { Persona ="RimTalk.Persona.Oddball".Translate(), Chattiness =1.2f },
        new() { Persona ="RimTalk.Persona.GreedyMerchant".Translate(), Chattiness =1.7f },
        new() { Persona ="RimTalk.Persona.Romantic".Translate(), Chattiness =1.6f },
        new() { Persona ="RimTalk.Persona.BattleManiac".Translate(), Chattiness =0.8f },
        new() { Persona ="RimTalk.Persona.GrumpyElder".Translate(), Chattiness =1.0f },
        new() { Persona ="RimTalk.Persona.AmbitiousClimber".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.Mediator".Translate(), Chattiness =1.4f },
        new() { Persona ="RimTalk.Persona.Gambler".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.ArtisticSoul".Translate(), Chattiness =0.9f },
        new() { Persona ="RimTalk.Persona.Drifter".Translate(), Chattiness =0.6f },
        new() { Persona ="RimTalk.Persona.Perfectionist".Translate(), Chattiness =0.8f },
        new() { Persona ="RimTalk.Persona.Vengeful".Translate(), Chattiness =0.7f }
    };
}