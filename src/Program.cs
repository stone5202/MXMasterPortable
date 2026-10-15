using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace MXMasterWin
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException)
            {
                SetProcessDPIAware();
            }
            // Two instances would fight over the global mouse hook and HID++ requests.
            // A second launch only asks the running one to show its window.
            bool createdNew;
            // The mutex and window message keep the pre-2.0 name so that an old MX Master Portable
            // copy and this one still count as the same app.
            using (var mutex = new Mutex(true, @"Local\MXMasterPortable.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    PostMessage(new IntPtr(0xFFFF), ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mxmaster-win.ini");
                // Up to 1.1 the app was called MX Master Portable; carry its settings file over once.
                try
                {
                    string oldConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mxmaster-portable.ini");
                    if (!File.Exists(configPath) && File.Exists(oldConfigPath)) File.Move(oldConfigPath, configPath);
                }
                catch { }
                Lang.Load(configPath);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                GC.KeepAlive(mutex);
            }
            // The mutex is gone by now, so the relaunched copy is not mistaken for a second instance.
            if (RestartRequested)
            {
                try { Process.Start(Application.ExecutablePath, "--show"); }
                catch { }
            }
        }

        internal const string Version = "2.1";

        // Set before closing the main window to start the app again (used after a language change).
        internal static bool RestartRequested;

        internal static readonly int ShowWindowMessage = (int)RegisterWindowMessage("MXMasterPortable.ShowWindow");

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();
    }

    // Two-language UI text. The choice is read from the INI before any window is built, so every
    // string is picked once; changing the language restarts the app.
    internal static class Lang
    {
        // "Auto" follows the Windows display language; "zh" and "en" force one.
        public static string Setting = "Auto";
        public static bool English;

        public static void Load(string iniPath)
        {
            try
            {
                if (File.Exists(iniPath))
                {
                    foreach (string line in File.ReadAllLines(iniPath))
                    {
                        string[] parts = line.Trim().Split(new[] { '=' }, 2);
                        if (parts.Length == 2 && parts[0].Trim().Equals("Language", StringComparison.OrdinalIgnoreCase))
                            Setting = Normalize(parts[1]);
                    }
                }
            }
            catch { }
            English = Setting == "en" || (Setting == "Auto" &&
                !System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        }

        public static string Normalize(string value)
        {
            string text = (value ?? "").Trim();
            if (text.Equals("en", StringComparison.OrdinalIgnoreCase)) return "en";
            if (text.Equals("zh", StringComparison.OrdinalIgnoreCase)) return "zh";
            return "Auto";
        }

        public static string T(string chinese, string english)
        {
            return English ? english : chinese;
        }
    }

    internal enum MouseInput
    {
        Middle,
        XButton1,
        XButton2,
        WheelLeft,
        WheelRight,
        GestureButton,
        ActionRingButton,
        WheelModeButton
    }

    internal enum MappedAction
    {
        PassThrough,
        BrowserBack,
        BrowserForward,
        Copy,
        Paste,
        Cut,
        Undo,
        Redo,
        TaskView,
        ShowDesktop,
        DesktopLeft,
        DesktopRight,
        AltTab,
        PlayPause,
        VolumeUp,
        VolumeDown,
        Mute,
        Enter,
        Escape,
        NextTrack,
        PrevTrack,
        FileExplorer,
        Settings,
        Calculator,
        TaskManager,
        Search,
        StartMenu,
        NewTab,
        CloseTab,
        ReopenTab,
        Refresh,
        ZoomIn,
        ZoomOut,
        Screenshot,
        Emoji,
        ClipboardHistory,
        LockPC,
        QuickSettings,
        MinimizeWindow,
        MaximizeWindow,
        CloseWindow,
        SnapLeft,
        SnapRight,
        SelectAll,
        Save,
        Find,
        Delete,
        InsertDate,
        InsertTime,
        OpenSettings,
        CustomShortcut,
        LaunchProgram
    }

    // An action plus its optional argument (shortcut text or program command line).
    // INI form: "Copy", "CustomShortcut|Ctrl+Shift+T", "LaunchProgram|\"C:\\app.exe\" -arg".
    internal sealed class ActionBinding
    {
        public readonly MappedAction Action;
        public readonly string Argument;

        public ActionBinding(MappedAction action, string argument)
        {
            Action = action;
            Argument = argument == null ? "" : argument.Trim();
        }

        public static ActionBinding Of(MappedAction action)
        {
            return new ActionBinding(action, "");
        }

        public bool HasArgument
        {
            get { return Action == MappedAction.CustomShortcut || Action == MappedAction.LaunchProgram; }
        }

        // True when triggering this binding must leave the original mouse event untouched.
        public bool IsPassThrough
        {
            get { return Action == MappedAction.PassThrough || (HasArgument && Argument.Length == 0); }
        }

        public static ActionBinding Parse(string text)
        {
            if (text == null) return null;
            string[] parts = text.Split(new[] { '|' }, 2);
            MappedAction action;
            if (!Enum.TryParse(parts[0].Trim(), true, out action) || !Enum.IsDefined(typeof(MappedAction), action))
                return null;
            return new ActionBinding(action, parts.Length > 1 ? parts[1] : "");
        }

        public override string ToString()
        {
            return HasArgument && Argument.Length > 0 ? Action + "|" + Argument : Action.ToString();
        }

        public string ShortLabel
        {
            get
            {
                if (IsPassThrough) return "—";
                if (Action == MappedAction.CustomShortcut) return Clip(Argument);
                if (Action == MappedAction.LaunchProgram) return Clip(ProgramName(Argument));
                return ActionCatalog.ShortLabel(Action);
            }
        }

        public static string ProgramName(string command)
        {
            string file, arguments;
            ActionRunner.SplitCommand(command, out file, out arguments);
            if (file.IndexOf("://", StringComparison.Ordinal) >= 0) return file;
            try
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!String.IsNullOrEmpty(name)) return name;
            }
            catch { }
            return file;
        }

        private static string Clip(string text)
        {
            return text.Length <= 14 ? text : text.Substring(0, 13) + "…";
        }
    }

    internal static class ShortcutText
    {
        public static string Format(Keys key, bool ctrl, bool alt, bool shift, bool win)
        {
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (win) parts.Add("Win");
            if (key != Keys.None) parts.Add(key.ToString());
            return String.Join("+", parts.ToArray());
        }

        public static bool TryParse(string text, out Keys key, out bool ctrl, out bool alt, out bool shift, out bool win)
        {
            key = Keys.None;
            ctrl = alt = shift = win = false;
            if (String.IsNullOrEmpty(text)) return false;
            foreach (string raw in text.Split('+'))
            {
                string token = raw.Trim();
                if (token.Length == 0) return false;
                if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) ctrl = true;
                else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
                else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
                else if (token.Equals("Win", StringComparison.OrdinalIgnoreCase)) win = true;
                else
                {
                    Keys parsed;
                    if (key != Keys.None || !Enum.TryParse(token, true, out parsed)) return false;
                    int code = (int)parsed;
                    if (code <= 0 || code > 0xFE) return false;
                    key = parsed;
                }
            }
            return key != Keys.None;
        }
    }

    internal sealed class ActionChoice
    {
        public MappedAction Action { get; private set; }
        public string Label { get; private set; }

        public ActionChoice(MappedAction action, string label)
        {
            Action = action;
            Label = label;
        }
    }

    internal static class ActionCatalog
    {
        public static List<ActionChoice> CreateChoices()
        {
            return new List<ActionChoice>
            {
                new ActionChoice(MappedAction.PassThrough, Lang.T("保留原功能（不攔截）", "Keep original function")),
                new ActionChoice(MappedAction.OpenSettings, Lang.T("MX設定", "MX Settings")),
                new ActionChoice(MappedAction.BrowserBack, Lang.T("上一頁", "Back")),
                new ActionChoice(MappedAction.BrowserForward, Lang.T("下一頁", "Forward")),
                new ActionChoice(MappedAction.Copy, Lang.T("複製", "Copy")),
                new ActionChoice(MappedAction.Paste, Lang.T("貼上", "Paste")),
                new ActionChoice(MappedAction.Cut, Lang.T("剪下", "Cut")),
                new ActionChoice(MappedAction.Undo, Lang.T("復原", "Undo")),
                new ActionChoice(MappedAction.Redo, Lang.T("重做", "Redo")),
                new ActionChoice(MappedAction.TaskView, Lang.T("工作檢視", "Task View")),
                new ActionChoice(MappedAction.ShowDesktop, Lang.T("顯示桌面", "Show desktop")),
                new ActionChoice(MappedAction.DesktopLeft, Lang.T("切換到左側虛擬桌面", "Virtual desktop left")),
                new ActionChoice(MappedAction.DesktopRight, Lang.T("切換到右側虛擬桌面", "Virtual desktop right")),
                new ActionChoice(MappedAction.AltTab, Lang.T("切換視窗（Alt+Tab）", "Switch window (Alt+Tab)")),
                new ActionChoice(MappedAction.PlayPause, Lang.T("播放／暫停", "Play / pause")),
                new ActionChoice(MappedAction.VolumeUp, Lang.T("提高音量", "Volume up")),
                new ActionChoice(MappedAction.VolumeDown, Lang.T("降低音量", "Volume down")),
                new ActionChoice(MappedAction.Mute, Lang.T("靜音／取消靜音", "Mute / unmute")),
                new ActionChoice(MappedAction.NextTrack, Lang.T("下一首", "Next track")),
                new ActionChoice(MappedAction.PrevTrack, Lang.T("上一首", "Previous track")),
                new ActionChoice(MappedAction.FileExplorer, Lang.T("檔案總管", "File Explorer")),
                new ActionChoice(MappedAction.Settings, Lang.T("Windows 設定", "Windows Settings")),
                new ActionChoice(MappedAction.Calculator, Lang.T("小算盤", "Calculator")),
                new ActionChoice(MappedAction.TaskManager, Lang.T("工作管理員", "Task Manager")),
                new ActionChoice(MappedAction.Search, Lang.T("Windows 搜尋", "Windows Search")),
                new ActionChoice(MappedAction.StartMenu, Lang.T("開始功能表", "Start menu")),
                new ActionChoice(MappedAction.NewTab, Lang.T("新分頁", "New tab")),
                new ActionChoice(MappedAction.CloseTab, Lang.T("關閉分頁", "Close tab")),
                new ActionChoice(MappedAction.ReopenTab, Lang.T("重新開啟已關閉的分頁", "Reopen closed tab")),
                new ActionChoice(MappedAction.Refresh, Lang.T("重新整理", "Refresh")),
                new ActionChoice(MappedAction.ZoomIn, Lang.T("放大", "Zoom in")),
                new ActionChoice(MappedAction.ZoomOut, Lang.T("縮小", "Zoom out")),
                new ActionChoice(MappedAction.Screenshot, Lang.T("螢幕截圖（剪取工具）", "Screenshot (Snipping Tool)")),
                new ActionChoice(MappedAction.Emoji, Lang.T("表情符號", "Emoji panel")),
                new ActionChoice(MappedAction.ClipboardHistory, Lang.T("剪貼簿歷程記錄", "Clipboard history")),
                new ActionChoice(MappedAction.LockPC, Lang.T("鎖定電腦", "Lock PC")),
                new ActionChoice(MappedAction.QuickSettings, Lang.T("快速設定（Win+A）", "Quick settings (Win+A)")),
                new ActionChoice(MappedAction.MinimizeWindow, Lang.T("最小化視窗", "Minimize window")),
                new ActionChoice(MappedAction.MaximizeWindow, Lang.T("最大化視窗", "Maximize window")),
                new ActionChoice(MappedAction.CloseWindow, Lang.T("關閉視窗（Alt+F4）", "Close window (Alt+F4)")),
                new ActionChoice(MappedAction.SnapLeft, Lang.T("視窗貼齊左側", "Snap window left")),
                new ActionChoice(MappedAction.SnapRight, Lang.T("視窗貼齊右側", "Snap window right")),
                new ActionChoice(MappedAction.SelectAll, Lang.T("全選", "Select all")),
                new ActionChoice(MappedAction.Save, Lang.T("儲存", "Save")),
                new ActionChoice(MappedAction.Find, Lang.T("尋找", "Find")),
                new ActionChoice(MappedAction.Delete, Lang.T("Delete 鍵", "Delete key")),
                new ActionChoice(MappedAction.InsertDate, Lang.T("輸入今天日期", "Type today's date")),
                new ActionChoice(MappedAction.InsertTime, Lang.T("輸入目前時間", "Type the current time")),
                new ActionChoice(MappedAction.Enter, Lang.T("Enter 鍵", "Enter key")),
                new ActionChoice(MappedAction.Escape, Lang.T("Esc 鍵", "Esc key")),
                new ActionChoice(MappedAction.CustomShortcut, Lang.T("自訂快捷鍵…", "Custom shortcut…")),
                new ActionChoice(MappedAction.LaunchProgram, Lang.T("啟動程式或開啟檔案…", "Launch program / file…"))
            };
        }

        // Compact wording for the Actions Ring, where each slice only fits a few characters.
        public static string ShortLabel(MappedAction action)
        {
            switch (action)
            {
                case MappedAction.PassThrough: return "—";
                case MappedAction.DesktopLeft: return Lang.T("左側桌面", "Left desktop");
                case MappedAction.DesktopRight: return Lang.T("右側桌面", "Right desktop");
                case MappedAction.AltTab: return Lang.T("切換視窗", "Switch window");
                case MappedAction.Mute: return Lang.T("靜音", "Mute");
                case MappedAction.Enter: return "Enter";
                case MappedAction.Escape: return "Esc";
                case MappedAction.Delete: return "Delete";
                case MappedAction.Settings: return Lang.T("設定", "Settings");
                case MappedAction.Search: return Lang.T("搜尋", "Search");
                case MappedAction.ReopenTab: return Lang.T("重開分頁", "Reopen tab");
                case MappedAction.Screenshot: return Lang.T("螢幕截圖", "Screenshot");
                case MappedAction.Emoji: return Lang.T("表情符號", "Emoji");
                case MappedAction.ClipboardHistory: return Lang.T("剪貼簿", "Clipboard");
                case MappedAction.QuickSettings: return Lang.T("快速設定", "Quick settings");
                case MappedAction.MinimizeWindow: return Lang.T("最小化", "Minimize");
                case MappedAction.MaximizeWindow: return Lang.T("最大化", "Maximize");
                case MappedAction.CloseWindow: return Lang.T("關閉視窗", "Close window");
                case MappedAction.SnapLeft: return Lang.T("貼齊左側", "Snap left");
                case MappedAction.SnapRight: return Lang.T("貼齊右側", "Snap right");
                case MappedAction.InsertDate: return Lang.T("今天日期", "Date");
                case MappedAction.InsertTime: return Lang.T("目前時間", "Time");
                case MappedAction.OpenSettings: return Lang.T("MX設定", "MX Settings");
            }
            foreach (ActionChoice choice in CreateChoices())
                if (choice.Action == action) return choice.Label;
            return action.ToString();
        }
    }

    // A mapping drop-down plus the button that edits a shortcut or program argument.
    internal sealed class ActionPicker
    {
        public readonly ComboBox Combo = new ComboBox();
        public readonly Button Edit;
        private string shortcut = "";
        private string program = "";
        private int committedIndex;
        private readonly int spacing;

        // Raised only for changes made by the user, not when Binding is assigned from code.
        public event Action Changed;

        public ActionPicker(Control parent, Point location, int comboWidth, int editWidth, int gap)
        {
            spacing = gap;
            Combo.DropDownStyle = ComboBoxStyle.DropDownList;
            Combo.Location = location;
            Combo.Width = comboWidth;
            Combo.FormattingEnabled = true;
            Combo.MaxDropDownItems = 18;
            Combo.Format += delegate(object sender, ListControlConvertEventArgs e)
            {
                ActionChoice choice = e.ListItem as ActionChoice;
                if (choice == null) return;
                if (choice.Action == MappedAction.CustomShortcut && shortcut.Length > 0)
                    e.Value = Lang.T("快捷鍵：", "Shortcut: ") + shortcut;
                else if (choice.Action == MappedAction.LaunchProgram && program.Length > 0)
                    e.Value = Lang.T("啟動：", "Launch: ") + ActionBinding.ProgramName(program);
                else
                    e.Value = choice.Label;
            };
            foreach (ActionChoice choice in ActionCatalog.CreateChoices())
                Combo.Items.Add(choice);
            Combo.SelectedIndex = 0;
            Combo.SelectionChangeCommitted += delegate { OnUserSelected(); };
            Combo.SelectedIndexChanged += delegate { UpdateEditState(); };
            parent.Controls.Add(Combo);

            Edit = Theme.CreateButton(Lang.T("設定", "Edit"), false);
            Edit.Font = new Font(Theme.UiFont, 8.5F);
            Edit.Location = new Point(location.X + comboWidth + gap, location.Y);
            Edit.Size = new Size(editWidth, Combo.Height);
            Edit.Click += delegate { if (EditArgument()) OnChanged(); };
            // The ComboBox only reaches its final height once its font and handle are resolved.
            Combo.SizeChanged += delegate { Edit.Height = Combo.Height; };
            Combo.HandleCreated += delegate { Edit.Height = Combo.Height; };
            parent.Controls.Add(Edit);
            UpdateEditState();
        }

        private MappedAction SelectedAction
        {
            get
            {
                ActionChoice choice = Combo.SelectedItem as ActionChoice;
                return choice == null ? MappedAction.PassThrough : choice.Action;
            }
        }

        public ActionBinding Binding
        {
            get
            {
                MappedAction action = SelectedAction;
                string argument = action == MappedAction.CustomShortcut ? shortcut
                    : action == MappedAction.LaunchProgram ? program : "";
                return new ActionBinding(action, argument);
            }
            set
            {
                ActionBinding binding = value ?? ActionBinding.Of(MappedAction.PassThrough);
                if (binding.Action == MappedAction.CustomShortcut) shortcut = binding.Argument;
                else if (binding.Action == MappedAction.LaunchProgram) program = binding.Argument;
                int index = 0;
                for (int i = 0; i < Combo.Items.Count; i++)
                {
                    ActionChoice choice = Combo.Items[i] as ActionChoice;
                    if (choice != null && choice.Action == binding.Action) { index = i; break; }
                }
                Combo.SelectedIndex = index;
                committedIndex = index;
                RefreshText();
            }
        }

        private void OnUserSelected()
        {
            MappedAction action = SelectedAction;
            bool needsArgument = (action == MappedAction.CustomShortcut && shortcut.Length == 0) ||
                (action == MappedAction.LaunchProgram && program.Length == 0);
            if (!needsArgument)
            {
                committedIndex = Combo.SelectedIndex;
                OnChanged();
                return;
            }
            // Let the drop-down close before the modal editor opens; cancelling restores the old choice.
            Combo.BeginInvoke(new Action(delegate
            {
                if (EditArgument())
                {
                    committedIndex = Combo.SelectedIndex;
                    OnChanged();
                }
                else Combo.SelectedIndex = committedIndex;
            }));
        }

        private void OnChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        private bool EditArgument()
        {
            MappedAction action = SelectedAction;
            Form owner = Combo.FindForm();
            if (action == MappedAction.CustomShortcut)
            {
                string result = ShortcutDialog.Prompt(owner, shortcut);
                if (result == null) return false;
                shortcut = result;
            }
            else if (action == MappedAction.LaunchProgram)
            {
                string result = ProgramDialog.Prompt(owner, program);
                if (result == null) return false;
                program = result;
            }
            else return false;
            RefreshText();
            return true;
        }

        private void RefreshText()
        {
            // Toggling formatting makes the ComboBox re-query the Format handler for its items.
            Combo.FormattingEnabled = false;
            Combo.FormattingEnabled = true;
            UpdateEditState();
        }

        private void UpdateEditState()
        {
            if (Edit == null) return;
            MappedAction action = SelectedAction;
            // Edit.Visible reads false while the form is still hidden, so keep the decision in a local.
            bool editable = action == MappedAction.CustomShortcut || action == MappedAction.LaunchProgram;
            Edit.Visible = editable;
            // The drop-down takes the whole row unless the edit button needs its share.
            Combo.Width = editable ? Edit.Left - Combo.Left - spacing : Edit.Right - Combo.Left;
        }
    }

    internal static class DialogStyle
    {
        public static void Apply(Form form, string title, int logicalWidth, int logicalHeight, float scale)
        {
            form.Text = title;
            form.Font = new Font(Theme.UiFont, 9.5F);
            form.BackColor = Theme.Page;
            form.ForeColor = Theme.Text;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.CenterParent;
            form.ClientSize = new Size((int)Math.Round(logicalWidth * scale), (int)Math.Round(logicalHeight * scale));
        }
    }

    internal sealed class ShortcutDialog : Form
    {
        private readonly Label preview = new Label();
        private readonly CheckBox winKey = new CheckBox();
        private readonly Button ok;
        private Keys key = Keys.None;
        private bool ctrl, alt, shift;

        private ShortcutDialog(string current)
        {
            float scale = Theme.GetScale();
            Func<int, int> s = delegate(int v) { return (int)Math.Round(v * scale); };
            DialogStyle.Apply(this, Lang.T("自訂快捷鍵", "Custom shortcut"), 420, 214, scale);

            Controls.Add(new Label
            {
                Text = Lang.T("請直接按下要送出的組合鍵，例如 Ctrl+Shift+T。", "Press the key combination to send, e.g. Ctrl+Shift+T."),
                AutoSize = true,
                Location = new Point(s(20), s(18))
            });

            preview.Location = new Point(s(20), s(48));
            preview.Size = new Size(s(380), s(52));
            preview.TextAlign = ContentAlignment.MiddleCenter;
            preview.BackColor = Theme.Card;
            preview.BorderStyle = BorderStyle.FixedSingle;
            preview.Font = new Font("Segoe UI Semibold", 14F);
            Controls.Add(preview);

            winKey.Text = Lang.T("加入 Win 鍵（Windows 會攔截 Win 組合鍵，需在此勾選）", "Add the Win key (Windows intercepts Win combos)");
            winKey.AutoSize = true;
            winKey.Location = new Point(s(20), s(114));
            winKey.CheckedChanged += delegate { UpdatePreview(); };
            Controls.Add(winKey);

            ok = Theme.CreateButton(Lang.T("確定", "OK"), true);
            ok.Location = new Point(s(190), s(162));
            ok.Size = new Size(s(100), s(34));
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            Controls.Add(ok);
            Button cancel = Theme.CreateButton(Lang.T("取消", "Cancel"), false);
            cancel.Location = new Point(s(300), s(162));
            cancel.Size = new Size(s(100), s(34));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);

            bool win;
            if (ShortcutText.TryParse(current, out key, out ctrl, out alt, out shift, out win))
                winKey.Checked = win;
            UpdatePreview();
        }

        public static string Prompt(IWin32Window owner, string current)
        {
            using (var dialog = new ShortcutDialog(current))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK || dialog.key == Keys.None) return null;
                return ShortcutText.Format(dialog.key, dialog.ctrl, dialog.alt, dialog.shift, dialog.winKey.Checked);
            }
        }

        // Every key press is treated as shortcut input, so the dialog is confirmed with the mouse.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code != Keys.ControlKey && code != Keys.ShiftKey && code != Keys.Menu &&
                code != Keys.LWin && code != Keys.RWin && code != Keys.None)
            {
                key = code;
                ctrl = (keyData & Keys.Control) != 0;
                alt = (keyData & Keys.Alt) != 0;
                shift = (keyData & Keys.Shift) != 0;
                UpdatePreview();
            }
            return true;
        }

        private void UpdatePreview()
        {
            preview.Text = key == Keys.None ? Lang.T("（尚未設定）", "(not set)") : ShortcutText.Format(key, ctrl, alt, shift, winKey.Checked);
            ok.Enabled = key != Keys.None;
        }
    }

    internal sealed class ProgramDialog : Form
    {
        private readonly TextBox command = new TextBox();
        private readonly Button ok;

        private ProgramDialog(string current)
        {
            float scale = Theme.GetScale();
            Func<int, int> s = delegate(int v) { return (int)Math.Round(v * scale); };
            DialogStyle.Apply(this, Lang.T("啟動程式或開啟檔案", "Launch a program or open a file"), 520, 190, scale);

            Controls.Add(new Label
            {
                Text = Lang.T("程式、檔案、資料夾或網址：", "Program, file, folder or URL:"),
                AutoSize = true,
                Location = new Point(s(20), s(18))
            });

            command.Location = new Point(s(20), s(46));
            command.Width = s(380);
            command.Text = current ?? "";
            command.TextChanged += delegate { ok.Enabled = command.Text.Trim().Length > 0; };
            Controls.Add(command);

            Button browse = Theme.CreateButton(Lang.T("瀏覽…", "Browse…"), false);
            browse.Location = new Point(s(410), s(44));
            browse.Size = new Size(s(90), s(30));
            browse.Click += delegate
            {
                using (var picker = new OpenFileDialog())
                {
                    picker.Filter = Lang.T("程式 (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|所有檔案 (*.*)|*.*",
                        "Programs (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|All files (*.*)|*.*");
                    if (picker.ShowDialog(this) == DialogResult.OK)
                        command.Text = "\"" + picker.FileName + "\"";
                }
            };
            Controls.Add(browse);

            Controls.Add(new Label
            {
                Text = Lang.T("需要加參數時，請用引號包住程式路徑，例如 \"C:\\Tools\\app.exe\" -option",
                    "To pass arguments, quote the program path, e.g. \"C:\\Tools\\app.exe\" -option"),
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(s(20), s(86))
            });

            ok = Theme.CreateButton(Lang.T("確定", "OK"), true);
            ok.Location = new Point(s(290), s(138));
            ok.Size = new Size(s(100), s(34));
            ok.Enabled = command.Text.Trim().Length > 0;
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            Controls.Add(ok);
            Button cancel = Theme.CreateButton(Lang.T("取消", "Cancel"), false);
            cancel.Location = new Point(s(400), s(138));
            cancel.Size = new Size(s(100), s(34));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public static string Prompt(IWin32Window owner, string current)
        {
            using (var dialog = new ProgramDialog(current))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                string text = dialog.command.Text.Trim();
                return text.Length == 0 ? null : text;
            }
        }
    }

    internal sealed class RingDialog : Form
    {
        private readonly ActionPicker[] pickers = new ActionPicker[8];
        private readonly Label[] badges = new Label[8];
        private readonly PictureBox guide = new PictureBox();
        // Never shown: it only draws the numbered picture of the ring being edited.
        private readonly ActionRingOverlay renderer = new ActionRingOverlay();
        private int highlight = -1;

        private RingDialog(ActionBinding[] current)
        {
            float scale = Theme.GetScale();
            Func<int, int> s = delegate(int v) { return (int)Math.Round(v * scale); };
            DialogStyle.Apply(this, Lang.T("自訂 Actions Ring", "Customize Actions Ring"), 756, 430, scale);

            Controls.Add(new Label
            {
                Text = Lang.T("圖上的編號對應右邊各列；點圖上的一格可直接設定該位置。", "Numbers on the picture match the rows; click a slice to set it."),
                AutoSize = true,
                Location = new Point(s(20), s(16))
            });

            guide.SizeMode = PictureBoxSizeMode.Zoom;
            guide.Location = new Point(s(20), s(50));
            guide.Size = new Size(s(308), s(308));
            guide.Cursor = Cursors.Hand;
            guide.MouseMove += delegate(object sender, MouseEventArgs e) { SetHighlight(renderer.SliceAt(e.Location, guide.Size)); };
            guide.MouseLeave += delegate { SetHighlight(-1); };
            guide.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                int slice = renderer.SliceAt(e.Location, guide.Size);
                if (slice < 0) return;
                pickers[slice].Combo.Focus();
                pickers[slice].Combo.DroppedDown = true;
            };
            Controls.Add(guide);

            for (int i = 0; i < 8; i++)
            {
                int index = i;
                int y = 50 + i * 38;
                Label badge = new Label { Location = new Point(s(350), s(y + 2)), Size = new Size(s(22), s(22)) };
                badge.Paint += delegate(object sender, PaintEventArgs e)
                {
                    bool active = highlight == index;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var circle = new RectangleF(1F, 1F, badge.Width - 2.5F, badge.Height - 2.5F);
                    using (var fill = new SolidBrush(active ? Theme.Accent : Theme.Card))
                    using (var ring = new Pen(Theme.Accent, 1.5F * scale))
                    using (var ink = new SolidBrush(active ? Color.White : Theme.Accent))
                    using (var font = new Font("Segoe UI", 8F, FontStyle.Bold))
                    using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        e.Graphics.FillEllipse(fill, circle);
                        e.Graphics.DrawEllipse(ring, circle);
                        e.Graphics.DrawString((index + 1).ToString(), font, ink, circle, center);
                    }
                };
                badge.MouseEnter += delegate { SetHighlight(index); };
                badges[i] = badge;
                Controls.Add(badge);

                pickers[i] = new ActionPicker(this, new Point(s(384), s(y)), s(294), s(52), s(6));
                pickers[i].Binding = current[i];
                pickers[i].Changed += delegate { RedrawGuide(); };
                pickers[i].Combo.Enter += delegate { SetHighlight(index); };
                pickers[i].Combo.MouseEnter += delegate { SetHighlight(index); };
            }

            Button reset = Theme.CreateButton(Lang.T("還原預設", "Reset"), false);
            reset.Location = new Point(s(20), s(378));
            reset.Size = new Size(s(100), s(34));
            reset.Click += delegate
            {
                ActionBinding[] defaults = ActionRingOverlay.DefaultSlots();
                for (int i = 0; i < 8; i++) pickers[i].Binding = defaults[i];
                RedrawGuide();
            };
            Controls.Add(reset);
            Button ok = Theme.CreateButton(Lang.T("確定", "OK"), true);
            ok.Location = new Point(s(526), s(378));
            ok.Size = new Size(s(100), s(34));
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            Controls.Add(ok);
            Button cancel = Theme.CreateButton(Lang.T("取消", "Cancel"), false);
            cancel.Location = new Point(s(636), s(378));
            cancel.Size = new Size(s(100), s(34));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);

            RedrawGuide();
        }

        // Highlights one position in both the picture and the list.
        private void SetHighlight(int index)
        {
            if (index == highlight) return;
            highlight = index;
            foreach (Label badge in badges) badge.Invalidate();
            RedrawGuide();
        }

        private void RedrawGuide()
        {
            var bindings = new ActionBinding[8];
            for (int i = 0; i < 8; i++) bindings[i] = pickers[i] != null ? pickers[i].Binding : ActionBinding.Of(MappedAction.PassThrough);
            renderer.SetSlots(bindings);
            Image old = guide.Image;
            guide.Image = renderer.RenderGuide(highlight);
            if (old != null) old.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (guide.Image != null) guide.Image.Dispose();
                renderer.Dispose();
            }
            base.Dispose(disposing);
        }

        public static ActionBinding[] Prompt(IWin32Window owner, ActionBinding[] current)
        {
            using (var dialog = new RingDialog(current))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                var result = new ActionBinding[8];
                for (int i = 0; i < 8; i++) result[i] = dialog.pickers[i].Binding;
                return result;
            }
        }
    }

    // Optional start with Windows: a plain shortcut in the user's own Startup folder. It needs no
    // administrator rights, touches no registry key, and the user can see and delete it.
    internal static class StartupShortcut
    {
        private static string LinkPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MX Master Win.lnk"); }
        }

        // The shortcut written by versions before 2.0 (MX Master Portable).
        private static string OldLinkPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MX Master Portable.lnk"); }
        }

        public static bool Exists
        {
            get
            {
                try { return File.Exists(LinkPath) || File.Exists(OldLinkPath); }
                catch { return false; }
            }
        }

        // Writes (or rewrites) the shortcut so that it points at the EXE that is running now.
        public static bool Create(out string error)
        {
            error = "";
            object link = null;
            try
            {
                string exe = Application.ExecutablePath;
                link = new ShellLink();
                var shell = (IShellLinkW)link;
                shell.SetPath(exe);
                shell.SetWorkingDirectory(Path.GetDirectoryName(exe));
                shell.SetDescription("MX Master Win");
                shell.SetIconLocation(exe, 0);
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(LinkPath, false);
                if (File.Exists(OldLinkPath)) File.Delete(OldLinkPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (link != null) Marshal.ReleaseComObject(link);
            }
        }

        public static bool Remove(out string error)
        {
            error = "";
            try
            {
                if (File.Exists(LinkPath)) File.Delete(LinkPath);
                if (File.Exists(OldLinkPath)) File.Delete(OldLinkPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        // Only the vtable order matters here; the getters are declared just to keep the slots in place.
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int length, IntPtr findData, int flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int length);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int length);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int length);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCommand);
            void SetShowCmd(int showCommand);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int length, out int icon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, int reserved);
            void Resolve(IntPtr window, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
    }

    // Lets the user pick the action for each of the four gesture directions.
    internal sealed class GestureDialog : Form
    {
        private readonly ActionPicker[] pickers = new ActionPicker[4];

        // Up, right, down, left.
        public static ActionBinding[] Defaults()
        {
            return new[]
            {
                ActionBinding.Of(MappedAction.TaskView), ActionBinding.Of(MappedAction.BrowserForward),
                ActionBinding.Of(MappedAction.ShowDesktop), ActionBinding.Of(MappedAction.BrowserBack)
            };
        }

        private GestureDialog(ActionBinding[] current)
        {
            float scale = Theme.GetScale();
            Func<int, int> s = delegate(int v) { return (int)Math.Round(v * scale); };
            DialogStyle.Apply(this, Lang.T("自訂拇指手勢", "Customize gestures"), 470, 268, scale);

            Controls.Add(new Label
            {
                Text = Lang.T("按住拇指手勢鍵並往一個方向滑動時要執行的動作。", "What to run when you hold the gesture button and swipe."),
                AutoSize = true,
                Location = new Point(s(20), s(16))
            });

            string[] names =
            {
                Lang.T("↑  向上", "↑  Up"), Lang.T("→  向右", "→  Right"),
                Lang.T("↓  向下", "↓  Down"), Lang.T("←  向左", "←  Left")
            };
            for (int i = 0; i < 4; i++)
            {
                int y = 52 + i * 38;
                Controls.Add(new Label { Text = names[i], AutoSize = false, Location = new Point(s(20), s(y + 3)), Size = new Size(s(82), s(22)) });
                pickers[i] = new ActionPicker(this, new Point(s(106), s(y)), s(286), s(52), s(6));
                pickers[i].Binding = current[i];
            }

            Button reset = Theme.CreateButton(Lang.T("還原預設", "Reset"), false);
            reset.Location = new Point(s(20), s(216));
            reset.Size = new Size(s(100), s(34));
            reset.Click += delegate
            {
                ActionBinding[] defaults = Defaults();
                for (int i = 0; i < 4; i++) pickers[i].Binding = defaults[i];
            };
            Controls.Add(reset);
            Button ok = Theme.CreateButton(Lang.T("確定", "OK"), true);
            ok.Location = new Point(s(240), s(216));
            ok.Size = new Size(s(100), s(34));
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            Controls.Add(ok);
            Button cancel = Theme.CreateButton(Lang.T("取消", "Cancel"), false);
            cancel.Location = new Point(s(350), s(216));
            cancel.Size = new Size(s(100), s(34));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);
        }

        public static ActionBinding[] Prompt(IWin32Window owner, ActionBinding[] current)
        {
            using (var dialog = new GestureDialog(current))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                var result = new ActionBinding[4];
                for (int i = 0; i < 4; i++) result[i] = dialog.pickers[i].Binding;
                return result;
            }
        }
    }

    internal static class Theme
    {
        public static string UiFont
        {
            get { return Lang.English ? "Segoe UI" : "Microsoft JhengHei UI"; }
        }
        public static readonly Color Page = Color.FromArgb(242, 244, 248);
        public static readonly Color Card = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(222, 227, 235);
        public static readonly Color Divider = Color.FromArgb(236, 239, 245);
        public static readonly Color Text = Color.FromArgb(32, 37, 48);
        public static readonly Color Muted = Color.FromArgb(110, 119, 135);
        public static readonly Color Accent = Color.FromArgb(30, 107, 240);
        public static readonly Color AccentHover = Color.FromArgb(22, 90, 210);
        public static readonly Color AccentDown = Color.FromArgb(16, 74, 180);
        public static readonly Color HeaderStart = Color.FromArgb(24, 29, 44);
        public static readonly Color HeaderEnd = Color.FromArgb(44, 54, 82);
        public static readonly Color HeaderSubtitle = Color.FromArgb(176, 185, 204);
        public static readonly Color Console = Color.FromArgb(23, 27, 36);
        public static readonly Color ConsoleText = Color.FromArgb(205, 224, 245);
        public static readonly Color Good = Color.FromArgb(24, 160, 110);
        public static readonly Color Warning = Color.FromArgb(217, 138, 0);
        // Segoe MDL2 Assets ships with Windows 10 and 11; null means "draw text only".
        public static readonly string IconFont = FindIconFont();

        private static string FindIconFont()
        {
            const string name = "Segoe MDL2 Assets";
            try
            {
                using (var probe = new Font(name, 10F))
                    return probe.Name == name ? name : null;
            }
            catch { return null; }
        }

        // Pixel layout is authored at 96 DPI; fonts already scale because the process is DPI aware.
        public static float GetScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Max(1F, g.DpiX / 96F);
            }
            catch { return 1F; }
        }

        public static Icon LoadAppIcon()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MXMasterWin.app.ico"))
                {
                    if (stream != null) return new Icon(stream);
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        public static Image LoadLogo()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MXMasterWin.app.png"))
                {
                    if (stream != null)
                    {
                        using (var decoded = new Bitmap(stream))
                            return new Bitmap(decoded);
                    }
                }
            }
            catch { }
            return SystemIcons.Application.ToBitmap();
        }

        public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            float d = radius * 2F;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Button CreateButton(string text, bool primary)
        {
            return new RoundButton(primary) { Text = text };
        }
    }

    // Rounded, flat button used everywhere instead of the stock control.
    internal sealed class RoundButton : Button
    {
        private readonly bool primary;
        private readonly float scale = Theme.GetScale();
        private bool hover, pressed;

        public RoundButton(bool isPrimary)
        {
            primary = isPrimary;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill, border, ink;
            if (!Enabled)
            {
                fill = Color.FromArgb(236, 239, 244);
                border = Theme.CardBorder;
                ink = Color.FromArgb(160, 168, 182);
            }
            else if (primary)
            {
                fill = pressed ? Theme.AccentDown : hover ? Theme.AccentHover : Theme.Accent;
                border = fill;
                ink = Color.White;
            }
            else
            {
                fill = pressed ? Color.FromArgb(222, 228, 238) : hover ? Color.FromArgb(240, 243, 249) : Theme.Card;
                border = Color.FromArgb(198, 205, 217);
                ink = Theme.Text;
            }
            if (Focused && ShowFocusCues && !primary) border = Theme.Accent;

            var bounds = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            using (GraphicsPath path = Theme.RoundedRectangle(bounds, 6F * scale))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ink, fill,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // Horizontal slider that snaps to Increment. Drag, click, mouse wheel and arrow keys all work.
    internal sealed class SliderBar : Control
    {
        private readonly float scale = Theme.GetScale();
        private int minimum, maximum = 100, increment = 1, current;
        private bool dragging;
        public event EventHandler ValueChanged;

        public SliderBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return minimum; } set { minimum = value; Value = current; Invalidate(); } }
        public int Maximum { get { return maximum; } set { maximum = value; Value = current; Invalidate(); } }
        public int Increment { get { return increment; } set { increment = Math.Max(1, value); } }

        public int Value
        {
            get { return current; }
            set
            {
                int next = Math.Max(minimum, Math.Min(Math.Max(minimum, maximum), value));
                if (next == current) return;
                current = next;
                Invalidate();
                EventHandler handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private float KnobRadius { get { return 9F * scale; } }

        private void SetFromX(int x)
        {
            float left = KnobRadius, right = Width - KnobRadius;
            if (right <= left || maximum <= minimum) return;
            float t = Math.Max(0F, Math.Min(1F, (x - left) / (right - left)));
            int steps = (int)Math.Round(t * (maximum - minimum) / increment);
            Value = minimum + steps * increment;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            dragging = true;
            SetFromX(e.X);
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) SetFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value = current + Math.Sign(e.Delta) * increment;
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            return code == Keys.Left || code == Keys.Right || code == Keys.Up || code == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value = current - increment;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value = current + increment;
            else if (e.KeyCode == Keys.PageDown) Value = current - increment * 10;
            else if (e.KeyCode == Keys.PageUp) Value = current + increment * 10;
            else if (e.KeyCode == Keys.Home) Value = minimum;
            else if (e.KeyCode == Keys.End) Value = maximum;
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float radius = KnobRadius;
            float left = radius, right = Width - radius, middle = Height / 2F;
            float t = maximum > minimum ? (current - minimum) / (float)(maximum - minimum) : 0F;
            float knobX = left + (right - left) * t;
            float thickness = 4F * scale;
            Color accent = Enabled ? Theme.Accent : Color.FromArgb(190, 197, 210);

            using (var rail = new SolidBrush(Color.FromArgb(210, 216, 226)))
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(left, middle - thickness / 2F, right - left, thickness), thickness / 2F - 0.2F))
                g.FillPath(rail, path);
            if (knobX - left > thickness)
            {
                using (var filled = new SolidBrush(accent))
                using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(left, middle - thickness / 2F, knobX - left, thickness), thickness / 2F - 0.2F))
                    g.FillPath(filled, path);
            }

            if (Focused && Enabled)
            {
                using (var halo = new SolidBrush(Color.FromArgb(40, Theme.Accent)))
                    g.FillEllipse(halo, knobX - radius, middle - radius, radius * 2F, radius * 2F);
            }
            float knob = radius - 2F * scale;
            using (var ring = new Pen(accent, 2F * scale))
            {
                g.FillEllipse(Brushes.White, knobX - knob, middle - knob, knob * 2F, knob * 2F);
                g.DrawEllipse(ring, knobX - knob, middle - knob, knob * 2F, knob * 2F);
            }
        }
    }

    // A CheckBox drawn as a label with an on/off switch at its right edge.
    internal sealed class ToggleSwitch : CheckBox
    {
        private readonly float scale = Theme.GetScale();

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Cursor = Cursors.Hand;
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Invalidate();
            base.OnCheckedChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = Parent != null ? Parent.BackColor : Theme.Page;
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float trackWidth = 40F * scale;
            float trackHeight = 22F * scale;
            float trackLeft = Width - trackWidth - 1F;
            float trackTop = (Height - trackHeight) / 2F;
            Color track = Checked ? Theme.Accent : Color.FromArgb(196, 203, 216);
            if (!Enabled) track = Color.FromArgb(222, 227, 235);
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(trackLeft, trackTop, trackWidth, trackHeight), trackHeight / 2F - 0.5F))
            using (var brush = new SolidBrush(track))
                g.FillPath(brush, path);

            float knob = trackHeight - 6F * scale;
            float knobLeft = Checked ? trackLeft + trackWidth - knob - 3F * scale : trackLeft + 3F * scale;
            g.FillEllipse(Brushes.White, knobLeft, trackTop + 3F * scale, knob, knob);

            var textArea = new Rectangle(0, 0, (int)(trackLeft - 8F * scale), Height);
            TextRenderer.DrawText(g, Text, Font, textArea, Enabled ? Theme.Text : Theme.Muted, back,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // Engineering view: the raw event list that used to sit on the main page.
    internal sealed class DiagnosticsForm : Form
    {
        private readonly CheckBox liveLog;

        public DiagnosticsForm(CheckBox liveLogSwitch, ListBox eventLog, Action clearEvents, string logPath, Icon icon)
        {
            liveLog = liveLogSwitch;
            float scale = Theme.GetScale();
            Func<int, int> s = delegate(int v) { return (int)Math.Round(v * scale); };

            Text = Lang.T("MX Master Win — 工程模式", "MX Master Win — Engineering mode");
            Icon = icon;
            Font = new Font(Theme.UiFont, 9.5F);
            BackColor = Theme.Console;
            ForeColor = Theme.Text;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(s(780), s(520));
            MinimumSize = new Size(s(600), s(320));

            var console = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Console, Padding = new Padding(s(12)) };
            eventLog.Dock = DockStyle.Fill;
            eventLog.Font = new Font("Consolas", 9F);
            eventLog.HorizontalScrollbar = true;
            eventLog.IntegralHeight = false;
            eventLog.BorderStyle = BorderStyle.None;
            eventLog.BackColor = Theme.Console;
            eventLog.ForeColor = Theme.ConsoleText;
            console.Controls.Add(eventLog);
            Controls.Add(console);

            var bar = new Panel { Dock = DockStyle.Top, Height = s(70), BackColor = Theme.Card };
            Controls.Add(bar);

            liveLog.Location = new Point(s(16), s(10));
            liveLog.Size = new Size(s(190), s(26));
            bar.Controls.Add(liveLog);
            bar.Controls.Add(new Label
            {
                Text = Lang.T("記錄檔：", "Log file: ") + logPath + Lang.T("　（關閉此視窗會停止記錄）", "   (closing this window stops capture)"),
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.25F),
                Location = new Point(s(16), s(42)),
                Size = new Size(bar.Width - s(32), s(20)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });

            Button folder = Theme.CreateButton(Lang.T("開啟記錄資料夾", "Open log folder"), false);
            folder.Size = new Size(s(130), s(30));
            folder.Location = new Point(bar.Width - s(16) - folder.Width, s(8));
            folder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            folder.Click += delegate
            {
                try
                {
                    if (File.Exists(logPath)) Process.Start("explorer.exe", "/select,\"" + logPath + "\"");
                    else Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(logPath) + "\"");
                }
                catch { }
            };
            bar.Controls.Add(folder);

            Button copy = Theme.CreateButton(Lang.T("複製全部", "Copy all"), false);
            copy.Size = new Size(s(96), s(30));
            copy.Location = new Point(folder.Left - s(8) - copy.Width, s(8));
            copy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            copy.Click += delegate
            {
                var text = new StringBuilder();
                foreach (object item in eventLog.Items) text.AppendLine(item.ToString());
                try
                {
                    if (text.Length > 0) Clipboard.SetText(text.ToString());
                }
                catch { }
            };
            bar.Controls.Add(copy);

            Button clear = Theme.CreateButton(Lang.T("清除", "Clear"), false);
            clear.Size = new Size(s(80), s(30));
            clear.Location = new Point(copy.Left - s(8) - clear.Width, s(8));
            clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            clear.Click += delegate { clearEvents(); };
            bar.Controls.Add(clear);
        }

        // Closing only hides the window, and capture stops so nothing is logged unseen.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                liveLog.Checked = false;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }

    // Simplified top view of the mouse with a numbered badge per mappable control. The numbers
    // match the rows of the button list; the thumb wheel carries two rows (left and right).
    // The outline is an original schematic, not a reproduction of any product artwork.
    internal sealed class MouseDiagram : Control
    {
        private const float DesignWidth = 296F, DesignHeight = 372F, OffsetX = 36F, OffsetY = 24F;
        private const float BadgeRadius = 11F;

        private sealed class Spot
        {
            public int[] Rows;
            public PointF Target, Badge;
        }

        // Row order: 0 middle, 1 gesture, 2 side A, 3 side B, 4/5 thumb wheel, 6 Actions Ring pad, 7 wheel mode.
        private static readonly Spot[] Spots =
        {
            new Spot { Rows = new[] { 0 }, Target = new PointF(144F, 78F), Badge = new PointF(246F, 78F) },
            new Spot { Rows = new[] { 7 }, Target = new PointF(144F, 140F), Badge = new PointF(246F, 140F) },
            new Spot { Rows = new[] { 1 }, Target = new PointF(62F, 125F), Badge = new PointF(-14F, 112F) },
            new Spot { Rows = new[] { 4, 5 }, Target = new PointF(66F, 152F), Badge = new PointF(-10F, 143F) },
            new Spot { Rows = new[] { 2 }, Target = new PointF(68F, 192F), Badge = new PointF(-14F, 246F) },
            new Spot { Rows = new[] { 3 }, Target = new PointF(70F, 207F), Badge = new PointF(-14F, 276F) },
            new Spot { Rows = new[] { 6 }, Target = new PointF(40F, 184F), Badge = new PointF(40F, 184F) }
        };

        private readonly float scale = Theme.GetScale();
        private int highlight = -1;
        public event Action<int> HotspotClicked;
        public event Action HighlightChanged;

        public MouseDiagram()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        // The highlighted list row, or -1.
        public int Highlight
        {
            get { return highlight; }
            set
            {
                if (value == highlight) return;
                highlight = value;
                Invalidate();
                Action handler = HighlightChanged;
                if (handler != null) handler();
            }
        }

        private float Fit
        {
            get { return Math.Min(Width / DesignWidth, Height / DesignHeight); }
        }

        private static float HalfWidth(Spot spot)
        {
            return spot.Rows.Length > 1 ? BadgeRadius + 7F : BadgeRadius;
        }

        private Spot SpotAt(Point location)
        {
            float x = location.X / Fit - OffsetX, y = location.Y / Fit - OffsetY;
            foreach (Spot spot in Spots)
            {
                if (Math.Abs(x - spot.Badge.X) <= HalfWidth(spot) + 3F && Math.Abs(y - spot.Badge.Y) <= BadgeRadius + 3F)
                    return spot;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Spot spot = SpotAt(e.Location);
            Cursor = spot != null ? Cursors.Hand : Cursors.Default;
            if (spot != null && Array.IndexOf(spot.Rows, highlight) < 0) Highlight = spot.Rows[0];
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Spot spot = SpotAt(e.Location);
            Action<int> handler = HotspotClicked;
            if (spot != null && handler != null) handler(Array.IndexOf(spot.Rows, highlight) >= 0 ? highlight : spot.Rows[0]);
            base.OnMouseClick(e);
        }

        private static void Pill(Graphics g, Brush brush, float x, float y, float width, float height)
        {
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(x, y, width, height), Math.Min(width, height) / 2F - 0.3F))
                g.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(Fit, Fit);
            g.TranslateTransform(OffsetX, OffsetY);

            // Shell, clockwise from the front-left corner. Proportions follow the real mouse seen
            // from above: squared front, straight left flank, and the thumb rest flaring out below it.
            PointF[] outline =
            {
                new PointF(67F, 24F), new PointF(73F, 12F), new PointF(100F, 9F), new PointF(135F, 15F),
                new PointF(170F, 23F), new PointF(197F, 38F), new PointF(206F, 56F), new PointF(212F, 110F),
                new PointF(220F, 180F), new PointF(218F, 250F), new PointF(200F, 300F), new PointF(170F, 320F),
                new PointF(140F, 325F), new PointF(110F, 316F), new PointF(86F, 294F), new PointF(68F, 276F),
                new PointF(45F, 275F), new PointF(20F, 260F), new PointF(6F, 233F), new PointF(5F, 208F),
                new PointF(16F, 168F), new PointF(31F, 141F), new PointF(50F, 133F), new PointF(63F, 130F),
                new PointF(65F, 90F), new PointF(66F, 50F)
            };
            using (var shell = new GraphicsPath())
            {
                shell.AddClosedCurve(outline, 0.35F);
                using (var shadow = new SolidBrush(Color.FromArgb(14, 20, 30, 50)))
                {
                    for (int i = 4; i >= 1; i--)
                    {
                        g.TranslateTransform(0F, i * 2.5F);
                        g.FillPath(shadow, shell);
                        g.TranslateTransform(0F, -i * 2.5F);
                    }
                }
                using (var body = new LinearGradientBrush(new RectangleF(0F, 0F, 226F, 334F),
                    Color.FromArgb(92, 95, 104), Color.FromArgb(40, 42, 48), 65F))
                    g.FillPath(body, shell);

                // Thumb rest panel with its ripple texture, clipped to the shell.
                PointF[] rest =
                {
                    new PointF(23F, 168F), new PointF(35F, 146F), new PointF(60F, 140F), new PointF(68F, 200F),
                    new PointF(70F, 268F), new PointF(50F, 270F), new PointF(26F, 257F), new PointF(12F, 231F),
                    new PointF(11F, 208F)
                };
                using (var panel = new GraphicsPath())
                {
                    panel.AddClosedCurve(rest, 0.3F);
                    using (var fill = new SolidBrush(Color.FromArgb(46, 48, 55)))
                        g.FillPath(fill, panel);
                    GraphicsState state = g.Save();
                    g.SetClip(panel, CombineMode.Intersect);
                    using (var ripple = new Pen(Color.FromArgb(26, 255, 255, 255), 1F))
                    {
                        for (float r = 14F; r < 150F; r += 9F)
                            g.DrawEllipse(ripple, 60F - r, 162F - r, r * 2F, r * 2F);
                    }
                    g.Restore(state);
                    using (var rim = new Pen(Color.FromArgb(150, 24, 26, 32), 1.2F))
                        g.DrawPath(rim, panel);
                }
                using (var edge = new Pen(Color.FromArgb(30, 32, 38), 1.4F))
                    g.DrawPath(edge, shell);
            }

            using (var seam = new Pen(Color.FromArgb(150, 24, 26, 32), 1.3F))
            using (var soft = new Pen(Color.FromArgb(44, 255, 255, 255), 1.1F))
            using (var slot = new SolidBrush(Color.FromArgb(26, 28, 34)))
            using (var part = new SolidBrush(Color.FromArgb(104, 108, 120)))
            using (var steel = new LinearGradientBrush(new RectangleF(0F, 0F, 20F, 20F),
                Color.FromArgb(232, 234, 240), Color.FromArgb(150, 154, 166), 0F))
            using (var ridge = new Pen(Color.FromArgb(110, 40, 44, 56), 1F))
            {
                steel.WrapMode = WrapMode.TileFlipX;
                // Split between the two main buttons, and the line where they end.
                g.DrawLine(seam, 135F, 16F, 135F, 50F);
                g.DrawLine(seam, 135F, 107F, 135F, 121F);
                g.DrawCurve(seam, new[] { new PointF(66F, 132F), new PointF(96F, 124F), new PointF(140F, 121F), new PointF(213F, 113F) }, 0.4F);
                // Contour of the right flank.
                g.DrawCurve(soft, new[] { new PointF(207F, 196F), new PointF(211F, 250F), new PointF(199F, 292F) }, 0.5F);

                // Scroll wheel in its slot, then the mode button and status light behind it.
                using (GraphicsPath well = Theme.RoundedRectangle(new RectangleF(122F, 51F, 27F, 55F), 5F))
                    g.FillPath(slot, well);
                using (GraphicsPath wheel = Theme.RoundedRectangle(new RectangleF(127F, 56F, 17F, 45F), 4F))
                    g.FillPath(steel, wheel);
                for (float y = 61F; y < 99F; y += 4F) g.DrawLine(ridge, 128F, y, 143F, y);
                using (GraphicsPath mode = Theme.RoundedRectangle(new RectangleF(127F, 130F, 17F, 20F), 4F))
                {
                    g.FillPath(part, mode);
                    g.DrawPath(seam, mode);
                }
                using (var light = new SolidBrush(Color.FromArgb(70, 208, 146)))
                    g.FillEllipse(light, 133.2F, 158.5F, 4F, 4F);

                // Along the left flank, front to back: gesture button, thumb wheel, two side buttons.
                Pill(g, part, 60F, 116F, 8F, 18F);
                using (GraphicsPath thumb = Theme.RoundedRectangle(new RectangleF(63F, 142F, 14F, 39F), 3.5F))
                    g.FillPath(steel, thumb);
                for (float y = 146F; y < 180F; y += 4F) g.DrawLine(ridge, 64F, y, 76F, y);
                g.DrawLine(seam, 63F, 161.5F, 77F, 161.5F);
                using (var forward = new GraphicsPath())
                using (var back = new GraphicsPath())
                {
                    forward.AddPolygon(new[] { new PointF(64F, 185F), new PointF(77F, 185F), new PointF(75F, 199F), new PointF(66F, 199F) });
                    back.AddPolygon(new[] { new PointF(66.5F, 201F), new PointF(74.5F, 201F), new PointF(72F, 216F) });
                    g.FillPath(part, forward);
                    g.FillPath(part, back);
                }
            }

            using (var font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var accent = new SolidBrush(Theme.Accent))
            using (var white = new SolidBrush(Color.White))
            using (var ring = new Pen(Theme.Accent, 1.6F))
            using (var leader = new Pen(Color.FromArgb(170, Theme.Accent), 1.1F))
            {
                foreach (Spot spot in Spots)
                {
                    bool active = Array.IndexOf(spot.Rows, highlight) >= 0;
                    float half = HalfWidth(spot);
                    if (spot.Badge != spot.Target)
                    {
                        float edgeX = spot.Badge.X + (spot.Target.X > spot.Badge.X ? half : -half);
                        g.DrawLine(leader, edgeX, spot.Badge.Y, spot.Target.X, spot.Target.Y);
                        g.FillEllipse(accent, spot.Target.X - 2.6F, spot.Target.Y - 2.6F, 5.2F, 5.2F);
                    }
                    var area = new RectangleF(spot.Badge.X - half, spot.Badge.Y - BadgeRadius, half * 2F, BadgeRadius * 2F);
                    using (GraphicsPath badge = Theme.RoundedRectangle(area, BadgeRadius - 0.3F))
                    {
                        g.FillPath(active ? accent : white, badge);
                        g.DrawPath(ring, badge);
                    }
                    string text = spot.Rows.Length > 1 ? (spot.Rows[0] + 1) + "·" + (spot.Rows[1] + 1) : (spot.Rows[0] + 1).ToString();
                    g.DrawString(text, font, active ? white : accent, area, center);
                }
            }
        }
    }

    // Left-hand page selector: icon plus label, filled with the accent colour when selected.
    internal sealed class NavButton : Control
    {
        private readonly float scale = Theme.GetScale();
        private bool selected, hover;
        public string Glyph = "";

        public NavButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public bool Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color page = Parent != null ? Parent.BackColor : Theme.Page;
            g.Clear(page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = selected ? Theme.Accent : hover ? Color.FromArgb(228, 233, 242) : page;
            Color ink = selected ? Color.White : Theme.Text;
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 8F * scale))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);

            int iconWidth = (int)(46F * scale);
            if (Theme.IconFont != null && Glyph.Length > 0)
            {
                using (var icons = new Font(Theme.IconFont, 12F))
                    TextRenderer.DrawText(g, Glyph, icons, new Rectangle(0, 0, iconWidth, Height), ink, fill,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
            using (var font = new Font(Theme.UiFont, 10F, selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, Text, font, new Rectangle(iconWidth, 0, Width - iconWidth - 6, Height), ink, fill,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // Small white chip showing battery level and how the mouse is connected.
    internal sealed class BatteryChip : Control
    {
        private readonly float scale = Theme.GetScale();
        // Call Invalidate() after changing these.
        public string ConnectionText = "";
        public bool Connected;
        public int BatteryPercent = -1;
        public bool Charging;

        public BatteryChip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 8F * scale))
            using (var fill = new SolidBrush(Theme.Card))
            using (var border = new Pen(Theme.CardBorder))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            float x = 12F * scale;
            float middle = Height / 2F;
            Color state = Connected ? Theme.Good : Theme.Warning;
            if (BatteryPercent >= 0)
            {
                bool low = BatteryPercent <= 20 && !Charging;
                Color level = low ? Color.FromArgb(220, 70, 70) : Theme.Good;
                using (var bold = new Font(Theme.UiFont, 9.5F, FontStyle.Bold))
                using (var ink = new SolidBrush(level))
                {
                    string percent = BatteryPercent + "%";
                    SizeF size = g.MeasureString(percent, bold);
                    g.DrawString(percent, bold, ink, x, middle - size.Height / 2F);
                    x += size.Width + 4F * scale;

                    float bodyWidth = 22F * scale, bodyHeight = 11F * scale;
                    float top = middle - bodyHeight / 2F;
                    float inset = 2F * scale;
                    using (GraphicsPath body = Theme.RoundedRectangle(new RectangleF(x, top, bodyWidth, bodyHeight), 2.5F * scale))
                    using (var outline = new Pen(level, 1.3F * scale))
                    {
                        g.FillRectangle(ink, x + inset, top + inset,
                            Math.Max(2F * scale, (bodyWidth - inset * 2F) * Math.Min(100, BatteryPercent) / 100F), bodyHeight - inset * 2F);
                        g.DrawPath(outline, body);
                        g.FillRectangle(ink, x + bodyWidth + 1F * scale, middle - 2.5F * scale, 2F * scale, 5F * scale);
                    }
                    x += bodyWidth + 12F * scale;
                }
            }
            else
            {
                float dot = 8F * scale;
                using (var mark = new SolidBrush(state))
                    g.FillEllipse(mark, x, middle - dot / 2F, dot, dot);
                x += dot + 8F * scale;
            }

            string text = ConnectionText + (Charging ? Lang.T("・充電中", " · charging") : "");
            using (var font = new Font(Theme.UiFont, 9F))
            using (var muted = new SolidBrush(Theme.Muted))
            {
                SizeF size = g.MeasureString(text, font);
                g.DrawString(text, font, muted, x, middle - size.Height / 2F);
            }
        }
    }

    internal sealed class CardPanel : Panel
    {
        private readonly float scale;
        public string Title = "";

        public CardPanel(float uiScale)
        {
            scale = uiScale;
            BackColor = Theme.Card;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            using (GraphicsPath path = Theme.RoundedRectangle(bounds, 10 * scale))
            using (var fill = new SolidBrush(Theme.Card))
            using (var border = new Pen(Theme.CardBorder))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var titleFont = new Font(Theme.UiFont, 10.5F, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, Title, titleFont,
                    new Point((int)Math.Round(16 * scale), (int)Math.Round(12 * scale)), Theme.Text, Theme.Card, TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Dictionary<MouseInput, ActionPicker> selectors = new Dictionary<MouseInput, ActionPicker>();
        private readonly Dictionary<MouseInput, ActionBinding> mappings = new Dictionary<MouseInput, ActionBinding>();
        private ActionBinding[] ringBindings = ActionRingOverlay.DefaultSlots();
        // Up, right, down, left.
        private ActionBinding[] gestureBindings = GestureDialog.Defaults();
        private readonly ComboBox ringStyle = new ComboBox();
        private readonly SliderBar ringSize = new SliderBar();
        private readonly SliderBar bubbleSize = new SliderBar();
        private readonly SliderBar ringOpacity = new SliderBar();
        private readonly ComboBox ringColor = new ComboBox();
        private readonly CheckBox ringHaptic = new ToggleSwitch();
        private readonly ComboBox ringHapticWave = new ComboBox();
        private Button hapticTry;
        // Short haptic waveforms that suit sliding across the ring, in menu order (ids follow Solaar;
        // the same list as the macOS version). 0x04 is the default.
        private static readonly int[] HapticWaves = { 0x1B, 0x04, 0x01, 0x03, 0x00, 0x02 };
        private const int DefaultHapticWave = 1;
        private readonly System.Windows.Forms.Timer appearanceSave = new System.Windows.Forms.Timer();
        private readonly DeviceSettings deviceSettings = new DeviceSettings();
        private DiagnosticsForm diagnostics;
        private readonly BatteryChip chip = new BatteryChip();
        private readonly List<NavButton> navigation = new List<NavButton>();
        private readonly List<CardPanel> pages = new List<CardPanel>();
        private readonly PictureBox ringPreview = new PictureBox();
        private readonly MouseDiagram diagram = new MouseDiagram();
        private readonly List<MouseInput> rowInputs = new List<MouseInput>();
        private readonly List<Label> rowBadges = new List<Label>();
        private readonly CheckBox enableSwitch = new ToggleSwitch();
        private readonly CheckBox startMinimized = new ToggleSwitch();
        private readonly CheckBox startWithWindows = new ToggleSwitch();
        private bool allowShow;
        private CardPanel devicePage;
        private DeviceDialog deviceEditor;
        private bool trayHintShown;
        private readonly System.Windows.Forms.Timer batteryTimer = new System.Windows.Forms.Timer();
        private string batteryText = "";
        private readonly ListBox eventLog = new ListBox();
        private readonly CheckBox liveLog = new ToggleSwitch();
        private readonly CheckBox gestureMode = new ToggleSwitch();
        private readonly CheckBox actionRingMode = new ToggleSwitch();
        private readonly Label status = new Label();
        private readonly Label statusDot = new Label();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Icon appIcon;
        private readonly float uiScale;
        private readonly string configPath;
        private readonly string diagnosticLogPath;
        private readonly RawInputMonitor rawInput;
        private readonly HidppDiversionManager hidpp;
        private readonly GestureOverlay overlay = new GestureOverlay();
        private readonly ActionRingOverlay actionRing = new ActionRingOverlay();
        private bool reallyExit;
        private bool gestureActive;
        private bool actionRingActive;
        private readonly Dictionary<int, bool> vendorState = new Dictionary<int, bool>();
        private bool swallowLeftUp;
        private bool swallowRightUp;
        private readonly CheckBox ringClickMode = new ToggleSwitch();
        // Edge switching: pushing the pointer against an outer screen edge moves the mouse to another host.
        private static readonly string[] EdgeKeys = { "EdgeLeft", "EdgeRight", "EdgeTop", "EdgeBottom" };   // in ScreenEdge order
        private static readonly string[] EdgeKeyNames =
            { "None", "Ctrl", "Alt", "Shift", "Win", "Middle", "XButton2", "XButton1", "GestureButton", "ActionRingButton" };
        private readonly CheckBox edgeSwitch = new ToggleSwitch();
        private readonly ComboBox[] edgeTargets = new ComboBox[4];
        private readonly int[] edgeHosts = { -1, -1, -1, -1 };      // host per edge, -1 = off
        private readonly SliderBar edgeDwell = new SliderBar();      // tenths of a second
        private readonly ComboBox edgeKey = new ComboBox();
        private readonly CheckBox edgePlaceCursor = new ToggleSwitch();
        private readonly Label edgeScreens = new Label();
        private readonly Label[] hostLabels = new Label[3];
        private readonly Button[] hostButtons = new Button[3];
        private readonly System.Windows.Forms.Timer hostCheck = new System.Windows.Forms.Timer();
        private List<HostEntry> hosts = new List<HostEntry>();
        private int currentHost = -1;
        private bool loadingEdge;
        private Rectangle[] screenBounds = new Rectangle[0];
        private bool edgePushing;
        private ScreenEdge edgeCandidate;
        private int pushStart, lastPush;
        private int edgeCooldownUntil = Environment.TickCount;
        private int lastResume = unchecked(Environment.TickCount - 600000);
        private int lastArrival = unchecked(Environment.TickCount - 600000);
        private bool readyOnce;
        private readonly HashSet<MouseInput> heldInputs = new HashSet<MouseInput>();
        private bool deferredPending;       // the edge-switch key's own action waits for its release
        private MouseInput deferredInput;
        private Microsoft.Win32.PowerModeChangedEventHandler powerHandler;
        private EventHandler displayHandler;
        private Point gestureStart;
        private string gestureDirection = "";
        private string gestureCandidate = "";
        private bool gestureMoved;

        public MainForm()
        {
            Text = "MX Master Win";
            uiScale = Theme.GetScale();
            Font = new Font(Theme.UiFont, 9.5F);
            BackColor = Theme.Page;
            ForeColor = Theme.Text;
            ClientSize = new Size(S(1040), S(620));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            appIcon = Theme.LoadAppIcon();
            Icon = appIcon;
            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mxmaster-win.ini");
            diagnosticLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mxmaster-events.log");

            BuildUi();
            LoadSettings();
            actionRing.ClickMode = ringClickMode.Checked;
            UpdateRingPreview();

            // Hooked up after loading so that restoring the saved state does not rewrite the file.
            foreach (ActionPicker picker in selectors.Values)
                picker.Changed += SaveSettings;
            gestureMode.CheckedChanged += delegate { SaveSettings(); };
            // Sliders report every step of a drag; apply at once but write the file after a pause.
            appearanceSave.Interval = 500;
            appearanceSave.Tick += delegate { appearanceSave.Stop(); SaveSettings(); };
            EventHandler appearanceChanged = delegate
            {
                ApplyRingAppearance();
                appearanceSave.Stop();
                appearanceSave.Start();
            };
            ringStyle.SelectedIndexChanged += appearanceChanged;
            ringSize.ValueChanged += appearanceChanged;
            bubbleSize.ValueChanged += appearanceChanged;
            ringOpacity.ValueChanged += appearanceChanged;
            ringColor.SelectedIndexChanged += appearanceChanged;
            ringHaptic.CheckedChanged += delegate
            {
                UpdateHapticControls();
                SaveSettings();
            };
            // Picking a waveform plays it once so it can be felt straight away.
            ringHapticWave.SelectedIndexChanged += delegate
            {
                PlayRingHaptic();
                SaveSettings();
            };
            actionRing.SlotEntered = delegate
            {
                if (ringHaptic.Checked) PlayRingHaptic();
            };
            startMinimized.CheckedChanged += delegate { SaveSettings(); };
            actionRingMode.CheckedChanged += delegate { SaveSettings(); };
            ringClickMode.CheckedChanged += delegate
            {
                actionRing.ClickMode = ringClickMode.Checked;
                UpdateRingPreview();
                SaveSettings();
            };

            edgeSwitch.CheckedChanged += delegate { edgePushing = false; SaveSettings(); };
            edgePlaceCursor.CheckedChanged += delegate { SaveSettings(); };
            edgeKey.SelectedIndexChanged += delegate { deferredPending = false; SaveSettings(); };
            edgeDwell.ValueChanged += delegate { appearanceSave.Stop(); appearanceSave.Start(); };
            hostCheck.Interval = 2500;
            hostCheck.Tick += delegate
            {
                // The mouse is still sending input here, so the switch did not take: pick the channel up again.
                hostCheck.Stop();
                if (rawInput.MillisecondsSinceInput < 1000) hidpp.Recheck();
            };
            powerHandler = delegate(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
            {
                if (e.Mode == Microsoft.Win32.PowerModes.Resume) lastResume = Environment.TickCount;
            };
            displayHandler = delegate
            {
                try { BeginInvoke(new Action(UpdateScreens)); }
                catch { }
            };
            Microsoft.Win32.SystemEvents.PowerModeChanged += powerHandler;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displayHandler;
            UpdateScreens();

            ActionRunner.OpenSettings = delegate
            {
                try { BeginInvoke(new Action(ShowWindow)); }
                catch { }
            };
            rawInput = new RawInputMonitor(this.Handle, AppendRawEvent, OnVendorButton, OnMouseActivity, OnMxMouseMove);
            hidpp = new HidppDiversionManager(OnHidppStatus, OnHidppReady);
            MouseHook.MouseEvent += OnMouseEvent;
            MouseHook.Install();

            bool capture = false;
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (argument.Equals("--capture", StringComparison.OrdinalIgnoreCase))
                {
                    liveLog.Checked = true;
                    capture = true;
                    break;
                }
            }

            tray.Icon = new Icon(appIcon, SystemInformation.SmallIconSize);
            tray.Text = "MX Master Win";
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowWindow(); };
            var menu = new ContextMenuStrip();
            menu.Items.Add(Lang.T("開啟設定", "Open settings"), null, delegate { ShowWindow(); });
            menu.Items.Add(Lang.T("暫停 / 啟用", "Pause / resume"), null, delegate { ToggleEnabled(); });
            // Ticked while MX Flow is on; the same setting as the switch on the MX Flow page.
            var flowItem = new ToolStripMenuItem("MX Flow");
            flowItem.Click += delegate { edgeSwitch.Checked = !edgeSwitch.Checked; };
            menu.Items.Add(flowItem);
            menu.Opening += delegate { flowItem.Checked = edgeSwitch.Checked; };
            menu.Items.Add(Lang.T("工程模式…", "Engineering mode…"), null, delegate { ShowDiagnostics(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("結束", "Exit"), null, delegate { reallyExit = true; Close(); });
            tray.ContextMenuStrip = menu;

            bool present = RawInputMonitor.IsMxMaster4Present();
            chip.ConnectionText = present ? Lang.T("連線中…", "Connecting…") : Lang.T("未偵測到滑鼠", "No mouse found");
            SetDeviceStatus(present
                ? Lang.T("正在連線到 MX Master 4…", "Connecting to MX Master 4…")
                : Lang.T("尚未偵測到 MX Master 4；標準按鍵對應仍可使用", "MX Master 4 not detected; standard button mapping still works"), false);

            batteryTimer.Interval = 10 * 60 * 1000;
            batteryTimer.Tick += delegate { RefreshBattery(); };
            batteryTimer.Start();

            // The window may never be shown (start minimized), so nothing here can wait for Shown.
            bool show = capture || !startMinimized.Checked;
            foreach (string argument in Environment.GetCommandLineArgs())
                if (argument.Equals("--show", StringComparison.OrdinalIgnoreCase)) show = true;
            allowShow = show;
            hidpp.Start();
            if (capture) ShowDiagnostics();
            if (!show && !trayHintShown)
            {
                trayHintShown = true;
                SaveSettings();
                try
                {
                    tray.ShowBalloonTip(4000, "MX Master Win",
                        Lang.T("已在系統匣執行。按兩下系統匣圖示可開啟設定。", "Running in the tray. Double-click the tray icon to open settings."), ToolTipIcon.Info);
                }
                catch { }
            }
        }

        // Keeps the form hidden when Application.Run tries to show it at start-up.
        protected override void SetVisibleCore(bool value)
        {
            if (value && !allowShow)
            {
                if (!IsHandleCreated) CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        // Reads the battery on a worker thread; HID++ requests can block for a second or more.
        private void RefreshBattery()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                DeviceSnapshot snapshot = null;
                try { snapshot = hidpp.ReadDevice(); }
                catch { }
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate { ShowBattery(snapshot); }));
                }
                catch { }
            });
        }

        // Runs once the HID++ channel is up: writes back the device settings saved in the INI
        // when the mouse reports something else, then shows the battery level.
        private void RestoreDeviceSettings()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                DeviceSnapshot snapshot = null;
                var results = new List<string>();
                try
                {
                    snapshot = hidpp.ReadDevice();
                    if (snapshot != null && deviceSettings.Restore(hidpp, snapshot, results))
                        snapshot = hidpp.ReadDevice();
                }
                catch { }
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate
                    {
                        ShowBattery(snapshot);
                        if (results.Count == 0) return;
                        LogDeviceLines(results);
                        bool failed = results.Exists(delegate(string line) { return line.EndsWith("verified=False"); });
                        status.Text = failed
                            ? Lang.T("部分儲存的裝置設定重新套用失敗，請到「指標與滾輪」確認", "Some saved device settings could not be re-applied; check Pointer & Wheel")
                            : Lang.T("已重新套用儲存的裝置設定", "Saved device settings re-applied");
                    }));
                }
                catch { }
            });
        }

        private void ShowBattery(DeviceSnapshot snapshot)
        {
            batteryText = snapshot == null || snapshot.BatteryPercent < 0 ? "" : Lang.T("電量 ", "Battery ") + snapshot.BatteryText;
            UpdateTrayText();
            if (snapshot == null)
            {
                chip.Connected = false;
                chip.ConnectionText = Lang.T("未連線", "Not connected");
                chip.BatteryPercent = -1;
            }
            else
            {
                chip.Connected = true;
                chip.ConnectionText = snapshot.Transport == "BOLT" ? "Logi Bolt" : "Bluetooth";
                // A sleeping mouse can miss a read; keep the last good value instead of blanking it.
                if (snapshot.BatteryPercent >= 0)
                {
                    chip.BatteryPercent = snapshot.BatteryPercent;
                    chip.Charging = snapshot.BatteryCharging;
                }
            }
            chip.Invalidate();
            if (liveLog.Checked && snapshot != null)
                foreach (string line in snapshot.Raw)
                    AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Device " + line);
        }

        private void UpdateTrayText()
        {
            string text = MouseHook.Enabled ? "MX Master Win" : Lang.T("MX Master Win（已暫停）", "MX Master Win (paused)");
            if (batteryText.Length > 0) text += "\n" + batteryText;
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void LogDeviceLines(IEnumerable<string> lines)
        {
            if (!liveLog.Checked) return;
            foreach (string line in lines)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Device " + line);
        }

        // Layout: device title on top, page navigation on the left with the battery chip under it,
        // one card per page on the right, and a single status line at the bottom.
        private void BuildUi()
        {
            int width = ClientSize.Width;
            Controls.Add(new PictureBox
            {
                Image = Theme.LoadLogo(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(S(28), S(24)),
                Size = new Size(S(48), S(48))
            });
            Controls.Add(new Label
            {
                Text = "MX Master 4",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 20F),
                Location = new Point(S(86), S(16))
            });
            Controls.Add(new Label
            {
                Text = Lang.T("MX Master Win　免安裝的按鍵、手勢與圓環自訂工具", "MX Master Win  ·  portable button, gesture and ring customizer"),
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 9F),
                Location = new Point(S(91), S(56))
            });

            AddNavigation(Lang.T("按鈕", "Buttons"), "", 0);
            AddNavigation(Lang.T("手勢與圓環", "Gestures & Ring"), "", 1);
            AddNavigation(Lang.T("指標與滾輪", "Pointer & Wheel"), "", 2);
            AddNavigation("MX Flow", "", 3);
            AddNavigation(Lang.T("進階", "Advanced"), "", 4);

            chip.Location = new Point(S(24), S(566));
            chip.Size = new Size(S(202), S(36));
            Controls.Add(chip);

            // ---- Page 0: button mapping ----
            CardPanel mappingPage = AddPage(Lang.T("按鈕", "Buttons"));
            diagram.Location = new Point(S(12), S(54));
            diagram.Size = new Size(S(284), S(390));
            diagram.HighlightChanged += delegate
            {
                foreach (Label badge in rowBadges) badge.Invalidate();
            };
            diagram.HotspotClicked += delegate(int row)
            {
                ComboBox combo = selectors[rowInputs[row]].Combo;
                combo.Focus();
                combo.DroppedDown = true;
            };
            mappingPage.Controls.Add(diagram);

            // The row order is also the numbering shown on the diagram.
            int y = 56;
            AddMappingRow(mappingPage, Lang.T("中鍵", "Middle button"), Lang.T("按下滾輪", "Press the wheel"), MouseInput.Middle, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("側鍵 1・拇指手勢鍵", "Side 1 · Gesture button"), Lang.T("單擊時執行", "Runs on a single click"), MouseInput.GestureButton, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("側鍵 2・上一頁", "Side 2 · Back"), "", MouseInput.XButton2, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("側鍵 3・下一頁", "Side 3 · Forward"), "", MouseInput.XButton1, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("水平滾輪向左", "Horizontal scroll left"), Lang.T("拇指滾輪", "Thumb wheel"), MouseInput.WheelLeft, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("水平滾輪向右", "Horizontal scroll right"), Lang.T("拇指滾輪", "Thumb wheel"), MouseInput.WheelRight, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("Actions Ring 鍵", "Actions Ring button"), Lang.T("圓環停用時執行", "Runs when the ring is off"), MouseInput.ActionRingButton, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("滾輪模式鍵", "Wheel mode button"), Lang.T("保留原功能即可切換滾輪模式", "Keep original to switch modes"), MouseInput.WheelModeButton, y);

            // ---- Page 1: gestures and the ring, with a live preview of the ring ----
            CardPanel modePage = AddPage(Lang.T("手勢與圓環", "Gestures & Ring"));
            int column = S(330);
            gestureMode.Text = Lang.T("拇指手勢鍵四向手勢", "Four-way gestures (gesture button)");
            gestureMode.Location = new Point(S(18), S(58));
            gestureMode.Size = new Size(column, S(26));
            modePage.Controls.Add(gestureMode);
            AddHint(modePage, Lang.T("按住並滑動；四個方向的動作可用「自訂手勢…」設定", "Hold and swipe; set each direction with Customize gestures…"), 88);
            AddDivider(modePage, 122, column);

            actionRingMode.Text = Lang.T("Actions Ring 八方向圓環", "Actions Ring eight-way menu");
            actionRingMode.Checked = true;
            actionRingMode.Location = new Point(S(18), S(136));
            actionRingMode.Size = new Size(column, S(26));
            modePage.Controls.Add(actionRingMode);
            AddHint(modePage, Lang.T("按住顯示、移動選擇、放開執行；在中心放開取消", "Hold to show, move to choose, release to run"), 166);

            ringClickMode.Text = Lang.T("圓環改用點選（不必按住）", "Click to choose (no holding)");
            ringClickMode.Location = new Point(S(18), S(200));
            ringClickMode.Size = new Size(column, S(26));
            modePage.Controls.Add(ringClickMode);
            AddHint(modePage, Lang.T("點一下顯示，再點功能執行；點中心、圓環外或右鍵取消", "Click to show, click a slice to run; right-click cancels"), 230);

            Button customizeRing = Theme.CreateButton(Lang.T("自訂圓環…", "Customize ring…"), true);
            customizeRing.Location = new Point(S(18), S(266));
            customizeRing.Size = new Size(S(124), S(34));
            customizeRing.Click += delegate
            {
                ActionBinding[] edited = RingDialog.Prompt(this, ringBindings);
                if (edited == null) return;
                ringBindings = edited;
                actionRing.SetSlots(ringBindings);
                UpdateRingPreview();
                SaveSettings();
            };
            modePage.Controls.Add(customizeRing);

            Button customizeGestures = Theme.CreateButton(Lang.T("自訂手勢…", "Customize gestures…"), false);
            customizeGestures.Location = new Point(S(152), S(266));
            customizeGestures.Size = new Size(S(144), S(34));
            customizeGestures.Click += delegate
            {
                ActionBinding[] edited = GestureDialog.Prompt(this, gestureBindings);
                if (edited == null) return;
                gestureBindings = edited;
                UpdateGestureNames();
                SaveSettings();
            };
            modePage.Controls.Add(customizeGestures);

            // Ring appearance: style, overall size, bubble size and opacity.
            AddRingOption(modePage, Lang.T("圓環樣式", "Ring style"), 314, null);
            ringStyle.DropDownStyle = ComboBoxStyle.DropDownList;
            ringStyle.Items.Add(Lang.T("分格圓環", "Ring"));
            ringStyle.Items.Add(Lang.T("泡泡（只顯示圖示）", "Bubbles (icons only)"));
            ringStyle.SelectedIndex = 0;
            ringStyle.Location = new Point(S(104), S(312));
            ringStyle.Width = S(186);
            modePage.Controls.Add(ringStyle);
            AddRingOption(modePage, Lang.T("整體大小", "Overall size"), 348, ringSize);
            AddRingOption(modePage, Lang.T("泡泡大小", "Bubble size"), 380, bubbleSize);
            ringOpacity.Minimum = 40;
            AddRingOption(modePage, Lang.T("不透明度", "Opacity"), 412, ringOpacity);

            ringPreview.SizeMode = PictureBoxSizeMode.Zoom;
            ringPreview.Location = new Point(S(400), S(54));
            ringPreview.Size = new Size(S(240), S(240));
            modePage.Controls.Add(ringPreview);
            modePage.Controls.Add(new Label
            {
                Text = Lang.T("圓環預覽", "Ring preview"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(S(400), S(296)),
                Size = new Size(S(240), S(20))
            });

            // Right column under the preview: ring colour, and a haptic tick as the pointer enters each slice.
            int ringRight = 372;
            modePage.Controls.Add(new Label { Text = Lang.T("圓環顏色", "Ring color"), AutoSize = false, Location = new Point(S(ringRight), S(330)), Size = new Size(S(84), S(22)) });
            ringColor.DropDownStyle = ComboBoxStyle.DropDownList;
            ringColor.Items.Add(Lang.T("深色", "Dark"));
            ringColor.Items.Add(Lang.T("白色", "White"));
            ringColor.SelectedIndex = 0;
            ringColor.Location = new Point(S(ringRight + 86), S(328));
            ringColor.Width = S(150);
            modePage.Controls.Add(ringColor);

            ringHaptic.Text = Lang.T("滑過選項時震動", "Haptic tick on each slice");
            ringHaptic.Checked = true;
            ringHaptic.Location = new Point(S(ringRight), S(362));
            ringHaptic.Size = new Size(S(376), S(26));
            modePage.Controls.Add(ringHaptic);
            AddHintAt(modePage, Lang.T("移到另一格時輕震；強度依「指標與滾輪」頁的觸覺回饋", "Strength follows the haptic feedback setting"), ringRight, 390);
            modePage.Controls.Add(new Label { Text = Lang.T("震動波形", "Waveform"), AutoSize = false, Location = new Point(S(ringRight), S(418)), Size = new Size(S(84), S(22)) });
            ringHapticWave.DropDownStyle = ComboBoxStyle.DropDownList;
            ringHapticWave.Items.Add(Lang.T("極輕", "Whisper"));
            ringHapticWave.Items.Add(Lang.T("輕微（預設）", "Subtle (default)"));
            ringHapticWave.Items.Add(Lang.T("柔和", "Soft"));
            ringHapticWave.Items.Add(Lang.T("柔和碰撞", "Soft bump"));
            ringHapticWave.Items.Add(Lang.T("清脆", "Crisp"));
            ringHapticWave.Items.Add(Lang.T("清脆碰撞", "Crisp bump"));
            ringHapticWave.SelectedIndex = DefaultHapticWave;
            ringHapticWave.Location = new Point(S(ringRight + 86), S(416));
            ringHapticWave.Width = S(150);
            modePage.Controls.Add(ringHapticWave);
            hapticTry = Theme.CreateButton(Lang.T("試震", "Try"), false);
            hapticTry.Location = new Point(S(ringRight + 244), S(414));
            hapticTry.Size = new Size(S(64), S(28));
            hapticTry.Click += delegate { PlayRingHaptic(); };
            modePage.Controls.Add(hapticTry);

            // ---- Page 2: pointer, wheel and haptics. Filled in on first use, once HID++ exists. ----
            devicePage = AddPage("");

            // ---- Page 3: MX Flow, switching computers at a screen edge ----
            CardPanel switchPage = AddPage("MX Flow");
            edgeSwitch.Text = Lang.T("啟用 MX Flow：游標推到螢幕邊緣時切換電腦", "Enable MX Flow: switch computers at a screen edge");
            edgeSwitch.Location = new Point(S(18), S(58));
            edgeSwitch.Size = new Size(switchPage.Width - S(36), S(26));
            switchPage.Controls.Add(edgeSwitch);
            AddHint(switchPage, Lang.T("游標貼著邊緣並持續往外推，滑鼠就切到指定的 Easy-Switch 頻道；按著左、右鍵拖曳時不會切換",
                "Keep pushing the pointer against an edge to move the mouse to that Easy-Switch channel; never while dragging"), 88);
            AddDivider(switchPage, 118, switchPage.Width - S(36));

            string[] edgeNames = { Lang.T("左邊緣", "Left edge"), Lang.T("右邊緣", "Right edge"), Lang.T("上邊緣", "Top edge"), Lang.T("下邊緣", "Bottom edge") };
            for (int i = 0; i < 4; i++)
            {
                int edgeIndex = i;
                int rowY = 130 + i * 34;
                switchPage.Controls.Add(new Label { Text = edgeNames[i], AutoSize = false, Location = new Point(S(18), S(rowY + 3)), Size = new Size(S(84), S(22)) });
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(S(104), S(rowY)), Width = S(250) };
                combo.SelectedIndexChanged += delegate
                {
                    if (loadingEdge) return;
                    edgeHosts[edgeIndex] = combo.SelectedIndex - 1;
                    edgePushing = false;
                    SaveSettings();
                };
                edgeTargets[i] = combo;
                switchPage.Controls.Add(combo);
            }

            int right = 390;
            switchPage.Controls.Add(new Label { Text = Lang.T("觸發時間", "Trigger time"), AutoSize = false, Location = new Point(S(right), S(133)), Size = new Size(S(84), S(22)) });
            edgeDwell.Maximum = 20;
            edgeDwell.Minimum = 1;
            edgeDwell.Value = 3;
            edgeDwell.Location = new Point(S(right + 86), S(130));
            edgeDwell.Size = new Size(S(186), S(26));
            var dwellReadout = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(S(right + 276), S(131)),
                Size = new Size(S(64), S(22))
            };
            EventHandler showDwell = delegate { dwellReadout.Text = (edgeDwell.Value / 10.0).ToString("0.0") + Lang.T(" 秒", " s"); };
            edgeDwell.ValueChanged += showDwell;
            showDwell(null, EventArgs.Empty);
            switchPage.Controls.Add(edgeDwell);
            switchPage.Controls.Add(dwellReadout);

            switchPage.Controls.Add(new Label { Text = Lang.T("按住按鍵", "Held key"), AutoSize = false, Location = new Point(S(right), S(167)), Size = new Size(S(84), S(22)) });
            edgeKey.DropDownStyle = ComboBoxStyle.DropDownList;
            edgeKey.Items.AddRange(new object[]
            {
                Lang.T("不需要", "None"), "Ctrl", "Alt", "Shift", "Win",
                Lang.T("中鍵", "Middle button"), Lang.T("側鍵 2・上一頁", "Side 2 · Back"), Lang.T("側鍵 3・下一頁", "Side 3 · Forward"),
                Lang.T("拇指手勢鍵", "Gesture button"), Lang.T("Actions Ring 鍵", "Actions Ring button")
            });
            edgeKey.SelectedIndex = 0;
            edgeKey.Location = new Point(S(right + 86), S(164));
            edgeKey.Width = S(200);
            switchPage.Controls.Add(edgeKey);
            AddHintAt(switchPage, Lang.T("選了按鍵就要按住它再推到邊緣；選滑鼠按鍵時，", "With a key chosen, hold it while pushing. A mouse button"), right, 194);
            AddHintAt(switchPage, Lang.T("它原本的動作改在放開時執行", "chosen here runs its own action on release instead"), right, 212);

            edgePlaceCursor.Text = Lang.T("切回來時把游標移到邊緣", "Place the pointer at the edge on return");
            edgePlaceCursor.Checked = true;
            edgePlaceCursor.Location = new Point(S(right), S(236));
            edgePlaceCursor.Size = new Size(switchPage.Width - S(18) - S(right), S(26));
            switchPage.Controls.Add(edgePlaceCursor);

            edgeScreens.AutoSize = true;
            edgeScreens.ForeColor = Theme.Muted;
            edgeScreens.Font = new Font(Theme.UiFont, 8.5F);
            edgeScreens.Location = new Point(S(18), S(270));
            switchPage.Controls.Add(edgeScreens);
            AddDivider(switchPage, 296, switchPage.Width - S(36));

            switchPage.Controls.Add(new Label { Text = Lang.T("Easy-Switch 頻道", "Easy-Switch channels"), AutoSize = true, Location = new Point(S(18), S(306)) });
            for (int i = 0; i < 3; i++)
            {
                int hostIndex = i;
                hostLabels[i] = new Label
                {
                    AutoSize = false,
                    AutoEllipsis = true,
                    UseMnemonic = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Location = new Point(S(18), S(332 + i * 30)),
                    Size = new Size(switchPage.Width - S(36) - S(130), S(26))
                };
                Button go = Theme.CreateButton(Lang.T("切換過去", "Switch"), false);
                go.Size = new Size(S(112), S(26));
                go.Location = new Point(switchPage.Width - S(18) - go.Width, S(332 + i * 30));
                go.Click += delegate { SwitchHost(hostIndex, "manual"); };
                hostButtons[i] = go;
                switchPage.Controls.Add(hostLabels[i]);
                switchPage.Controls.Add(go);
            }
            AddHint(switchPage, Lang.T("切過去後，另一台電腦也要執行本程式並設定邊緣才能同樣切回來；否則請按滑鼠底部的 Easy-Switch 鍵。不傳送剪貼簿或檔案",
                "To come back the same way the other computer must run this app with an edge set; otherwise use the Easy-Switch button. No clipboard or files"), 428);

            // ---- Page 4: everything else ----
            CardPanel advancedPage = AddPage(Lang.T("進階", "Advanced"));
            enableSwitch.Text = Lang.T("啟用按鍵對應", "Enable button mapping");
            enableSwitch.Checked = true;
            enableSwitch.Location = new Point(S(18), S(58));
            enableSwitch.Size = new Size(advancedPage.Width - S(36), S(26));
            enableSwitch.CheckedChanged += delegate
            {
                if (enableSwitch.Checked != MouseHook.Enabled) ToggleEnabled();
            };
            advancedPage.Controls.Add(enableSwitch);
            AddHint(advancedPage, Lang.T("關閉後所有按鍵恢復原本功能，等同系統匣選單的「暫停」", "When off, every button behaves normally (same as Pause in the tray menu)"), 88);
            AddDivider(advancedPage, 122, advancedPage.Width - S(36));

            startMinimized.Text = Lang.T("啟動時直接縮小到系統匣", "Start minimized to the tray");
            startMinimized.Checked = true;
            startMinimized.Location = new Point(S(18), S(136));
            startMinimized.Size = new Size(advancedPage.Width - S(36), S(26));
            advancedPage.Controls.Add(startMinimized);
            AddHint(advancedPage, Lang.T("開啟程式時不顯示視窗；關閉視窗也只是隱藏，要結束請用系統匣圖示的選單",
                "No window is shown at launch. Closing the window only hides it; use the tray icon menu to exit"), 166);

            // Off unless the user turns it on. The switch mirrors whether the shortcut exists, so it is
            // not stored in the INI, and a shortcut left by an older location is refreshed at start-up.
            startWithWindows.Text = Lang.T("登入 Windows 時自動啟動", "Start when I sign in to Windows");
            startWithWindows.Location = new Point(S(18), S(194));
            startWithWindows.Size = new Size(advancedPage.Width - S(36), S(26));
            startWithWindows.Checked = StartupShortcut.Exists;
            if (startWithWindows.Checked)
            {
                string ignored;
                StartupShortcut.Create(out ignored);
            }
            startWithWindows.CheckedChanged += delegate { ApplyStartWithWindows(); };
            advancedPage.Controls.Add(startWithWindows);
            AddHint(advancedPage, Lang.T("會在你的「啟動」資料夾放一個捷徑；關閉時移除。不需要管理員權限，也不修改登錄檔",
                "Puts a shortcut in your Startup folder and removes it when turned off. No admin rights, no registry change"), 224);
            AddDivider(advancedPage, 256, advancedPage.Width - S(36));

            // Language: every string is chosen at start-up, so a change relaunches the app.
            advancedPage.Controls.Add(new Label { Text = "語言 / Language", AutoSize = true, Location = new Point(S(18), S(268)) });
            AddHint(advancedPage, Lang.T("變更後程式會自動重新啟動", "The app restarts after a change"), 292);
            var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(190) };
            language.Items.Add(Lang.T("跟隨系統", "Follow system"));
            language.Items.Add("繁體中文");
            language.Items.Add("English");
            language.SelectedIndex = Lang.Setting == "zh" ? 1 : Lang.Setting == "en" ? 2 : 0;
            language.Location = new Point(advancedPage.Width - S(18) - language.Width, S(276));
            language.SelectionChangeCommitted += delegate
            {
                string chosen = language.SelectedIndex == 1 ? "zh" : language.SelectedIndex == 2 ? "en" : "Auto";
                if (chosen == Lang.Setting) return;
                Lang.Setting = chosen;
                SaveSettings();
                Program.RestartRequested = true;
                reallyExit = true;
                Close();
            };
            advancedPage.Controls.Add(language);
            AddDivider(advancedPage, 330, advancedPage.Width - S(36));

            AddActionRow(advancedPage, Lang.T("工程模式", "Engineering mode"), Lang.T("檢視滑鼠送出的原始事件，用於辨識按鍵或回報問題", "View the raw events the mouse sends, to identify buttons or report problems"), Lang.T("開啟工程模式…", "Open…"), 342,
                delegate { ShowDiagnostics(); });
            AddDivider(advancedPage, 402, advancedPage.Width - S(36));

            advancedPage.Controls.Add(new Label
            {
                Text = Lang.T("關於", "About") + "　MX Master Win " + Program.Version + Lang.T("　免安裝、免管理員權限，不安裝驅動或服務", "    Portable, no admin rights, no driver or service"),
                AutoSize = true,
                Location = new Point(S(18), S(412))
            });
            advancedPage.Controls.Add(new Label
            {
                Text = Lang.T("設定檔：", "Settings file: ") + configPath,
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(S(18), S(434)),
                Size = new Size(advancedPage.Width - S(36), S(20))
            });

            statusDot.Text = "●";
            statusDot.TextAlign = ContentAlignment.MiddleCenter;
            statusDot.Location = new Point(S(250), S(568));
            statusDot.Size = new Size(S(22), S(32));
            statusDot.ForeColor = Theme.Warning;
            Controls.Add(statusDot);

            status.UseMnemonic = false;
            status.AutoSize = false;
            status.AutoEllipsis = true;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Theme.Muted;
            status.Location = new Point(S(274), S(568));
            status.Size = new Size(width - S(274) - S(24), S(32));
            Controls.Add(status);

            liveLog.Text = Lang.T("記錄即時事件", "Capture live events");
            liveLog.CheckedChanged += delegate
            {
                if (liveLog.Checked)
                {
                    try
                    {
                        File.WriteAllText(diagnosticLogPath, "MX Master Win diagnostic capture\r\n", Encoding.UTF8);
                        AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Diagnostics enabled; movement-only events are filtered.");
                        AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Log file: " + diagnosticLogPath);
                    }
                    catch (Exception ex)
                    {
                        AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Cannot create log file: " + ex.Message);
                    }
                }
            };
            // The event list and its switch live in the engineering window, not on the main page.
            diagnostics = new DiagnosticsForm(liveLog, eventLog, ClearEvents, diagnosticLogPath, appIcon);

            ShowPage(0);
        }

        private void AddNavigation(string text, string glyph, int index)
        {
            var item = new NavButton
            {
                Text = text,
                Glyph = glyph,
                Location = new Point(S(24), S(100) + index * S(52)),
                Size = new Size(S(202), S(44))
            };
            item.Click += delegate { ShowPage(index); };
            navigation.Add(item);
            Controls.Add(item);
        }

        private CardPanel AddPage(string title)
        {
            var page = new CardPanel(uiScale)
            {
                Title = title,
                Location = new Point(S(250), S(100)),
                Size = new Size(S(766), S(458)),
                Visible = false
            };
            pages.Add(page);
            Controls.Add(page);
            return page;
        }

        private void ShowPage(int index)
        {
            if (index == 2) ShowDevicePage();
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == index;
                navigation[i].Selected = i == index;
            }
        }

        // The pointer/wheel page hosts the device editor directly inside the card.
        private void ShowDevicePage()
        {
            if (hidpp == null) return;
            if (deviceEditor == null)
            {
                deviceEditor = new DeviceDialog(hidpp, LogDeviceLines, deviceSettings, delegate
                {
                    SaveSettings();
                    RefreshBattery();
                });
                deviceEditor.Embed(devicePage, new Point(S(12), S(10)));
            }
            deviceEditor.Reload();
        }

        private void UpdateRingPreview()
        {
            Image old = ringPreview.Image;
            ringPreview.Image = actionRing.RenderFrame();
            if (old != null) old.Dispose();
        }

        private void ClearEvents()
        {
            eventLog.Items.Clear();
            if (liveLog.Checked)
            {
                try { File.WriteAllText(diagnosticLogPath, "MX Master Win diagnostic capture\r\n", Encoding.UTF8); }
                catch { }
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Capture cleared; waiting for input...");
            }
        }

        private void ShowDiagnostics()
        {
            if (!diagnostics.Visible) diagnostics.Show();
            if (diagnostics.WindowState == FormWindowState.Minimized) diagnostics.WindowState = FormWindowState.Normal;
            diagnostics.Activate();
        }

        private void HideToTray()
        {
            Hide();
            if (trayHintShown) return;
            // Shown once ever, so the user knows where the window went.
            trayHintShown = true;
            SaveSettings();
            try { tray.ShowBalloonTip(4000, "MX Master Win", Lang.T("程式仍在背景執行。可從系統匣圖示開啟設定或結束。", "Still running in the background. Use the tray icon to open settings or exit."), ToolTipIcon.Info); }
            catch { }
        }

        private void SetDeviceStatus(string text, bool good)
        {
            status.Text = text;
            statusDot.ForeColor = good ? Theme.Good : Theme.Warning;
        }

        private int S(int value)
        {
            return (int)Math.Round(value * uiScale);
        }

        private void AddHint(Control parent, string text, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(S(18), S(y))
            });
        }

        // One labelled row of the ring appearance settings; slider rows also get a percent readout.
        private void AddRingOption(Control parent, string text, int y, SliderBar slider)
        {
            parent.Controls.Add(new Label { Text = text, AutoSize = false, Location = new Point(S(18), S(y + 2)), Size = new Size(S(84), S(22)) });
            if (slider == null) return;
            slider.Maximum = 100;
            slider.Value = slider == ringOpacity ? 100 : 50;
            slider.Location = new Point(S(104), S(y));
            slider.Size = new Size(S(186), S(26));
            var readout = new Label
            {
                Text = slider.Value + "%",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(S(294), S(y + 1)),
                Size = new Size(S(48), S(22))
            };
            slider.ValueChanged += delegate { readout.Text = slider.Value + "%"; };
            parent.Controls.Add(slider);
            parent.Controls.Add(readout);
        }

        // Percent settings to multipliers. 50% is the default size; the scales match the macOS version.
        private void ApplyRingAppearance()
        {
            float size = 0.78F * (0.5F + ringSize.Value / 100F);
            float bubble = bubbleSize.Value <= 50
                ? 0.6F + 0.2F * (bubbleSize.Value / 50F)
                : 0.8F + 0.225F * ((bubbleSize.Value - 50) / 50F);
            bubbleSize.Enabled = ringStyle.SelectedIndex == 1;
            actionRing.Configure(ringStyle.SelectedIndex == 1, ringColor.SelectedIndex == 1, size, bubble, ringOpacity.Value / 100F);
            UpdateRingPreview();
        }

        private void UpdateHapticControls()
        {
            ringHapticWave.Enabled = hapticTry.Enabled = ringHaptic.Checked;
        }

        private void PlayRingHaptic()
        {
            if (hidpp == null) return;
            hidpp.PlayHaptic((byte)HapticWaves[Math.Max(0, ringHapticWave.SelectedIndex)]);
        }

        private bool applyingStartup;

        private void ApplyStartWithWindows()
        {
            if (applyingStartup) return;
            string error;
            bool ok = startWithWindows.Checked ? StartupShortcut.Create(out error) : StartupShortcut.Remove(out error);
            if (ok)
            {
                status.Text = startWithWindows.Checked
                    ? Lang.T("已在「啟動」資料夾建立捷徑", "Shortcut added to the Startup folder")
                    : Lang.T("已移除「啟動」資料夾的捷徑", "Shortcut removed from the Startup folder");
                return;
            }
            // Put the switch back to what is really on disk, then say why.
            applyingStartup = true;
            startWithWindows.Checked = StartupShortcut.Exists;
            applyingStartup = false;
            MessageBox.Show(Lang.T("無法變更自動啟動：", "Could not change start-up: ") + error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void UpdateGestureNames()
        {
            for (int i = 0; i < 4; i++) overlay.Names[i] = gestureBindings[i].ShortLabel;
        }

        private void AddHintAt(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(S(x), S(y))
            });
        }

        private void AddDivider(Control parent, int y, int width)
        {
            parent.Controls.Add(new Panel
            {
                BackColor = Theme.Divider,
                Location = new Point(S(18), S(y)),
                Size = new Size(width, 1)
            });
        }

        // A titled row with a description on the left and one button on the right.
        private void AddActionRow(Control parent, string title, string hint, string buttonText, int y, EventHandler click)
        {
            parent.Controls.Add(new Label { Text = title, AutoSize = true, Location = new Point(S(18), S(y)) });
            AddHint(parent, hint, y + 24);
            Button button = Theme.CreateButton(buttonText, false);
            button.Size = new Size(S(150), S(34));
            button.Location = new Point(parent.Width - S(18) - button.Width, S(y + 6));
            button.Click += click;
            parent.Controls.Add(button);
        }

        private void AddMappingRow(Control parent, string labelText, string hint, MouseInput input, int y)
        {
            int row = rowInputs.Count;
            rowInputs.Add(input);
            int left = S(304);
            parent.Controls.Add(new Panel
            {
                BackColor = Theme.Divider,
                Location = new Point(left, S(y)),
                Size = new Size(parent.Width - S(18) - left, 1)
            });

            // Number badge matching the diagram; filled while its control is highlighted there.
            var badge = new Label { Location = new Point(left, S(y + 14)), Size = new Size(S(22), S(22)) };
            badge.Paint += delegate(object sender, PaintEventArgs e)
            {
                bool active = diagram.Highlight == row;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var circle = new RectangleF(1F, 1F, badge.Width - 2.5F, badge.Height - 2.5F);
                using (var fill = new SolidBrush(active ? Theme.Accent : Theme.Card))
                using (var ring = new Pen(Theme.Accent, 1.5F * uiScale))
                using (var ink = new SolidBrush(active ? Color.White : Theme.Accent))
                using (var font = new Font("Segoe UI", 8F, FontStyle.Bold))
                using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    e.Graphics.FillEllipse(fill, circle);
                    e.Graphics.DrawEllipse(ring, circle);
                    e.Graphics.DrawString((row + 1).ToString(), font, ink, circle, center);
                }
            };
            rowBadges.Add(badge);
            parent.Controls.Add(badge);

            parent.Controls.Add(new Label
            {
                Text = labelText,
                AutoSize = true,
                Location = new Point(left + S(30), S(hint.Length > 0 ? y + 7 : y + 15))
            });
            if (hint.Length > 0)
            {
                parent.Controls.Add(new Label
                {
                    Text = hint,
                    AutoSize = true,
                    ForeColor = Theme.Muted,
                    Font = new Font(Theme.UiFont, 8.25F),
                    Location = new Point(left + S(30), S(y + 27))
                });
            }
            var picker = new ActionPicker(parent, new Point(S(522), S(y + 12)), S(172), S(50), S(4));
            picker.Combo.Enter += delegate { diagram.Highlight = row; };
            picker.Combo.MouseEnter += delegate { diagram.Highlight = row; };
            selectors[input] = picker;
        }

        private void LoadSettings()
        {
            mappings[MouseInput.Middle] = ActionBinding.Of(MappedAction.PassThrough);
            mappings[MouseInput.XButton1] = ActionBinding.Of(MappedAction.BrowserBack);
            mappings[MouseInput.XButton2] = ActionBinding.Of(MappedAction.BrowserForward);
            mappings[MouseInput.WheelLeft] = ActionBinding.Of(MappedAction.PassThrough);
            mappings[MouseInput.WheelRight] = ActionBinding.Of(MappedAction.PassThrough);
            mappings[MouseInput.GestureButton] = ActionBinding.Of(MappedAction.AltTab);
            mappings[MouseInput.ActionRingButton] = ActionBinding.Of(MappedAction.TaskView);
            mappings[MouseInput.WheelModeButton] = ActionBinding.Of(MappedAction.PassThrough);

            if (File.Exists(configPath))
            {
                foreach (string original in File.ReadAllLines(configPath))
                {
                    string line = original.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.IndexOf('=') < 0) continue;
                    string[] parts = line.Split(new[] { '=' }, 2);
                    if (parts[0].Equals("GestureMode", StringComparison.OrdinalIgnoreCase))
                    {
                        bool value;
                        if (bool.TryParse(parts[1], out value)) gestureMode.Checked = value;
                        continue;
                    }
                    if (parts[0].Equals("ActionRingMode", StringComparison.OrdinalIgnoreCase))
                    {
                        bool value;
                        if (bool.TryParse(parts[1], out value)) actionRingMode.Checked = value;
                        continue;
                    }
                    if (parts[0].Equals("ActionRingClick", StringComparison.OrdinalIgnoreCase))
                    {
                        bool value;
                        if (bool.TryParse(parts[1], out value)) ringClickMode.Checked = value;
                        continue;
                    }
                    string key = parts[0].Trim();
                    if (key.Equals("RingStyle", StringComparison.OrdinalIgnoreCase))
                    {
                        ringStyle.SelectedIndex = parts[1].Trim().Equals("Bubble", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        continue;
                    }
                    if (key.Equals("RingColor", StringComparison.OrdinalIgnoreCase))
                    {
                        ringColor.SelectedIndex = parts[1].Trim().Equals("Light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        continue;
                    }
                    if (key.Equals("RingHaptic", StringComparison.OrdinalIgnoreCase))
                    {
                        bool on;
                        if (bool.TryParse(parts[1].Trim(), out on)) ringHaptic.Checked = on;
                        continue;
                    }
                    if (key.Equals("RingHapticWave", StringComparison.OrdinalIgnoreCase))
                    {
                        // Stored as the waveform id; anything not in the menu falls back to the default.
                        int wave;
                        int found = int.TryParse(parts[1].Trim(), out wave) ? Array.IndexOf(HapticWaves, wave) : -1;
                        ringHapticWave.SelectedIndex = found >= 0 ? found : DefaultHapticWave;
                        continue;
                    }
                    if (key.Equals("RingSize", StringComparison.OrdinalIgnoreCase) || key.Equals("BubbleSize", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("RingOpacity", StringComparison.OrdinalIgnoreCase))
                    {
                        int percent;
                        if (int.TryParse(parts[1].Trim(), out percent))
                        {
                            if (key.Equals("RingSize", StringComparison.OrdinalIgnoreCase)) ringSize.Value = percent;
                            else if (key.Equals("BubbleSize", StringComparison.OrdinalIgnoreCase)) bubbleSize.Value = percent;
                            else ringOpacity.Value = percent;
                        }
                        continue;
                    }
                    if (key.Equals("TrayHintShown", StringComparison.OrdinalIgnoreCase))
                    {
                        bool.TryParse(parts[1], out trayHintShown);
                        continue;
                    }
                    if (key.Equals("StartMinimized", StringComparison.OrdinalIgnoreCase))
                    {
                        bool minimized;
                        if (bool.TryParse(parts[1], out minimized)) startMinimized.Checked = minimized;
                        continue;
                    }
                    if (key.Equals("EdgeSwitch", StringComparison.OrdinalIgnoreCase) || key.Equals("EdgePlaceCursor", StringComparison.OrdinalIgnoreCase))
                    {
                        bool on;
                        if (bool.TryParse(parts[1], out on))
                        {
                            if (key.Equals("EdgeSwitch", StringComparison.OrdinalIgnoreCase)) edgeSwitch.Checked = on;
                            else edgePlaceCursor.Checked = on;
                        }
                        continue;
                    }
                    if (key.Equals("EdgeDwell", StringComparison.OrdinalIgnoreCase))
                    {
                        int milliseconds;
                        if (int.TryParse(parts[1].Trim(), out milliseconds)) edgeDwell.Value = (milliseconds + 50) / 100;
                        continue;
                    }
                    if (key.Equals("EdgeKey", StringComparison.OrdinalIgnoreCase))
                    {
                        string wanted = parts[1].Trim();
                        int found = Array.FindIndex(EdgeKeyNames, delegate(string name) { return name.Equals(wanted, StringComparison.OrdinalIgnoreCase); });
                        if (found >= 0) edgeKey.SelectedIndex = found;
                        continue;
                    }
                    int edgeSlot = Array.FindIndex(EdgeKeys, delegate(string name) { return name.Equals(key, StringComparison.OrdinalIgnoreCase); });
                    if (edgeSlot >= 0)
                    {
                        // Stored as the channel number printed under the mouse (1-3); 0 means off.
                        int channel;
                        if (int.TryParse(parts[1].Trim(), out channel) && channel >= 0 && channel <= 8) edgeHosts[edgeSlot] = channel - 1;
                        continue;
                    }
                    // Read earlier by Lang.Load, before any window existed.
                    if (key.Equals("Language", StringComparison.OrdinalIgnoreCase)) continue;
                    if (deviceSettings.TryLoad(key, parts[1])) continue;
                    ActionBinding binding = ActionBinding.Parse(parts[1]);
                    if (binding == null) continue;
                    int gesture = Array.FindIndex(GestureKeys, delegate(string name) { return name.Equals(key, StringComparison.OrdinalIgnoreCase); });
                    if (gesture >= 0)
                    {
                        gestureBindings[gesture] = binding;
                        continue;
                    }
                    int slot;
                    if (key.Length == 5 && key.StartsWith("Ring", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(key.Substring(4), out slot) && slot >= 0 && slot < 8)
                    {
                        ringBindings[slot] = binding;
                        continue;
                    }
                    MouseInput input;
                    if (Enum.TryParse(key, true, out input) && Enum.IsDefined(typeof(MouseInput), input))
                        mappings[input] = binding;
                }
            }

            foreach (var pair in selectors)
                pair.Value.Binding = mappings[pair.Key];
            actionRing.SetSlots(ringBindings);
            UpdateGestureNames();
            ApplyRingAppearance();
            UpdateHapticControls();
            RefreshHostUi();
        }

        private static readonly string[] GestureKeys = { "GestureUp", "GestureRight", "GestureDown", "GestureLeft" };

        private void SaveSettings()
        {
            try
            {
                var lines = new List<string>();
                lines.Add("# MX Master Win settings");
                foreach (var pair in selectors)
                {
                    ActionBinding binding = pair.Value.Binding;
                    mappings[pair.Key] = binding;
                    lines.Add(pair.Key + "=" + binding);
                }
                lines.Add("GestureMode=" + gestureMode.Checked);
                lines.Add("ActionRingMode=" + actionRingMode.Checked);
                lines.Add("ActionRingClick=" + ringClickMode.Checked);
                lines.Add("RingStyle=" + (ringStyle.SelectedIndex == 1 ? "Bubble" : "Ring"));
                lines.Add("RingSize=" + ringSize.Value);
                lines.Add("BubbleSize=" + bubbleSize.Value);
                lines.Add("RingOpacity=" + ringOpacity.Value);
                lines.Add("RingColor=" + (ringColor.SelectedIndex == 1 ? "Light" : "Dark"));
                lines.Add("RingHaptic=" + ringHaptic.Checked);
                lines.Add("RingHapticWave=" + HapticWaves[Math.Max(0, ringHapticWave.SelectedIndex)]);
                for (int i = 0; i < GestureKeys.Length; i++)
                    lines.Add(GestureKeys[i] + "=" + gestureBindings[i]);
                lines.Add("StartMinimized=" + startMinimized.Checked);
                lines.Add("EdgeSwitch=" + edgeSwitch.Checked);
                for (int i = 0; i < EdgeKeys.Length; i++)
                    lines.Add(EdgeKeys[i] + "=" + (edgeHosts[i] + 1));
                lines.Add("EdgeDwell=" + edgeDwell.Value * 100);
                lines.Add("EdgeKey=" + EdgeKeyNames[Math.Max(0, edgeKey.SelectedIndex)]);
                lines.Add("EdgePlaceCursor=" + edgePlaceCursor.Checked);
                lines.Add("Language=" + Lang.Setting);
                // Ring0 is the top slice; the rest follow clockwise.
                for (int i = 0; i < ringBindings.Length; i++)
                    lines.Add("Ring" + i + "=" + ringBindings[i]);
                lines.AddRange(deviceSettings.ToLines());
                if (trayHintShown) lines.Add("TrayHintShown=True");
                File.WriteAllLines(configPath, lines.ToArray(), Encoding.UTF8);
                status.Text = Lang.T("設定已自動儲存", "Settings saved");
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("無法儲存設定：", "Cannot save settings: ") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool OnMouseEvent(MouseHookEvent e)
        {
            if (liveLog.Checked && e.Kind != MouseHookKind.Move)
                BeginInvoke(new Action(delegate
                {
                    AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Hook " + e.Kind + (e.Injected ? " [logitech-translated]" : ""));
                }));

            // Logitech's Bluetooth translation layer marks real horizontal-wheel
            // and side-button messages as injected. This app only injects keyboard
            // events, so accepting these mouse messages cannot recurse into itself.

            if (actionRingActive && e.Kind == MouseHookKind.Move)
            {
                actionRing.UpdateSelection(e.Point);
                return false;
            }

            // Click-to-choose ring: the next click picks a slice or dismisses the ring, and
            // neither that click nor its release reaches the window underneath.
            if (actionRingActive && actionRing.ClickMode)
            {
                if (e.Kind == MouseHookKind.LeftDown)
                {
                    actionRing.UpdateSelection(e.Point);
                    actionRingActive = false;
                    swallowLeftUp = true;
                    BeginInvoke(new Action(FinishActionRing));
                    return true;
                }
                if (e.Kind == MouseHookKind.RightDown)
                {
                    actionRingActive = false;
                    swallowRightUp = true;
                    BeginInvoke(new Action(CancelActionRing));
                    return true;
                }
            }
            if (swallowLeftUp && e.Kind == MouseHookKind.LeftUp)
            {
                swallowLeftUp = false;
                return true;
            }
            if (swallowRightUp && e.Kind == MouseHookKind.RightUp)
            {
                swallowRightUp = false;
                return true;
            }

            if (gestureMode.Checked && gestureActive && e.Kind == MouseHookKind.Move)
            {
                int dx = e.Point.X - gestureStart.X;
                int dy = e.Point.Y - gestureStart.Y;
                int ax = Math.Abs(dx);
                int ay = Math.Abs(dy);
                int major = Math.Max(ax, ay);
                if (major >= 40) gestureMoved = true;

                // Do not let a late wobble change an already selected action. Before
                // locking, require one axis to be at least 35% stronger than the other.
                if (gestureDirection.Length == 0)
                {
                    string next = "";
                    if (major >= 40)
                    {
                        if (ax * 100 >= ay * 135) next = dx < 0 ? "左" : "右";
                        else if (ay * 100 >= ax * 135) next = dy < 0 ? "上" : "下";
                        else next = "斜";
                    }

                    if (major >= 65 && next != "" && next != "斜")
                        gestureDirection = next;

                    string display = gestureDirection.Length > 0 ? gestureDirection : next;
                    if (display != gestureCandidate)
                    {
                        gestureCandidate = display;
                        BeginInvoke(new Action(delegate { overlay.SetDirection(gestureCandidate); }));
                    }
                }
                return false;
            }

            MouseInput input;
            bool actionable = true;
            bool button = true;
            switch (e.Kind)
            {
                case MouseHookKind.MiddleUp: return ButtonReleased(MouseInput.Middle);
                case MouseHookKind.XButton1Up: return ButtonReleased(MouseInput.XButton1);
                case MouseHookKind.XButton2Up: return ButtonReleased(MouseInput.XButton2);
                case MouseHookKind.MiddleDown: input = MouseInput.Middle; break;
                case MouseHookKind.XButton1Down: input = MouseInput.XButton1; break;
                case MouseHookKind.XButton2Down: input = MouseInput.XButton2; break;
                case MouseHookKind.WheelLeft: input = MouseInput.WheelLeft; button = false; break;
                case MouseHookKind.WheelRight: input = MouseInput.WheelRight; button = false; break;
                default: input = MouseInput.Middle; actionable = false; break;
            }
            if (!actionable) return false;
            if (button) heldInputs.Add(input);
            ActionBinding mapped = GetMapping(input);
            if (mapped.IsPassThrough) return false;
            if (button && IsEdgeKey(input))
            {
                // Held to switch computers: its own action runs on release, unless a switch happened.
                deferredPending = true;
                deferredInput = input;
                return true;
            }
            BeginInvoke(new Action(delegate { ActionRunner.Run(mapped); }));
            return true;
        }

        // Releases are passed on as before; a deferred action of the edge-switch key runs now.
        private bool ButtonReleased(MouseInput input)
        {
            heldInputs.Remove(input);
            if (deferredPending && deferredInput == input)
            {
                deferredPending = false;
                ActionBinding mapped = GetMapping(input);
                if (!mapped.IsPassThrough) BeginInvoke(new Action(delegate { ActionRunner.Run(mapped); }));
            }
            return false;
        }

        private void OnVendorButton(string transport, int deviceIndex, int controlCode, bool pressed)
        {
            if (!MouseHook.Enabled) return;

            MouseInput input;
            string name;
            if (controlCode == 0x00C3)
            {
                input = MouseInput.GestureButton;
                name = "GestureButton";
            }
            else if (controlCode == 0x01A0)
            {
                input = MouseInput.ActionRingButton;
                name = "ActionRingButton";
            }
            else if (controlCode == 0x00C4)
            {
                input = MouseInput.WheelModeButton;
                name = "WheelModeButton";
            }
            else return;

            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Decoded " + transport +
                    " dev=" + deviceIndex.ToString("X2") + " " + name + (pressed ? " Down" : " Up"));

            // The same press can arrive in more than one report format (for example while
            // Options+ is also running). Act only when the button's state really changes.
            bool last;
            if (vendorState.TryGetValue(controlCode, out last) && last == pressed) return;
            vendorState[controlCode] = pressed;

            if (input == MouseInput.GestureButton && gestureMode.Checked)
            {
                if (pressed) BeginGesture(Cursor.Position);
                else if (gestureActive) FinishGesture(MouseInput.GestureButton);
                return;
            }

            if (input == MouseInput.ActionRingButton && actionRingMode.Checked)
            {
                if (actionRingActive && actionRing.ClickMode)
                {
                    // An open click-to-choose ring ignores the release and closes on the next press.
                    if (pressed) CancelActionRing();
                }
                else if (pressed) BeginActionRing(Cursor.Position);
                else if (actionRingActive) FinishActionRing();
                return;
            }

            // A special button used as a plain button acts on press. When it is the edge-switch key
            // it acts on release instead, and not at all when the mouse switched in between.
            bool run = pressed;
            if (IsEdgeKey(input))
            {
                run = !pressed && deferredPending && deferredInput == input;
                deferredPending = pressed;
                deferredInput = input;
            }
            if (run)
            {
                ActionBinding action = GetMapping(input);
                bool sent = !action.IsPassThrough && ActionRunner.Run(action);
                if (liveLog.Checked)
                    AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Mapped " + name +
                        " selected=" + action + " sent=" + sent);
            }
        }

        private void BeginGesture(Point start)
        {
            gestureActive = true;
            gestureStart = start;
            gestureDirection = "";
            gestureCandidate = "";
            gestureMoved = false;
            overlay.ShowAt(gestureStart);
        }

        private void FinishGesture(MouseInput fallbackInput)
        {
            gestureActive = false;
            string direction = gestureDirection;
            overlay.Hide();
            ActionBinding selected;
            if (direction == "左") selected = gestureBindings[3];
            else if (direction == "右") selected = gestureBindings[1];
            else if (direction == "上") selected = gestureBindings[0];
            else if (direction == "下") selected = gestureBindings[2];
            else selected = gestureMoved ? ActionBinding.Of(MappedAction.PassThrough) : GetMapping(fallbackInput);

            bool sent = !selected.IsPassThrough && ActionRunner.Run(selected);
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Gesture direction=" +
                    (String.IsNullOrEmpty(direction) ? (gestureMoved ? "Ambiguous" : "None") : direction) +
                    " selected=" + selected + " sent=" + sent);
        }

        private void BeginActionRing(Point center)
        {
            actionRingActive = true;
            actionRing.ClickMode = ringClickMode.Checked;
            actionRing.ShowAt(center);
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " ActionRing opened" + (actionRing.ClickMode ? " click-mode" : ""));
        }

        private void CancelActionRing()
        {
            actionRingActive = false;
            actionRing.Hide();
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " ActionRing cancelled");
        }

        private void FinishActionRing()
        {
            actionRingActive = false;
            ActionBinding selected = actionRing.SelectedBinding;
            actionRing.Hide();
            bool sent = selected != null && !selected.IsPassThrough && ActionRunner.Run(selected);
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " ActionRing selected=" +
                    (selected != null ? selected.ToString() : "None") + " sent=" + sent +
                    " " + actionRing.SelectionDebug);
        }

        private ActionBinding GetMapping(MouseInput input)
        {
            ActionPicker picker;
            if (selectors.TryGetValue(input, out picker))
                return picker.Binding;
            return mappings.ContainsKey(input) ? mappings[input] : ActionBinding.Of(MappedAction.PassThrough);
        }

        private void AppendRawEvent(string text)
        {
            if (!liveLog.Checked) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendRawEvent), text);
                return;
            }
            AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Raw  " + text);
        }

        private void AddLog(string text)
        {
            eventLog.Items.Add(text);
            while (eventLog.Items.Count > 250) eventLog.Items.RemoveAt(0);
            eventLog.TopIndex = eventLog.Items.Count - 1;
            if (liveLog.Checked)
            {
                try { File.AppendAllText(diagnosticLogPath, text + Environment.NewLine, Encoding.UTF8); }
                catch { }
            }
        }

        private void OnHidppReady(int deviceIndex, int featureIndex)
        {
            rawInput.ConfigureHidppDiversion(deviceIndex, featureIndex);
            try
            {
                // A button state left over from before a reconnect would swallow the next press.
                BeginInvoke(new Action(delegate
                {
                    vendorState.Clear();
                    rawInput.ResetPressed();
                    MouseArrived();
                }));
            }
            catch { }
            RestoreDeviceSettings();
            RefreshHosts();
        }

        // ---- MX Flow: switching computers ----

        private void UpdateScreens()
        {
            Screen[] all = Screen.AllScreens;
            screenBounds = Array.ConvertAll(all, delegate(Screen screen) { return screen.Bounds; });
            edgeScreens.Text = all.Length > 1
                ? String.Format(Lang.T("偵測到 {0} 個螢幕：只有最外側螢幕的外緣會觸發，螢幕之間相接的邊與角落不會",
                    "{0} monitors detected: only the outer edge of the outermost monitor triggers; edges between monitors and corners never do"), all.Length)
                : Lang.T("角落不會觸發。接上多個螢幕時，只有最外側螢幕的外緣會觸發",
                    "Corners never trigger. With several monitors, only the outer edge of the outermost monitor triggers");
        }

        private void RefreshHosts()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                int current = -1;
                List<HostEntry> read = null;
                try { read = hidpp.ReadHosts(out current); }
                catch { }
                if (read == null || read.Count == 0) return;
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate
                    {
                        hosts = read;
                        currentHost = current;
                        RefreshHostUi();
                    }));
                }
                catch { }
            });
        }

        private string HostTitle(int index)
        {
            string channel = String.Format(Lang.T("頻道 {0}", "Channel {0}"), index + 1);
            HostEntry entry = hosts.Find(delegate(HostEntry h) { return h.Index == index; });
            return entry == null || entry.Name.Length == 0 ? channel : channel + " · " + entry.Name;
        }

        // Fills the per-edge lists and the channel rows from what the mouse reported; before the
        // first read the three channels are listed without names so the settings stay visible.
        private void RefreshHostUi()
        {
            int count = hosts.Count > 0 ? hosts.Count : 3;
            loadingEdge = true;
            try
            {
                for (int i = 0; i < edgeTargets.Length; i++)
                {
                    ComboBox combo = edgeTargets[i];
                    combo.Items.Clear();
                    combo.Items.Add(Lang.T("不切換", "Off"));
                    for (int h = 0; h < Math.Max(count, edgeHosts[i] + 1); h++)
                        combo.Items.Add(HostTitle(h) + (h == currentHost ? Lang.T("（這台電腦）", " (this computer)") : ""));
                    combo.SelectedIndex = edgeHosts[i] + 1;
                }
            }
            finally { loadingEdge = false; }

            for (int i = 0; i < hostLabels.Length; i++)
            {
                HostEntry entry = hosts.Find(delegate(HostEntry h) { return h.Index == i; });
                var parts = new List<string>();
                parts.Add(HostTitle(i));
                if (entry != null && entry.BusName.Length > 0) parts.Add(entry.BusName);
                if (entry != null && !entry.Paired) parts.Add(Lang.T("未配對", "Not paired"));
                if (i == currentHost) parts.Add(Lang.T("目前使用中", "In use now"));
                hostLabels[i].Text = String.Join("　", parts.ToArray());
                hostButtons[i].Enabled = entry != null && entry.Paired && i != currentHost;
            }
        }

        private bool IsEdgeKey(MouseInput input)
        {
            return edgeSwitch.Checked && edgeKey.SelectedIndex >= 5 &&
                EdgeKeyNames[edgeKey.SelectedIndex].Equals(input.ToString(), StringComparison.Ordinal);
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private bool VendorDown(int controlCode)
        {
            bool down;
            return vendorState.TryGetValue(controlCode, out down) && down;
        }

        private bool EdgeKeyHeld()
        {
            switch (edgeKey.SelectedIndex)
            {
                case 1: return IsKeyDown(0x11);                       // VK_CONTROL
                case 2: return IsKeyDown(0x12);                       // VK_MENU
                case 3: return IsKeyDown(0x10);                       // VK_SHIFT
                case 4: return IsKeyDown(0x5B) || IsKeyDown(0x5C);      // VK_LWIN / VK_RWIN
                case 5: return heldInputs.Contains(MouseInput.Middle);
                case 6: return heldInputs.Contains(MouseInput.XButton2);
                case 7: return heldInputs.Contains(MouseInput.XButton1);
                case 8: return VendorDown(0x00C3);
                case 9: return VendorDown(0x01A0);
                default: return true;
            }
        }

        // Called for every relative movement report of the MX mouse itself, so a touchpad or another
        // mouse never triggers a switch. The pointer has to sit on a configured outer edge and keep
        // being pushed outwards for the trigger time; resting there or sliding along the edge does not count.
        private void OnMxMouseMove(int dx, int dy)
        {
            if (!MouseHook.Enabled || !edgeSwitch.Checked)
            {
                edgePushing = false;
                return;
            }
            int now = Environment.TickCount;
            if (unchecked(now - edgeCooldownUntil) < 0) return;
            // A ring or gesture in progress blocks switching, unless the held key is the very button that opens it.
            if ((actionRingActive && edgeKey.SelectedIndex != 9) || (gestureActive && edgeKey.SelectedIndex != 8) ||
                IsKeyDown(0x01) || IsKeyDown(0x02))
            {
                edgePushing = false;
                return;
            }
            ScreenEdge edge;
            if (!EdgeGeometry.TryGetEdge(Cursor.Position, screenBounds, out edge))
            {
                edgePushing = false;
                return;
            }
            int target = edgeHosts[(int)edge];
            if (target < 0 || target == currentHost || !EdgeKeyHeld())
            {
                edgePushing = false;
                return;
            }

            int outward, along;
            switch (edge)
            {
                case ScreenEdge.Left: outward = -dx; along = Math.Abs(dy); break;
                case ScreenEdge.Right: outward = dx; along = Math.Abs(dy); break;
                case ScreenEdge.Top: outward = -dy; along = Math.Abs(dx); break;
                default: outward = dy; along = Math.Abs(dx); break;
            }
            if (outward <= 0 || outward < along) return;

            if (!edgePushing || edge != edgeCandidate || unchecked(now - lastPush) > 250)
            {
                edgePushing = true;
                edgeCandidate = edge;
                pushStart = now;
            }
            lastPush = now;
            if (unchecked(now - pushStart) >= edgeDwell.Value * 100)
                SwitchHost(target, "edge " + edge);
        }

        private void SwitchHost(int host, string reason)
        {
            edgeCooldownUntil = unchecked(Environment.TickCount + 3000);
            edgePushing = false;
            // Once the mouse has left, the releases of whatever is held never arrive here.
            deferredPending = false;
            heldInputs.Clear();
            vendorState.Clear();
            rawInput.ResetPressed();
            if (actionRingActive) CancelActionRing();
            if (gestureActive)
            {
                gestureActive = false;
                overlay.Hide();
            }
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Switch host -> " + host + " (" + reason + ")");
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool sent = false;
                try { sent = hidpp.ChangeHost(host); }
                catch { }
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate
                    {
                        if (liveLog.Checked)
                            AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Switch host sent=" + sent);
                        if (!sent) return;
                        hostCheck.Stop();
                        hostCheck.Start();
                    }));
                }
                catch { }
            });
        }

        // The HID++ channel came up again after the first time: the mouse is back on this computer.
        // With exactly one edge configured the pointer is put just inside it, as if it came in from
        // that side. Which computer it came from is unknown, so several edges mean no move, and a
        // reconnect right after the computer wakes up is left alone.
        private void MouseArrived()
        {
            bool first = !readyOnce;
            readyOnce = true;
            int now = Environment.TickCount;
            if (first || !MouseHook.Enabled || !edgeSwitch.Checked || !edgePlaceCursor.Checked) return;
            if (unchecked(now - lastResume) < 60000 || unchecked(now - lastArrival) < 5000) return;
            lastArrival = now;
            int only = -1, configured = 0;
            for (int i = 0; i < edgeHosts.Length; i++)
            {
                if (edgeHosts[i] < 0) continue;
                only = i;
                configured++;
            }
            Point target;
            if (configured != 1 || !EdgeGeometry.TryGetArrivalPoint((ScreenEdge)only, Cursor.Position, screenBounds, S(24), out target)) return;
            Cursor.Position = target;
            edgeCooldownUntil = unchecked(now + 1000);
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Mouse arrived; pointer placed at " + (ScreenEdge)only + " edge");
        }

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);

        // The mouse sent something after a pause: it may have just come back from another host.
        private void OnMouseActivity()
        {
            if (hidpp != null) hidpp.Recheck();
        }

        private void OnHidppStatus(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(OnHidppStatus), text);
                return;
            }
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " HID++ " + text);
            // Protocol details stay in the engineering log; the main window only shows the outcome.
            if (text.StartsWith(HidppDiversionManager.ReadyMark, StringComparison.Ordinal))
                SetDeviceStatus(Lang.T("滑鼠已就緒，專用按鍵已啟用", "Mouse ready; special buttons enabled"), true);
            else if (text.StartsWith(HidppDiversionManager.ProblemMark, StringComparison.Ordinal))
                SetDeviceStatus(text.Substring(HidppDiversionManager.ProblemMark.Length), false);
        }

        private void ToggleEnabled()
        {
            MouseHook.Enabled = !MouseHook.Enabled;
            if (actionRingActive) CancelActionRing();
            edgePushing = false;
            deferredPending = false;
            heldInputs.Clear();
            hidpp.SetEnabled(MouseHook.Enabled);
            UpdateTrayText();
            enableSwitch.Checked = MouseHook.Enabled;
            status.Text = MouseHook.Enabled ? Lang.T("已啟用按鍵對應", "Button mapping enabled") : Lang.T("已暫停：所有按鍵恢復原本功能", "Paused: all buttons behave normally");
        }

        private void ShowWindow()
        {
            allowShow = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.ShowWindowMessage && Program.ShowWindowMessage != 0)
            {
                ShowWindow();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            tray.Visible = false;
            diagnostics.Dispose();
            batteryTimer.Stop();
            hostCheck.Stop();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= powerHandler;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayHandler;
            if (appearanceSave.Enabled)
            {
                appearanceSave.Stop();
                SaveSettings();
            }
            hidpp.Dispose();
            rawInput.Dispose();
            MouseHook.Uninstall();
            overlay.Close();
            actionRing.Close();
            base.OnFormClosing(e);
        }
    }

    internal sealed class GestureOverlay : Form
    {
        private readonly Label label = new Label();

        public GestureOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(35, 35, 38);
            Opacity = 0.90;
            Size = new Size(230, 86);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.ForeColor = Color.White;
            label.Font = new Font("Segoe UI Semibold", 11F);
            Controls.Add(label);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void ShowAt(Point cursor)
        {
            Location = new Point(cursor.X - Width / 2, cursor.Y - Height / 2);
            SetDirection("");
            Show();
        }

        // Names of the actions bound to up, right, down and left, in that order.
        public string[] Names = { "", "", "", "" };

        public void SetDirection(string direction)
        {
            if (direction == "左") label.Text = "←  " + Names[3];
            else if (direction == "右") label.Text = Names[1] + "  →";
            else if (direction == "上") label.Text = "↑  " + Names[0];
            else if (direction == "下") label.Text = "↓  " + Names[2];
            else if (direction == "斜") label.Text = Lang.T("方向不明\r\n請往單一方向直線移動", "Unclear direction\r\nMove in one straight line");
            else label.Text = Lang.T("按住並往 上／下／左／右 移動", "Hold and move up, down, left or right");
        }
    }

    internal sealed class ActionRingOverlay : Form
    {
        // Slot 0 is the top slice; the rest follow clockwise.
        private ActionBinding[] slots = DefaultSlots();
        private string[] labels = LabelsFor(DefaultSlots());

        public static ActionBinding[] DefaultSlots()
        {
            return new[]
            {
                ActionBinding.Of(MappedAction.Copy), ActionBinding.Of(MappedAction.PlayPause),
                ActionBinding.Of(MappedAction.BrowserForward), ActionBinding.Of(MappedAction.Paste),
                ActionBinding.Of(MappedAction.ShowDesktop), ActionBinding.Of(MappedAction.TaskView),
                ActionBinding.Of(MappedAction.BrowserBack), ActionBinding.Of(MappedAction.AltTab)
            };
        }

        private static string[] LabelsFor(ActionBinding[] bindings)
        {
            var result = new string[8];
            for (int i = 0; i < 8; i++) result[i] = bindings[i].ShortLabel;
            return result;
        }

        public void SetSlots(ActionBinding[] bindings)
        {
            slots = (ActionBinding[])bindings.Clone();
            labels = LabelsFor(slots);
            if (Visible) Redraw();
        }

        // Geometry at 96 DPI; everything is multiplied by the system scale.
        private const float OuterRadius = 152F;
        private const float InnerRadius = 60F;
        private const float HubRadius = 50F;
        private const float PopOut = 7F;
        private const float ShadowSize = 14F;
        private const float GapDegrees = 3F;

        private readonly float dpiScale;
        private float scale;
        private float bubbleFactor = 1F;
        private int maxAlpha = 255;
        private readonly string iconFont;
        private readonly System.Windows.Forms.Timer fade = new System.Windows.Forms.Timer();
        private Bitmap frame;
        private int alpha = 255;
        private Point origin;
        private Point screenCenter;
        private int selectedIndex = -1;
        private int lastDx;
        private int lastDy;

        public ActionRingOverlay()
        {
            dpiScale = Theme.GetScale();
            scale = dpiScale;
            iconFont = Theme.IconFont;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            // A borderless form would otherwise be held to the system minimum window size.
            MinimumSize = new Size(1, 1);
            int side = (int)Math.Ceiling((OuterRadius + PopOut + ShadowSize) * 2F * scale);
            Size = new Size(side, side);
            fade.Interval = 15;
            fade.Tick += delegate
            {
                alpha = Math.Min(maxAlpha, alpha + 64);
                if (alpha >= maxAlpha) fade.Stop();
                Push();
            };
        }

        // Bubble style: one round icon per slot; the name shows only for the slot under the pointer.
        public bool Bubbles;

        // White theme: light slices with dark text instead of the default dark ones. The selection stays blue.
        public bool Light;

        // Called when the pointer moves onto another slice (used for the haptic tick).
        public Action<int> SlotEntered;

        // sizeFactor and bubble are multipliers of the design size; opacityValue is 0.4-1.
        public void Configure(bool bubbles, bool light, float sizeFactor, float bubble, float opacityValue)
        {
            Bubbles = bubbles;
            Light = light;
            bubbleFactor = bubble;
            maxAlpha = (int)Math.Round(255F * Math.Max(0.4F, Math.Min(1F, opacityValue)));
            scale = dpiScale * sizeFactor;
            int side = (int)Math.Ceiling((OuterRadius + PopOut + ShadowSize) * 2F * scale);
            Size = new Size(side, side);
            if (Visible) Redraw();
        }

        // A per-pixel-alpha layered window: smooth edges and a soft shadow, and it never takes
        // focus or mouse input away from the window underneath.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080; // LAYERED, TRANSPARENT, NOACTIVATE, TOOLWINDOW
                return p;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            fade.Dispose();
            if (frame != null) frame.Dispose();
            frame = null;
            base.OnFormClosed(e);
        }

        private static string Glyph(MappedAction action)
        {
            switch (action)
            {
                case MappedAction.BrowserBack: return "";
                case MappedAction.BrowserForward: return "";
                case MappedAction.Copy: return "";
                case MappedAction.Paste: return "";
                case MappedAction.Cut: return "";
                case MappedAction.Undo: return "";
                case MappedAction.Redo: return "";
                case MappedAction.TaskView: return "";
                case MappedAction.ShowDesktop: return "";
                case MappedAction.DesktopLeft: return "";
                case MappedAction.DesktopRight: return "";
                case MappedAction.AltTab: return "";
                case MappedAction.PlayPause: return "";
                case MappedAction.VolumeUp: return "";
                case MappedAction.VolumeDown: return "";
                case MappedAction.Mute: return "";
                case MappedAction.Enter: return "";
                case MappedAction.Escape: return "";
                case MappedAction.CustomShortcut: return "";
                case MappedAction.LaunchProgram: return "";
            }
            return ExtraGlyph(action);
        }

        private static string ExtraGlyph(MappedAction action)
        {
            switch (action)
            {
                case MappedAction.NextTrack: return "";
                case MappedAction.PrevTrack: return "";
                case MappedAction.FileExplorer: return "";
                case MappedAction.Settings: return "";
                case MappedAction.Calculator: return "";
                case MappedAction.TaskManager: return "";
                case MappedAction.Search: return "";
                case MappedAction.StartMenu: return "";
                case MappedAction.NewTab: return "";
                case MappedAction.CloseTab: return "";
                case MappedAction.ReopenTab: return "";
                case MappedAction.Refresh: return "";
                case MappedAction.ZoomIn: return "";
                case MappedAction.ZoomOut: return "";
                case MappedAction.Screenshot: return "";
                case MappedAction.Emoji: return "";
                case MappedAction.ClipboardHistory: return "";
                case MappedAction.LockPC: return "";
                case MappedAction.QuickSettings: return "";
                case MappedAction.MinimizeWindow: return "";
                case MappedAction.MaximizeWindow: return "";
                case MappedAction.CloseWindow: return "";
                case MappedAction.SnapLeft: return "";
                case MappedAction.SnapRight: return "";
                case MappedAction.SelectAll: return "";
                case MappedAction.Save: return "";
                case MappedAction.Find: return "";
                case MappedAction.Delete: return "";
                case MappedAction.InsertDate: return "";
                case MappedAction.InsertTime: return "";
                case MappedAction.OpenSettings: return "";
            }
            return "";
        }

        // Draws one frame without showing the window: highlight is the slice to pop out
        // (-1 for none) and numbered adds the 1-8 badges used by the customize dialog.
        internal Bitmap RenderGuide(int highlight)
        {
            int previous = selectedIndex;
            selectedIndex = highlight;
            showNumbers = true;
            try { return RenderFrame(); }
            finally
            {
                showNumbers = false;
                selectedIndex = previous;
            }
        }

        // Which slice a point inside a guide picture of the given size falls on, or -1.
        internal int SliceAt(Point point, Size picture)
        {
            float zoom = Math.Min(picture.Width, picture.Height) / (float)Width;
            double dx = (point.X - picture.Width / 2.0) / zoom;
            double dy = (point.Y - picture.Height / 2.0) / zoom;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < InnerRadius * scale || distance > (OuterRadius + PopOut) * scale) return -1;
            double angleFromUp = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
            if (angleFromUp < 0) angleFromUp += 360.0;
            return ((int)Math.Floor((angleFromUp + 22.5) / 45.0)) % 8;
        }

        private bool showNumbers;

        public ActionBinding SelectedBinding
        {
            get { return selectedIndex >= 0 ? slots[selectedIndex] : null; }
        }

        public string SelectionDebug
        {
            get { return "delta=" + lastDx + "," + lastDy + " center=" + screenCenter.X + "," + screenCenter.Y; }
        }

        // Click-to-choose: the ring stays open until a click, so only the ring itself selects
        // and it is kept fully on screen where every slice can be reached.
        public bool ClickMode;

        public void ShowAt(Point center)
        {
            if (ClickMode)
            {
                Rectangle area = Screen.FromPoint(center).Bounds;
                int half = Width / 2;
                if (area.Width >= Width && area.Height >= Height)
                    center = new Point(Math.Max(area.Left + half, Math.Min(area.Right - half, center.X)),
                        Math.Max(area.Top + half, Math.Min(area.Bottom - half, center.Y)));
            }
            screenCenter = center;
            selectedIndex = -1;
            lastDx = 0;
            lastDy = 0;
            origin = new Point(center.X - Width / 2, center.Y - Height / 2);
            Location = origin;
            alpha = 0;
            Redraw();
            Show();
            fade.Start();
        }

        public void UpdateSelection(Point cursor)
        {
            int dx = cursor.X - screenCenter.X;
            int dy = cursor.Y - screenCenter.Y;
            lastDx = dx;
            lastDy = dy;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            int next = -1;
            // The dead zone is the centre hub, so it grows with the ring on high-DPI screens.
            if (distance >= HubRadius * scale && (!ClickMode || distance <= (OuterRadius + PopOut) * scale))
            {
                double angleFromUp = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
                if (angleFromUp < 0) angleFromUp += 360.0;
                next = ((int)Math.Floor((angleFromUp + 22.5) / 45.0)) % 8;
            }
            if (next != selectedIndex)
            {
                selectedIndex = next;
                Redraw();
                if (next >= 0 && SlotEntered != null) SlotEntered(next);
            }
        }

        private void Redraw()
        {
            Bitmap next = RenderFrame();
            if (frame != null) frame.Dispose();
            frame = next;
            Push();
        }

        // Hands the current frame to the compositor with the fade-in opacity.
        private void Push()
        {
            if (frame == null || IsDisposed) return;
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;
            try
            {
                bitmap = frame.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memory, bitmap);
                var size = new RingSize { cx = frame.Width, cy = frame.Height };
                var source = new RingPoint();
                var target = new RingPoint { x = origin.X, y = origin.Y };
                var blend = new BlendFunction { SourceConstantAlpha = (byte)alpha, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screen, ref target, ref size, memory, ref source, 0, ref blend, 2);
            }
            catch { }
            finally
            {
                if (bitmap != IntPtr.Zero)
                {
                    SelectObject(memory, previous);
                    DeleteObject(bitmap);
                }
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        private static GraphicsPath Slice(float c, float inner, float outer, float start, float sweep)
        {
            var path = new GraphicsPath();
            path.AddArc(c - outer, c - outer, outer * 2F, outer * 2F, start, sweep);
            path.AddArc(c - inner, c - inner, inner * 2F, inner * 2F, start + sweep, -sweep);
            path.CloseFigure();
            return path;
        }

        // Text and centre colours for the dark (default) and white themes.
        private Color Ink { get { return Light ? Color.FromArgb(52, 60, 78) : Color.FromArgb(214, 221, 236); } }
        private Color MutedInk { get { return Light ? Color.FromArgb(150, 158, 175) : Color.FromArgb(138, 148, 170); } }
        private Color HubFace { get { return Light ? Color.FromArgb(250, 255, 255, 255) : Color.FromArgb(246, 20, 23, 33); } }

        // Eight round icons on a circle. Hit-testing stays the same eight directions as the ring.
        private Bitmap RenderBubbles()
        {
            int side = Width;
            float c = side / 2F;
            float outer = OuterRadius * scale;
            float bubble = 35F * bubbleFactor * scale;
            // Larger bubbles push the circle outwards so neighbours keep a small gap.
            float orbit = Math.Max(outer * 0.74F, (bubble * 2F + 8F * scale) * 8F / (2F * (float)Math.PI));
            var bitmap = new Bitmap(side, side, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var glyphFont = new Font(iconFont ?? Theme.UiFont, Math.Max(8F, 23F * bubbleFactor * scale), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var textFont = new Font(Theme.UiFont, Math.Max(8F, 10.5F * bubbleFactor * scale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var nameFont = new Font(Theme.UiFont, Math.Max(12F, 14.5F * scale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var white = new SolidBrush(Color.White))
            using (var soft = new SolidBrush(Ink))
            using (var muted = new SolidBrush(MutedInk))
            using (var normal = new SolidBrush(Light ? Color.FromArgb(246, 250, 251, 253) : Color.FromArgb(240, 30, 34, 47)))
            using (var shadow = new SolidBrush(Color.FromArgb(Light ? 14 : 12, 0, 0, 0)))
            using (var edge = new Pen(Light ? Color.FromArgb(70, 120, 130, 150) : Color.FromArgb(70, 255, 255, 255), Math.Max(1F, 1.2F * scale)))
            using (var chosenEdge = new Pen(Color.FromArgb(120, 255, 255, 255), Math.Max(1F, 1.4F * scale)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                for (int i = 0; i < 8; i++)
                {
                    bool selected = i == selectedIndex;
                    double angle = (-90.0 + i * 45.0) * Math.PI / 180.0;
                    float x = c + (float)Math.Cos(angle) * orbit;
                    float y = c + (float)Math.Sin(angle) * orbit;
                    float r = selected ? bubble + 4.5F * scale : bubble;
                    for (int k = 5; k >= 1; k--)
                    {
                        float grow = r + k * 1.3F * scale;
                        g.FillEllipse(shadow, x - grow, y - grow + 2F * scale, grow * 2F, grow * 2F);
                    }
                    var circle = new RectangleF(x - r, y - r, r * 2F, r * 2F);
                    if (selected)
                    {
                        using (var glow = new LinearGradientBrush(circle, Color.FromArgb(250, 64, 140, 255), Color.FromArgb(250, 24, 92, 220), 60F))
                            g.FillEllipse(glow, circle);
                    }
                    else g.FillEllipse(normal, circle);
                    g.DrawEllipse(selected ? chosenEdge : edge, circle);

                    ActionBinding slot = slots[i];
                    string glyph = iconFont == null || slot.IsPassThrough ? "" : Glyph(slot.Action);
                    Brush ink = selected ? white : slot.IsPassThrough ? muted : soft;
                    // Without the icon font (or for an unset slot) the short name goes inside the bubble.
                    if (glyph.Length > 0)
                    {
                        // DrawString pads the text box and follows the font's line metrics, which leaves
                        // icon glyphs off-centre. Centre the glyph's actual outline on the bubble instead.
                        using (var outline = new GraphicsPath())
                        {
                            outline.AddString(glyph, glyphFont.FontFamily, (int)FontStyle.Regular, glyphFont.Size,
                                PointF.Empty, StringFormat.GenericTypographic);
                            RectangleF bounds = outline.GetBounds();
                            using (var move = new Matrix())
                            {
                                move.Translate(x - (bounds.Left + bounds.Width / 2F), y - (bounds.Top + bounds.Height / 2F));
                                outline.Transform(move);
                            }
                            g.FillPath(ink, outline);
                        }
                    }
                    else g.DrawString(labels[i], textFont, ink, circle, center);
                }

                if (selectedIndex >= 0)
                {
                    string name = labels[selectedIndex];
                    float width = Math.Min(g.MeasureString(name, nameFont).Width + 26F * scale, Math.Max(60F * scale, (orbit - bubble) * 2F - 6F * scale));
                    float height = Math.Max(26F, 32F * scale);
                    var pill = new RectangleF(c - width / 2F, c - height / 2F, width, height);
                    using (var back = new SolidBrush(HubFace))
                    using (var pillEdge = new Pen(Light ? Color.FromArgb(70, 120, 130, 150) : Color.Transparent, Math.Max(1F, scale)))
                    using (var title = new SolidBrush(Light ? Theme.Text : Color.White))
                    using (GraphicsPath shape = Theme.RoundedRectangle(pill, height / 2F - 0.5F))
                    {
                        g.FillPath(back, shape);
                        if (Light) g.DrawPath(pillEdge, shape);
                        using (var single = new StringFormat(center) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
                            g.DrawString(name, nameFont, title, pill, single);
                    }
                }
                else
                {
                    // A small dot marks the centre, where releasing or clicking cancels.
                    float dot = Math.Max(5F, 8F * scale);
                    using (var back = new SolidBrush(Light ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(190, 20, 23, 33)))
                    using (var ring = new Pen(Light ? Color.FromArgb(130, 120, 130, 150) : Color.FromArgb(130, 255, 255, 255), Math.Max(1F, 1.4F * scale)))
                    {
                        g.FillEllipse(back, c - dot, c - dot, dot * 2F, dot * 2F);
                        g.DrawEllipse(ring, c - dot, c - dot, dot * 2F, dot * 2F);
                    }
                }
            }
            return bitmap;
        }

        internal Bitmap RenderFrame()
        {
            if (Bubbles && !showNumbers) return RenderBubbles();
            int side = Width;
            float c = side / 2F;
            float outer = OuterRadius * scale;
            float inner = InnerRadius * scale;
            float hub = HubRadius * scale;
            var bitmap = new Bitmap(side, side, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var labelFont = new Font(Theme.UiFont, 12.5F * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var hubFont = new Font(Theme.UiFont, 14.5F * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var hintFont = new Font(Theme.UiFont, 11F * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var glyphFont = new Font(iconFont ?? Theme.UiFont, 21F * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var white = new SolidBrush(Color.White))
            using (var soft = new SolidBrush(Ink))
            using (var muted = new SolidBrush(MutedInk))
            using (var title = new SolidBrush(Light ? Theme.Text : Color.White))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                // Stacked translucent discs fade out towards the edge and read as a soft shadow.
                using (var shadow = new SolidBrush(Color.FromArgb(7, 0, 0, 0)))
                {
                    for (int i = 12; i >= 1; i--)
                    {
                        float r = outer + ShadowSize * scale * i / 12F;
                        g.FillEllipse(shadow, c - r, c - r + 2F * scale, r * 2F, r * 2F);
                    }
                }

                using (var normal = new SolidBrush(Light ? Color.FromArgb(244, 250, 251, 253) : Color.FromArgb(238, 30, 34, 47)))
                using (var edge = new Pen(Light ? Color.FromArgb(60, 120, 130, 150) : Color.FromArgb(46, 255, 255, 255), Math.Max(1F, scale)))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        bool selected = i == selectedIndex;
                        float start = -112.5F + i * 45F + GapDegrees / 2F;
                        float reach = selected ? outer + PopOut * scale : outer;
                        using (GraphicsPath slice = Slice(c, inner, reach, start, 45F - GapDegrees))
                        {
                            if (selected)
                            {
                                using (var glow = new LinearGradientBrush(new RectangleF(0, 0, side, side),
                                    Color.FromArgb(250, 64, 140, 255), Color.FromArgb(250, 24, 92, 220), 60F))
                                    g.FillPath(glow, slice);
                            }
                            else
                            {
                                g.FillPath(normal, slice);
                                g.DrawPath(edge, slice);
                            }
                        }

                        double angle = (-90.0 + i * 45.0) * Math.PI / 180.0;
                        float middle = (inner + reach) / 2F;
                        float x = c + (float)Math.Cos(angle) * middle;
                        float y = c + (float)Math.Sin(angle) * middle;
                        ActionBinding slot = slots[i];
                        string glyph = iconFont == null || slot.IsPassThrough ? "" : Glyph(slot.Action);
                        Brush ink = selected ? white : slot.IsPassThrough ? muted : soft;
                        float labelY = y;
                        if (glyph.Length > 0)
                        {
                            g.DrawString(glyph, glyphFont, ink, new RectangleF(x - 20F * scale, y - 27F * scale, 40F * scale, 30F * scale), center);
                            labelY = y + 15F * scale;
                        }
                        g.DrawString(labels[i], labelFont, ink,
                            new RectangleF(x - 43F * scale, labelY - 17F * scale, 86F * scale, 34F * scale), center);
                    }
                }

                using (var hubBrush = new SolidBrush(HubFace))
                using (var hubEdge = new Pen(selectedIndex >= 0 ? Color.FromArgb(220, 64, 140, 255)
                    : Light ? Color.FromArgb(80, 120, 130, 150) : Color.FromArgb(60, 255, 255, 255), 1.5F * scale))
                {
                    g.FillEllipse(hubBrush, c - hub, c - hub, hub * 2F, hub * 2F);
                    g.DrawEllipse(hubEdge, c - hub, c - hub, hub * 2F, hub * 2F);
                }
                bool none = selectedIndex < 0;
                string hubText = none ? (showNumbers ? "Actions Ring" : ClickMode ? Lang.T("點選功能", "Choose") : Lang.T("移動選擇", "Select")) : labels[selectedIndex];
                var hubArea = new RectangleF(c - hub + 4F * scale, c - 24F * scale, hub * 2F - 8F * scale, 30F * scale);
                // Long custom shortcuts or program names drop to the smaller font and stay on one line.
                bool compact = g.MeasureString(hubText, hubFont).Width > hubArea.Width;
                using (var single = new StringFormat(center) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
                    g.DrawString(hubText, compact ? hintFont : hubFont, none ? soft : title, hubArea, single);
                string hubHint = ClickMode
                    ? (none ? Lang.T("點此取消", "click to cancel") : Lang.T("點一下執行", "click to run"))
                    : (none ? Lang.T("放開取消", "release to cancel") : Lang.T("放開執行", "release to run"));
                if (showNumbers)
                {
                    // A numbered tab on the rim of each slice, matching the rows of the customize dialog.
                    using (var badgeFont = new Font("Segoe UI", 11F * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var accent = new SolidBrush(Color.FromArgb(47, 107, 255)))
                    using (var rim = new Pen(Color.White, 1.5F * scale))
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            bool selected = i == selectedIndex;
                            double angle = (-90.0 + i * 45.0) * Math.PI / 180.0;
                            float radius = (selected ? outer + PopOut * scale : outer) - 2F * scale;
                            float r = 10F * scale;
                            var circle = new RectangleF(c + (float)Math.Cos(angle) * radius - r, c + (float)Math.Sin(angle) * radius - r, r * 2F, r * 2F);
                            g.FillEllipse(selected ? white : accent, circle);
                            g.DrawEllipse(rim, circle);
                            g.DrawString((i + 1).ToString(), badgeFont, selected ? accent : white, circle, center);
                        }
                    }
                }
                if (showNumbers)
                    hubHint = Lang.T("點一格來設定", "click a slice");
                g.DrawString(hubHint, hintFont, muted,
                    new RectangleF(c - hub, c + 6F * scale, hub * 2F, 18F * scale), center);
            }
            return bitmap;
        }

        [StructLayout(LayoutKind.Sequential)] private struct RingPoint { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct RingSize { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref RingPoint position,
            ref RingSize size, IntPtr source, ref RingPoint sourcePoint, int colorKey, ref BlendFunction blend, int flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr gdiObject);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);
    }

    internal static class ActionRunner
    {
        private const ushort KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_MENU = 0x12;
        private const ushort VK_LWIN = 0x5B;
        private const ushort VK_SHIFT = 0x10;

        // Set by MainForm: shows this app's settings window (posted to the UI thread).
        public static Action OpenSettings;

        public static bool Run(ActionBinding binding)
        {
            if (binding == null) return false;
            if (binding.Action == MappedAction.CustomShortcut) return RunShortcut(binding.Argument);
            if (binding.Action == MappedAction.LaunchProgram) return Launch(binding.Argument);
            return Run(binding.Action);
        }

        private static bool RunShortcut(string text)
        {
            Keys key;
            bool ctrl, alt, shift, win;
            if (!ShortcutText.TryParse(text, out key, out ctrl, out alt, out shift, out win)) return false;
            var keys = new List<ushort>();
            if (ctrl) keys.Add(VK_CONTROL);
            if (alt) keys.Add(VK_MENU);
            if (shift) keys.Add(VK_SHIFT);
            if (win) keys.Add(VK_LWIN);
            keys.Add((ushort)key);
            bool ok = true;
            for (int i = 0; i < keys.Count; i++) ok = Key(keys[i], false, IsExtended(keys[i])) && ok;
            for (int i = keys.Count - 1; i >= 0; i--) ok = Key(keys[i], true, IsExtended(keys[i])) && ok;
            return ok;
        }

        // Navigation-cluster keys need the extended flag, otherwise Shift+arrow is read as numpad input.
        private static bool IsExtended(ushort key)
        {
            return (key >= 0x21 && key <= 0x28) || key == 0x2D || key == 0x2E ||
                key == 0x5B || key == 0x5C || key == 0x6F || key == 0x90;
        }

        // A quoted first token is the program and the remainder its arguments; otherwise the
        // whole text is one target, so unquoted paths with spaces and URLs work as typed.
        public static void SplitCommand(string command, out string file, out string arguments)
        {
            file = (command ?? "").Trim();
            arguments = "";
            if (file.StartsWith("\""))
            {
                int end = file.IndexOf('"', 1);
                if (end > 0)
                {
                    arguments = file.Substring(end + 1).Trim();
                    file = file.Substring(1, end - 1);
                }
            }
        }

        private static bool Launch(string command)
        {
            string file, arguments;
            SplitCommand(command, out file, out arguments);
            if (file.Length == 0) return false;
            try
            {
                var info = new ProcessStartInfo(file, arguments);
                info.UseShellExecute = true;
                if (File.Exists(file))
                {
                    string directory = Path.GetDirectoryName(file);
                    if (!String.IsNullOrEmpty(directory)) info.WorkingDirectory = directory;
                }
                Process.Start(info);
                return true;
            }
            catch { return false; }
        }

        public static bool Run(MappedAction action)
        {
            switch (action)
            {
                case MappedAction.PassThrough: return false;
                case MappedAction.OpenSettings:
                    if (OpenSettings == null) return false;
                    OpenSettings();
                    return true;
                case MappedAction.BrowserBack: return Tap(0xA6);
                case MappedAction.BrowserForward: return Tap(0xA7);
                case MappedAction.Copy: return Chord(VK_CONTROL, 0x43);
                case MappedAction.Paste: return Chord(VK_CONTROL, 0x56);
                case MappedAction.Cut: return Chord(VK_CONTROL, 0x58);
                case MappedAction.Undo: return Chord(VK_CONTROL, 0x5A);
                case MappedAction.Redo: return Chord(VK_CONTROL, 0x59);
                case MappedAction.TaskView: return Chord(VK_LWIN, 0x09);
                case MappedAction.ShowDesktop: return Chord(VK_LWIN, 0x44);
                case MappedAction.DesktopLeft: return Chord(VK_LWIN, VK_CONTROL, 0x25);
                case MappedAction.DesktopRight: return Chord(VK_LWIN, VK_CONTROL, 0x27);
                case MappedAction.AltTab: return Chord(VK_MENU, 0x09);
                case MappedAction.PlayPause: return Tap(0xB3);
                case MappedAction.VolumeUp: return Tap(0xAF);
                case MappedAction.VolumeDown: return Tap(0xAE);
                case MappedAction.Mute: return Tap(0xAD);
                case MappedAction.Enter: return Tap(0x0D);
                case MappedAction.Escape: return Tap(0x1B);
                case MappedAction.NextTrack: return Tap(0xB0);
                case MappedAction.PrevTrack: return Tap(0xB1);
                case MappedAction.FileExplorer: return Combo(VK_LWIN, 0x45);
                case MappedAction.Settings: return Combo(VK_LWIN, 0x49);
                case MappedAction.Calculator: return Launch("calc.exe");
                case MappedAction.TaskManager: return Combo(VK_CONTROL, VK_SHIFT, 0x1B);
                case MappedAction.Search: return Combo(VK_LWIN, 0x53);
                case MappedAction.StartMenu: return Combo(VK_LWIN);
                case MappedAction.NewTab: return Combo(VK_CONTROL, 0x54);
                case MappedAction.CloseTab: return Combo(VK_CONTROL, 0x57);
                case MappedAction.ReopenTab: return Combo(VK_CONTROL, VK_SHIFT, 0x54);
                case MappedAction.Refresh: return Combo(0x74);
                case MappedAction.ZoomIn: return Combo(VK_CONTROL, 0xBB);
                case MappedAction.ZoomOut: return Combo(VK_CONTROL, 0xBD);
                case MappedAction.Screenshot: return Combo(VK_LWIN, VK_SHIFT, 0x53);
                case MappedAction.Emoji: return Combo(VK_LWIN, 0xBE);
                case MappedAction.ClipboardHistory: return Combo(VK_LWIN, 0x56);
                // Windows ignores a synthesized Win+L, so this one goes through the API.
                case MappedAction.LockPC: return LockWorkStation();
                case MappedAction.QuickSettings: return Combo(VK_LWIN, 0x41);
                case MappedAction.MinimizeWindow: return Combo(VK_LWIN, 0x28);
                case MappedAction.MaximizeWindow: return Combo(VK_LWIN, 0x26);
                case MappedAction.CloseWindow: return Combo(VK_MENU, 0x73);
                case MappedAction.SnapLeft: return Combo(VK_LWIN, 0x25);
                case MappedAction.SnapRight: return Combo(VK_LWIN, 0x27);
                case MappedAction.SelectAll: return Combo(VK_CONTROL, 0x41);
                case MappedAction.Save: return Combo(VK_CONTROL, 0x53);
                case MappedAction.Find: return Combo(VK_CONTROL, 0x46);
                case MappedAction.Delete: return Combo(0x2E);
                case MappedAction.InsertDate: return TypeText(DateTime.Now.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture));
                case MappedAction.InsertTime: return TypeText(DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            }
            return false;
        }

        // Like Chord, but navigation-cluster and Win keys carry the extended flag.
        private static bool Combo(params ushort[] keys)
        {
            bool ok = true;
            for (int i = 0; i < keys.Length; i++) ok = Key(keys[i], false, IsExtended(keys[i])) && ok;
            for (int i = keys.Length - 1; i >= 0; i--) ok = Key(keys[i], true, IsExtended(keys[i])) && ok;
            return ok;
        }

        // Types text as Unicode characters, independent of the keyboard layout or IME state.
        private static bool TypeText(string text)
        {
            bool ok = text.Length > 0;
            foreach (char c in text)
            {
                for (int phase = 0; phase < 2; phase++)
                {
                    INPUT input = new INPUT();
                    input.type = 1;
                    input.U.ki.wScan = c;
                    input.U.ki.dwFlags = 0x0004u | (phase == 1 ? (uint)KEYEVENTF_KEYUP : 0u);
                    ok = SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1 && ok;
                }
            }
            return ok;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool LockWorkStation();

        private static bool Tap(ushort key)
        {
            bool down = Key(key, false);
            bool up = Key(key, true);
            return down && up;
        }

        private static bool Chord(params ushort[] keys)
        {
            bool ok = true;
            for (int i = 0; i < keys.Length; i++) ok = Key(keys[i], false) && ok;
            for (int i = keys.Length - 1; i >= 0; i--) ok = Key(keys[i], true) && ok;
            return ok;
        }

        private static bool Key(ushort key, bool up)
        {
            return Key(key, up, false);
        }

        private static bool Key(ushort key, bool up, bool extended)
        {
            INPUT input = new INPUT();
            input.type = 1;
            input.U.ki.wVk = key;
            input.U.ki.dwFlags = (up ? KEYEVENTF_KEYUP : (ushort)0) | (extended ? 1u : 0u);
            INPUT[] inputs = { input };
            return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) == 1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    }

    // A host stored on one of the mouse's Easy-Switch channels.
    internal sealed class HostEntry
    {
        public int Index;
        public string Name = "";
        public bool Paired;
        public int Bus;     // 4 = Bluetooth, 5 = Logi Bolt (matched against replies captured from the mouse)

        public string BusName
        {
            get { return Bus == 4 ? "Bluetooth" : Bus == 5 ? "Logi Bolt" : ""; }
        }
    }

    internal enum ScreenEdge { Left, Right, Top, Bottom }

    // Which outer edge of the desktop the pointer is touching. With several monitors only the
    // outer edge of the outermost monitor counts; edges where two monitors meet never do.
    internal static class EdgeGeometry
    {
        private static Rectangle Union(Rectangle[] screens)
        {
            Rectangle all = screens[0];
            for (int i = 1; i < screens.Length; i++) all = Rectangle.Union(all, screens[i]);
            return all;
        }

        private static bool Outermost(Rectangle screen, ScreenEdge edge, Rectangle all)
        {
            switch (edge)
            {
                case ScreenEdge.Left: return screen.Left <= all.Left;
                case ScreenEdge.Right: return screen.Right >= all.Right;
                case ScreenEdge.Top: return screen.Top <= all.Top;
                default: return screen.Bottom >= all.Bottom;
            }
        }

        // A corner touches two edges at once and is ignored.
        public static bool TryGetEdge(Point point, Rectangle[] screens, out ScreenEdge edge)
        {
            edge = ScreenEdge.Left;
            if (screens == null || screens.Length == 0) return false;
            Rectangle all = Union(screens);
            foreach (Rectangle screen in screens)
            {
                if (!Rectangle.Inflate(screen, 1, 1).Contains(point)) continue;
                int found = 0;
                // The pointer's range is Left ... Right-1, so the right and bottom sides get one more pixel.
                if (point.X <= screen.Left + 1 && Outermost(screen, ScreenEdge.Left, all)) { edge = ScreenEdge.Left; found++; }
                if (point.X >= screen.Right - 2 && Outermost(screen, ScreenEdge.Right, all)) { edge = ScreenEdge.Right; found++; }
                if (point.Y <= screen.Top + 1 && Outermost(screen, ScreenEdge.Top, all)) { edge = ScreenEdge.Top; found++; }
                if (point.Y >= screen.Bottom - 2 && Outermost(screen, ScreenEdge.Bottom, all)) { edge = ScreenEdge.Bottom; found++; }
                return found == 1;
            }
            return false;
        }

        // Where to put the pointer when the mouse comes back: just inside the given outer edge,
        // keeping its position along the edge when that still falls on the monitor.
        public static bool TryGetArrivalPoint(ScreenEdge edge, Point near, Rectangle[] screens, int inset, out Point result)
        {
            result = near;
            if (screens == null || screens.Length == 0) return false;
            Rectangle all = Union(screens);
            bool horizontal = edge == ScreenEdge.Left || edge == ScreenEdge.Right;
            bool have = false;
            Rectangle chosen = screens[0];
            foreach (Rectangle screen in screens)
            {
                if (!Outermost(screen, edge, all)) continue;
                bool spans = horizontal ? near.Y >= screen.Top && near.Y < screen.Bottom : near.X >= screen.Left && near.X < screen.Right;
                if (!have || spans)
                {
                    chosen = screen;
                    have = true;
                    if (spans) break;
                }
            }
            if (!have) return false;
            int x = Math.Min(Math.Max(near.X, chosen.Left + inset), chosen.Right - 1 - inset);
            int y = Math.Min(Math.Max(near.Y, chosen.Top + inset), chosen.Bottom - 1 - inset);
            switch (edge)
            {
                case ScreenEdge.Left: result = new Point(chosen.Left + inset, y); break;
                case ScreenEdge.Right: result = new Point(chosen.Right - 1 - inset, y); break;
                case ScreenEdge.Top: result = new Point(x, chosen.Top + inset); break;
                default: result = new Point(x, chosen.Bottom - 1 - inset); break;
            }
            return true;
        }
    }

    internal enum MouseHookKind
    {
        Move, LeftDown, LeftUp, RightDown, RightUp, MiddleDown, MiddleUp, XButton1Down, XButton1Up, XButton2Down, XButton2Up, WheelLeft, WheelRight, Other
    }

    internal sealed class MouseHookEvent
    {
        public MouseHookKind Kind;
        public Point Point;
        public bool Injected;
    }

    internal static class MouseHook
    {
        public delegate bool MouseEventHandler(MouseHookEvent e);
        public static event MouseEventHandler MouseEvent;
        public static bool Enabled = true;
        private static IntPtr hook;
        private static readonly HookProc callback = HookCallback;
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
        private const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        private const int WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C, WM_MOUSEHWHEEL = 0x020E;

        public static void Install()
        {
            hook = SetWindowsHookEx(WH_MOUSE_LL, callback, IntPtr.Zero, 0);
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public static void Uninstall()
        {
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }

        private static IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && Enabled && MouseEvent != null)
            {
                MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                MouseHookKind kind = MouseHookKind.Other;
                int msg = wParam.ToInt32();
                if (msg == WM_MOUSEMOVE) kind = MouseHookKind.Move;
                else if (msg == WM_LBUTTONDOWN) kind = MouseHookKind.LeftDown;
                else if (msg == WM_LBUTTONUP) kind = MouseHookKind.LeftUp;
                else if (msg == WM_RBUTTONDOWN) kind = MouseHookKind.RightDown;
                else if (msg == WM_RBUTTONUP) kind = MouseHookKind.RightUp;
                else if (msg == WM_MBUTTONDOWN) kind = MouseHookKind.MiddleDown;
                else if (msg == WM_MBUTTONUP) kind = MouseHookKind.MiddleUp;
                else if (msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP)
                {
                    int button = (short)((data.mouseData >> 16) & 0xFFFF);
                    if (button == 1) kind = msg == WM_XBUTTONDOWN ? MouseHookKind.XButton1Down : MouseHookKind.XButton1Up;
                    else if (button == 2) kind = msg == WM_XBUTTONDOWN ? MouseHookKind.XButton2Down : MouseHookKind.XButton2Up;
                }
                else if (msg == WM_MOUSEHWHEEL)
                {
                    short delta = (short)((data.mouseData >> 16) & 0xFFFF);
                    kind = delta < 0 ? MouseHookKind.WheelLeft : MouseHookKind.WheelRight;
                }
                var ev = new MouseHookEvent { Kind = kind, Point = new Point(data.pt.x, data.pt.y), Injected = (data.flags & 1) != 0 };
                try { if (MouseEvent(ev)) return new IntPtr(1); }
                catch { }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT
        {
            public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo;
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr mod, uint threadId);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    }

    internal sealed class HidppDiversionManager : IDisposable
    {
        private const ushort TargetFeature = 0x1B04;
        private const byte SoftwareId = 0x0A;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileFlagOverlapped = 0x40000000;
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;
        private const int HidpStatusSuccess = 0x00110000;
        private static readonly int[] TargetControls = { 0x00C3, 0x01A0 };
        // Prefixes on report text that tell the UI the outcome without parsing translated wording.
        public const string ReadyMark = "[ready] ";
        public const string ProblemMark = "[!] ";

        private readonly Action<string> report;
        private readonly Action<int, int> ready;
        private readonly object sync = new object();
        private Thread worker;
        private volatile bool disposed;
        private volatile bool desiredEnabled = true;
        private HidTransport transport;
        private int deviceIndex = -1;
        private int featureIndex = -1;
        private readonly List<int> divertedControls = new List<int>();
        private int recheckBusy;
        private int silentChecks;
        private int lastBoltIndex = -1;

        public HidppDiversionManager(Action<string> reportAction, Action<int, int> readyAction)
        {
            report = reportAction;
            ready = readyAction;
        }

        public void Start()
        {
            if (worker != null) return;
            worker = new Thread(Initialize);
            worker.IsBackground = true;
            worker.Name = "MXMasterWin HID++";
            worker.Start();
        }

        public void SetEnabled(bool enabled)
        {
            desiredEnabled = enabled;
            Thread thread = new Thread(new ThreadStart(delegate
            {
                lock (sync)
                {
                    if (disposed || transport == null || featureIndex < 0) return;
                    // Multiple tray toggles can queue worker threads in a different order.
                    // Always apply the newest requested state once the lock is acquired.
                    try { ApplyDiversion(desiredEnabled); }
                    catch { }
                }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        private void Initialize()
        {
            try
            {
                lock (sync)
                {
                    if (disposed) return;
                    Connect();
                }
            }
            catch (Exception ex)
            {
                Report(ProblemMark + Lang.T("HID++ 初始化失敗：", "HID++ initialisation failed: ") + ex.Message);
            }
        }

        // Temporary diversion only lasts for the mouse's current connection: it is gone after the
        // mouse switches to another host and back, or reconnects after sleep or power-off. Called
        // when the mouse shows activity after a pause; puts the diversion back if it was dropped.
        public void Recheck()
        {
            if (disposed || worker == null) return;
            if (Interlocked.Exchange(ref recheckBusy, 1) != 0) return;
            Thread thread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    // HID++ may not answer yet right after the first input report of a reconnect.
                    for (int attempt = 0; attempt < 4 && !disposed; attempt++)
                    {
                        if (attempt > 0) Thread.Sleep(1500);
                        bool settled;
                        try { settled = EnsureDiversion(); }
                        catch (Exception ex)
                        {
                            settled = false;
                            Report("recheck failed: " + ex.Message);
                        }
                        if (settled) break;
                    }
                }
                finally { Interlocked.Exchange(ref recheckBusy, 0); }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        // Returns true when nothing is left to do, false when the caller should try again.
        private bool EnsureDiversion()
        {
            lock (sync)
            {
                if (disposed) return true;
                if (transport != null && featureIndex >= 0 && divertedControls.Count > 0)
                {
                    // 1 = as requested, 0 = the mouse reports something else, -1 = no answer, -2 = handle unusable
                    int state;
                    try { state = ReadDiversionState(); }
                    catch { state = -2; }
                    if (state == 1)
                    {
                        silentChecks = 0;
                        return true;
                    }
                    if (state == 0)
                    {
                        silentChecks = 0;
                        Report("diversion state changed on the mouse (reconnected?); re-applying");
                        if (!ApplyDiversion(desiredEnabled)) return false;
                        ready(deviceIndex, featureIndex);
                        Report(ReadyMark + transport.Name + " special buttons re-diverted");
                        return true;
                    }
                    // A Bolt receiver stays plugged in after the mouse has moved to Bluetooth, so a silent
                    // channel must be given up as well; Connect() then looks on every interface.
                    if (state == -1 && ++silentChecks < 2) return false;
                    Report("HID++ channel is not answering; reopening");
                }
                ResetConnection();
                return Connect();
            }
        }

        private int ReadDiversionState()
        {
            foreach (int cid in divertedControls)
            {
                byte[] state = transport.Request(deviceIndex, featureIndex, 2, SoftwareId,
                    new byte[] { (byte)(cid >> 8), (byte)cid }, 700);
                if (state == null || state.Length < 9 ||
                    state[4] != (byte)(cid >> 8) || state[5] != (byte)cid) return -1;
                if (((state[6] & 0x01) != 0) != desiredEnabled) return 0;
            }
            return 1;
        }

        private void ResetConnection()
        {
            if (transport != null)
            {
                transport.Dispose();
                transport = null;
            }
            deviceIndex = -1;
            featureIndex = -1;
            silentChecks = 0;
            divertedControls.Clear();
            featureIndexes.Clear();
            wheelCapability = null;
        }

        // Finds the mouse's HID++ channel and applies the requested diversion. Caller holds sync.
        private bool Connect()
        {
            List<HidTransport> transports = HidTransport.FindMxMasterTransports(Report);
            // The direct (Bluetooth) interface answers or times out with a single probe, while an
            // empty receiver slot costs a full timeout each, so try Bluetooth first.
            transports.Sort(delegate(HidTransport a, HidTransport b) { return a.IsBolt.CompareTo(b.IsBolt); });
            foreach (HidTransport candidateTransport in transports)
            {
                if (disposed)
                {
                    candidateTransport.Dispose();
                    continue;
                }

                int[] candidateDevices = new[] { 0xFF };
                if (candidateTransport.IsBolt)
                {
                    // Start with the slot the mouse used last time.
                    var slots = new List<int>(new[] { 1, 2, 3, 4, 5, 6 });
                    if (slots.Remove(lastBoltIndex)) slots.Insert(0, lastBoltIndex);
                    candidateDevices = slots.ToArray();
                }

                foreach (int candidateDevice in candidateDevices)
                {
                    byte[] root = candidateTransport.Request(candidateDevice, 0, 0, SoftwareId,
                        // A sleeping Bolt device can take about one second to wake and answer.
                        new byte[] { (byte)(TargetFeature >> 8), (byte)(TargetFeature & 0xFF), 0 }, 1800);
                    if (root == null || root.Length < 7 || root[4] == 0) continue;
                    transport = candidateTransport;
                    deviceIndex = candidateDevice;
                    if (candidateTransport.IsBolt) lastBoltIndex = candidateDevice;
                    featureIndex = root[4];
                    int version = root[6];
                    Report(transport.Name + " dev=" + deviceIndex.ToString("X2") +
                        " feature 1B04 index=" + featureIndex.ToString("X2") + " v" + version);
                    break;
                }

                if (transport == candidateTransport) break;
                candidateTransport.Dispose();
            }

            foreach (HidTransport candidateTransport in transports)
            {
                if (candidateTransport != transport) candidateTransport.Dispose();
            }

            if (transport == null)
            {
                Report(ProblemMark + (transports.Count == 0
                    ? Lang.T("找不到可寫入的 Logitech HID++ 長報告介面", "No writable Logitech HID++ long-report interface was found")
                    : Lang.T("找到 HID++ 長報告介面，但找不到支援 1B04 的 MX Master 4 通道",
                        "A HID++ interface was found, but no MX Master 4 channel supporting 1B04")));
                return false;
            }

            DiscoverDivertableControls();
            if (divertedControls.Count == 0)
            {
                Report(ProblemMark + Lang.T("裝置沒有回報可 diversion 的 C3／1A0 控制",
                    "The device reported no divertable C3 / 1A0 controls"));
                return false;
            }

            if (!ApplyDiversion(desiredEnabled)) return false;
            ready(deviceIndex, featureIndex);
            Report(ReadyMark + transport.Name + " special buttons diverted (" +
                String.Join(", ", divertedControls.ConvertAll(delegate(int c) { return c.ToString("X4"); }).ToArray()) + ")");
            return true;
        }

        private void DiscoverDivertableControls()
        {
            byte[] countReply = transport.Request(deviceIndex, featureIndex, 0, SoftwareId, new byte[0], 600);
            if (countReply == null || countReply.Length < 5) return;
            int count = Math.Min((int)countReply[4], 64);
            for (int i = 0; i < count; i++)
            {
                byte[] info = transport.Request(deviceIndex, featureIndex, 1, SoftwareId,
                    new byte[] { (byte)i }, 600);
                if (info == null || info.Length < 9) continue;
                int cid = (info[4] << 8) | info[5];
                int flags = info[8];
                bool target = Array.IndexOf(TargetControls, cid) >= 0;
                bool temporarilyDivertable = (flags & 0x20) != 0;
                if (target)
                {
                    Report("CID " + cid.ToString("X4") + " flags=" + flags.ToString("X2") +
                        (temporarilyDivertable ? " divertable" : " not-divertable"));
                    if (temporarilyDivertable) divertedControls.Add(cid);
                }
            }
        }

        private bool ApplyDiversion(bool enable)
        {
            bool allSucceeded = true;
            foreach (int cid in divertedControls)
            {
                // Bit 0 is temporary diversion; bit 1 says to update bit 0.
                byte changeFlags = enable ? (byte)0x03 : (byte)0x02;
                byte[] response = transport.Request(deviceIndex, featureIndex, 3, SoftwareId,
                    new byte[] { (byte)(cid >> 8), (byte)cid, changeFlags, 0, 0 }, 700);
                bool ok = response != null && response.Length >= 9 &&
                    response[4] == (byte)(cid >> 8) && response[5] == (byte)cid;
                if (ok)
                {
                    byte[] state = transport.Request(deviceIndex, featureIndex, 2, SoftwareId,
                        new byte[] { (byte)(cid >> 8), (byte)cid }, 700);
                    ok = state != null && state.Length >= 9 &&
                        state[4] == (byte)(cid >> 8) && state[5] == (byte)cid &&
                        ((state[6] & 0x01) != 0) == enable;
                }
                if (!ok)
                {
                    allSucceeded = false;
                    Report(ProblemMark + (enable
                        ? Lang.T("無法啟用專用按鍵 ", "Could not divert special button ")
                        : Lang.T("無法解除專用按鍵 ", "Could not release special button ")) + cid.ToString("X4"));
                }
                else
                {
                    Report("CID " + cid.ToString("X4") + " diversion=" + (enable ? "on" : "off") + " verified");
                }
            }
            return allSucceeded;
        }

        // ---- Device information and settings (battery, firmware, DPI, SmartShift, haptics) ----
        // Feature ids and byte layouts follow Solaar's hidpp20 implementation. Every write is
        // read back, and callers only report success when the device confirms the new value.

        private readonly Dictionary<int, int> featureIndexes = new Dictionary<int, int>();
        private string wheelCapability;

        private int FindFeature(int feature)
        {
            int index;
            if (featureIndexes.TryGetValue(feature, out index)) return index;
            byte[] root = transport.Request(deviceIndex, 0, 0, SoftwareId,
                new byte[] { (byte)(feature >> 8), (byte)feature, 0 }, 1800);
            if (root == null || root.Length < 5) return -1;
            index = root[4] != 0 ? root[4] : -1;
            featureIndexes[feature] = index;
            return index;
        }

        private byte[] Call(int feature, int function, params byte[] parameters)
        {
            int index = FindFeature(feature);
            if (index < 0) return null;
            byte[] reply = transport.Request(deviceIndex, index, function, SoftwareId, parameters, 900);
            return reply != null && reply.Length >= 9 ? reply : null;
        }

        private static string Hex(byte[] reply)
        {
            return BitConverter.ToString(reply, 4, Math.Min(reply.Length - 4, 12));
        }

        public DeviceSnapshot ReadDevice()
        {
            lock (sync)
            {
                if (disposed || transport == null || deviceIndex < 0) return null;
                var snapshot = new DeviceSnapshot();
                snapshot.Transport = transport.Name;
                try
                {
                    ReadBattery(snapshot);
                    ReadFirmware(snapshot);
                    ReadDpi(snapshot);
                    ReadSmartShift(snapshot);
                    ReadWheelDirection(snapshot);
                    ReadHaptic(snapshot);
                }
                catch (Exception ex)
                {
                    snapshot.Raw.Add("read failed: " + ex.Message);
                }
                return snapshot;
            }
        }

        private void ReadBattery(DeviceSnapshot snapshot)
        {
            byte[] reply = Call(0x1004, 1);
            if (reply != null)
            {
                snapshot.Raw.Add("1004 status=" + Hex(reply));
                snapshot.BatteryPercent = reply[4];
                snapshot.BatteryCharging = reply[6] == 1 || reply[6] == 2;
                snapshot.BatteryFull = reply[6] == 3;
                return;
            }
            reply = Call(0x1000, 0);
            if (reply != null)
            {
                snapshot.Raw.Add("1000 status=" + Hex(reply));
                snapshot.BatteryPercent = reply[4];
                snapshot.BatteryCharging = reply[6] == 1 || reply[6] == 2 || reply[6] == 4;
                snapshot.BatteryFull = reply[6] == 3;
            }
        }

        private void ReadFirmware(DeviceSnapshot snapshot)
        {
            byte[] count = Call(0x0003, 0);
            if (count == null) return;
            int entities = Math.Min((int)count[4], 8);
            for (int i = 0; i < entities; i++)
            {
                byte[] info = Call(0x0003, 1, (byte)i);
                if (info == null || info.Length < 12) continue;
                snapshot.Raw.Add("0003 entity" + i + "=" + Hex(info));
                if ((info[4] & 0x0F) != 0) continue;
                string name = Encoding.ASCII.GetString(info, 5, 3).Trim('\0', ' ');
                snapshot.Firmware = name + " " + info[8].ToString("X2") + "." + info[9].ToString("X2") +
                    ".B" + ((info[10] << 8) | info[11]).ToString("X4");
                return;
            }
        }

        private void ReadDpi(DeviceSnapshot snapshot)
        {
            byte[] current = Call(0x2201, 2, 0);
            if (current == null)
            {
                byte[] extended = Call(0x2202, 5, 0);
                if (extended != null)
                {
                    snapshot.DpiFeature = 0x2202;
                    snapshot.Raw.Add("2202 dpi=" + Hex(extended));
                }
                return;
            }
            snapshot.Raw.Add("2201 dpi=" + Hex(current));
            snapshot.DpiFeature = 0x2201;
            snapshot.DpiDefault = (current[7] << 8) | current[8];
            snapshot.Dpi = (current[5] << 8) | current[6];
            if (snapshot.Dpi == 0) snapshot.Dpi = snapshot.DpiDefault;

            byte[] list = Call(0x2201, 1, 0);
            if (list == null) return;
            snapshot.Raw.Add("2201 list=" + Hex(list));
            var values = new List<int>();
            for (int i = 5; i + 1 < list.Length; i += 2)
            {
                int value = (list[i] << 8) | list[i + 1];
                if (value == 0) break;
                values.Add(value);
            }
            // A value with the top three bits set is a step size between its two neighbours.
            for (int i = 1; i + 1 < values.Count; i++)
            {
                if ((values[i] >> 13) != 7) continue;
                snapshot.DpiMin = values[i - 1];
                snapshot.DpiStep = values[i] & 0x1FFF;
                snapshot.DpiMax = values[i + 1];
                return;
            }
        }

        private void ReadSmartShift(DeviceSnapshot snapshot)
        {
            byte[] reply = Call(0x2111, 1);
            int feature = 0x2111;
            if (reply == null)
            {
                reply = Call(0x2110, 0);
                feature = 0x2110;
            }
            if (reply == null) return;
            snapshot.Raw.Add(feature.ToString("X4") + " smartshift=" + Hex(reply));
            snapshot.SmartShiftFeature = feature;
            snapshot.WheelMode = reply[4];
            snapshot.SmartShiftThreshold = reply[5];
        }

        // 0x2121 HiRes Wheel mode byte: bit0 = HID++ target, bit1 = high resolution, bit2 = inverted.
        private void ReadWheelDirection(DeviceSnapshot snapshot)
        {
            byte[] reply = Call(0x2121, 1);
            if (reply == null) return;
            snapshot.Raw.Add("2121 mode=" + Hex(reply));
            if (wheelCapability == null)
            {
                byte[] capability = Call(0x2121, 0);
                wheelCapability = capability != null ? Hex(capability) : "";
            }
            if (wheelCapability.Length > 0) snapshot.Raw.Add("2121 capability=" + wheelCapability);
            snapshot.WheelInvertPresent = true;
            snapshot.WheelInverted = (reply[4] & 0x04) != 0;
        }

        private void ReadHaptic(DeviceSnapshot snapshot)
        {
            byte[] reply = Call(0x19B0, 1);
            if (reply == null) return;
            snapshot.Raw.Add("19B0 haptic=" + Hex(reply));
            snapshot.HapticPresent = true;
            snapshot.HapticLevel = (reply[4] & 0x01) != 0 ? reply[5] : 0;
        }

        public bool SetDpi(int dpi)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    if (Call(0x2201, 3, 0, (byte)(dpi >> 8), (byte)dpi) == null) return false;
                    byte[] check = Call(0x2201, 2, 0);
                    return check != null && ((check[5] << 8) | check[6]) == dpi;
                }
                catch { return false; }
            }
        }

        // mode: 1 = free spin, 2 = ratchet. threshold: 1-50, or 255 to never auto-disengage.
        public bool SetSmartShift(int mode, int threshold)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    bool enhanced = FindFeature(0x2111) >= 0;
                    int feature = enhanced ? 0x2111 : 0x2110;
                    if (Call(feature, enhanced ? 2 : 1, (byte)mode, (byte)threshold) == null) return false;
                    byte[] check = Call(feature, enhanced ? 1 : 0);
                    return check != null && check[4] == mode && check[5] == threshold;
                }
                catch { return false; }
            }
        }

        // Only the invert bit changes; the target and resolution bits are written back as read.
        public bool SetWheelInvert(bool inverted)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    byte[] mode = Call(0x2121, 1);
                    if (mode == null) return false;
                    byte value = (byte)(inverted ? mode[4] | 0x04 : mode[4] & ~0x04);
                    if (Call(0x2121, 2, value) == null) return false;
                    byte[] check = Call(0x2121, 1);
                    return check != null && ((check[4] & 0x04) != 0) == inverted;
                }
                catch { return false; }
            }
        }

        // level: 0 turns haptics off; otherwise 1-100.
        public bool SetHaptic(int level)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    byte[] sent = level <= 0 ? Call(0x19B0, 2, 0x00, 0x32) : Call(0x19B0, 2, 0x01, (byte)level);
                    if (sent == null) return false;
                    byte[] check = Call(0x19B0, 1);
                    if (check == null) return false;
                    bool enabled = (check[4] & 0x01) != 0;
                    return level <= 0 ? !enabled : enabled && check[5] == level;
                }
                catch { return false; }
            }
        }

        private int hapticBusy;
        private bool hapticFailureReported;

        // Plays one haptic waveform (0x19B0 function 4; waveform ids follow Solaar, 0x04 = subtle bump).
        // The strength follows the mouse's own haptic setting, so nothing plays while that is off.
        // Runs in the background; a play still in flight, or a channel busy for long, skips this one
        // rather than letting taps queue up behind a fast sweep across the ring.
        public void PlayHaptic(byte waveform)
        {
            if (disposed || Interlocked.CompareExchange(ref hapticBusy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool taken = false;
                try
                {
                    Monitor.TryEnter(sync, 100, ref taken);
                    if (!taken || disposed || transport == null || deviceIndex < 0) return;
                    int index = FindFeature(0x19B0);
                    if (index < 0) return;
                    byte[] reply = transport.Request(deviceIndex, index, 4, SoftwareId, new[] { waveform }, 250);
                    if (reply == null && !hapticFailureReported)
                    {
                        hapticFailureReported = true;
                        Report("haptic play " + waveform.ToString("X2") + " no reply");
                    }
                }
                catch { }
                finally
                {
                    if (taken) Monitor.Exit(sync);
                    Interlocked.Exchange(ref hapticBusy, 0);
                }
            });
        }

        // ---- Easy-Switch hosts (0x1814 Change Host, 0x1815 Hosts Info; byte layouts follow Solaar) ----

        // The host stored on each channel and the channel the mouse is on now (-1 when unknown).
        public List<HostEntry> ReadHosts(out int current)
        {
            current = -1;
            var hosts = new List<HostEntry>();
            lock (sync)
            {
                if (disposed || transport == null || deviceIndex < 0) return hosts;
                try
                {
                    byte[] info = Call(0x1814, 0);
                    if (info == null) return hosts;
                    current = info[5];
                    int count = Math.Min((int)info[4], 8);
                    var summary = new StringBuilder();
                    for (int i = 0; i < count; i++)
                    {
                        var entry = new HostEntry { Index = i };
                        byte[] host = Call(0x1815, 1, (byte)i);
                        if (host != null)
                        {
                            entry.Paired = host[5] != 0;
                            entry.Bus = host[6];
                            int length = host[8];
                            var name = new List<byte>();
                            while (name.Count < length)
                            {
                                byte[] piece = Call(0x1815, 3, (byte)i, (byte)name.Count);
                                if (piece == null) break;
                                int take = Math.Min(length - name.Count, piece.Length - 6);
                                if (take <= 0) break;
                                for (int k = 0; k < take; k++) name.Add(piece[6 + k]);
                            }
                            entry.Name = Encoding.UTF8.GetString(name.ToArray());
                        }
                        hosts.Add(entry);
                        summary.Append(" " + i + ":" + (entry.Name.Length == 0 ? "-" : entry.Name) + "/" + entry.Bus);
                    }
                    Report("hosts current=" + current + summary);
                }
                catch (Exception ex)
                {
                    Report("hosts read failed: " + ex.Message);
                }
            }
            return hosts;
        }

        // Moves the mouse to another channel. It leaves this computer at once and sometimes has no
        // time to answer, so a request that went out counts as done. The channel is dropped here and
        // found again by Recheck() when the mouse shows activity on this computer.
        public bool ChangeHost(int host)
        {
            lock (sync)
            {
                if (disposed || transport == null || deviceIndex < 0) return false;
                try
                {
                    int index = FindFeature(0x1814);
                    if (index < 0) return false;
                    byte[] info = transport.Request(deviceIndex, index, 0, SoftwareId, new byte[0], 900);
                    if (info == null || info.Length < 6 || host < 0 || host >= info[4] || host == info[5]) return false;
                    Report("change host " + info[5] + " -> " + host);
                    try { transport.Request(deviceIndex, index, 1, SoftwareId, new byte[] { (byte)host }, 300); }
                    catch (Exception ex) { Report("change host: " + ex.Message); }
                    ResetConnection();
                    Report(ProblemMark + Lang.T("滑鼠已切到另一台電腦", "The mouse has moved to another computer"));
                    return true;
                }
                catch (Exception ex)
                {
                    Report("change host failed: " + ex.Message);
                    return false;
                }
            }
        }

        private void Report(string text)
        {
            try { report(text); }
            catch { }
        }

        public void Dispose()
        {
            disposed = true;
            lock (sync)
            {
                if (transport != null)
                {
                    if (featureIndex >= 0 && divertedControls.Count > 0)
                    {
                        try { ApplyDiversion(false); }
                        catch { }
                    }
                    transport.Dispose();
                    transport = null;
                }
            }
        }

        private sealed class HidTransport : IDisposable
        {
            private readonly FileStream stream;
            private readonly SafeFileHandle handle;
            private readonly int inputLength;
            private readonly int outputLength;
            private readonly Action<string> report;
            public readonly string Name;
            public readonly bool IsBolt;

            private HidTransport(string path, SafeFileHandle fileHandle, HIDP_CAPS caps, Action<string> reportAction)
            {
                handle = fileHandle;
                inputLength = caps.InputReportByteLength;
                outputLength = caps.OutputReportByteLength;
                report = reportAction;
                IsBolt = path.IndexOf("PID_C548", StringComparison.OrdinalIgnoreCase) >= 0;
                Name = IsBolt ? "BOLT" : "BLE";
                stream = new FileStream(handle, FileAccess.ReadWrite, Math.Max(inputLength, outputLength), true);
            }

            public static List<HidTransport> FindMxMasterTransports(Action<string> report)
            {
                var transports = new List<HidTransport>();
                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);
                IntPtr infoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                    DigcfPresent | DigcfDeviceInterface);
                if (infoSet == new IntPtr(-1)) return transports;
                try
                {
                    for (uint index = 0; ; index++)
                    {
                        SP_DEVICE_INTERFACE_DATA data = new SP_DEVICE_INTERFACE_DATA();
                        data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                        if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref data))
                        {
                            if (Marshal.GetLastWin32Error() == 259) break;
                            continue;
                        }

                        uint required = 0;
                        SetupDiGetDeviceInterfaceDetail(infoSet, ref data, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                        if (required == 0) continue;
                        IntPtr detail = Marshal.AllocHGlobal((int)required);
                        try
                        {
                            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                            if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref data, detail, required, ref required, IntPtr.Zero))
                                continue;
                            string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                            if (String.IsNullOrEmpty(path)) continue;
                            string upper = path.ToUpperInvariant();
                            bool bolt = upper.Contains("VID_046D&PID_C548");
                            bool direct = upper.Contains("VID_046D&PID_B042") ||
                                (upper.Contains("VID&02046D") && upper.Contains("PID&B042"));
                            if (!bolt && !direct) continue;

                            SafeFileHandle file = CreateFile(path, GenericRead | GenericWrite,
                                FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting,
                                FileFlagOverlapped, IntPtr.Zero);
                            if (file.IsInvalid)
                            {
                                file.Dispose();
                                continue;
                            }

                            HIDP_CAPS caps;
                            if (!TryGetCaps(file, out caps) || caps.OutputReportByteLength < 20 || caps.InputReportByteLength < 20)
                            {
                                file.Dispose();
                                continue;
                            }
                            report((bolt ? "BOLT" : "BLE") + " HID++ interface usage=" +
                                caps.UsagePage.ToString("X4") + ":" + caps.Usage.ToString("X4") +
                                " in=" + caps.InputReportByteLength + " out=" + caps.OutputReportByteLength);
                            transports.Add(new HidTransport(path, file, caps, report));
                        }
                        finally { Marshal.FreeHGlobal(detail); }
                    }
                }
                finally { SetupDiDestroyDeviceInfoList(infoSet); }
                return transports;
            }

            private static bool TryGetCaps(SafeFileHandle file, out HIDP_CAPS caps)
            {
                caps = new HIDP_CAPS();
                IntPtr preparsed;
                if (!HidD_GetPreparsedData(file, out preparsed)) return false;
                try { return HidP_GetCaps(preparsed, out caps) == HidpStatusSuccess; }
                finally { HidD_FreePreparsedData(preparsed); }
            }

            public byte[] Request(int targetDevice, int targetFeature, int function, byte softwareId,
                byte[] parameters, int timeoutMilliseconds)
            {
                byte[] packet = new byte[outputLength];
                packet[0] = 0x11;
                packet[1] = (byte)targetDevice;
                packet[2] = (byte)targetFeature;
                packet[3] = (byte)((function << 4) | (softwareId & 0x0F));
                int parameterCount = Math.Min(parameters.Length, packet.Length - 4);
                Array.Copy(parameters, 0, packet, 4, parameterCount);

                stream.Write(packet, 0, packet.Length);
                stream.Flush();

                Stopwatch timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < timeoutMilliseconds)
                {
                    int remaining = Math.Max(1, timeoutMilliseconds - (int)timer.ElapsedMilliseconds);
                    byte[] response = ReadOne(remaining);
                    if (response == null) return null;
                    if (response.Length < 4 || response[1] != (byte)targetDevice) continue;
                    if (response[2] == 0xFF && response.Length >= 6 &&
                        response[3] == (byte)targetFeature && response[4] == packet[3])
                    {
                        report(Name + " HID++ error=" + response[5].ToString("X2") +
                            " request=" + BitConverter.ToString(packet, 0, Math.Min(packet.Length, 10)));
                        return null;
                    }
                    if (response[2] == (byte)targetFeature && response[3] == packet[3])
                        return response;
                }
                return null;
            }

            private byte[] ReadOne(int timeoutMilliseconds)
            {
                byte[] buffer = new byte[inputLength];
                IAsyncResult pending = stream.BeginRead(buffer, 0, buffer.Length, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(timeoutMilliseconds))
                {
                    CancelIoEx(handle, IntPtr.Zero);
                    try { stream.EndRead(pending); }
                    catch { }
                    return null;
                }
                int read = stream.EndRead(pending);
                if (read <= 0) return null;
                if (read == buffer.Length) return buffer;
                byte[] exact = new byte[read];
                Array.Copy(buffer, exact, read);
                return exact;
            }

            public void Dispose()
            {
                try { stream.Dispose(); }
                catch { }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data,
            IntPtr detail, uint detailSize, ref uint required, IntPtr deviceInfo);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
    }

    internal sealed class DeviceSnapshot
    {
        public string Transport = "";
        public int BatteryPercent = -1;
        public bool BatteryCharging;
        public bool BatteryFull;
        public string Firmware = "";
        public int DpiFeature, Dpi, DpiDefault, DpiMin, DpiMax, DpiStep;
        public int SmartShiftFeature, WheelMode, SmartShiftThreshold;
        public bool WheelInvertPresent, WheelInverted;
        public bool HapticPresent;
        public int HapticLevel;
        public readonly List<string> Raw = new List<string>();

        public string BatteryText
        {
            get
            {
                if (BatteryPercent < 0) return Lang.T("未知", "Unknown");
                return BatteryPercent + "%" + (BatteryFull ? Lang.T("（已充飽）", " (full)") : BatteryCharging ? Lang.T("（充電中）", " (charging)") : "");
            }
        }
    }

    // Device values the user applied from this app. They are kept in the INI and written back
    // at startup; -1 means "never set here, leave the mouse as it is".
    internal sealed class DeviceSettings
    {
        public int Dpi = -1;
        public int WheelMode = -1;
        public int SmartShiftThreshold = -1;
        public int WheelInvert = -1;
        public int Haptic = -1;

        public bool TryLoad(string key, string text)
        {
            if (!key.StartsWith("Device", StringComparison.OrdinalIgnoreCase)) return false;
            int value;
            if (!int.TryParse(text.Trim(), out value)) return true;
            if (key.Equals("DeviceDpi", StringComparison.OrdinalIgnoreCase))
            {
                if (value > 0 && value <= 0xFFFF) Dpi = value;
            }
            else if (key.Equals("DeviceWheelMode", StringComparison.OrdinalIgnoreCase))
            {
                if (value == 1 || value == 2) WheelMode = value;
            }
            else if (key.Equals("DeviceSmartShift", StringComparison.OrdinalIgnoreCase))
            {
                if ((value >= 1 && value <= 50) || value == 255) SmartShiftThreshold = value;
            }
            else if (key.Equals("DeviceWheelInvert", StringComparison.OrdinalIgnoreCase))
            {
                if (value == 0 || value == 1) WheelInvert = value;
            }
            else if (key.Equals("DeviceHaptic", StringComparison.OrdinalIgnoreCase))
            {
                if (value >= 0 && value <= 100) Haptic = value;
            }
            return true;
        }

        public List<string> ToLines()
        {
            var lines = new List<string>();
            if (Dpi > 0) lines.Add("DeviceDpi=" + Dpi);
            if (WheelMode > 0) lines.Add("DeviceWheelMode=" + WheelMode);
            if (SmartShiftThreshold > 0) lines.Add("DeviceSmartShift=" + SmartShiftThreshold);
            if (WheelInvert >= 0) lines.Add("DeviceWheelInvert=" + WheelInvert);
            if (Haptic >= 0) lines.Add("DeviceHaptic=" + Haptic);
            return lines;
        }

        // Writes only the values that differ from what the device reports. Returns true when
        // anything was written; each attempt is described in results.
        public bool Restore(HidppDiversionManager hidpp, DeviceSnapshot s, List<string> results)
        {
            bool wrote = false;
            bool dpiInRange = s.DpiMax <= s.DpiMin || (Dpi >= s.DpiMin && Dpi <= s.DpiMax);
            if (Dpi > 0 && s.DpiFeature == 0x2201 && s.Dpi != Dpi && dpiInRange)
            {
                results.Add("restore DPI " + Dpi + " verified=" + hidpp.SetDpi(Dpi));
                wrote = true;
            }
            if (WheelMode > 0 && SmartShiftThreshold > 0 && s.SmartShiftFeature != 0 &&
                (s.WheelMode != WheelMode || s.SmartShiftThreshold != SmartShiftThreshold))
            {
                results.Add("restore SmartShift " + WheelMode + "/" + SmartShiftThreshold +
                    " verified=" + hidpp.SetSmartShift(WheelMode, SmartShiftThreshold));
                wrote = true;
            }
            if (WheelInvert >= 0 && s.WheelInvertPresent && s.WheelInverted != (WheelInvert == 1))
            {
                results.Add("restore wheel invert " + WheelInvert + " verified=" + hidpp.SetWheelInvert(WheelInvert == 1));
                wrote = true;
            }
            if (Haptic >= 0 && s.HapticPresent && s.HapticLevel != Haptic)
            {
                results.Add("restore haptic " + Haptic + " verified=" + hidpp.SetHaptic(Haptic));
                wrote = true;
            }
            return wrote;
        }
    }

    internal sealed class DeviceDialog : Form
    {
        private readonly HidppDiversionManager hidpp;
        private readonly Action<IEnumerable<string>> log;
        private readonly DeviceSettings saved;
        private readonly Action save;
        private readonly Label connection = new Label();
        private readonly Label battery = new Label();
        private readonly Label firmware = new Label();
        private readonly SliderBar dpi = new SliderBar();
        private readonly Label dpiValue = new Label();
        private readonly Label hapticValue = new Label();
        private readonly Label dpiRange = new Label();
        private readonly ComboBox wheelMode = new ComboBox();
        private readonly NumericUpDown threshold = new NumericUpDown();
        private readonly CheckBox neverDisengage = new CheckBox();
        private readonly SliderBar haptic = new SliderBar();
        private readonly Label result = new Label();
        private readonly ComboBox wheelDirection = new ComboBox();
        private readonly Button dpiApply, shiftApply, directionApply, hapticApply, reload, close;
        private readonly float scale;
        private DeviceSnapshot current;

        public DeviceDialog(HidppDiversionManager manager, Action<IEnumerable<string>> logAction,
            DeviceSettings savedSettings, Action saveAction)
        {
            hidpp = manager;
            log = logAction;
            saved = savedSettings;
            save = saveAction;
            scale = Theme.GetScale();
            DialogStyle.Apply(this, Lang.T("裝置設定", "Device settings"), 480, 500, scale);

            AddInfoRow(Lang.T("連線", "Connection"), connection, 12);
            AddInfoRow(Lang.T("電量", "Battery"), battery, 36);
            AddInfoRow(Lang.T("韌體", "Firmware"), firmware, 60);

            AddSection(Lang.T("指標速度（DPI）", "Pointer speed (DPI)"), 94);
            dpi.Location = new Point(S(14), S(120));
            dpi.Size = new Size(S(270), S(30));
            dpi.ValueChanged += delegate { dpiValue.Text = dpi.Value + " DPI"; };
            Controls.Add(dpi);
            AddValueLabel(dpiValue, 120);
            dpiRange.AutoSize = true;
            dpiRange.ForeColor = Theme.Muted;
            dpiRange.Font = new Font(Theme.UiFont, 8.5F);
            dpiRange.Location = new Point(S(20), S(149));
            Controls.Add(dpiRange);
            dpiApply = AddApply(120);
            dpiApply.Click += delegate
            {
                DeviceSnapshot s = current;
                if (s == null || s.DpiStep <= 0) return;
                int steps = (int)Math.Round(((int)dpi.Value - s.DpiMin) / (double)s.DpiStep);
                int value = Math.Max(s.DpiMin, Math.Min(s.DpiMax, s.DpiMin + steps * s.DpiStep));
                RunWrite("DPI " + value, delegate { return hidpp.SetDpi(value); },
                    delegate { saved.Dpi = value; });
            };

            AddSection(Lang.T("滾輪", "Wheel"), 180);
            wheelMode.DropDownStyle = ComboBoxStyle.DropDownList;
            wheelMode.Items.Add(Lang.T("段落滾動（Ratchet）", "Ratchet"));
            wheelMode.Items.Add(Lang.T("自由滾動（Free spin）", "Free spin"));
            wheelMode.Location = new Point(S(20), S(208));
            wheelMode.Width = S(200);
            Controls.Add(wheelMode);
            shiftApply = AddApply(206);
            Controls.Add(new Label { Text = Lang.T("自動切換靈敏度", "Auto-shift sensitivity"), AutoSize = true, Location = new Point(S(20), S(247)) });
            threshold.Minimum = 1;
            threshold.Maximum = 50;
            threshold.Location = new Point(S(150), S(244));
            threshold.Width = S(70);
            Controls.Add(threshold);
            neverDisengage.Text = Lang.T("不自動切換", "Never auto-shift");
            neverDisengage.AutoSize = true;
            neverDisengage.Location = new Point(S(236), S(245));
            neverDisengage.CheckedChanged += delegate { threshold.Enabled = !neverDisengage.Checked; };
            Controls.Add(neverDisengage);
            shiftApply.Click += delegate
            {
                int mode = wheelMode.SelectedIndex == 1 ? 1 : 2;
                int value = neverDisengage.Checked ? 255 : (int)threshold.Value;
                RunWrite("SmartShift", delegate { return hidpp.SetSmartShift(mode, value); },
                    delegate { saved.WheelMode = mode; saved.SmartShiftThreshold = value; });
            };

            Controls.Add(new Label { Text = Lang.T("捲動方向", "Scroll direction"), AutoSize = true, Location = new Point(S(20), S(283)) });
            wheelDirection.DropDownStyle = ComboBoxStyle.DropDownList;
            // Index 0 is the mouse's own direction, index 1 the inverted one. Windows does not flip
            // scrolling itself, so the device direction is "standard" and the inverted one "natural"
            // (on macOS, where the system flips it, the two names are the other way round).
            wheelDirection.Items.Add(Lang.T("標準", "Standard"));
            wheelDirection.Items.Add(Lang.T("自然", "Natural"));
            wheelDirection.Location = new Point(S(150), S(280));
            wheelDirection.Width = S(210);
            Controls.Add(wheelDirection);
            directionApply = AddApply(278);
            directionApply.Click += delegate
            {
                bool inverted = wheelDirection.SelectedIndex == 1;
                RunWrite(Lang.T("捲動方向", "Scroll direction"), delegate { return hidpp.SetWheelInvert(inverted); },
                    delegate { saved.WheelInvert = inverted ? 1 : 0; });
            };

            AddSection(Lang.T("觸覺回饋強度", "Haptic feedback strength"), 321);
            haptic.Minimum = 0;
            haptic.Maximum = 100;
            haptic.Increment = 5;
            haptic.Location = new Point(S(14), S(347));
            haptic.Size = new Size(S(270), S(30));
            haptic.ValueChanged += delegate { hapticValue.Text = haptic.Value > 0 ? haptic.Value.ToString() : Lang.T("關閉", "Off"); };
            Controls.Add(haptic);
            AddValueLabel(hapticValue, 347);
            hapticValue.Text = Lang.T("關閉", "Off");
            Controls.Add(new Label
            {
                Text = Lang.T("拉到最左邊為關閉，最大 100", "Drag fully left to turn off; maximum 100"),
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(S(20), S(376))
            });
            hapticApply = AddApply(347);
            hapticApply.Click += delegate
            {
                int level = (int)haptic.Value;
                RunWrite(Lang.T("觸覺回饋 ", "Haptic feedback ") + level, delegate { return hidpp.SetHaptic(level); },
                    delegate { saved.Haptic = level; });
            };

            result.AutoSize = false;
            result.ForeColor = Theme.Muted;
            result.Location = new Point(S(20), S(402));
            result.Size = new Size(S(440), S(40));
            Controls.Add(result);

            reload = Theme.CreateButton(Lang.T("重新讀取", "Reload"), false);
            reload.Location = new Point(S(250), S(450));
            reload.Size = new Size(S(100), S(34));
            reload.Click += delegate { Reload(); };
            Controls.Add(reload);
            close = Theme.CreateButton(Lang.T("關閉", "Close"), false);
            close.Location = new Point(S(360), S(450));
            close.Size = new Size(S(100), S(34));
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;

            Shown += delegate { Reload(); };
        }

        private int S(int value)
        {
            return (int)Math.Round(value * scale);
        }

        private void AddInfoRow(string name, Label value, int y)
        {
            Controls.Add(new Label { Text = name, AutoSize = true, ForeColor = Theme.Muted, Location = new Point(S(20), S(y)) });
            value.AutoSize = true;
            value.Location = new Point(S(112), S(y));
            Controls.Add(value);
        }

        private void AddSection(string title, int y)
        {
            Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font(Theme.UiFont, 10F, FontStyle.Bold),
                Location = new Point(S(20), S(y))
            });
        }

        // The number shown between a slider and its apply button.
        private void AddValueLabel(Label label, int y)
        {
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font(Theme.UiFont, 9.5F, FontStyle.Bold);
            label.Location = new Point(S(288), S(y));
            label.Size = new Size(S(88), S(30));
            Controls.Add(label);
        }

        private Button AddApply(int y)
        {
            Button button = Theme.CreateButton(Lang.T("套用", "Apply"), false);
            button.Location = new Point(S(380), S(y));
            button.Size = new Size(S(80), S(30));
            Controls.Add(button);
            return button;
        }

        private void SetBusy(string message)
        {
            dpiApply.Enabled = shiftApply.Enabled = directionApply.Enabled = hapticApply.Enabled = reload.Enabled = false;
            result.Text = message;
        }

        private void Post(Action action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
            }
            catch { }
        }

        // Turns the dialog into a borderless child of a page card: no close button, and the
        // reload button moves up beside the result text.
        public void Embed(Control host, Point location)
        {
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Card;
            close.Visible = false;
            result.Size = new Size(S(330), S(40));
            reload.Location = new Point(S(360), S(404));
            ClientSize = new Size(S(480), S(444));
            Location = location;
            host.Controls.Add(this);
            Show();
        }

        public void Reload()
        {
            SetBusy(Lang.T("正在讀取裝置…", "Reading device…"));
            ThreadPool.QueueUserWorkItem(delegate
            {
                DeviceSnapshot snapshot = null;
                try { snapshot = hidpp.ReadDevice(); }
                catch { }
                Post(delegate { ShowSnapshot(snapshot, snapshot == null ? Lang.T("尚未連上 HID++ 裝置，無法讀取。", "Not connected to the mouse over HID++; nothing to read.") : Lang.T("已讀取裝置目前設定。", "Current device settings loaded.")); });
            });
        }

        // remember records the confirmed value so it is saved and re-applied on the next start.
        private void RunWrite(string name, Func<bool> write, Action remember)
        {
            SetBusy(Lang.T("正在套用 ", "Applying ") + name + "…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try { ok = write(); }
                catch { }
                DeviceSnapshot snapshot = null;
                try { snapshot = hidpp.ReadDevice(); }
                catch { }
                Post(delegate
                {
                    log(new[] { "write " + name + " verified=" + ok });
                    if (ok)
                    {
                        remember();
                        save();
                    }
                    ShowSnapshot(snapshot, ok ? name + Lang.T(" 已套用並儲存，下次啟動會自動重新套用。", " applied and saved; it will be re-applied at the next start.") : name + Lang.T(" 套用失敗：裝置未確認新數值。", " failed: the device did not confirm the new value."));
                });
            });
        }

        private void ShowSnapshot(DeviceSnapshot s, string message)
        {
            current = s;
            reload.Enabled = true;
            result.Text = message;
            if (s == null)
            {
                connection.Text = battery.Text = firmware.Text = "—";
                dpiRange.Text = "";
                dpi.Enabled = wheelMode.Enabled = threshold.Enabled = neverDisengage.Enabled = wheelDirection.Enabled = haptic.Enabled = false;
                return;
            }
            log(s.Raw);

            connection.Text = s.Transport == "BOLT" ? Lang.T("Logi Bolt 接收器", "Logi Bolt receiver") : "Bluetooth LE";
            battery.Text = s.BatteryText;
            firmware.Text = s.Firmware.Length > 0 ? s.Firmware : Lang.T("未知", "Unknown");

            bool dpiWritable = s.DpiFeature == 0x2201 && s.DpiStep > 0 && s.DpiMax > s.DpiMin;
            dpi.Enabled = dpiApply.Enabled = dpiWritable;
            if (dpiWritable)
            {
                dpi.Minimum = s.DpiMin;
                dpi.Maximum = s.DpiMax;
                dpi.Increment = s.DpiStep;
                dpi.Value = Math.Max(s.DpiMin, Math.Min(s.DpiMax, s.Dpi));
                dpiRange.Text = Lang.T("範圍 ", "Range ") + s.DpiMin + "–" + s.DpiMax + Lang.T("，預設 ", ", default ") + s.DpiDefault;
            }
            else if (s.DpiFeature == 0x2201)
                dpiRange.Text = Lang.T("目前 ", "Current ") + s.Dpi + Lang.T("；裝置未回報可用範圍，暫不開放修改", "; the device reported no range, so it cannot be changed here");
            else if (s.DpiFeature == 0x2202)
                dpiRange.Text = Lang.T("裝置使用延伸 DPI（2202），目前僅能偵測", "This device uses extended DPI (2202), which is read-only here");
            else
                dpiRange.Text = Lang.T("此裝置未回報 DPI 功能", "This device reports no DPI feature");

            bool shift = s.SmartShiftFeature != 0;
            wheelMode.Enabled = neverDisengage.Enabled = shiftApply.Enabled = shift;
            if (shift)
            {
                wheelMode.SelectedIndex = s.WheelMode == 1 ? 1 : 0;
                bool never = s.SmartShiftThreshold < 1 || s.SmartShiftThreshold > 50;
                neverDisengage.Checked = never;
                if (!never) threshold.Value = s.SmartShiftThreshold;
            }
            threshold.Enabled = shift && !neverDisengage.Checked;

            wheelDirection.Enabled = directionApply.Enabled = s.WheelInvertPresent;
            if (s.WheelInvertPresent)
                wheelDirection.SelectedIndex = s.WheelInverted ? 1 : 0;

            haptic.Enabled = hapticApply.Enabled = s.HapticPresent;
            if (s.HapticPresent)
                haptic.Value = Math.Max(0, Math.Min(100, s.HapticLevel));
        }
    }

    internal sealed class RawInputMonitor : NativeWindow, IDisposable
    {
        private readonly Action<string> report;
        private readonly Action<string, int, int, bool> vendorButton;
        private readonly Dictionary<IntPtr, string> names = new Dictionary<IntPtr, string>();
        private readonly Dictionary<int, HashSet<int>> divertedPressed = new Dictionary<int, HashSet<int>>();
        private volatile int divertedDeviceIndex = -1;
        private volatile int divertedFeatureIndex = -1;
        private readonly Action activity;
        private readonly Action<int, int> mouseMove;
        private int lastMxInput;
        private bool sawMxInput;
        // A host switch or reconnect always leaves at least this long without any report.
        private const int ActivityGapMilliseconds = 2000;
        private const int WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007;
        private const uint RIDEV_INPUTSINK = 0x00000100, RIDEV_PAGEONLY = 0x00000020;

        public RawInputMonitor(IntPtr handle, Action<string> reportAction, Action<string, int, int, bool> vendorButtonAction,
            Action activityAction, Action<int, int> mouseMoveAction)
        {
            AssignHandle(handle);
            report = reportAction;
            vendorButton = vendorButtonAction;
            activity = activityAction;
            mouseMove = mouseMoveAction;
            Register(handle);
        }

        // Time since the mouse last sent anything; a large value when it never has.
        public int MillisecondsSinceInput
        {
            get { return sawMxInput ? unchecked(Environment.TickCount - lastMxInput) : int.MaxValue; }
        }

        public static bool IsMxMaster4Present()
        {
            uint count = 0;
            uint size = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
            GetRawInputDeviceList(IntPtr.Zero, ref count, size);
            IntPtr mem = Marshal.AllocHGlobal((int)(count * size));
            try
            {
                if (GetRawInputDeviceList(mem, ref count, size) == uint.MaxValue) return false;
                for (uint i = 0; i < count; i++)
                {
                    IntPtr item = new IntPtr(mem.ToInt64() + i * size);
                    RAWINPUTDEVICELIST d = (RAWINPUTDEVICELIST)Marshal.PtrToStructure(item, typeof(RAWINPUTDEVICELIST));
                    string name = DeviceName(d.hDevice);
                    if (IsMx(name)) return true;
                }
            }
            finally { Marshal.FreeHGlobal(mem); }
            return false;
        }

        public void ConfigureHidppDiversion(int deviceIndex, int featureIndex)
        {
            divertedDeviceIndex = deviceIndex;
            divertedFeatureIndex = featureIndex;
        }

        public void ResetPressed()
        {
            divertedPressed.Clear();
        }

        private static void Register(IntPtr handle)
        {
            var devices = new List<RAWINPUTDEVICE>();
            devices.Add(new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = handle });
            ushort[] pages = { 0x0C, 0xFF00, 0xFF01, 0xFF43 };
            foreach (ushort page in pages)
                devices.Add(new RAWINPUTDEVICE { usUsagePage = page, usUsage = 0, dwFlags = RIDEV_INPUTSINK | RIDEV_PAGEONLY, hwndTarget = handle });
            RegisterRawInputDevices(devices.ToArray(), (uint)devices.Count, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT) ProcessInput(m.LParam);
            base.WndProc(ref m);
        }

        private void ProcessInput(IntPtr rawHandle)
        {
            uint size = 0;
            GetRawInputData(rawHandle, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
            if (size == 0) return;
            IntPtr mem = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(rawHandle, RID_INPUT, mem, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) != size) return;
                RAWINPUTHEADER header = (RAWINPUTHEADER)Marshal.PtrToStructure(mem, typeof(RAWINPUTHEADER));
                string name;
                if (!names.TryGetValue(header.hDevice, out name))
                {
                    name = DeviceName(header.hDevice);
                    names[header.hDevice] = name;
                }
                if (!IsMx(name)) return;
                int now = Environment.TickCount;
                bool resumed = !sawMxInput || unchecked(now - lastMxInput) >= ActivityGapMilliseconds;
                sawMxInput = true;
                lastMxInput = now;
                if (resumed) activity();
                string transport = TransportName(name);
                int offset = Marshal.SizeOf(typeof(RAWINPUTHEADER));
                if (header.dwType == 0)
                {
                    RAWMOUSE mouse = (RAWMOUSE)Marshal.PtrToStructure(new IntPtr(mem.ToInt64() + offset), typeof(RAWMOUSE));
                    // Movement-only packets are too noisy for button mapping.
                    if (mouse.usButtonFlags != 0)
                        report(transport + " MOUSE flags=0x" + mouse.usButtonFlags.ToString("X4") + " data=" + mouse.usButtonData + " dx=" + mouse.lLastX + " dy=" + mouse.lLastY);
                    // Relative movement of this mouse only (bit 0 of usFlags marks absolute coordinates).
                    if ((mouse.usFlags & 1) == 0 && (mouse.lLastX != 0 || mouse.lLastY != 0))
                        mouseMove(mouse.lLastX, mouse.lLastY);
                }
                else if (header.dwType == 2)
                {
                    IntPtr hidPtr = new IntPtr(mem.ToInt64() + offset);
                    uint bytesPerReport = (uint)Marshal.ReadInt32(hidPtr);
                    uint reportCount = (uint)Marshal.ReadInt32(hidPtr, 4);
                    int total = (int)Math.Min(bytesPerReport * reportCount, 128);
                    byte[] bytes = new byte[total];
                    Marshal.Copy(new IntPtr(hidPtr.ToInt64() + 8), bytes, 0, total);
                    report(transport + " HID size=" + bytesPerReport + " count=" + reportCount + " hex=" + BitConverter.ToString(bytes));

                    // Captured MX Master 4 HID++ diverted-button notification:
                    // 11 [FF for BLE / 01-06 for Bolt] 0D 20
                    // [control high] [control low] [01 down / 00 up]
                    if (bytes.Length >= 7 && (bytes[0] == 0x11 || bytes[0] == 0x10) &&
                        bytes[2] == 0x0D && bytes[3] == 0x20)
                    {
                        int deviceIndex = bytes[1];
                        int controlCode = (bytes[4] << 8) | bytes[5];
                        bool pressed = bytes[6] != 0;
                        vendorButton(transport, deviceIndex, controlCode, pressed);
                    }
                    else if (bytes.Length >= 6 && bytes[0] == 0x11 &&
                        bytes[1] == divertedDeviceIndex && bytes[2] == divertedFeatureIndex && bytes[3] == 0x00)
                    {
                        DecodeDivertedButtonSet(transport, bytes);
                    }
                }
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        private void DecodeDivertedButtonSet(string transport, byte[] bytes)
        {
            int deviceIndex = bytes[1];
            var next = new HashSet<int>();
            for (int offset = 4; offset + 1 < bytes.Length && offset < 12; offset += 2)
            {
                int cid = (bytes[offset] << 8) | bytes[offset + 1];
                if (cid != 0) next.Add(cid);
            }

            HashSet<int> previous;
            if (!divertedPressed.TryGetValue(deviceIndex, out previous))
                previous = new HashSet<int>();

            int[] known = { 0x00C3, 0x01A0, 0x00C4 };
            foreach (int cid in known)
            {
                bool wasPressed = previous.Contains(cid);
                bool isPressed = next.Contains(cid);
                if (wasPressed != isPressed)
                    vendorButton(transport, deviceIndex, cid, isPressed);
            }
            divertedPressed[deviceIndex] = next;
        }

        private static bool IsMx(string name)
        {
            if (String.IsNullOrEmpty(name)) return false;
            string n = name.ToUpperInvariant();
            bool usbStyle = n.Contains("VID_046D") && n.Contains("PID_B042");
            bool bluetoothStyle = n.Contains("VID&02046D") && n.Contains("PID&B042");
            bool boltReceiver = n.Contains("VID_046D") && n.Contains("PID_C548");
            return usbStyle || bluetoothStyle || boltReceiver;
        }

        private static string TransportName(string name)
        {
            string n = (name ?? "").ToUpperInvariant();
            return n.Contains("PID_C548") ? "BOLT" : "BLE";
        }

        private static string DeviceName(IntPtr device)
        {
            uint length = 0;
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref length);
            if (length == 0) return "";
            var sb = new StringBuilder((int)length + 2);
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, sb, ref length);
            return sb.ToString();
        }

        public void Dispose() { ReleaseHandle(); }

        [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTDEVICE
        { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
        [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTDEVICELIST
        { public IntPtr hDevice; public uint dwType; }
        [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTHEADER
        { public uint dwType, dwSize; public IntPtr hDevice, wParam; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] private struct RAWMOUSE
        {
            [FieldOffset(0)] public ushort usFlags;
            [FieldOffset(4)] public uint ulButtons;
            [FieldOffset(4)] public ushort usButtonFlags;
            [FieldOffset(6)] public ushort usButtonData;
            [FieldOffset(8)] public uint ulRawButtons;
            [FieldOffset(12)] public int lLastX;
            [FieldOffset(16)] public int lLastY;
            [FieldOffset(20)] public uint ulExtraInformation;
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputDeviceList(IntPtr list, ref uint count, uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);
    }
}
