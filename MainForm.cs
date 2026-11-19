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
    DateTime _lastByteAt = DateTime.MinValue;
    readonly List<(DateTime From, DateTime To)> _silence = new();
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
    readonly List<(DateTime T, int G)> _recent = new();
    const double StableSec = 1.5;
    const int StableSpread = 15;
    readonly List<(DateTime T, int G)> _dryAccum = new();
    int _nextStt = 1;
    readonly List<(DateTime T, int Stt)> _chartStts = new();
    DateTime _lowSince = DateTime.MinValue;
    DateTime _openAt = DateTime.MinValue;
    readonly List<DateTime> _trialStarts = new();
    CheckBox _hideLow = null!;

    public MainForm()
    {
        _store.Init();
        Text = "BM-ThuDiem 1.5.2  ·  COM4  ·  QĐ 228/1999";
        Width = 1180;
        Height = 880;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(42, 10, 18);
        ForeColor = Color.FromArgb(246, 238, 230);
        Font = new Font("Segoe UI", 10f);

        TrySetIcon();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(12, 8, 12, 4), WrapContents = false };
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

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(12, 6, 12, 6) };
        var listen = Button("Nghe COM4");
        var stop = Button("Ngừng");
        var edit = Button("Mở sửa");
        var shifts = Button("Ca cân");
        var rules = Button("Quy tắc");
        var clear = Button("Clear");
        var drop = Button("Xóa phiếu");
        var hist = Button("Lịch sử COM");
        var hideLow = new CheckBox { Text = "Lọc dưới 15", AutoSize = true, Checked = true, ForeColor = Color.FromArgb(246, 238, 230), Margin = new Padding(12, 8, 8, 0) };
        listen.Click += (_, _) => StartListen();
        stop.Click += (_, _) => StopListen();
        edit.Click += (_, _) => Unlock();
        shifts.Click += (_, _) => EditShifts();
        rules.Click += (_, _) => ShowRules();
        clear.Click += (_, _) => _chart.ClearDisplay();
        drop.Click += (_, _) => DeleteChosen();
        hist.Click += (_, _) => ShowSilence();
        hideLow.CheckedChanged += (_, _) => Reload();
        _hideLow = hideLow;
        bar.Controls.AddRange(new Control[] { listen, stop, edit, shifts, rules, clear, drop, hist, hideLow, Mini("TB a Phước", _avgA), Mini("TB a Đấu", _avgB) });
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2 };
        _grid.BackgroundColor = Color.FromArgb(42, 16, 22);
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(42, 16, 22);
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(246, 238, 230);
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(74, 24, 32);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(232, 197, 71);
        _grid.EnableHeadersVisualStyles = false;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
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
            if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not ThuRow row) return;
            var low = row.Score is < 15;
            if (row.Id != 0 && !low) return;
            e.CellStyle.BackColor = low ? Color.FromArgb(92, 28, 28) : Color.FromArgb(90, 48, 18);
            e.CellStyle.ForeColor = Color.FromArgb(232, 197, 71);
            e.CellStyle.SelectionBackColor = low ? Color.FromArgb(92, 28, 28) : Color.FromArgb(90, 48, 18);
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
        TrackSilence();
        try { _port?.Pump(); } catch { }
        try { File.WriteAllText(Path.Combine(_store.DataDir, "thudiem.host"), DateTimeOffset.Now.ToString("o")); } catch { }
        var shown = raw.Length > 0 ? raw : OneLine(_chunk);
        if (shown.Length > 0 && DateTime.UtcNow - _rawAt >= TimeSpan.FromMilliseconds(500))
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
        if (_chartAt == DateTime.MinValue || DateTime.UtcNow - _chartAt >= TimeSpan.FromMilliseconds(500))
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
        var now = DateTime.UtcNow;
        _recent.Add((now, grams));
        if (_recent.Count > 60) _recent.RemoveRange(0, _recent.Count - 60);

        // Aggressive close: already have dry+wet and weight dropped low
        if (_session && _dryLocked && _wetG != null && grams < 400)
        {
            if (_lowSince == DateTime.MinValue) _lowSince = now;
            if (now - _lowSince >= TimeSpan.FromSeconds(0.6))
            {
                if (Wet(out var wet)) Commit(_dryG, wet);
                else EndSession();
                return;
            }
        }
        else if (grams < 100)
        {
            if (_lowSince == DateTime.MinValue) _lowSince = now;
            if (now - _lowSince < TimeSpan.FromSeconds(0.8)) return;
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
        else
            _lowSince = DateTime.MinValue;

        var rising = grams > _prev + 30;
        _prev = grams;

        // Force close previous session if a new rise starts after we already locked values
        if (_session && _dryLocked && rising && grams > 800)
        {
            if (_wetG != null && Wet(out var wet))
                Commit(_dryG, wet);
            else
                EndSession();
            // fall through to open new
        }

        if (!_session)
        {
            if (grams > 1000 && rising)
            {
                _session = true;
                _openAt = DateTime.Now;
                var stt = _nextStt++;
                _chartStts.Add((DateTime.Now, stt));
                if (_chartStts.Count > 40) _chartStts.RemoveAt(0);
                _chart.AddStt(DateTime.Now, stt);
                _state.Text = "mở phiên #" + stt;
            }
            return;
        }

        // Dry accumulation only in 4900-5100
        if (grams >= 4900 && grams <= 5100)
        {
            _dryAccum.Add((now, grams));
            if (_dryAccum.Count > 80) _dryAccum.RemoveRange(0, _dryAccum.Count - 80);
            _state.Text = "đang tích lũy khô...";
            return;
        }
        else if (_dryAccum.Count > 0)
        {
            FinalizeDry();
        }

        if (!_dryLocked)
        {
            _state.Text = "phiên mở, chưa thấy khô 4900–5100 g";
            return;
        }
        if (grams >= 150 && grams <= 1200)
        {
            _wetSeen.Add(grams);
            if (_wetSeen.Count > 50) _wetSeen.RemoveRange(0, _wetSeen.Count - 50);
            TryLockWet();
            return;
        }
        _state.Text = _wetG is double held
            ? "ướt " + held.ToString("0") + " g, chờ cân về 0"
            : "đã có khô, chờ cân ướt";
    }

    void FinalizeDry()
    {
        if (_dryAccum.Count < 3)
        {
            _dryAccum.Clear();
            return;
        }
        var span = (_dryAccum[^1].T - _dryAccum[0].T).TotalSeconds;
        if (span < 1.2)
        {
            _dryAccum.Clear();
            return;
        }
        var vals = _dryAccum.Select(s => s.G).OrderBy(x => x).ToList();
        if (vals[^1] - vals[0] > StableSpread)
        {
            _dryAccum.Clear();
            return;
        }
        var median = vals[vals.Count / 2];
        var dist = Math.Abs(median - 5000);
        if (_dryLocked && dist > _best + 3) 
        {
            _dryAccum.Clear();
            return;
        }
        _dryG = median;
        _best = dist;
        _dryLocked = true;
        _dry.Text = median.ToString("0") + " g";
        _wet.Text = "—";
        _eq.Text = "—";
        _point.Text = "—";
        _wetG = null;
        _wetSeen.Clear();
        _state.Text = "khô ổn " + median.ToString("0") + " g";
        _chart.MarkCapture(DateTime.Now, (int)median);
        _dryAccum.Clear();
    }

    void TryLockWet()
    {
        if (!TryStable(150, 1200, out var stableG, out var stableT)) return;
        _wetG = stableG;
        _wet.Text = stableG.ToString("0") + " g";
        var equiv = StarchTable.EquivalentWet(_dryG, stableG);
        _eq.Text = equiv.ToString("0") + " g";
        if (StarchTable.TryNearest(equiv, out _, out var diem))
        {
            _point.Text = diem.ToString("0.00") + " %";
            _state.Text = "ướt ổn " + stableG.ToString("0") + " g → " + diem.ToString("0.00") + "%";
        }
        else
        {
            _point.Text = "ngoài bảng";
            _state.Text = "ướt " + equiv.ToString("0") + " g ngoài 280–670";
        }
        _chart.MarkCapture(DateTime.Now, (int)stableG);
    }

    bool TryStable(int minG, int maxG, out double median, out DateTime at)
    {
        median = 0;
        at = DateTime.MinValue;
        if (_recent.Count < 3) return false;
        var cutoff = DateTime.UtcNow.AddSeconds(-StableSec);
        var window = _recent.Where(s => s.T >= cutoff && s.G >= minG && s.G <= maxG).ToList();
        if (window.Count < 2) return false;
        var span = (window[^1].T - window[0].T).TotalSeconds;
        if (span < StableSec * 0.8) return false;
        var vals = window.Select(s => s.G).OrderBy(x => x).ToList();
        if (vals[^1] - vals[0] > StableSpread) return false;
        median = vals[vals.Count / 2];
        at = window[^1].T;
        return true;
    }

    bool Wet(out double wet)
    {
        wet = 0;
        if (_wetG is double held)
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
        try
        {
            var equiv = StarchTable.EquivalentWet(dry, wet);
            var ok = StarchTable.TryNearest(equiv, out var listed, out var diem);
            var low = ok && diem < 15;
            var who = _store.OperatorNow();
            var note = !ok ? "ngoài bảng" : low ? "dưới 15" : "";
            var id = _store.Add(ok ? diem : null, who, dry, wet, equiv, listed, "", note);
            if (_chartStts.Count > 0)
            {
                var last = _chartStts[^1];
                _chart.SetLastStt((int)id);
            }
            if (ok && !low)
            {
                _store.HandOff(id, diem, who, dry, wet, equiv, listed);
                _point.Text = diem.ToString("0.00") + " %";
                _state.Text = "đã chốt " + diem.ToString("0.00") + "%";
            }
            else if (low)
            {
                _point.Text = diem.ToString("0.00") + " %";
                _state.Text = "dưới 15, không tính";
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
        catch (Exception ex)
        {
            _point.Text = "lỗi";
            _state.Text = ex.Message.Length > 80 ? ex.Message[..80] : ex.Message;
            EndSession();
        }
    }

    void RememberTrial()
    {
        if (_openAt == DateTime.MinValue) _openAt = DateTime.Now;
        _trialStarts.Add(_openAt);
        if (_trialStarts.Count > 45) _trialStarts.RemoveRange(0, _trialStarts.Count - 30);
        if (_trialStarts.Count >= 15)
            _chart.ShowFrom(_trialStarts[^15]);
    }

    void EndSession()
    {
        _session = false;
        _dryLocked = false;
        _wetG = null;
        _wetSeen.Clear();
        _recent.Clear();
        _dryAccum.Clear();
        _best = double.MaxValue;
        _prev = 0;
        _lowSince = DateTime.MinValue;
        _openAt = DateTime.MinValue;
    }

    void TrackSilence()
    {
        var now = DateTime.UtcNow;
        var silent = !_sawByte || _sampleAt == DateTime.MinValue || now - _sampleAt > TimeSpan.FromSeconds(8);
        if (silent)
        {
            if (_lastByteAt != DateTime.MinValue && (_silence.Count == 0 || _silence[^1].To != DateTime.MinValue))
                _silence.Add((_lastByteAt == DateTime.MinValue ? now : _lastByteAt, DateTime.MinValue));
        }
        else
        {
            _lastByteAt = now;
            if (_silence.Count > 0 && _silence[^1].To == DateTime.MinValue)
            {
                var s = _silence[^1];
                _silence[^1] = (s.From, now);
            }
        }
        if (_silence.Count > 200) _silence.RemoveRange(0, _silence.Count - 150);
    }

    void ShowSilence()
    {
        var lines = new List<string>();
        if (_silence.Count == 0) lines.Add("Chưa ghi nhận khoảng im COM4.");
        else
        {
            foreach (var (from, to) in _silence.TakeLast(40))
            {
                var end = to == DateTime.MinValue ? DateTime.Now : to;
                var hours = (end - from).TotalHours;
                lines.Add($"{from.ToLocalTime():dd/MM HH:mm} → {(to == DateTime.MinValue ? "đang im" : to.ToLocalTime().ToString("HH:mm"))}  ({hours:0.0} giờ)");
            }
        }
        MessageBox.Show(this, string.Join(Environment.NewLine, lines), "Lịch sử im COM4", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void DeleteChosen()
    {
        var ids = new List<long>();
        foreach (DataGridViewRow line in _grid.SelectedRows)
        {
            if (line.DataBoundItem is ThuRow row && row.Id > 0 && !ids.Contains(row.Id))
                ids.Add(row.Id);
        }
        if (ids.Count == 0)
        {
            _state.Text = "chọn phiếu lỗi rồi bấm Xóa phiếu";
            return;
        }
        if (!AskPin()) return;
        if (MessageBox.Show(this, "Xóa " + ids.Count + " phiếu đã chọn?", "Xóa phiếu", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;
        _store.Delete(ids);
        Reload();
        _state.Text = "đã xóa " + ids.Count + " phiếu";
    }

    void Reload()
    {
        _rows.Clear();
        _rows.AddRange(_store.Recent().Where(row => row.Id == 0 || !_hideLow.Checked || row.Score is not < 15));
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
