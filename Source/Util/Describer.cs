using System;
using Verse;

namespace RimTalk.Util;

public static class Describer
{
    public static string Wealth(float wealthTotal)
    {
        return wealthTotal switch
        {
            < 50_000f => "家徒四壁",
            < 100_000f => "贫穷",
            < 200_000f => "温饱",
            < 300_000f => "小康",
            < 400_000f => "富裕",
            < 600_000f => "奢华",
            < 1_000_000f => "大富大贵",
            < 1_500_000f => "家财万贯",
            < 2_000_000f => "闪耀世界一般",
            _ => "全银河最富有"
        };
    }
    
    public static string Beauty(float beauty)
    {
        return beauty switch
        {
            > 100f => "wondrously", 
            > 20f => "impressive",
            > 10f => "beautiful",
            > 5f => "decent",
            > -1f => "general",
            > -5f => "awful",
            > -20f => "very awful",
            _ => "disgusting"
        };
    }

    public static string Cleanliness(float cleanliness)
    {
        return cleanliness switch
        {
            > 1.5f => "spotless",
            > 0.5f => "clean",
            > -0.5f => "neat",
            > -1.5f => "a bit dirty",
            > -2.5f => "dirty",
            > -5f => "very dirty",
            _ => "foul"
        };
    }
    
    public static string Resistance(float value)
    {
        if (value <= 0f) return "这名囚犯的抵抗意志已经完全崩溃，随时准备加入殖民地";
        if (value < 2f) return "这名囚犯几乎放弃了抵抗，内心已经开始动摇，很快就会屈服";
        if (value < 6f) return "这名囚犯的抵抗意志已经被削弱，但仍保持着警惕和戒备";
        if (value < 12f) return "这名囚犯意志坚定，需要花费相当大的努力才能招募";
        return "这名囚犯极度顽固反抗，招募过程将会非常漫长而艰难";
    }

    public static string Will(float value)
    {
        if (value <= 0f) return "这名囚犯已经没有任何意志可言，完全准备好被奴役";
        if (value < 2f) return "这名囚犯意志薄弱，很容易就能被驯化成奴隶";
        if (value < 6f) return "这名囚犯还有一些意志力，可能会稍作抵抗";
        if (value < 12f) return "这名囚犯意志坚强，奴役过程会比较困难";
        return "这名囚犯意志顽强不屈，极难被驯化为奴隶";
    }

    public static string Suppression(float value)
    {
        if (value < 20f) return "Openly rebellious, likely to resist or escape";
        if (value < 50f) return "Unstable, may push boundaries";
        if (value < 80f) return "Generally obedient, but watchful";
        return "Completely cowed, unlikely to resist";
    }
    
    public static string GetLabelShort(this Gender gender)
    {
        return gender switch
        {
            Gender.Male => "M",
            Gender.Female => "F",
            _ => ""
        };
    }
}