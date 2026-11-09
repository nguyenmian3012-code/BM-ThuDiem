namespace BMThuDiem;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var mutex = new Mutex(false, "Local\\BM.ThuDiem.External");
        var first = false;
        try { first = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { first = true; }
        if (!first) return;
        Application.Run(new MainForm());
    }
}
