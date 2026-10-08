using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace JingTingYue.Portable;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            var launcher = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位程序文件");
            using var hashStream = typeof(Program).Assembly.GetManifestResourceStream("JingTingYue.PayloadHash")
                ?? throw new InvalidDataException("发布文件缺少校验信息");
            using var hashReader = new StreamReader(hashStream);
            var expectedHash = Convert.FromHexString(hashReader.ReadToEnd().Trim());
            if (expectedHash.Length != 32) throw new InvalidDataException("校验信息无效");

            var hashName = Convert.ToHexString(expectedHash).ToLowerInvariant();
            var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JingTingYue", "Runtime");
            var runtimeDir = Path.Combine(cacheRoot, hashName);
            var appPath = Path.Combine(runtimeDir, "JingTingYue.exe");
            var marker = Path.Combine(runtimeDir, ".ready");

            if (!File.Exists(marker) || !File.Exists(appPath))
            {
                Application.EnableVisualStyles();
                using var splash = new PreparationForm();
                splash.Shown += async (_, _) =>
                {
                    try
                    {
                        await Task.Run(() => Prepare(expectedHash,
                            cacheRoot, runtimeDir, marker));
                        splash.DialogResult = DialogResult.OK;
                    }
                    catch (Exception error)
                    {
                        splash.Error = error;
                        splash.DialogResult = DialogResult.Abort;
                    }
                    splash.Close();
                };
                if (splash.ShowDialog() != DialogResult.OK)
                    throw splash.Error ?? new IOException("运行资源准备失败");
            }

            var launcherDir = Path.GetDirectoryName(launcher)!;
            var dataDir = launcherDir;
            var start = new ProcessStartInfo(appPath) { UseShellExecute = false, WorkingDirectory = runtimeDir };
            start.Environment["JINGTINGYUE_DATA_DIR"] = dataDir;
            if (Process.Start(start) is null) throw new IOException("无法启动静听阅");
        }
        catch (Exception error)
        {
            MessageBox.Show("静听阅启动失败：\n" + error.Message +
                "\n\n请确认 EXE 文件完整，且其所在文件夹可写入。", "静听阅", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void Prepare(byte[] expectedHash,
        string cacheRoot, string runtimeDir, string marker)
    {
        Directory.CreateDirectory(cacheRoot);
        using var mutex = new Mutex(false, "Local\\JingTingYue-" + Convert.ToHexString(expectedHash));
        mutex.WaitOne();
        try
        {
            if (File.Exists(marker) && File.Exists(Path.Combine(runtimeDir, "JingTingYue.exe"))) return;
            using var payload = typeof(Program).Assembly.GetManifestResourceStream("JingTingYue.Payload")
                ?? throw new InvalidDataException("发布文件缺少运行资源");
            var hash = SHA256.HashData(payload);
            if (!CryptographicOperations.FixedTimeEquals(hash, expectedHash))
                throw new InvalidDataException("运行资源校验失败，请重新下载 EXE");
            payload.Position = 0;
            if (Directory.Exists(runtimeDir)) Directory.Delete(runtimeDir, recursive: true);
            var stage = runtimeDir + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(stage);
                using (var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true))
                    archive.ExtractToDirectory(stage);
                if (!File.Exists(Path.Combine(stage, "JingTingYue.exe")) ||
                    !File.Exists(Path.Combine(stage, "WebView2Fixed", "msedgewebview2.exe")) ||
                    !File.Exists(Path.Combine(stage, "tts-server", "python", "python.exe")))
                    throw new InvalidDataException("运行资源缺少必要文件");
                File.WriteAllText(Path.Combine(stage, ".ready"), Convert.ToHexString(expectedHash));
                Directory.Move(stage, runtimeDir);
            }
            finally
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            }
        }
        finally { mutex.ReleaseMutex(); }
    }

    private sealed class PreparationForm : Form
    {
        public Exception? Error { get; set; }

        public PreparationForm()
        {
            Text = "静听阅";
            Width = 390;
            Height = 145;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Controls.Add(new Label
            {
                Text = "首次启动，正在准备阅读与朗读组件…",
                Left = 24, Top = 20, Width = 340, Height = 32,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 10)
            });
            Controls.Add(new ProgressBar
            {
                Left = 24, Top = 62, Width = 330, Height = 14,
                Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 20
            });
        }
    }

}
