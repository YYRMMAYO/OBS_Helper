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

                // 减少攻击面：本地静态内容无需远程调试 / DevTools。
                // 关闭后即使内容被篡改，也无法借助远程调试协议逃逸到宿主机。
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.IsScriptEnabled = true; // 站点本身是可信的本地内容

                // WebMessage 是站点访问宿主原生能力（DPAPI 加密保存密码 / 读取 OBS 日志）的唯一通道。
                // 必须开启，但受两道约束：
                //   ① 只接受来自本地虚拟主机 app.obshelper.local 的消息（下方 Source 校验）；
                //   ② 宿主侧仅认识固定命令白名单，且对文件路径做目录限定（HostBridgeHandler）。
                _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                // 渲染进程异常时自动重载，而非直接白屏
                _webView.CoreWebView2.ProcessFailed += (s, args) =>
                {
                    if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                    {
                        BeginInvoke(() => _webView.CoreWebView2.Reload());
                    }
                };

                // 站点内的「官方文档」等外链需离开本地虚拟主机，统一交由系统默认浏览器打开，
                // 避免 WebView2 直接导航到外部不可信页面（纵深防御）。
                _webView.CoreWebView2.NewWindowRequested += (s, args) =>
                {
                    if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
                    {
                        args.Handled = true;
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                        catch { }
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
        /// 处理站点发来的宿主命令。只接受本地虚拟主机来源，其余一律丢弃。
        /// 处理结果通过 PostWebMessageAsString 原路回传（前端按消息里的 id 关联）。
        /// </summary>
        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                // 来源校验：只信任映射出来的本地虚拟主机，避免站点被导航到外部页面后仍能调用宿主。
                if (!Uri.TryCreate(args.Source, UriKind.Absolute, out var src) ||
                    !string.Equals(src.Host, "app.obshelper.local", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string raw = args.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(raw)) return;

                // 宿主命令可能包含网络请求（云端 AI 转发），必须异步执行以免卡住 UI 线程。
                string reply = await Host.HostBridgeHandler.HandleAsync(raw).ConfigureAwait(true);
                if (IsDisposed || _webView.IsDisposed) return;
                _webView.CoreWebView2.PostWebMessageAsString(reply);
            }
            catch (Exception)
            {
                // 宿主命令通道出问题不应影响主界面：前端会因超时自行降级。
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
