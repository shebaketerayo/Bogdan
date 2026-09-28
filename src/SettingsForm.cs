using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace DesktopCat
{
    internal sealed class SettingsPreview : Control
    {
        static readonly CatAnim[] Phases = { CatAnim.Sit, CatAnim.Walk, CatAnim.Sleep, CatAnim.Happy };
        static readonly double[] PhaseLen = { 3.4, 4.2, 4.6, 3.6 };
        static readonly string[] PhaseText = { "сидит", "гуляет", "спит", "мурлычет" };

        ICatSprites sprites;
        readonly EffectsSystem fx = new EffectsSystem();
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly ImageAttributes attrs = new ImageAttributes();
        double lastT, phaseT, animT, nextFx;
        int phase;
        double catX;
        bool faceLeft;
        public int ArtScale = 4;
        public event Action<string> PhaseChanged;

        public SettingsPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            timer.Interval = 33;
            timer.Tick += OnTick;
        }

        public string CurrentText { get { return PhaseText[phase]; } }

        public void SetSprites(ICatSprites s)
        {
            ICatSprites old = sprites;
            sprites = s;
            if (old != null && !ReferenceEquals(old, s)) { try { old.Dispose(); } catch { } }
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            catX = Width / 2.0;
            lastT = clock.Elapsed.TotalSeconds;
            timer.Start();
        }

        void OnTick(object sender, EventArgs e)
        {
            try
            {
                if (!Visible) return;
                double t = clock.Elapsed.TotalSeconds;
                double dt = Math.Min(0.1, Math.Max(0, t - lastT));
                lastT = t;
                phaseT += dt;
                animT += dt;
                if (phaseT >= PhaseLen[phase])
                {
                    phase = (phase + 1) % Phases.Length;
                    phaseT = 0;
                    animT = 0;
                    nextFx = phase == 2 ? 0.8 : 0.25;
                    if (PhaseChanged != null) PhaseChanged(PhaseText[phase]);
                }
                double moved = 0;
                if (Phases[phase] == CatAnim.Walk)
                {
                    double speed = 22.0 * ArtScale;
                    double half = sprites != null ? sprites.FrameWidth * ArtScale / 2.0 : 64;
                    double lo = half + 6, hi = Width - half - 6;
                    double nx = catX + (faceLeft ? -speed : speed) * dt;
                    if (nx > hi) { nx = hi; faceLeft = true; }
                    if (nx < lo) { nx = lo; faceLeft = false; }
                    moved = nx - catX;
                    catX = nx;
                }
                if (sprites != null && phaseT >= nextFx)
                {
                    if (Phases[phase] == CatAnim.Sleep)
                    {
                        // no new Z shortly before waking up, so none drifts over the purring cat
                        if (phaseT < PhaseLen[phase] - 2.2) Spawn(CatEffect.Zzz);
                        nextFx = phaseT + 1.4;
                    }
                    else if (Phases[phase] == CatAnim.Happy)
                    {
                        Spawn(phaseT < 0.5 ? CatEffect.Hearts : (phaseT > 2 ? CatEffect.Note : CatEffect.Heart));
                        nextFx = phaseT + 0.9;
                    }
                    else nextFx = 1e9;
                }
                fx.Update(dt, moved / ArtScale, 0);
                Invalidate();
            }
            catch (Exception ex) { CatLog.Error("SettingsPreview.OnTick", ex); }
        }

        void Spawn(CatEffect e)
        {
            CatAnim a = Phases[phase];
            int f = sprites.FrameAt(a, animT);
            Point hp = sprites.HeadPoint(a, f);
            int hx = faceLeft ? sprites.FrameWidth - 1 - hp.X : hp.X;
            fx.Spawn(e, hx - sprites.Anchor.X + 0.5, hp.Y - sprites.Anchor.Y + 0.5, faceLeft);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            try
            {
                Rectangle r = ClientRectangle;
                if (r.Width < 4 || r.Height < 4) return;
                int s = ArtScale;
                int floorY = r.Height - 7 * s;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Rounded(new Rectangle(0, 0, r.Width - 1, r.Height - 1), Math.Max(6, 3 * s)))
                {
                    using (LinearGradientBrush sky = new LinearGradientBrush(r, Color.FromArgb(226, 238, 255), Color.FromArgb(255, 243, 230), 90f))
                        g.FillPath(sky, path);
                    Region old = g.Clip;
                    g.SetClip(path);
                    using (SolidBrush floor = new SolidBrush(Color.FromArgb(236, 220, 200)))
                        g.FillRectangle(floor, 0, floorY, r.Width, r.Height - floorY);
                    using (Pen line = new Pen(Color.FromArgb(214, 192, 168), Math.Max(1, s / 2)))
                        g.DrawLine(line, 0, floorY, r.Width, floorY);
                    if (sprites != null)
                    {
                        using (SolidBrush sh = new SolidBrush(Color.FromArgb(40, 90, 60, 40)))
                            g.FillEllipse(sh, (float)(catX - 11 * s), floorY - 1.5f * s, 22 * s, 3 * s);
                    }
                    g.Clip = old;
                    old.Dispose();
                    using (Pen border = new Pen(Color.FromArgb(214, 200, 190)))
                        g.DrawPath(border, path);
                }
                if (sprites == null) return;

                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                CatAnim a = Phases[phase];
                int f = sprites.FrameAt(a, animT);
                Bitmap bmp = sprites.GetFrame(a, f, faceLeft);
                Point anc = sprites.Anchor;
                int ax = (int)Math.Round(catX / s) * s;
                int ay = floorY;
                Rectangle canvasArt = new Rectangle(-ax / s, -ay / s, r.Width / s, r.Height / s);
                fx.Draw(g, ax, ay, s, true, canvasArt);
                if (bmp != null)
                {
                    Rectangle dest = new Rectangle(ax - anc.X * s, ay - anc.Y * s, bmp.Width * s, bmp.Height * s);
                    g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
                }
                fx.Draw(g, ax, ay, s, false, canvasArt);
            }
            catch (Exception ex) { CatLog.Error("SettingsPreview.OnPaint", ex); }
        }

        static GraphicsPath Rounded(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop();
                timer.Dispose();
                attrs.Dispose();
                if (sprites != null) { try { sprites.Dispose(); } catch { } sprites = null; }
            }
            base.Dispose(disposing);
        }
    }

    public sealed class SettingsForm : Form
    {
        static readonly string[] SlotNames = {
            "Контур", "Мех", "Тень меха", "Полоски", "Грудка и живот", "Лапки",
            "Кончики ушей и хвоста", "Мордочка", "Пятна 1", "Пятна 2", "Глаза", "Носик и ушки" };
        // the same four sizes as the tray menu, so a size picked here is always checked there
        static readonly int[] SizeValues = { 1, 2, 3, 4 };
        static readonly string[] SizeNames = { "Маленький", "Средний", "Большой", "Огромный" };
        static readonly PlayMode[] PlayValues = { PlayMode.Often, PlayMode.Sometimes, PlayMode.Never };
        static readonly string[] PlayNames = { "Часто", "Иногда", "Никогда" };

        readonly IAppCommands app;
        readonly CatPalette[] presets;
        CatPalette colors;
        CatPalette lastCustom;      // own colours, kept while a preset is selected
        bool updating;
        Icon formIcon;

        TextBox tbName;
        ComboBox cbCoat, cbSize, cbPlay;
        readonly Button[] swatches = new Button[CatPalette.SlotCount];
        CheckBox chkTop, chkWalk, chkFolders, chkFull, chkFx, chkAuto, chkScratch;
        SettingsPreview preview;
        Label lblPreview;
        ToolTip tips;
        Font titleFont;

        public SettingsForm(IAppCommands app)
        {
            this.app = app;
            presets = AppPalette.Presets();
            SuspendLayout();
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Настройки котика";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (!SystemInformation.HighContrast) BackColor = Color.FromArgb(252, 250, 247);
            tips = new ToolTip();
            tips.AutoPopDelay = 12000;
            titleFont = new Font(Font.FontFamily, Font.Size * 1.15f, FontStyle.Bold);
            BuildUi();
            LoadFrom(app.Settings);
            ResumeLayout(true);
        }

        int Em { get { return Math.Max(12, Font.Height); } }

        static TableLayoutPanel Table(int cols)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = cols;
            t.Margin = new Padding(0);
            t.Padding = new Padding(0);
            for (int i = 0; i < cols; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        Label L(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, 4, 8, 4);
            return l;
        }

        GroupBox Group(string text)
        {
            GroupBox g = new GroupBox();
            g.Text = text;
            g.AutoSize = true;
            g.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            g.Padding = new Padding(Em / 2, Em / 3, Em / 2, Em / 3);
            g.Margin = new Padding(0, 0, 0, Em / 2);
            g.Dock = DockStyle.Fill;
            return g;
        }

        CheckBox Check(string text, string tip)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Margin = new Padding(0, 3, Em, 3);
            c.CheckedChanged += delegate { if (!updating) SyncEnabled(); };
            if (tip != null) tips.SetToolTip(c, tip);
            return c;
        }

        void BuildUi()
        {
            int em = Em;
            TableLayoutPanel root = Table(2);
            root.Padding = new Padding(em, em, em, em * 2 / 3);
            root.Dock = DockStyle.Fill;

            TableLayoutPanel left = Table(1);
            left.Margin = new Padding(0, 0, em, 0);
            preview = new SettingsPreview();
            int sc = Math.Max(3, (int)Math.Round(em / 16.0 * 4));
            preview.ArtScale = sc;
            preview.Size = new Size(58 * sc, 60 * sc);
            preview.Margin = new Padding(0, 0, 0, em / 3);
            preview.PhaseChanged += delegate (string s) { UpdatePreviewLabel(); };
            left.Controls.Add(preview, 0, 0);
            lblPreview = new Label();
            lblPreview.AutoSize = false;
            lblPreview.TextAlign = ContentAlignment.MiddleCenter;
            lblPreview.Size = new Size(preview.Width, em + 6);
            lblPreview.ForeColor = Color.FromArgb(110, 90, 80);
            lblPreview.Margin = new Padding(0);
            left.Controls.Add(lblPreview, 0, 1);
            root.Controls.Add(left, 0, 0);

            TableLayoutPanel right = Table(1);
            right.Margin = new Padding(0);

            TableLayoutPanel top = Table(2);
            top.Margin = new Padding(0, 0, 0, em / 2);
            top.Controls.Add(L("Имя котика:"), 0, 0);
            tbName = new TextBox();
            tbName.MaxLength = 40;
            tbName.Width = em * 14;
            tbName.Margin = new Padding(0, 3, 0, 3);
            tbName.TextChanged += delegate { if (!updating) UpdatePreviewLabel(); };
            top.Controls.Add(tbName, 1, 0);
            top.Controls.Add(L("Окрас:"), 0, 1);
            cbCoat = new ComboBox();
            cbCoat.DropDownStyle = ComboBoxStyle.DropDownList;
            cbCoat.DrawMode = DrawMode.OwnerDrawFixed;
            cbCoat.ItemHeight = em + 4;
            cbCoat.Width = em * 14;
            cbCoat.MaxDropDownItems = 12;
            cbCoat.Margin = new Padding(0, 3, 0, 3);
            foreach (CatPalette p in presets) cbCoat.Items.Add(p.Name);
            cbCoat.Items.Add(CatSettings.CustomPaletteName);
            // wide enough for the longest coat name + its three colour chips + the drop-down arrow
            int longest = 0;
            foreach (object it in cbCoat.Items)
                longest = Math.Max(longest, TextRenderer.MeasureText(it as string ?? "", Font).Width);
            int coatW = longest + 3 * (cbCoat.ItemHeight - 7) + 6 + 12 + SystemInformation.VerticalScrollBarWidth + 8;
            cbCoat.Width = Math.Max(em * 14, coatW);
            cbCoat.DropDownWidth = cbCoat.Width;
            tbName.Width = cbCoat.Width;
            cbCoat.DrawItem += DrawCoatItem;
            cbCoat.SelectedIndexChanged += OnCoatChanged;
            top.Controls.Add(cbCoat, 1, 1);
            right.Controls.Add(top, 0, 0);

            GroupBox gColors = Group("Цвета (нажми на квадратик, чтобы поменять)");
            TableLayoutPanel grid = Table(4);
            grid.Dock = DockStyle.Fill;
            for (int i = 0; i < CatPalette.SlotCount; i++)
            {
                int col = i < 6 ? 0 : 2, row = i % 6;
                Button b = new Button();
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Color.FromArgb(120, 110, 105);
                b.FlatAppearance.BorderSize = 1;
                b.Size = new Size(em * 2, em + 2);
                b.Margin = new Padding(0, 2, em / 3, 2);
                b.Text = "";
                b.UseVisualStyleBackColor = false;
                b.Cursor = Cursors.Hand;
                int slot = i;
                b.Click += delegate { PickColor(slot); };
                swatches[i] = b;
                grid.Controls.Add(b, col, row);
                Label l = L(SlotNames[i]);
                l.Margin = new Padding(0, 4, col == 0 ? em : 0, 4);
                grid.Controls.Add(l, col + 1, row);
            }
            gColors.Controls.Add(grid);
            right.Controls.Add(gColors, 0, 1);
            root.Controls.Add(right, 1, 0);

            GroupBox gBeh = Group("Поведение");
            TableLayoutPanel beh = Table(1);
            beh.Dock = DockStyle.Fill;
            TableLayoutPanel combos = Table(4);
            combos.Margin = new Padding(0, 0, 0, em / 3);
            combos.Controls.Add(L("Размер:"), 0, 0);
            cbSize = new ComboBox();
            cbSize.DropDownStyle = ComboBoxStyle.DropDownList;
            cbSize.Items.AddRange(SizeNames);
            cbSize.Width = em * 9;
            cbSize.Margin = new Padding(0, 3, em * 2, 3);
            combos.Controls.Add(cbSize, 1, 0);
            combos.Controls.Add(L("Играть с курсором:"), 2, 0);
            cbPlay = new ComboBox();
            cbPlay.DropDownStyle = ComboBoxStyle.DropDownList;
            cbPlay.Items.AddRange(PlayNames);
            cbPlay.Width = em * 7;
            cbPlay.Margin = new Padding(0, 3, 0, 3);
            combos.Controls.Add(cbPlay, 3, 0);
            tips.SetToolTip(cbPlay, "Часто — бегает за курсором. Иногда — когда захочет. Никогда — только смотрит.");
            beh.Controls.Add(combos, 0, 0);

            TableLayoutPanel checks = Table(2);
            chkTop = Check("Поверх всех окон", "Если выключить, котик живёт на рабочем столе — под окнами, но над обоями и значками.");
            chkWalk = Check("Гулять по окнам", "Котик ходит по верхним краям окон.");
            chkFolders = Check("Спать на папках", "Котик любит дремать на значках папок.");
            chkFull = Check("Прятаться в полноэкранных приложениях", "Во время игр и фильмов на весь экран котик тихонько спит.");
            chkFx = Check("Сердечки, «Мяу!» и Zzz", "Показывать эффекты над котиком.");
            chkAuto = Check("Запускать вместе с Windows", null);
            chkScratch = Check("Точить когти об иконки", "Иногда котик точит когти о значок, и на нём остаются царапины. Убрать их можно в меню котика.");
            checks.Controls.Add(chkTop, 0, 0);
            checks.Controls.Add(chkFolders, 1, 0);
            checks.Controls.Add(chkWalk, 0, 1);
            checks.Controls.Add(chkScratch, 1, 1);
            checks.Controls.Add(chkFull, 0, 2);
            checks.Controls.Add(chkFx, 1, 2);
            checks.Controls.Add(chkAuto, 0, 3);
            chkWalk.Margin = new Padding(em, 3, em, 3);
            beh.Controls.Add(checks, 0, 1);
            gBeh.Controls.Add(beh);
            root.Controls.Add(gBeh, 0, 1);
            root.SetColumnSpan(gBeh, 2);

            TableLayoutPanel buttons = Table(4);
            buttons.Dock = DockStyle.Fill;
            buttons.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100f);
            Button bReset = MakeButton("Сбросить");
            bReset.Click += delegate { LoadFrom(new CatSettings()); };
            tips.SetToolTip(bReset, "Вернуть всё как было сначала (сохранится, только если нажать «Сохранить»).");
            Button bSave = MakeButton("Сохранить");
            bSave.Click += OnSave;
            Button bCancel = MakeButton("Отмена");
            bCancel.Click += delegate { Close(); };
            bCancel.DialogResult = DialogResult.Cancel;
            bSave.Margin = new Padding(0, 0, em / 2, 0);
            buttons.Controls.Add(bReset, 0, 0);
            buttons.Controls.Add(new Label() { AutoSize = true, Margin = new Padding(0) }, 1, 0);
            buttons.Controls.Add(bSave, 2, 0);
            buttons.Controls.Add(bCancel, 3, 0);
            root.Controls.Add(buttons, 0, 2);
            root.SetColumnSpan(buttons, 2);
            AcceptButton = bSave;
            CancelButton = bCancel;

            Controls.Add(root);
        }

        Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowOnly;
            b.MinimumSize = new Size(Em * 6, Em + 12);
            b.Padding = new Padding(Em / 2, 2, Em / 2, 2);
            b.Margin = new Padding(0);
            b.UseVisualStyleBackColor = true;
            return b;
        }

        void LoadFrom(CatSettings src)
        {
            CatSettings s = SettingsStore.Sanitize(src.Clone());
            updating = true;
            try
            {
                tbName.Text = s.CatName;
                if (s.CustomPalette != null) lastCustom = AppPalette.Sanitize(s.CustomPalette, CatSettings.CustomPaletteName);
                int idx = -1;
                if (s.PaletteName == CatSettings.CustomPaletteName && s.CustomPalette != null) idx = presets.Length;
                else
                {
                    string want = string.IsNullOrEmpty(s.PaletteName) ? AppPalette.DefaultPalette().Name : s.PaletteName;
                    for (int i = 0; i < presets.Length; i++) if (presets[i].Name == want) { idx = i; break; }
                    if (idx < 0) idx = 0;
                }
                cbCoat.SelectedIndex = idx;
                colors = idx < presets.Length ? AppPalette.Sanitize(presets[idx], presets[idx].Name)
                                              : AppPalette.Sanitize(lastCustom, CatSettings.CustomPaletteName);
                int si = 1;      // nearest offered size (an ini may hold any value 1..6)
                for (int i = 0; i < SizeValues.Length; i++)
                    if (Math.Abs(SizeValues[i] - s.Size) < Math.Abs(SizeValues[si] - s.Size)) si = i;
                cbSize.SelectedIndex = si;
                int pi = Array.IndexOf(PlayValues, s.Play);
                cbPlay.SelectedIndex = pi >= 0 ? pi : 1;
                chkTop.Checked = s.AlwaysOnTop;
                chkWalk.Checked = s.WalkOnWindows;
                chkFolders.Checked = s.SleepOnFolders;
                chkScratch.Checked = s.ScratchIcons;
                chkFull.Checked = s.HideInFullscreen;
                chkFx.Checked = s.ShowEffects;
                chkAuto.Checked = s.Autostart;
            }
            finally { updating = false; }
            SyncEnabled();
            ColorsChanged();
        }

        void SyncEnabled()
        {
            chkWalk.Enabled = chkTop.Checked;
        }

        void OnCoatChanged(object sender, EventArgs e)
        {
            if (updating) return;
            int idx = cbCoat.SelectedIndex;
            if (idx < 0) return;
            if (idx < presets.Length) colors = AppPalette.Sanitize(presets[idx], presets[idx].Name);
            else colors = AppPalette.Sanitize(lastCustom ?? colors, CatSettings.CustomPaletteName);
            ColorsChanged();
        }

        void PickColor(int slot)
        {
            try
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.FullOpen = true;
                    cd.AnyColor = true;
                    cd.Color = colors.Colors[slot];
                    int[] custom = new int[16];
                    for (int i = 0; i < 16; i++)
                    {
                        Color c = i < CatPalette.SlotCount ? colors.Colors[i] : Color.White;
                        custom[i] = c.R | (c.G << 8) | (c.B << 16);
                    }
                    cd.CustomColors = custom;
                    if (cd.ShowDialog(this) != DialogResult.OK) return;
                    Color picked = Color.FromArgb(255, cd.Color.R, cd.Color.G, cd.Color.B);
                    colors = AppPalette.Sanitize(colors, CatSettings.CustomPaletteName);
                    colors.Colors[slot] = picked;
                    lastCustom = colors.Clone();
                    updating = true;
                    try { cbCoat.SelectedIndex = presets.Length; } finally { updating = false; }
                    cbCoat.Invalidate();
                    ColorsChanged();
                }
            }
            catch (Exception ex) { CatLog.Error("SettingsForm.PickColor", ex); }
        }

        void ColorsChanged()
        {
            for (int i = 0; i < CatPalette.SlotCount; i++)
            {
                Color c = colors.Colors[i];
                swatches[i].BackColor = c;
                swatches[i].FlatAppearance.MouseOverBackColor = Lighten(c, 0.18);
                swatches[i].FlatAppearance.MouseDownBackColor = Lighten(c, 0.3);
                tips.SetToolTip(swatches[i], SlotNames[i] + ": " + SettingsStore.ColorToHex(c));
            }
            try
            {
                ICatSprites s = CatSpriteFactory.Create(colors.Clone());
                preview.SetSprites(s);
                if (formIcon == null)
                {
                    formIcon = SpriteExport.MakeIcon(s, 32);
                    if (formIcon != null) Icon = formIcon;
                }
            }
            catch (Exception ex) { CatLog.Error("SettingsForm.ColorsChanged", ex); }
            UpdatePreviewLabel();
        }

        void UpdatePreviewLabel()
        {
            string n = (tbName.Text ?? "").Trim();
            if (n.Length == 0) n = SettingsStore.DefaultName;
            lblPreview.Text = n + " " + preview.CurrentText;
        }

        static Color Lighten(Color c, double k)
        {
            return Color.FromArgb(255, (int)(c.R + (255 - c.R) * k), (int)(c.G + (255 - c.G) * k), (int)(c.B + (255 - c.B) * k));
        }

        void DrawCoatItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            try
            {
                CatPalette p = e.Index < presets.Length ? presets[e.Index] : (lastCustom ?? colors);
                Graphics g = e.Graphics;
                int h = e.Bounds.Height - 6;
                int x = e.Bounds.X + 3, y = e.Bounds.Y + 3;
                PaletteSlot[] chips = { PaletteSlot.Fur, PaletteSlot.Stripes, PaletteSlot.Light };
                Color outline = p != null && p.Colors.Length > 0 ? p[PaletteSlot.Outline] : Color.Black;
                foreach (PaletteSlot sl in chips)
                {
                    Color c = p != null && (int)sl < p.Colors.Length ? p[sl] : Color.Gray;
                    using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, x, y, h, h);
                    using (Pen pen = new Pen(outline)) g.DrawRectangle(pen, x, y, h - 1, h - 1);
                    x += h - 1;
                }
                x += 6;
                string text = cbCoat.Items[e.Index] as string ?? "";
                TextRenderer.DrawText(g, text, e.Font, new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x, e.Bounds.Height),
                    e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            catch { }
            e.DrawFocusRectangle();
        }

        void OnSave(object sender, EventArgs e)
        {
            try
            {
                CatSettings s = app.Settings.Clone();
                s.CatName = tbName.Text;
                int idx = cbCoat.SelectedIndex;
                if (idx >= presets.Length || idx < 0)
                {
                    s.PaletteName = CatSettings.CustomPaletteName;
                    s.CustomPalette = AppPalette.Sanitize(colors, CatSettings.CustomPaletteName);
                }
                else
                {
                    s.PaletteName = presets[idx].Name;
                    if (lastCustom != null) s.CustomPalette = AppPalette.Sanitize(lastCustom, CatSettings.CustomPaletteName);
                }
                s.Size = SizeValues[Math.Max(0, cbSize.SelectedIndex)];
                s.Play = PlayValues[Math.Max(0, cbPlay.SelectedIndex)];
                s.AlwaysOnTop = chkTop.Checked;
                s.WalkOnWindows = chkWalk.Checked;
                s.SleepOnFolders = chkFolders.Checked;
                s.ScratchIcons = chkScratch.Checked;
                s.HideInFullscreen = chkFull.Checked;
                s.ShowEffects = chkFx.Checked;
                s.Autostart = chkAuto.Checked;
                app.ApplySettings(SettingsStore.Sanitize(s));
            }
            catch (Exception ex) { CatLog.Error("SettingsForm.OnSave", ex); }
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (tips != null) tips.Dispose();
                if (titleFont != null) titleFont.Dispose();
                if (formIcon != null) { formIcon.Dispose(); formIcon = null; }
            }
            base.Dispose(disposing);
        }
    }
}
