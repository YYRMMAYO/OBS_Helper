namespace OBS_Helper.Wpf.Services;

/// <summary>
/// fire-and-forget 辅助（P3-2）：丢弃 Task 的地方统一用它包装，
/// 异常一律落 FileLogger，杜绝「后台任务失败但无从查证」。
/// 用法：<c>someTask.FireAndForget("Category", "做了什么");</c>
/// </summary>
public static class TaskExtensions
{
    public static async void FireAndForget(this Task task, string category, string what)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            FileLogger.Error(category, $"{what}: {ex.Message}");
        }
    }

    public static async void FireAndForget(this ValueTask task, string category, string what)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            FileLogger.Error(category, $"{what}: {ex.Message}");
        }
    }
}
