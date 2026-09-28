using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopCat
{
    public interface IAppCommands
    {
        CatSettings Settings { get; }            // treat as read-only, callers edit a clone
        bool CatSleeping { get; }
        bool CatHidden { get; }
        void CallCat();
        void SleepOrWake();
        void ToggleHidden();
        void OpenSettings();
        void ApplySettings(CatSettings s);
        int ScratchedIcons { get; }
        void ClearScratches();
        void Exit();
    }

    internal sealed class TrayMenuColors : ProfessionalColorTable
    {
        static readonly Color Back = Color.FromArgb(255, 253, 250);
        static readonly Color Margin = Color.FromArgb(255, 245, 236);
        static readonly Color Hot = Color.FromArgb(255, 231, 212);
        static readonly Color HotBorder = Color.FromArgb(240, 184, 140);
        static readonly Color Border = Color.FromArgb(215, 196, 180);
        static readonly Color Sep = Color.FromArgb(234, 220, 206);
        static readonly Color Check = Color.FromArgb(255, 214, 180);

        public override Color ToolStripDropDownBackground { get { return Back; } }
        public override Color ImageMarginGradientBegin { get { return Margin; } }
        public override Color ImageMarginGradientMiddle { get { return Margin; } }
        public override Color ImageMarginGradientEnd { get { return Margin; } }
        public override Color MenuBorder { get { return Border; } }
        public override Color MenuItemBorder { get { return HotBorder; } }
        public override Color MenuItemSelected { get { return Hot; } }
        public override Color MenuItemSelectedGradientBegin { get { return Hot; } }
        public override Color MenuItemSelectedGradientEnd { get { return Hot; } }
        public override Color MenuItemPressedGradientBegin { get { return Hot; } }
        public override Color MenuItemPressedGradientEnd { get { return Hot; } }
        public override Color SeparatorDark { get { return Sep; } }
        public override Color SeparatorLight { get { return Back; } }
        public override Color CheckBackground { get { return Check; } }
        public override Color CheckSelectedBackground { get { return HotBorder; } }
        public override Color CheckPressedBackground { get { return HotBorder; } }
        public override Color ButtonSelectedBorder { get { return HotBorder; } }
    }

    internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() : base(new TrayMenuColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // the disabled header keeps a readable warm colour instead of the default grey
            if (e.Item.Tag as string == "header") e.TextColor = Color.FromArgb(150, 90, 50);
            else if (e.Item.Enabled) e.TextColor = Color.FromArgb(50, 38, 32);
            base.OnRenderItemText(e);
        }
    }

    public sealed class TrayController : IDisposable
    {
        readonly IAppCommands app;
        readonly NotifyIcon icon;
        readonly ContextMenuStrip menu;
        readonly Font boldFont;
        readonly TrayMenuHost host = new TrayMenuHost();
        Icon trayIcon;
        IntPtr restoreForeground;

        ToolStripMenuItem miHeader, miCall, miSleep, miPlay, miPlayOften, miPlaySometimes, miPlayNever, miSize;
        ToolStripMenuItem miOnTop, miWalkWindows, miFolders, miFullscreen, miHide, miSettings, miAutostart, miExit;
        ToolStripMenuItem miScratch, miClearScratches;
        readonly ToolStripMenuItem[] sizeItems = new ToolStripMenuItem[4];
        static readonly int[] SizeValues = { 1, 2, 3, 4 };
        static readonly string[] SizeNames = { "Маленький", "Средний", "Большой", "Огромный" };

        public TrayController(IAppCommands app)
        {
            this.app = app;
            menu = new ContextMenuStrip();
            menu.Renderer = new TrayMenuRenderer();
            menu.ShowImageMargin = true;
            boldFont = new Font(menu.Font, FontStyle.Bold);
            Build();
            menu.Opening += OnOpening;
            menu.Closed += OnClosed;

            icon = new NotifyIcon();
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += delegate { Safe("call", app.CallCat); };
        }

        ToolStripMenuItem Item(string text, Action onClick)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            if (onClick != null) mi.Click += delegate { Safe(text, onClick); };
            return mi;
        }

        void Build()
        {
            miHeader = new ToolStripMenuItem(SettingsStore.DefaultName);
            miHeader.Font = boldFont;
            miHeader.Enabled = false;
            miHeader.Tag = "header";

            miCall = Item("Позвать котика", app.CallCat);
            miSleep = Item("Уложить спать", app.SleepOrWake);

            miPlay = new ToolStripMenuItem("Играть с курсором");
            miPlayOften = Item("Часто", delegate { Change(delegate (CatSettings s) { s.Play = PlayMode.Often; }); });
            miPlaySometimes = Item("Иногда", delegate { Change(delegate (CatSettings s) { s.Play = PlayMode.Sometimes; }); });
            miPlayNever = Item("Никогда", delegate { Change(delegate (CatSettings s) { s.Play = PlayMode.Never; }); });
            miPlay.DropDownItems.AddRange(new ToolStripItem[] { miPlayOften, miPlaySometimes, miPlayNever });
            miPlay.DropDown.Renderer = menu.Renderer;

            miSize = new ToolStripMenuItem("Размер");
            for (int i = 0; i < SizeValues.Length; i++)
            {
                int v = SizeValues[i];
                sizeItems[i] = Item(SizeNames[i], delegate { Change(delegate (CatSettings s) { s.Size = v; }); });
                miSize.DropDownItems.Add(sizeItems[i]);
            }
            miSize.DropDown.Renderer = menu.Renderer;

            miOnTop = Item("Поверх всех окон", delegate { Change(delegate (CatSettings s) { s.AlwaysOnTop = !s.AlwaysOnTop; }); });
            miWalkWindows = Item("Гулять по окнам", delegate { Change(delegate (CatSettings s) { s.WalkOnWindows = !s.WalkOnWindows; }); });
            miFolders = Item("Спать на папках", delegate { Change(delegate (CatSettings s) { s.SleepOnFolders = !s.SleepOnFolders; }); });
            miFullscreen = Item("Прятаться в полноэкранных приложениях", delegate { Change(delegate (CatSettings s) { s.HideInFullscreen = !s.HideInFullscreen; }); });
            miScratch = Item("Точить когти об иконки", delegate { Change(delegate (CatSettings s) { s.ScratchIcons = !s.ScratchIcons; }); });
            miClearScratches = Item("Убрать царапины", app.ClearScratches);
            miHide = Item("Спрятать котика", app.ToggleHidden);
            miSettings = Item("Настройки…", app.OpenSettings);
            miAutostart = Item("Запускать вместе с Windows", delegate { Change(delegate (CatSettings s) { s.Autostart = !s.Autostart; }); });
            miExit = Item("Выход", app.Exit);

            menu.Items.AddRange(new ToolStripItem[] {
                miHeader, new ToolStripSeparator(),
                miCall, miSleep, miPlay, miSize, new ToolStripSeparator(),
                miOnTop, miWalkWindows, miFolders, miScratch, miFullscreen, new ToolStripSeparator(),
                miClearScratches, miHide, miSettings, miAutostart, new ToolStripSeparator(),
                miExit });
            Refresh();
        }

        void Change(Action<CatSettings> edit)
        {
            CatSettings s = app.Settings.Clone();
            edit(s);
            app.ApplySettings(s);
        }

        static void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { CatLog.Error("menu: " + what, ex); }
        }

        void OnOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try { Refresh(); } catch (Exception ex) { CatLog.Error("TrayController.OnOpening", ex); }
        }

        void OnClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            // give the keyboard focus back to where it was, if our invisible menu host still has it
            try
            {
                IntPtr prev = restoreForeground;
                restoreForeground = IntPtr.Zero;
                if (prev != IntPtr.Zero && host.Handle != IntPtr.Zero && AppNative.GetForegroundWindow() == host.Handle &&
                    AppNative.IsWindow(prev) && AppNative.IsWindowVisible(prev))
                    AppNative.SetForegroundWindow(prev);
            }
            catch { }
        }

        public void Refresh()
        {
            CatSettings s = app.Settings;
            miHeader.Text = s.CatName;
            miSleep.Text = app.CatSleeping ? "Разбудить" : "Уложить спать";
            miPlayOften.Checked = s.Play == PlayMode.Often;
            miPlaySometimes.Checked = s.Play == PlayMode.Sometimes;
            miPlayNever.Checked = s.Play == PlayMode.Never;
            for (int i = 0; i < sizeItems.Length; i++) sizeItems[i].Checked = s.Size == SizeValues[i];
            miOnTop.Checked = s.AlwaysOnTop;
            miWalkWindows.Checked = s.WalkOnWindows && s.AlwaysOnTop;
            miWalkWindows.Enabled = s.AlwaysOnTop;
            miFolders.Checked = s.SleepOnFolders;
            miScratch.Checked = s.ScratchIcons;
            miClearScratches.Enabled = app.ScratchedIcons > 0;
            miFullscreen.Checked = s.HideInFullscreen;
            miHide.Text = app.CatHidden ? "Показать котика" : "Спрятать котика";
            miAutostart.Checked = s.Autostart;
            bool hidden = app.CatHidden;
            miCall.Enabled = true;
            miSleep.Enabled = !hidden;
            if (icon != null) icon.Text = TrimTip(s.CatName);
        }

        static string TrimTip(string s)
        {
            if (string.IsNullOrEmpty(s)) return SettingsStore.DefaultName;
            return s.Length > 63 ? s.Substring(0, 63) : s;
        }

        public void SetIcon(Icon ic)
        {
            Icon old = trayIcon;
            trayIcon = ic;
            try { icon.Icon = ic; } catch (Exception ex) { CatLog.Error("TrayController.SetIcon", ex); }
            if (old != null && !ReferenceEquals(old, ic)) old.Dispose();
        }

        public void Show()
        {
            Refresh();
            icon.Visible = true;
        }

        public void ShowWelcome()
        {
            try
            {
                string name = app.Settings.CatName;
                string title = "Мяу! Я " + name + ".";
                if (title.Length > 60) title = "Мяу! Я котик.";
                icon.ShowBalloonTip(10000, title,
                    "Теперь я живу у тебя на рабочем столе. Погладь меня мышкой, возьми и перенеси куда хочешь. " +
                    "Двойной щелчок по этому значку позовёт меня, а правая кнопка — по мне или по значку — откроет меню.",
                    ToolTipIcon.None);
            }
            catch (Exception ex) { CatLog.Error("TrayController.ShowWelcome", ex); }
        }

        public void HideIcon()
        {
            try { if (menu.Visible) menu.Close(); } catch { }
            icon.Visible = false;
        }

        public void ShowMenuAt(Point screen)
        {
            try
            {
                if (menu.Visible) { menu.Close(); return; }
                IntPtr fg = AppNative.GetForegroundWindow();
                // a menu only closes on an outside click while our process is in the foreground; the right click
                // we just received allows taking it, so park the focus on an invisible host window of ours
                restoreForeground = fg;
                host.EnsureCreated();
                AppNative.SetForegroundWindow(host.Handle);
                menu.Show(screen);
            }
            catch (Exception ex) { CatLog.Error("TrayController.ShowMenuAt", ex); }
        }

        public void Dispose()
        {
            try { icon.Visible = false; } catch { }
            try { icon.Dispose(); } catch { }
            try { menu.Dispose(); } catch { }
            try { boldFont.Dispose(); } catch { }
            try { host.DestroyHandle(); } catch { }
            if (trayIcon != null) { trayIcon.Dispose(); trayIcon = null; }
        }
    }

    internal sealed class TrayMenuHost : NativeWindow
    {
        public void EnsureCreated()
        {
            if (Handle != IntPtr.Zero) return;
            CreateParams cp = new CreateParams();
            cp.Caption = "DesktopCatMenuHost";
            cp.Style = AppNative.WS_POPUP;
            cp.ExStyle = AppNative.WS_EX_TOOLWINDOW;
            cp.X = -32000; cp.Y = -32000; cp.Width = 1; cp.Height = 1;
            CreateHandle(cp);
        }
    }
}
