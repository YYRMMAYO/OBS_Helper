namespace OBS_Helper.Client;

public static class UIHelper
{
    public static string SevClass(string s) => s.Trim() switch
    {
        "常见" => "common",
        "基础" => "basic",
        "进阶" => "adv",
        "一般" => "normal",
        _ => "normal"
    };

    public static string LevelClass(string s) => s.Trim() == "进阶" ? "adv" : "basic";
}
