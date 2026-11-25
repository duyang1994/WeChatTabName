using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WeChatTabName
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "WeChatTabName_SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }

    internal sealed class Config
    {
        public static string Folder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WeChatTabName");
            }
        }

        public static string FilePath { get { return Path.Combine(Folder, "config.ini"); } }

        public List<string> Roots = new List<string>();
        public Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static Config Load()
        {
            var cfg = new Config();
            try
            {
                if (!File.Exists(FilePath)) return cfg;
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line.Substring(0, eq).Trim();
                    var val = line.Substring(eq + 1).Trim();
                    if (key.Equals("DataRoot", StringComparison.OrdinalIgnoreCase))
                    {
                        if (val.Length > 0) cfg.Roots.Add(val);
                    }
                    else if (key.StartsWith("wxid_", StringComparison.OrdinalIgnoreCase))
                    {
                        cfg.Names[key] = val;
                    }
                }
            }
            catch { }
            return cfg;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
                var sb = new StringBuilder();
                sb.AppendLine("# WeChatTabName config");
                sb.AppendLine("# DataRoot: one line per WeChat data dir;  wxid_xxx=name");
                foreach (var r in Roots) sb.AppendLine("DataRoot=" + r);
                foreach (var kv in Names) sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    internal static class Win
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool SetWindowText(IntPtr hWnd, string text);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        public static string Title(IntPtr h)
        {
            var sb = new StringBuilder(512);
            if (h == IntPtr.Zero) return "";
            try { GetWindowText(h, sb, sb.Capacity); }
            catch { }
            return sb.ToString();
        }

        public static void SetTitle(IntPtr h, string name)
        {
            try { SetWindowText(h, name); }
            catch { }
        }
    }

    /// <summary>Restart Manager: find which process keeps a file open.</summary>
    internal static class RM
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        private const int CCH_RM_MAX_APP_NAME = 255;
        private const int CCH_RM_MAX_SVC_NAME = 63;

        private enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;
            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, [In] RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

        public static int[] WhoUses(string file)
        {
            uint session;
            if (RmStartSession(out session, 0, Guid.NewGuid().ToString()) != 0) return new int[0];
            try
            {
                if (RmRegisterResources(session, 1, new string[] { file }, 0, null, 0, null) != 0) return new int[0];

                uint needed = 0;
                uint count = 0;
                uint reasons = 0;
                int res = RmGetList(session, out needed, ref count, null, ref reasons);
                if (res == 234 && needed > 0)
                {
                    var arr = new RM_PROCESS_INFO[needed];
                    count = needed;
                    res = RmGetList(session, out needed, ref count, arr, ref reasons);
                    if (res == 0)
                    {
                        var pids = new int[count];
                        for (int i = 0; i < count; i++) pids[i] = arr[i].Process.dwProcessId;
                        return pids;
                    }
                }
                return new int[0];
            }
            finally
            {
                RmEndSession(session);
            }
        }
    }

    internal static class WeChat
    {
        private static readonly string[] DbCandidates = new string[]
        {
            @"db_storage\session\session.db",
            @"db_storage\message\message_0.db",
            @"db_storage\contact\contact.db"
        };

        public static bool IsWeChatName(string name)
        {
            return name.Equals("Weixin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("WeChat", StringComparison.OrdinalIgnoreCase);
        }

        public static List<Process> MainProcesses()
        {
            var list = new List<Process>();
            foreach (var name in new string[] { "Weixin", "WeChat" })
            {
                Process[] found;
                try { found = Process.GetProcessesByName(name); }
                catch { continue; }
                foreach (var p in found)
                {
                    try
                    {
                        p.Refresh();
                        if (p.MainWindowHandle == IntPtr.Zero) continue;
                        list.Add(p);
                    }
                    catch { }
                }
            }
            return list;
        }

        public static List<string> AccountDirs(IEnumerable<string> roots)
        {
            var list = new List<string>();
            foreach (var r in roots)
            {
                try
                {
                    if (!Directory.Exists(r)) continue;
                    list.AddRange(Directory.GetDirectories(r, "wxid_*"));
                }
                catch { }
            }
            return list;
        }

        public static string FindDb(string accountDir)
        {
            foreach (var rel in DbCandidates)
            {
                var f = Path.Combine(accountDir, rel);
                try { if (File.Exists(f)) return f; }
                catch { }
            }
            try
            {
                var dbDir = Path.Combine(accountDir, "db_storage");
                if (Directory.Exists(dbDir))
                {
                    var any = Directory.GetFiles(dbDir, "*.db", SearchOption.AllDirectories);
                    if (any.Length > 0) return any[0];
                }
            }
            catch { }
            return null;
        }

        public static Dictionary<int, string> BuildPidMap(IEnumerable<string> roots)
        {
            var map = new Dictionary<int, string>();
            foreach (var dir in AccountDirs(roots))
            {
                var db = FindDb(dir);
                if (db == null) continue;
                int[] pids;
                try { pids = RM.WhoUses(db); }
                catch { continue; }
                var wxid = Path.GetFileName(dir.TrimEnd('\\'));
                foreach (var pid in pids)
                {
                    if (!map.ContainsKey(pid)) map[pid] = wxid;
                }
            }
            return map;
        }
    }

    internal sealed class HotkeyForm : Form
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId = 0xA11;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;

        public event Action HotkeyPressed;

        public HotkeyForm()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-30000, -30000);
            Size = new Size(1, 1);
            Show();
        }

        public bool Register()
        {
            return RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.R);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                var handler = HotkeyPressed;
                if (handler != null) handler();
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            try { UnregisterHotKey(Handle, HotkeyId); }
            catch { }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    internal sealed class RenameForm : Form
    {
        private readonly TextBox _box;

        public string Value { get { return _box.Text; } }

        public RenameForm(string current)
        {
            Text = "微信任务栏改名";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ClientSize = new Size(372, 112);
            Font = new Font("Microsoft YaHei UI", 9f);

            var label = new Label();
            label.Text = "这个名字会显示在任务栏上：";
            label.AutoSize = true;
            label.Location = new Point(16, 16);
            Controls.Add(label);

            _box = new TextBox();
            _box.Text = current;
            _box.Location = new Point(16, 42);
            _box.Width = 336;
            Controls.Add(_box);

            var ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(196, 78);
            ok.Width = 76;
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(278, 78);
            cancel.Width = 76;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            _box.SelectAll();
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly HotkeyForm _hotkey;
        private readonly Config _cfg;
        private MenuItem _autostartItem;
        private readonly Dictionary<int, string> _pidMap = new Dictionary<int, string>();
        private DateTime _mapStamp = DateTime.MinValue;

        public TrayApp()
        {
            _cfg = Config.Load();

            _tray = new NotifyIcon();
            _tray.Icon = SystemIcons.Application;
            _tray.Text = "微信任务栏改名";
            _tray.Visible = true;

            var menu = new ContextMenu();
            menu.MenuItems.Add("给当前微信窗口改名 (Ctrl+Alt+R)", new EventHandler(OnMenuRename));
            menu.MenuItems.Add("重新扫描并应用", new EventHandler(OnMenuApply));
            menu.MenuItems.Add("选择微信数据目录…", new EventHandler(OnMenuPickRoot));
            menu.MenuItems.Add("打开配置文件", new EventHandler(OnMenuOpenConfig));

            _autostartItem = new MenuItem("开机自动启动");
            _autostartItem.Click += new EventHandler(OnMenuAutostart);
            menu.MenuItems.Add(_autostartItem);
            menu.Popup += new EventHandler(OnMenuPopup);

            menu.MenuItems.Add("-");
            menu.MenuItems.Add("退出", new EventHandler(OnMenuQuit));
            _tray.ContextMenu = menu;
            _tray.DoubleClick += new EventHandler(OnMenuRename);

            EnsureRoots();

            _hotkey = new HotkeyForm();
            _hotkey.HotkeyPressed += new Action(RenameForeground);
            if (!_hotkey.Register())
            {
                _tray.ShowBalloonTip(5000, "微信任务栏改名",
                    "Ctrl+Alt+R 被别的程序占用了，可以用托盘菜单改名。", ToolTipIcon.Warning);
            }

            RefreshMap();
            Apply();
            UpdateStatus();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 2000;
            _timer.Tick += new EventHandler(OnTick);
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            Apply();
            UpdateStatus();
        }

        private void OnMenuRename(object sender, EventArgs e)
        {
            RenameForeground();
        }

        private void OnMenuApply(object sender, EventArgs e)
        {
            RefreshMap();
            Apply();
        }

        private void OnMenuPickRoot(object sender, EventArgs e)
        {
            PickRoot();
            RefreshMap();
            Apply();
        }

        private void OnMenuOpenConfig(object sender, EventArgs e)
        {
            try { Process.Start("notepad.exe", Config.FilePath); }
            catch { }
        }

        private void OnMenuQuit(object sender, EventArgs e)
        {
            _timer.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            try { _hotkey.Close(); }
            catch { }
            ExitThread();
        }

        private void Apply()
        {
            try
            {
                foreach (var p in WeChat.MainProcesses())
                {
                    IntPtr h = p.MainWindowHandle;
                    string title = Win.Title(h);
                    if (title.Length == 0) continue;

                    string wxid = Resolve(p.Id);
                    if (wxid == null && DateTime.Now - _mapStamp > TimeSpan.FromSeconds(3))
                    {
                        RefreshMap();
                        wxid = Resolve(p.Id);
                    }
                    if (wxid == null) continue;

                    string name;
                    if (_cfg.Names.TryGetValue(wxid, out name) && name.Length > 0)
                    {
                        if (title != name) Win.SetTitle(h, name);
                    }
                    else if (title != "微信" && title != "Weixin" && title != "WeChat")
                    {
                        _cfg.Names[wxid] = title;
                        _cfg.Save();
                    }
                }
            }
            catch { }
        }

        private string Resolve(int pid)
        {
            string v;
            if (_pidMap.TryGetValue(pid, out v)) return v;
            return null;
        }

        private void RefreshMap()
        {
            _pidMap.Clear();
            try
            {
                var map = WeChat.BuildPidMap(_cfg.Roots);
                foreach (var kv in map) _pidMap[kv.Key] = kv.Value;
            }
            catch { }
            _mapStamp = DateTime.Now;
        }

        private void RenameForeground()
        {
            try
            {
                IntPtr h = Win.GetForegroundWindow();
                if (h == IntPtr.Zero) return;

                uint pid;
                Win.GetWindowThreadProcessId(h, out pid);
                string procName = "";
                try { procName = Process.GetProcessById((int)pid).ProcessName; }
                catch { }
                if (!WeChat.IsWeChatName(procName))
                {
                    Info("请先把要改名的微信窗口切到前台，再按 Ctrl+Alt+R。");
                    return;
                }

                string wxid = Resolve((int)pid);
                if (wxid == null)
                {
                    RefreshMap();
                    wxid = Resolve((int)pid);
                }
                if (wxid == null)
                {
                    Info("查不到这个窗口对应的账号目录。\n请用托盘菜单里的“选择微信数据目录…”确认路径。");
                    return;
                }

                using (var dlg = new RenameForm(Win.Title(h)))
                {
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    string name = dlg.Value.Trim();
                    if (name.Length == 0) return;
                    _cfg.Names[wxid] = name;
                    _cfg.Save();
                    Win.SetTitle(h, name);
                }
            }
            catch (Exception ex)
            {
                Info("改名失败：" + ex.Message);
            }
        }

        private void EnsureRoots()
        {
            var alive = _cfg.Roots.Where(Directory.Exists).ToList();
            _cfg.Roots = alive;
            if (alive.Count > 0) return;

            var found = DiscoverRoots();
            if (found.Count > 0)
            {
                _cfg.Roots.AddRange(found);
                _cfg.Save();
                return;
            }
            PickRoot();
        }

        /// <summary>扫常见位置（含各磁盘根、X:\Users\*\），找放着 wxid_* 目录的 xwechat_files。</summary>
        private static List<string> DiscoverRoots()
        {
            var seeds = new List<string>();
            seeds.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "xwechat_files"));
            seeds.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "xwechat_files"));
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    var root = drive.RootDirectory.FullName;
                    seeds.Add(Path.Combine(root, "xwechat_files"));
                    try
                    {
                        var users = Path.Combine(root, "Users");
                        if (Directory.Exists(users))
                        {
                            foreach (var userDir in Directory.GetDirectories(users))
                                seeds.Add(Path.Combine(userDir, "xwechat_files"));
                        }
                    }
                    catch { }
                }
            }
            catch { }

            var found = new List<string>();
            foreach (var s in seeds)
            {
                try
                {
                    if (!Directory.Exists(s)) continue;
                    if (Directory.GetDirectories(s, "wxid_*").Length == 0) continue;
                    if (!found.Contains(s, StringComparer.OrdinalIgnoreCase)) found.Add(s);
                }
                catch { }
            }
            return found;
        }

        private void PickRoot()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "请选择微信数据目录（里面是 wxid_ 开头的文件夹）";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() == DialogResult.OK && dlg.SelectedPath.Length > 0)
                {
                    if (!_cfg.Roots.Contains(dlg.SelectedPath, StringComparer.OrdinalIgnoreCase))
                        _cfg.Roots.Add(dlg.SelectedPath);
                    _cfg.Save();
                }
            }
        }

        private void OnMenuAutostart(object sender, EventArgs e)
        {
            ToggleAutostart();
        }

        private void OnMenuPopup(object sender, EventArgs e)
        {
            try { _autostartItem.Checked = IsAutostart(); }
            catch { }
        }

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "WeChatTabName";

        private static bool IsAutostart()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (key == null) return false;
                    var v = key.GetValue(RunValueName) as string;
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch { return false; }
        }

        private void ToggleAutostart()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return;
                    if (IsAutostart()) key.DeleteValue(RunValueName, false);
                    else key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch (Exception ex)
            {
                Info("设置开机启动失败：" + ex.Message);
            }
        }

        private void UpdateStatus()
        {
            try
            {
                int windows = WeChat.MainProcesses().Count;
                _tray.Text = "微信任务栏改名：窗口 " + windows + " 个 / 已记住 " + _cfg.Names.Count + " 个账号";
            }
            catch { }
        }

        private static void Info(string text)
        {
            MessageBox.Show(text, "微信任务栏改名", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
