using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MyWords;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private EventWaitHandle? showSignal;
    private RegisteredWaitHandle? signalRegistration;
    private Forms.NotifyIcon? tray;
    private AppController? controller;
    private ManggoBridge? bridge;
    private MainWindow? window;
    private bool ownsMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Length == 2 && e.Args[0] == "--self-test")
            {
                var result = await SelfTests.RunAsync(Path.GetFullPath(e.Args[1]));
                Shutdown(result); return;
            }
            if (e.Args.Length == 2 && e.Args[0] == "--preview")
            {
                Preview.Render(Path.GetFullPath(e.Args[1]));
                Shutdown(); return;
            }
            var user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            var identity = @"Local\MyWords-" + user;
            instanceMutex = new Mutex(true, identity, out ownsMutex);
            if (!ownsMutex)
            {
                try { using var signal = EventWaitHandle.OpenExisting(identity + "-show"); signal.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                Shutdown(); return;
            }
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, identity + "-show");
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyWords");
            controller = new AppController(new WordStore(Path.Combine(dataDirectory, "words.json")), new SettingsStore(dataDirectory));
            var bridgeStatus = "已就绪。拾词运行时，Manggo 可以一键收藏。";
            try { bridge = new ManggoBridge(controller); bridge.Start(); }
            catch (Exception ex) when (ex is System.Net.HttpListenerException or InvalidOperationException)
            { bridge?.Dispose(); bridge = null; bridgeStatus = "Manggo 连接未启动，可能是端口 17653 被占用。请关闭占用程序后重新打开拾词。"; }
            window = new MainWindow(controller, bridgeStatus);
            MainWindow = window;
            using var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
            tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(resource), Text = "拾词 · My Words", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开单词库", null, (_, _) => Dispatcher.Invoke(window.ShowLibrary));
            menu.Items.Add("今日学习", null, (_, _) => Dispatcher.Invoke(window.ShowStudy));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出拾词", null, (_, _) => Dispatcher.Invoke(() => { window.AllowClose = true; Shutdown(); }));
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) window.ShowLibrary(); };
            controller.Notice += (message, error) => Dispatcher.BeginInvoke(() =>
            {
                if (error) tray?.ShowBalloonTip(2500, "拾词 · 需要处理", message, Forms.ToolTipIcon.Warning);
            });
            signalRegistration = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Dispatcher.BeginInvoke(window.ShowLibrary), null, Timeout.Infinite, false);
            if (Array.IndexOf(e.Args, "--background") < 0) window.ShowLibrary();
            SessionEnding += (_, _) => { if (window != null) window.AllowClose = true; Shutdown(); };
        }
        catch (Exception ex)
        {
            if (e.Args.Length == 2 && e.Args[0].StartsWith("--", StringComparison.Ordinal))
            {
                var errorPath = e.Args[0] == "--self-test" ? e.Args[1] : Path.Combine(e.Args[1], "preview-error.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(errorPath))!);
                File.WriteAllText(errorPath, ex.ToString());
            }
            else MessageBox.Show("拾词无法启动：" + ex.Message + "\n\n原词库文件没有被重置。", "拾词", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (window != null) window.AllowClose = true;
        signalRegistration?.Unregister(null);
        bridge?.Dispose();
        controller?.Dispose();
        if (tray != null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Icon?.Dispose(); tray.Dispose(); }
        showSignal?.Dispose();
        if (ownsMutex) instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
