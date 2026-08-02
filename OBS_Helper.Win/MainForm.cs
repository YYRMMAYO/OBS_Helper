using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OBS_Helper.Win
{
    /// <summary>
    /// Windows 桌面壳：用 WebView2 承载已发布的 Blazor 静态站点，无需联网即可离线运行。
    /// 站点文件来自程序目录下的 wwwroot 文件夹，通过虚拟主机名映射到 https://app.obshelper.local/。
    ///
    /// 关键修复：
    /// 1) 以往 WebView2 使用默认 user-data 目录（即 exe 所在目录）。当软件安装到
    ///    C:\Program Files 时，标准用户对该目录没有写权限，EnsureCoreWebView2Async
    ///    会抛异常，表现为“打开后弹出错误、白屏”。现改为写入 LocalAppData 下可写目录。
    /// 2) 优先使用随包内置的 WebView2 固定版本运行时（WebView2Runtime 目录，可选），
    ///    使应用尽可能自包含、不依赖用户系统是否已安装 WebView2 Runtime。
    /// 3) 渲染进程崩溃时自动重载，避免白屏。
    /// </summary>
    public class MainForm : Form
    {
        private readonly WebView2 _webView;

        public MainForm()
        {
            Text = "OBS 排障助手";
            MinimumSize = new Size(820, 600);
            Width = 1280;
            Height = 820;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;

            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            Controls.Add(_webView);

            Load += MainForm_Load;
        }

        private async void MainForm_Load(object? sender, EventArgs e)
        {
            try
            {
                // 1) 可写的 user-data 目录（修复 Program Files 下无写权限导致的启动失败）
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OBS_Helper", "WebView2");
                Directory.CreateDirectory(userDataFolder);

                // 2) 优先使用随包内置的固定版本运行时，否则回退到系统已安装的 WebView2
                string? runtimeFolder = FindBundledRuntime();
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: runtimeFolder,
                    userDataFolder: userDataFolder,
                    options: null);

                await _webView.EnsureCoreWebView2Async(env);

                string appFolder = Path.Combine(AppContext.BaseDirectory, "wwwroot");
                if (!Directory.Exists(appFolder))
                {
                    MessageBox.Show(
                        Errors.AppError.Format(Errors.AppError.SiteResourceMissing, "wwwroot 目录不存在: " + appFolder),
                        "资源缺失 " + Errors.AppError.SiteResourceMissing, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 将本地文件夹映射为虚拟主机，避免 file:// 方案的 CORS 限制，并支持 fetch/wasm。
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.obshelper.local", appFolder, CoreWebView2HostResourceAccessKind.Allow);

                // 渲染进程异常时自动重载，而非直接白屏
                _webView.CoreWebView2.ProcessFailed += (s, args) =>
                {
                    if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                    {
                        BeginInvoke(() => _webView.CoreWebView2.Reload());
                    }
                };

                // 注意：必须导航到虚拟主机的“根路径”而非 /index.html。
                // Blazor 路由按 URL 路径匹配 @page 路由，若路径为 /index.html 则会落入
                // <NotFound> 分支导致首页显示“404 没有找到这个页面”。导航到根路径 / 时，
                // WebView2 会将虚拟主机根目录的默认文档（index.html）作为 / 提供，路由即可命中首页。
                _webView.CoreWebView2.Navigate("https://app.obshelper.local/");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Errors.AppError.Format(Errors.AppError.WebViewInitFailed, ex.Message),
                    "启动失败 " + Errors.AppError.WebViewInitFailed, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 查找随包内置的 WebView2 固定版本运行时目录（位于 exe 同级的 WebView2Runtime 文件夹）。
        /// 存在则返回其路径，否则返回 null（交给系统 WebView2）。
        /// </summary>
        private static string? FindBundledRuntime()
        {
            try
            {
                string candidate = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime");
                if (Directory.Exists(candidate) &&
                    File.Exists(Path.Combine(candidate, "msedgewebview2.exe")))
                {
                    return candidate;
                }
            }
            catch { }
            return null;
        }
    }
}
