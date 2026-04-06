using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenClawManager.Common.ProcessClass;



public static class BrowserLauncher
{

    // 使用系统默认浏览器打开 URL
    public static bool Open(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: 使用 explorer 或 start
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer",
                    Arguments = $"\"{url}\"",
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS: 使用 open
                Process.Start("open", url);
            }
            else
            {
                // Linux: 使用 xdg-open
                Process.Start("xdg-open", url);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"打开浏览器失败: {ex.Message}");
            return false;
        }
    }

    // 异步版本，带重试
    public static async Task<bool> OpenAsync(string url, int retryCount = 1)
    {
        for (int i = 0; i < retryCount; i++)
        {
            if (Open(url)) return true;
            await Task.Delay(500);
        }
        return false;
    }
}
