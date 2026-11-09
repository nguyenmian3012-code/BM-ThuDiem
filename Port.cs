using System.IO.Ports;
using System.Text;

namespace BMThuDiem;

public sealed class ListenCom4 : IDisposable
{
    SerialPort? _sp;
    readonly object _gate = new();
    string _buf = "";

    public event Action<string>? Frame;

    public void Open()
    {
        Close();
        var sp = new SerialPort("COM4", 1200, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false,
            ReadTimeout = 200,
            Encoding = Encoding.ASCII,
        };
        sp.DataReceived += OnData;
        sp.Open();
        sp.DtrEnable = false;
        sp.RtsEnable = false;
        sp.Handshake = Handshake.None;
        _sp = sp;
    }

    void OnData(object? sender, SerialDataReceivedEventArgs e)
    {
        SerialPort? sp;
        lock (_gate) sp = _sp;
        if (sp == null || !sp.IsOpen) return;
        string chunk;
        try { chunk = sp.ReadExisting(); }
        catch { return; }
        if (string.IsNullOrEmpty(chunk)) return;
        lock (_gate)
        {
            _buf += chunk;
            while (_buf.Length > 0)
            {
                var eq = _buf.IndexOf('=');
                var cr = _buf.IndexOf('\r');
                var lf = _buf.IndexOf('\n');
                var cut = -1;
                if (eq >= 0 && eq <= 24) cut = eq;
                if (cr >= 0 && (cut < 0 || cr < cut)) cut = cr;
                if (lf >= 0 && (cut < 0 || lf < cut)) cut = lf;
                if (cut < 0)
                {
                    if (_buf.Length > 240) _buf = _buf[^40..];
                    break;
                }
                var line = _buf[..(cut + 1)];
                _buf = _buf[(cut + 1)..];
                var raw = line.Trim('\r', '\n', '\0', ' ');
                if (raw.Length > 0) Frame?.Invoke(raw);
            }
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            if (_sp == null) return;
            try { _sp.DataReceived -= OnData; } catch { }
            try { if (_sp.IsOpen) _sp.Close(); } catch { }
            try { _sp.Dispose(); } catch { }
            _sp = null;
            _buf = "";
        }
    }

    public void Dispose() => Close();
}
