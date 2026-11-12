namespace BMThuDiem;

public sealed class MainForm : Form
{
    readonly TrialStore _store = new();
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 200 };
    readonly Label _live = Value("0 g");
    readonly Label _dry = Value("—");
    readonly Label _wet = Value("—");
    readonly Label _eq = Value("—");
    readonly Label _point = Value("—");
    readonly Label _state = new() { AutoSize = true, ForeColor = Color.FromArgb(232, 197, 71), Text = "chờ COM4" };
    readonly Label _com = Lamp();
    readonly Label _auto = Lamp();
    readonly ComDot _comDot = new();
    readonly Label _avgA = Value("—");
    readonly Label _avgB = Value("—");
    readonly TextBox _raw = new()
    {
        Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(16, 8, 10), ForeColor = Color.FromArgb(186, 214, 158),
        Font = new Font("Consolas", 9f), ScrollBars = ScrollBars.Vertical,
    };
    readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false };
    readonly List<ThuRow> _rows = new();
    readonly object _gate = new();
    ListenCom4? _port;
    string _pending = "";
    int _grams;
    bool _kingbird;
    DateTime _sampleAt = DateTime.MinValue;
    DateTime _rawAt = DateTime.MinValue;
    readonly ChartPane _chart = new();
    DateTime _chartAt = DateTime.MinValue;
    string _chunk = "";
    bool _sawByte;
    bool _listening;
    bool _unlocked;
    bool _session;
    bool _dryLocked;
    int _prev;
    double _dryG;
    double _best = double.MaxValue;
    double? _wetG;
    readonly List<int> _wetSeen = new();
    DateTime _lowSince = DateTime.MinValue;
    DateTime _openAt = DateTime.MinValue;
    readonly List<DateTime> _trialStarts = new();

    public MainForm()
    {
        _store.Init();
        Text = "BM-ThuDiem 1.3.0  ·  COM4  ·  QĐ 228/1999";
        Width = 1180;
        Height = 880;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(42, 10, 18);
        ForeColor = Color.FromArgb(246, 238, 230);
        Font = new Font("Segoe UI", 10f);

        TrySetIcon();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 188, Padding = new Padding(12, 8, 12, 4), WrapContents = false };
        top.Controls.Add(Card("Số g hiện tại", _live));
        top.Controls.Add(Card("Chuỗi khô", _dry));
        top.Controls.Add(Card("Cân ướt", _wet));
        top.Controls.Add(Card("Ướt quy về 5 kg", _eq));
        top.Controls.Add(Card("Số điểm", _point));
        var lamps = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8, 4, 0, 0) };
        var comRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        _comDot.Margin = new Padding(0, 6, 6, 0);
        comRow.Controls.Add(_comDot);
        comRow.Controls.Add(_com);
        lamps.Controls.Add(comRow);
        lamps.Controls.Add(_auto);
        lamps.Controls.Add(_state);
        top.Controls.Add(lamps);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 86, Padding = new Padding(12, 6, 12, 6) };
        var listen = Button("Nghe COM4");
        var stop = Button("Ngừng");
        var edit = Button("Mở sửa");
        var shifts = Button("Ca cân");
        var rules = Button("Quy tắc");
        var clear = Button("Clear");
        listen.Click += (_, _) => StartListen();
        stop.Click += (_, _) => StopListen();
        edit.Click += (_, _) => Unlock();
        shifts.Click += (_, _) => EditShifts();
        rules.Click += (_, _) => ShowRules();
        clear.Click += (_, _) => _chart.ClearDisplay();
        bar.Controls.AddRange(new Control[] { listen, stop, edit, shifts, rules, clear, Mini("TB a Phước", _avgA), Mini("TB a Đấu", _avgB) });
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2 };
        _grid.BackgroundColor = Color.FromArgb(42, 16, 22);
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(42, 16, 22);
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(246, 238, 230);
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(74, 24, 32);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(232, 197, 71);
        _grid.EnableHeadersVisualStyles = false;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.Columns.Add(Col("Stt", "Số thứ tự", true));
        _grid.Columns.Add(Col("When", "Lúc", true));
        _grid.Columns.Add(Col("Diem", "Số điểm", true));
        _grid.Columns.Add(Col("Operator", "Người cân", false));
        _grid.Columns.Add(Col("Plate", "Số xe", false));
        _grid.Columns.Add(Col("Impurity", "% tạp chất", false));
        _grid.Columns.Add(Col("Price", "Giá củ", false));
        _grid.CellBeginEdit += (_, e) =>
        {
            if (!_unlocked) e.Cancel = true;
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is ThuRow row && row.Id == 0)
                e.Cancel = true;
        };
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not ThuRow row || row.Id != 0) return;
            e.CellStyle.BackColor = Color.FromArgb(90, 48, 18);
            e.CellStyle.ForeColor = Color.FromArgb(232, 197, 71);
            e.CellStyle.SelectionBackColor = Color.FromArgb(90, 48, 18);
        };
        _grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not ThuRow row) return;
            _store.Update(row);
            ShowAverages();
        };
        split.Panel1.Controls.Add(_grid);
        var rawHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        rawHost.Controls.Add(_raw);
        split.Panel2.Controls.Add(rawHost);
        split.Panel2.Controls.Add(new Label { Text = "RAW COM4  ·  1,5 giây", Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(232, 197, 71) });
        Controls.Add(split);
        Controls.Add(_chart);
        Controls.Add(bar);
        Controls.Add(top);
        Load += (_, _) =>
        {
            Reload();
            StartListen();
            try
            {
                var room = split.Width - 280;
                if (room > 200) split.SplitterDistance = room;
            }
            catch { }
        };
        FormClosing += OnFormClosing;
        _tick.Tick += (_, _) => Pulse();
        _tick.Start();
    }

    void StartListen()
    {
        StopListen();
        try
        {
            _port = new ListenCom4();
            _port.Frame += OnFrame;
            _port.Chunk += s => { lock (_gate) { _chunk = s; _sawByte = true; _sampleAt = DateTime.UtcNow; } };
            _port.Open();
            _listening = true;
            SetLamp(_com, true, "COM4 đang nghe 1200 8N1");
        }
        catch (Exception ex)
        {
            SetLamp(_com, false, "COM4 " + ex.Message);
            _comDot.Set(ComDot.Kind.Bad);
            _state.Text = "không mở được COM4";
        }
    }

    void StopListen()
    {
        _listening = false;
        try { _port?.Dispose(); } catch { }
        _port = null;
        SetLamp(_com, false, "COM4 ngừng");
        _comDot.Set(ComDot.Kind.Bad);
    }

    int _serial;
    int _seen;

    void OnFrame(string raw)
    {
        var king = Gram.IsKingbird(raw);
        var ok = Gram.TryGrams(raw, out var grams);
        lock (_gate)
        {
            _pending = raw;
            _kingbird = king;
            if (ok && !king)
            {
                _grams = grams;
                _serial++;
            }
            _sampleAt = DateTime.UtcNow;
        }
    }

    void Pulse()
    {
        if (IsDisposed) return;
        string raw;
        int grams;
        bool king;
        DateTime at;
        int serial;
        lock (_gate)
        {
            raw = _pending;
            grams = _grams;
            king = _kingbird;
            at = _sampleAt;
            serial = _serial;
        }
        var auto = AutoCanAlive();
        SetLamp(_auto, auto, auto ? "AutoCan đang chạy" : "AutoCan tắt, phiếu chờ gửi");
        PaintCom(at);
        try { _port?.Pump(); } catch { }
        try { File.WriteAllText(Path.Combine(_store.DataDir, "thudiem.host"), DateTimeOffset.Now.ToString("o")); } catch { }
        var shown = raw.Length > 0 ? raw : OneLine(_chunk);
        if (shown.Length > 0 && DateTime.UtcNow - _rawAt >= TimeSpan.FromMilliseconds(1500))
        {
            _rawAt = DateTime.UtcNow;
            _raw.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + shown + Environment.NewLine);
            if (_raw.TextLength > 4000) _raw.Text = _raw.Text[^2000..];
        }
        MarkDone();
        if (!_listening) return;
        if (!_sawByte || at == DateTime.MinValue || DateTime.UtcNow - at > TimeSpan.FromSeconds(3))
        {
            _state.Text = _listening && !_sawByte ? "COM4 mở, chưa có byte" : "COM4 im";
            return;
        }
        if (_chartAt == DateTime.MinValue || DateTime.UtcNow - _chartAt >= TimeSpan.FromMilliseconds(1500))
        {
            _chartAt = DateTime.UtcNow;
            _chart.Add(DateTime.Now, grams);
        }
        if (king)
        {
            _state.Text = "COM4 ra khung cân xe. Không chấm.";
            return;
        }
        if (serial != _seen)
        {
            _seen = serial;
            OnGrams(grams);
        }
        else if (_session && grams < 100 && _lowSince != DateTime.MinValue)
            OnGrams(grams);
    }

    void OnGrams(int grams)
    {
        _live.Text = grams.ToString("0") + " g";
        if (grams < 100)
        {
            if (_lowSince == DateTime.MinValue) _lowSince = DateTime.UtcNow;
            if (DateTime.UtcNow - _lowSince < TimeSpan.FromSeconds(1)) return;
            if (_session && _dryLocked && Wet(out var wet))
                Commit(_dryG, wet);
            else if (_session && _dryLocked)
            {
                _state.Text = "không ra phiếu, chưa thấy cân ướt";
                EndSession();
            }
            else EndSession();
            return;
        }
        _lowSince = DateTime.MinValue;
        var rising = grams > _prev;
        _prev = grams;
        if (!_session)
        {
            if (grams > 1000 && rising)
            {
                _session = true;
                _openAt = DateTime.Now;
                _state.Text = "mở phiên";
                ConsiderDry(grams);
            }
            return;
        }
        if (grams >= 4200 && grams <= 5800)
        {
            ConsiderDry(grams);
            return;
        }
        if (!_dryLocked)
        {
            _state.Text = "phiên mở, chưa thấy khô quanh 5000 g";
            return;
        }
        if (grams >= 150 && grams <= 1200)
        {
            _wetSeen.Add(grams);
            if (_wetSeen.Count > 40) _wetSeen.RemoveRange(0, _wetSeen.Count - 40);
            if (Wet(out var wetNow))
            {
                _wetG = wetNow;
                _wet.Text = wetNow.ToString("0") + " g";
                var equiv = StarchTable.EquivalentWet(_dryG, wetNow);
                _eq.Text = equiv.ToString("0") + " g";
                if (StarchTable.TryNearest(equiv, out _, out var diem))
                {
                    _point.Text = diem.ToString("0.00") + " %";
                    _state.Text = "ướt ổn " + wetNow.ToString("0") + " g → " + diem.ToString("0.00") + "%";
                }
                else
                {
                    _point.Text = "ngoài bảng";
                    _state.Text = "ướt " + equiv.ToString("0") + " g ngoài 280–670";
                }
            }
            return;
        }
        _state.Text = _wetG is double held
            ? "ướt " + held.ToString("0") + " g, chờ cân về 0"
            : "đã có khô, chờ cân ướt";
    }

    void ConsiderDry(int grams)
    {
        if (grams < 4200 || grams > 5800) return;
        var dist = Math.Abs(grams - 5000);
        if (_dryLocked && dist > _best) return;
        if (_dryLocked && Math.Abs(dist - _best) < 0.1 && grams >= _dryG) return;
        _dryG = grams;
        _best = dist;
        _dryLocked = true;
        _dry.Text = grams.ToString("0") + " g";
        _wet.Text = "—";
        _eq.Text = "—";
        _point.Text = "—";
        _wetG = null;
        _wetSeen.Clear();
        _state.Text = "khô " + grams.ToString("0") + " g";
    }

    bool Wet(out double wet)
    {
        wet = 0;
        if (_wetG is double held && _wetSeen.Count == 0)
        {
            wet = held;
            return true;
        }
        if (_wetSeen.Count == 0) return false;
        var tail = _wetSeen.Skip(Math.Max(0, _wetSeen.Count - 8)).OrderBy(x => x).ToList();
        wet = tail[tail.Count / 2];
        return true;
    }

    void Commit(double dry, double wet)
    {
        var equiv = StarchTable.EquivalentWet(dry, wet);
        var ok = StarchTable.TryNearest(equiv, out var listed, out var diem);
        var who = _store.OperatorNow();
        var note = ok ? "" : "ngoài bảng";
        var id = _store.Add(ok ? diem : null, who, dry, wet, equiv, listed, "", note);
        if (ok)
        {
            _store.HandOff(id, diem, who, dry, wet, equiv, listed);
            _point.Text = diem.ToString("0.00") + " %";
            _state.Text = "đã chốt " + diem.ToString("0.00") + "%";
        }
        else
        {
            _point.Text = "ngoài bảng";
            _state.Text = "không chốt, cân ướt ngoài phụ lục";
        }
        _eq.Text = equiv.ToString("0") + " g";
        RememberTrial();
        EndSession();
        Reload();
    }

    void RememberTrial()
    {
        if (_openAt == DateTime.MinValue) _openAt = DateTime.Now;
        _trialStarts.Add(_openAt);
        if (_trialStarts.Count > 30) _trialStarts.RemoveRange(0, _trialStarts.Count - 30);
        if (_trialStarts.Count >= 10)
            _chart.ShowFrom(_trialStarts[^10]);
    }

    void EndSession()
    {
        _session = false;
        _dryLocked = false;
        _wetG = null;
        _wetSeen.Clear();
        _best = double.MaxValue;
        _prev = 0;
        _lowSince = DateTime.MinValue;
        _openAt = DateTime.MinValue;
    }

    void Reload()
    {
        _rows.Clear();
        _rows.AddRange(_store.Recent());
        _grid.DataSource = null;
        _grid.DataSource = _rows;
        ShowAverages();
    }

    void ShowAverages()
    {
        var boxes = _store.Averages();
        PaintAvg(_avgA, boxes.Count > 0 ? boxes[0] : null);
        PaintAvg(_avgB, boxes.Count > 1 ? boxes[1] : null);
    }

    static void PaintAvg(Label label, TrialStore.AvgBox? box)
    {
        if (box is null || box.Count == 0)
        {
            label.Text = "—";
            return;
        }
        label.Text = box.Avg.ToString("0.00") + "\n" + box.Day.ToString("dd/MM");
    }

    void PaintCom(DateTime at)
    {
        if (!_listening)
        {
            _comDot.Set(ComDot.Kind.Bad);
            return;
        }
        var live = _sawByte && at != DateTime.MinValue && DateTime.UtcNow - at <= TimeSpan.FromSeconds(3);
        _comDot.Set(live ? ComDot.Kind.Ok : ComDot.Kind.Wait);
    }

    bool _allowClose;
    bool _closeAsked;

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            StopListen();
            return;
        }
        e.Cancel = true;
        if (_closeAsked) return;
        _closeAsked = true;
        BeginInvoke(AskThenClose);
    }

    void AskThenClose()
    {
        try
        {
            if (!AskPin())
            {
                _closeAsked = false;
                return;
            }
            StopListen();
            _allowClose = true;
            Close();
        }
        catch
        {
            _closeAsked = false;
        }
    }

    bool AskPin()
    {
        using var box = new Form
        {
            Text = "Mã admin",
            Width = 320,
            Height = 150,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };
        var tb = new TextBox { Location = new Point(24, 24), Width = 250, UseSystemPasswordChar = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(180, 64) };
        box.Controls.Add(tb);
        box.Controls.Add(ok);
        box.AcceptButton = ok;
        if (box.ShowDialog(this) != DialogResult.OK) return false;
        if (tb.Text.Trim() != _store.AdminPin())
        {
            MessageBox.Show(this, "Sai mã.");
            return false;
        }
        return true;
    }

    void TrySetIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
            if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(path)) Icon = new Icon(path);
        }
        catch { }
    }

    void MarkDone()
    {
        Sweep(Path.Combine(_store.DataDir, "starch_done"));
        Sweep(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BmCancomp", "data", "starch_done"));
    }

    void Sweep(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "td*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.StartsWith("td") && long.TryParse(name[2..], out var id))
                _store.Mark(id, "đã giao");
            try { File.Delete(file); } catch { }
        }
    }

    static string OneLine(string chunk)
    {
        if (chunk.Length == 0) return "";
        var ascii = new string(chunk.Select(c => c >= 32 && c < 127 ? c : '·').ToArray()).Trim();
        return ascii.Length > 80 ? ascii[..80] : ascii;
    }

    static bool AutoCanAlive()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BmCancomp", "data", "heartbeat.txt");
        if (!File.Exists(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BmCancomp", "data", "heartbeat.txt");
        return File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromSeconds(8);
    }

    void Unlock()
    {
        using var box = new Form { Text = "Mã admin", Width = 320, Height = 150, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog };
        var tb = new TextBox { Location = new Point(24, 24), Width = 250, UseSystemPasswordChar = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(180, 64) };
        box.Controls.Add(tb);
        box.Controls.Add(ok);
        box.AcceptButton = ok;
        if (box.ShowDialog(this) != DialogResult.OK) return;
        if (tb.Text.Trim() != _store.AdminPin())
        {
            MessageBox.Show(this, "Sai mã.");
            return;
        }
        _unlocked = true;
    }

    void EditShifts()
    {
        if (!_unlocked) Unlock();
        if (!_unlocked) return;
        using var box = new Form { Text = "Ca cân — mỗi dòng HH:mm Tên", Width = 420, Height = 320, StartPosition = FormStartPosition.CenterParent };
        var tb = new TextBox { Multiline = true, Dock = DockStyle.Fill, Text = string.Join(Environment.NewLine, _store.ShiftLines()), Font = new Font("Consolas", 11f) };
        var ok = new Button { Text = "Lưu", Dock = DockStyle.Bottom, Height = 36, DialogResult = DialogResult.OK };
        box.Controls.Add(tb);
        box.Controls.Add(ok);
        box.AcceptButton = ok;
        if (box.ShowDialog(this) == DialogResult.OK) _store.SaveShifts(tb.Text);
    }

    void ShowRules()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "quy-tac.md");
        var text = File.Exists(path) ? File.ReadAllText(path) : "Không thấy quy-tac.md cạnh chương trình.";
        using var box = new Form { Text = "Quy tắc điểm bột", Width = 760, Height = 560, StartPosition = FormStartPosition.CenterParent };
        var tb = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Text = text, Font = new Font("Segoe UI", 10f) };
        box.Controls.Add(tb);
        box.ShowDialog(this);
    }

    static Label Value(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Segoe UI", 18f, FontStyle.Bold),
        ForeColor = Color.FromArgb(246, 238, 230), Location = new Point(10, 36),
    };

    static Label Lamp() => new() { AutoSize = true, Margin = new Padding(0, 4, 0, 4), Text = "○ …" };

    static void SetLamp(Label lamp, bool on, string text)
    {
        lamp.Text = (on ? "● " : "○ ") + text;
        lamp.ForeColor = on ? Color.FromArgb(94, 184, 108) : Color.FromArgb(201, 180, 173);
    }

    static Panel Mini(string title, Label value)
    {
        var p = new Panel { Width = 150, Height = 68, Margin = new Padding(18, 0, 0, 0), BackColor = Color.FromArgb(62, 18, 28) };
        p.Controls.Add(new Label { Text = title, Location = new Point(8, 4), AutoSize = true, ForeColor = Color.FromArgb(232, 197, 71) });
        value.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        value.Location = new Point(8, 24);
        p.Controls.Add(value);
        return p;
    }

    static Panel Card(string title, Label value)
    {
        var p = new Panel { Width = 176, Height = 88, Margin = new Padding(0, 0, 8, 0), BackColor = Color.FromArgb(62, 18, 28) };
        p.Controls.Add(new Label { Text = title, Location = new Point(10, 8), AutoSize = true, ForeColor = Color.FromArgb(232, 197, 71) });
        p.Controls.Add(value);
        return p;
    }

    static Button Button(string text) => new()
    {
        Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(74, 24, 32), ForeColor = Color.FromArgb(246, 238, 230),
        Margin = new Padding(0, 0, 8, 0),
    };

    static DataGridViewTextBoxColumn Col(string prop, string header, bool readOnly) => new()
    {
        DataPropertyName = prop, HeaderText = header, ReadOnly = readOnly,
        AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
    };
}

sealed class ComDot : Control
{
    public enum Kind { Bad, Wait, Ok }
    Kind _kind = Kind.Bad;

    public ComDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(16, 16);
        TabStop = false;
    }

    public void Set(Kind kind)
    {
        if (_kind == kind) return;
        _kind = kind;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var color = _kind switch
        {
            Kind.Ok => Color.FromArgb(94, 184, 108),
            Kind.Wait => Color.FromArgb(232, 197, 71),
            _ => Color.FromArgb(214, 48, 49),
        };
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var s = Math.Min(Width, Height) - 2;
        using var brush = new SolidBrush(color);
        e.Graphics.FillEllipse(brush, 1, 1, s, s);
    }
}
