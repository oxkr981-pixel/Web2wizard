using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using System.Diagnostics;
using System.ComponentModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Drawing;
using System.Net;

namespace Launcher;

public partial class Form1 : Form
{
    string outputPath;
    string templatePath;
    string projectRoot;
    private BackgroundWorker buildWorker;
    private byte[]? latestIconIcoBytes;
    private readonly object iconLock = new object();
    private static readonly HttpClient httpClient = new HttpClient();
    private CancellationTokenSource? iconCts;
    private readonly string iconCacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Web2Executable", "IconCache");

    public Form1()
    {
        InitializeComponent();

        // Navigate from bin\Debug\net8.0-windows\ up to project root
        string baseDir = AppContext.BaseDirectory;
        projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
        templatePath = Path.Combine(projectRoot, "Template");
        outputPath = Path.Combine(templatePath, "GeneratedApp");

        // Initialize background worker for async build operations
        buildWorker = new BackgroundWorker();
        buildWorker.DoWork += BuildWorker_DoWork;
        buildWorker.RunWorkerCompleted += BuildWorker_RunWorkerCompleted;
        buildWorker.ProgressChanged += BuildWorker_ProgressChanged;
        buildWorker.WorkerReportsProgress = true;
    }

    private bool IsInnoSetupInstalled()
{
    string[] paths =
    {
        @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        @"C:\Program Files\Inno Setup 6\ISCC.exe"
    };

    return paths.Any(File.Exists);
}

    private void UpdateStatus(string message, bool append = false)
    {
        if (lblStatus.InvokeRequired)
        {
            lblStatus.Invoke(() => UpdateStatus(message, append));
        }
        else
        {
            if (append)
                lblStatus.Text += Environment.NewLine + message;
            else
                lblStatus.Text = message;
        }
    }

    private void UpdateProgress(int percentage)
    {
        if (progressBar1.InvokeRequired)
        {
            progressBar1.Invoke(() => UpdateProgress(percentage));
        }
        else
        {
            progressBar1.Value = Math.Min(100, Math.Max(0, percentage));
        }
    }

    private void txtUrl_TextChanged(object? sender, EventArgs e)
    {
        // Debounce: cancel previous and wait 500ms before fetching
        iconCts?.Cancel();
        iconCts?.Dispose();
        iconCts = new CancellationTokenSource();
        var token = iconCts.Token;

        string input = txtUrl.Text.Trim();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
            }
            catch (TaskCanceledException) { return; }

            if (token.IsCancellationRequested) return;

            // Validate URL
            if (!Uri.TryCreate(input, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
                return;

            string domain = u.Host;
            await LoadIconForDomainAsync(domain, token).ConfigureAwait(false);
        });
    }

    private async Task LoadIconForDomainAsync(string domain, CancellationToken token)
    {
        UpdateStatus("Checking cache...");

        try
        {
            Directory.CreateDirectory(iconCacheDir);
            string cachePath = Path.Combine(iconCacheDir, domain + ".ico");

            if (File.Exists(cachePath))
            {
                byte[] cached = File.ReadAllBytes(cachePath);
                lock (iconLock)
                {
                    latestIconIcoBytes = cached;
                }

                // Show preview from cached ICO (use 64px frame)
                ShowPreviewFromIcoBytes(cached);
                UpdateStatus("Loaded from cache.");
                return;
            }

            UpdateStatus("Downloading icon...");

            byte[]? fetched = await FetchFaviconMultiSourceAsync(domain, token).ConfigureAwait(false);

            if (token.IsCancellationRequested) return;

            if (fetched != null && fetched.Length > 0)
            {
                // Convert to multi-size ICO
                byte[] icoBytes = ConvertToMultiSizeIco(fetched);

                // Save to cache
                try
                {
                    File.WriteAllBytes(cachePath, icoBytes);
                }
                catch { }

                lock (iconLock)
                {
                    latestIconIcoBytes = icoBytes;
                }

                // Show preview
                ShowPreviewFromIcoBytes(icoBytes);
                UpdateStatus("Icon ready.");
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch { }

        // Fallback default
        try
        {
            var defaultIco = CreateIcoFromSystemIcon(SystemIcons.Application);
            lock (iconLock)
            {
                latestIconIcoBytes = defaultIco;
            }

            ShowPreviewFromIcoBytes(defaultIco);
            UpdateStatus("Using default icon.");
        }
        catch { }
    }

    private void ShowPreviewFromIcoBytes(byte[] icoBytes)
    {
        try
        {
            using var ms = new MemoryStream(icoBytes);
            // Load icon and convert to bitmap for preview
            using Icon icon = new Icon(ms);
            using Bitmap bmp = icon.ToBitmap();
            Bitmap preview = new Bitmap(bmp, new Size(64, 64));

            if (picIconPreview.InvokeRequired)
                picIconPreview.Invoke(() => picIconPreview.Image = new Bitmap(preview));
            else
                picIconPreview.Image = new Bitmap(preview);
        }
        catch { }
    }

    private async Task<byte[]?> FetchFaviconMultiSourceAsync(string domain, CancellationToken token)
    {
        string[] sources = new[]
        {
            $"https://www.google.com/s2/favicons?domain={domain}&sz=256",
            $"https://icon.horse/icon/{domain}",
            $"https://{domain}/favicon.ico"
        };

        foreach (var src in sources)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, src);
                using var resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;

                var data = await resp.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                if (data != null && data.Length > 0)
                    return data;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* try next */ }
        }

        return null;
    }

    private async Task<byte[]?> EnsureIconForDomainAsync(string domain)
    {
        try
        {
            // Check cache
            string cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Web2Executable", "IconCache", domain + ".ico");
            if (File.Exists(cachePath))
            {
                try
                {
                    return File.ReadAllBytes(cachePath);
                }
                catch { }
            }

            // Fetch
            byte[]? fetched = await FetchFaviconMultiSourceAsync(domain, CancellationToken.None).ConfigureAwait(false);
            if (fetched == null || fetched.Length == 0)
                return null;

            byte[] icoBytes;
            try
            {
                icoBytes = ConvertToMultiSizeIco(fetched);
            }
            catch
            {
                // If conversion fails, but source is already ICO, try to use it
                icoBytes = fetched;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath) ?? string.Empty);
                File.WriteAllBytes(cachePath, icoBytes);
            }
            catch { }

            return icoBytes;
        }
        catch { return null; }
    }

    private byte[] ConvertToMultiSizeIco(byte[] sourceImageData)
    {
        // Sizes to include
        int[] sizes = new[] { 16, 32, 64, 256 };

        Image? src = null;
        try
        {
            using var ms = new MemoryStream(sourceImageData);
            try
            {
                src = Image.FromStream(ms);
            }
            catch
            {
                // Possibly ICO; try Icon
                try
                {
                    ms.Position = 0;
                    using var ic = new Icon(ms);
                    src = ic.ToBitmap();
                }
                catch
                {
                    src = null;
                }
            }
        }
        catch { src = null; }

        if (src == null)
            throw new Exception("Failed to parse source image for icon conversion");

        var pngImages = new List<byte[]>();

        foreach (var s in sizes)
        {
            using var bmp = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, s, s);
            }

            using var pms = new MemoryStream();
            bmp.Save(pms, System.Drawing.Imaging.ImageFormat.Png);
            pngImages.Add(pms.ToArray());
        }

        return BuildIcoFromPngImages(pngImages.ToArray(), sizes);
    }

    private byte[] BuildIcoFromPngImages(byte[][] pngImages, int[] sizes)
    {
        using var outMs = new MemoryStream();
        // ICONDIR
        // Reserved 2 bytes, Type 2 bytes (1 for icon), Count 2 bytes
        outMs.Write(BitConverter.GetBytes((short)0), 0, 2);
        outMs.Write(BitConverter.GetBytes((short)1), 0, 2);
        outMs.Write(BitConverter.GetBytes((short)pngImages.Length), 0, 2);

        int entrySize = 16;
        int offset = 6 + (entrySize * pngImages.Length);

        var dirEntries = new List<byte[]>();

        for (int i = 0; i < pngImages.Length; i++)
        {
            byte[] img = pngImages[i];
            int size = sizes[i];

            byte width = (byte)(size >= 256 ? 0 : size);
            byte height = (byte)(size >= 256 ? 0 : size);

            using var entry = new MemoryStream();
            entry.WriteByte(width); // width
            entry.WriteByte(height); // height
            entry.WriteByte(0); // color count
            entry.WriteByte(0); // reserved
            // planes (2 bytes)
            entry.Write(BitConverter.GetBytes((short)0), 0, 2);
            // bitcount (2 bytes)
            entry.Write(BitConverter.GetBytes((short)32), 0, 2);
            // bytes in resource (4 bytes)
            entry.Write(BitConverter.GetBytes(img.Length), 0, 4);
            // image offset (4 bytes)
            entry.Write(BitConverter.GetBytes(offset), 0, 4);

            dirEntries.Add(entry.ToArray());

            offset += img.Length;
        }

        // Write directory entries
        foreach (var d in dirEntries)
            outMs.Write(d, 0, d.Length);

        // Write image data
        for (int i = 0; i < pngImages.Length; i++)
        {
            outMs.Write(pngImages[i], 0, pngImages[i].Length);
        }

        return outMs.ToArray();
    }

    private byte[] CreateIcoFromSystemIcon(Icon sysIcon)
    {
        // Create multiple PNG frames from the icon bitmap scaled
        using var bmp = sysIcon.ToBitmap();
        int[] sizes = new[] { 16, 32, 64, 256 };
        var pngs = new List<byte[]>();
        foreach (var s in sizes)
        {
            using var resized = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, 0, 0, s, s);
            }
            using var ms = new MemoryStream();
            resized.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }

        return BuildIcoFromPngImages(pngs.ToArray(), sizes);
    }

    private void btnGenerate_Click(object? sender, EventArgs e)
    {
        if (!IsInnoSetupInstalled())
{
            _ = MessageBox.Show(
                "Inno Setup was not found.\n\nPlease install Inno Setup before generating installers.",
                "Dependency Missing",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

    Process.Start(new ProcessStartInfo
    {
        FileName = "https://jrsoftware.org/isinfo.php",
        UseShellExecute = true
    });

    return;
}
        try
        {
            // Step 0: Validate inputs
            string appName = txtAppName.Text.Trim();
            string websiteUrl = txtUrl.Text.Trim();

            UpdateStatus("Validating inputs...");
            
            if (string.IsNullOrEmpty(appName))
            {
                UpdateStatus("❌ Validation Error: App Name is empty.\n\nPlease enter an application name.");
                return;
            }

            if (string.IsNullOrEmpty(websiteUrl))
            {
                UpdateStatus("❌ Validation Error: URL is empty.\n\nPlease enter a valid website URL.");
                return;
            }
             
            // Validate URL using Uri.TryCreate
            if (!Uri.TryCreate(websiteUrl, UriKind.Absolute, out var uriResult) ||
                (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
            {
                UpdateStatus("❌ Validation Error: Invalid URL format.\n\nURL must be a valid HTTP or HTTPS URL (e.g., https://example.com)");
                return;
            }

            UpdateStatus("✓ Validation passed. Starting build...");

            // Disable Generate button and reset progress bar
            btnGenerate.Enabled = false;
            progressBar1.Value = 0;

            // Store values for background worker
            var buildData = new BuildData
            {
                AppName = appName,
                WebsiteUrl = websiteUrl,
                Version = ""
            };

            // Start background build process
            buildWorker.RunWorkerAsync(buildData);
        }
        catch (Exception ex)
        {
            UpdateStatus($"❌ Error: {ex.Message}");
            btnGenerate.Enabled = true;
        }
    }

    private void BuildWorker_DoWork(object? sender, DoWorkEventArgs e)
    {
        try
        {
            var buildData = (BuildData)e.Argument;
            string appName = buildData.AppName ?? "MyApp";
            string websiteUrl = buildData.WebsiteUrl ?? "";

            // Step 1: Manage version
            buildWorker.ReportProgress(5, "Step 1/7: Managing version...");
            string version = IncrementAndGetVersion();
            buildData.Version = version;

            // Step 2: Clean and recreate output structure
            buildWorker.ReportProgress(15, "Step 2/7: Cleaning and recreating directories...");
            if (Directory.Exists(outputPath))
                Directory.Delete(outputPath, true);

            Directory.CreateDirectory(outputPath);
            Directory.CreateDirectory(Path.Combine(outputPath, "publish"));
            Directory.CreateDirectory(Path.Combine(outputPath, "installer"));

            // Ensure icon for the domain is prepared and cached (run on worker thread)
            try
            {
                string domainForIcon = "";
                try
                {
                    if (Uri.TryCreate(websiteUrl, UriKind.Absolute, out var u))
                        domainForIcon = u.Host;
                }
                catch { }

                if (!string.IsNullOrEmpty(domainForIcon))
                {
                    buildWorker.ReportProgress(20, "Preparing icon for domain...");
                    try
                    {
                        // Run synchronously on background thread
                        var icoBytes = EnsureIconForDomainAsync(domainForIcon).GetAwaiter().GetResult();
                        if (icoBytes != null && icoBytes.Length > 0)
                        {
                            lock (iconLock)
                            {
                                latestIconIcoBytes = icoBytes;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Step 3: Generate WebView2 app files
            buildWorker.ReportProgress(30, "Step 3/7: Generating app files...");
            GenerateAppFiles(version, appName, websiteUrl);

            // Save favicon (if any) to output as appicon.ico
            try
            {
                string iconPath = Path.Combine(outputPath, "appicon.ico");
                byte[]? icoBytes = null;
                lock (iconLock)
                {
                    icoBytes = latestIconIcoBytes;
                }

                if (icoBytes != null && icoBytes.Length > 0)
                {
                    File.WriteAllBytes(iconPath, icoBytes);
                    buildWorker.ReportProgress(35, "Icon saved.");
                }
                else
                {
                    // Fallback: generate ICO bytes from system icon
                    try
                    {
                        var defaultIco = CreateIcoFromSystemIcon(SystemIcons.Application);
                        File.WriteAllBytes(iconPath, defaultIco);
                        buildWorker.ReportProgress(35, "Using default icon.");
                    }
                    catch
                    {
                        // last resort: ignore
                    }
                }
            }
            catch
            {
                // ignore icon save failures and continue
            }

            // Step 4: Create version.txt
            buildWorker.ReportProgress(40, "Step 4/7: Creating version.txt...");
            File.WriteAllText(Path.Combine(outputPath, "version.txt"), version);

            // Step 5: Publish app
            buildWorker.ReportProgress(50, "Step 5/7: Publishing WebView2 app...");
            PublishApp(appName);

            // Step 6: Generate update.json
            buildWorker.ReportProgress(65, "Step 6/7: Generating update.json...");
            GenerateUpdateJson(version);

            // Step 7: Generate Inno Setup installer script
            buildWorker.ReportProgress(75, "Step 7/7: Generating installer script...");
            GenerateInstallerScript(version, appName);

            // Step 8: Build installer
            buildWorker.ReportProgress(85, "Building installer...");
            BuildInstaller();

            buildWorker.ReportProgress(100, $"✅ Build Complete!\n\nApp Name: {appName}\nVersion: {version}\nURL: {websiteUrl}\n\nOutput: {outputPath}");
            e.Result = true;
        }
        catch (Exception ex)
        {
            e.Result = false;
            buildWorker.ReportProgress(100, $"❌ Build Failed: {ex.Message}\n\nDetails: {ex.InnerException?.Message}");
        }
    }

    private void BuildWorker_ProgressChanged(object? sender, ProgressChangedEventArgs e)
    {
        UpdateProgress(e.ProgressPercentage);
        if (e.UserState is string message)
            UpdateStatus(message, false);
    }

    private void BuildWorker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
    {
        btnGenerate.Enabled = true;

        if (e.Error != null)
        {
            UpdateStatus($"❌ Build Failed:\n{e.Error.Message}\n\n{e.Error.StackTrace}", false);
        }
        else if (e.Result is bool success && success)
        {
            // Open installer output folder
            string installerOutput = Path.Combine(outputPath, "installer", "Output");
            if (Directory.Exists(installerOutput))
            {
                UpdateStatus($"\nOpening output folder...\n\n{installerOutput}", true);
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "explorer.exe",
                        Arguments = installerOutput,
                        UseShellExecute = true
                    })?.Dispose();
                }
                catch (Exception ex)
                {
                    UpdateStatus($"\n⚠️ Could not open folder: {ex.Message}", true);
                }
            }
        }
    }

    private void btnAbout_Click(object? sender, EventArgs e)
    {
        ShowAboutDialog();
    }

    private void ShowAboutDialog()
    {
        string aboutText = @"Web2EXE Wizard

A powerful tool to convert any website into a standalone Windows application.

Version: 1.0.0
Developer: Sashwat
© 2026 All Rights Reserved

Features:
✓ Convert websites to desktop apps
✓ WebView2 integration
✓ Automatic installer generation
✓ Update checking capability
✓ GitHub distribution support

GitHub: https://github.com/oxkr981-pixel/Web2wizard
Support: Check the documentation for help";

        using (Form aboutForm = new Form())
        {
            aboutForm.Text = "About Web2EXE Wizard";
            aboutForm.Width = 400;
            aboutForm.Height = 400;
            aboutForm.StartPosition = FormStartPosition.CenterParent;
            aboutForm.FormBorderStyle = FormBorderStyle.FixedDialog;
            aboutForm.MaximizeBox = false;
            aboutForm.MinimizeBox = false;
            aboutForm.ShowIcon = false;

            Label lblAbout = new Label();
            lblAbout.Text = aboutText;
            lblAbout.Dock = DockStyle.Fill;
            lblAbout.Padding = new Padding(20);
            lblAbout.AutoSize = false;
            lblAbout.TextAlign = ContentAlignment.TopLeft;
            lblAbout.Font = new System.Drawing.Font(this.Font.FontFamily, 9);
            aboutForm.Controls.Add(lblAbout);

            Button btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.Dock = DockStyle.Bottom;
            btnClose.Height = 40;
            btnClose.Click += (s, e) => aboutForm.Close();
            aboutForm.Controls.Add(btnClose);

            aboutForm.ShowDialog(this);
        }
    }

    private string IncrementAndGetVersion()
    {
        string versionFile = Path.Combine(templatePath, "version.json");
        Version currentVersion;

        if (File.Exists(versionFile))
        {
            try
            {
                string json = File.ReadAllText(versionFile);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    string? versionStr = doc.RootElement.GetProperty("version").GetString();
                    currentVersion = new Version(versionStr ?? "1.0.0");
                }
            }
            catch
            {
                currentVersion = new Version(1, 0, 0);
            }
        }
        else
        {
            Directory.CreateDirectory(templatePath);
            currentVersion = new Version(1, 0, 0);
        }

        // Increment patch version (1.0.0 -> 1.0.1)
        Version nextVersion = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build + 1);
        string nextVersionStr = $"{nextVersion.Major}.{nextVersion.Minor}.{nextVersion.Build}";

        // Save to version.json
        var versionObj = new { version = nextVersionStr, buildDate = DateTime.Now.ToString("O") };
        string versionJson = JsonSerializer.Serialize(versionObj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(versionFile, versionJson);

        return nextVersionStr;
    }

    private void GenerateAppFiles(string version, string appName, string websiteUrl)
    {
        try
        {
            // Sanitize app name for namespace (remove spaces and special chars)
            string namespaceName = System.Text.RegularExpressions.Regex.Replace(appName, @"[^\w]", "");
            if (string.IsNullOrEmpty(namespaceName))
                namespaceName = "GeneratedApp";

            // Generate config.json with the provided website URL
            try
            {
                var configObj = new { StartUrl = websiteUrl, Version = version };
                string configJson = JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(outputPath, "config.json"), configJson);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to generate config.json: {ex.Message}", ex);
            }

            // Generate Program.cs
            string programCs = $@"namespace {namespaceName};

static class Program
{{
    [STAThread]
    static void Main()
    {{
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }}
}}";
            File.WriteAllText(Path.Combine(outputPath, "Program.cs"), programCs);

        // Generate MainForm.cs
        string mainFormCs = $@"using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace {namespaceName};

public class MainForm : Form
{{
    private WebView2 webView;
    private Label versionLabel;

    public MainForm()
    {{
        Text = ""{appName}"";
        Width = 1280;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;

        webView = new WebView2();
        webView.Dock = DockStyle.Fill;
        Controls.Add(webView);

        versionLabel = new Label();
        versionLabel.AutoSize = true;
        versionLabel.BackColor = System.Drawing.Color.White;
        versionLabel.ForeColor = System.Drawing.Color.Gray;
        versionLabel.Text = """";
        versionLabel.Dock = DockStyle.Bottom;
        Controls.Add(versionLabel);

        Load += MainForm_Load;
    }}

    private async void MainForm_Load(object? sender, EventArgs e)
    {{
        try
        {{
            string configPath = Path.Combine(AppContext.BaseDirectory, ""config.json"");
            if (!File.Exists(configPath))
                configPath = ""config.json"";

            if (!File.Exists(configPath))
            {{
                MessageBox.Show(""config.json not found"", ""Error"");
                return;
            }}

            string json = File.ReadAllText(configPath);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {{
                string? startUrl = doc.RootElement.GetProperty(""StartUrl"").GetString();
                string? version = doc.RootElement.TryGetProperty(""Version"", out var versionProp) 
                    ? versionProp.GetString() 
                    : ""1.0.0"";

                if (!string.IsNullOrEmpty(version))
                    versionLabel.Text = $""v{{version}}"";

if (string.IsNullOrEmpty(startUrl))
{{
    MessageBox.Show(""StartUrl not found in config.json"", ""Error"");
    return;
}}

string userDataFolder =
    Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        ""{namespaceName}"",
        ""WebView2""
    );

Directory.CreateDirectory(userDataFolder);

var env =
    await CoreWebView2Environment.CreateAsync(
        null,
        userDataFolder
    );

await webView.EnsureCoreWebView2Async(env);

webView.Source = new Uri(startUrl);            }}
        }}
        catch (Exception ex)
        {{
            MessageBox.Show(""Error: "" + ex.Message, ""Error"");
        }}
    }}
}}";
            File.WriteAllText(Path.Combine(outputPath, "MainForm.cs"), mainFormCs);

            // Generate {appName}.csproj with version
            string csproj = $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyVersion>{version}</AssemblyVersion>
    <FileVersion>{version}</FileVersion>
    <ProductVersion>{version}</ProductVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Microsoft.Web.WebView2"" Version=""1.0.3967.48"" />
  </ItemGroup>

  <ItemGroup>
    <None Update=""config.json"">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>";
                        // Ensure csproj includes application icon and copies it to output
                        csproj = $@"<Project Sdk=""Microsoft.NET.Sdk"">

    <PropertyGroup>
        <OutputType>WinExe</OutputType>
        <TargetFramework>net8.0-windows</TargetFramework>
        <UseWindowsForms>true</UseWindowsForms>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <AssemblyVersion>{version}</AssemblyVersion>
        <FileVersion>{version}</FileVersion>
        <ProductVersion>{version}</ProductVersion>
        <ApplicationIcon>appicon.ico</ApplicationIcon>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include=""Microsoft.Web.WebView2"" Version=""1.0.3967.48"" />
    </ItemGroup>

    <ItemGroup>
        <None Update=""config.json"">
            <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        </None>
    </ItemGroup>

    <ItemGroup>
        <None Include=""appicon.ico"">
            <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        </None>
    </ItemGroup>

</Project>";
            File.WriteAllText(Path.Combine(outputPath, $"{appName}.csproj"), csproj);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to generate app files: {ex.Message}", ex);
        }
    }

    private void GenerateUpdateJson(string version)
    {
        try
        {
            var updateObj = new
            {
                version = version,
                downloadUrl = "https://oxkr981-pixel.github.io/Web2wizard/Setup.exe",
                changelog = "Release " + version,
                releaseDate = DateTime.Now.ToString("yyyy-MM-dd")
            };
            
            string updateJsonPath = Path.Combine(outputPath, "update.json");
            string updateJson = JsonSerializer.Serialize(updateObj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(updateJsonPath, updateJson);

            // Copy to publish folder if it exists
            string publishFolder = Path.Combine(outputPath, "publish");
            if (Directory.Exists(publishFolder))
            {
                string publishUpdateJsonPath = Path.Combine(publishFolder, "update.json");
                File.Copy(updateJsonPath, publishUpdateJsonPath, true);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to generate update.json: {ex.Message}", ex);
        }
    }

    private void PublishApp(string appName)
    {
        string csprojPath = Path.Combine(outputPath, $"{appName}.csproj");
        string publishOutput = Path.Combine(outputPath, "publish");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $@"publish ""{csprojPath}"" -c Release -o ""{publishOutput}""",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var process = Process.Start(psi))
        {
            if (process == null)
                throw new Exception("Failed to start dotnet publish");

            try
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new Exception($"Publish failed: {error}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void GenerateInstallerScript(string version, string appName)
    {
        string installerPath = Path.Combine(outputPath, "installer");
        string publishPath = Path.Combine(outputPath, "publish");
        string setupScript = Path.Combine(installerPath, "setup.iss");

        StringBuilder innoBuilder = new StringBuilder();
        innoBuilder.AppendLine("; Inno Setup Script");
        innoBuilder.AppendLine("; Generated by Web2EXE Wizard");
        innoBuilder.AppendLine($"; Application: {appName} v{version}");
        innoBuilder.AppendLine();
        innoBuilder.AppendLine("[Setup]");
        innoBuilder.AppendLine("SetupIconFile=..\\appicon.ico");
        innoBuilder.AppendLine($"AppName={appName}");
        innoBuilder.AppendLine($"AppVersion={version}");
        innoBuilder.AppendLine("AppPublisher=Sashwat");
        innoBuilder.AppendLine("AppPublisherURL=https://github.com/oxkr981-pixel");
        innoBuilder.AppendLine($@"DefaultDirName={{commonpf}}\{appName}");
        innoBuilder.AppendLine($"OutputDir=\"{installerPath}\\Output\"");
        innoBuilder.AppendLine($"OutputBaseFilename={appName} Setup");
        innoBuilder.AppendLine("Compression=lzma2");
        innoBuilder.AppendLine("SolidCompression=yes");
        innoBuilder.AppendLine("ArchitecturesInstallIn64BitMode=x64");
        innoBuilder.AppendLine("ArchitecturesAllowed=x64");
        innoBuilder.AppendLine("PrivilegesRequired=admin");
        innoBuilder.AppendLine();
        innoBuilder.AppendLine("[Languages]");
        innoBuilder.AppendLine("Name: \"english\"; MessagesFile: \"compiler:Default.isl\"");
        innoBuilder.AppendLine();
        innoBuilder.AppendLine("[Files]");
        innoBuilder.AppendLine($@"Source: ""{publishPath}\*""; DestDir: ""{{app}}""; Flags: ignoreversion recursesubdirs createallsubdirs");        innoBuilder.AppendLine();
        innoBuilder.AppendLine("[Icons]");
innoBuilder.AppendLine($@"Name: ""{{commonprograms}}\{appName}""; Filename: ""{{app}}\{appName}.exe""");
innoBuilder.AppendLine($@"Name: ""{{commonprograms}}\Uninstall {appName}""; Filename: ""{{uninstallexe}}""");        innoBuilder.AppendLine($@"Name: ""{{commondesktop}}\{appName}""; Filename: ""{{app}}\{appName}.exe""");
        innoBuilder.AppendLine();
        innoBuilder.AppendLine("[Run]");
        innoBuilder.AppendLine($@"Filename: ""{{app}}\{appName}.exe""; Description: ""Launch {appName}""; Flags: postinstall nowait skipifsilent");

        try
        {
            File.WriteAllText(setupScript, innoBuilder.ToString());
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to write installer script to {setupScript}: {ex.Message}", ex);
        }
    }

    private void BuildInstaller()
    {
        string setupScript = Path.Combine(outputPath, "installer", "setup.iss");
        string installerOutput = Path.Combine(outputPath, "installer", "Output");
        Directory.CreateDirectory(installerOutput);

        // Try to find ISCC.exe in standard Inno Setup locations
        string[] isccPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 6", "ISCC.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inno Setup 6", "ISCC.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 5", "ISCC.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inno Setup 5", "ISCC.exe")
        };

        string? isccExe = null;
        foreach (var path in isccPaths)
        {
            if (File.Exists(path))
            {
                isccExe = path;
                break;
            }
        }

        if (isccExe == null)
        {
            throw new Exception($"Inno Setup (ISCC.exe) not found. Installer script generated at: {setupScript}\n\nPlease install Inno Setup 5 or 6 from: https://jrsoftware.org/iss.php");
        }

        var psi = new ProcessStartInfo
        {
            FileName = isccExe,
            Arguments = $@"""{setupScript}""",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var process = Process.Start(psi))
        {
            if (process == null)
                throw new Exception("Failed to start ISCC.exe");

            try
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new Exception($"Installer build failed: {error}\n\nSetup script: {setupScript}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}

// Helper class to pass data to background worker
internal class BuildData
{
    public string? AppName { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? Version { get; set; }
}