using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BaixadorLivesWindows;

public sealed class MainForm : Form
{
    private readonly TextBox urlBox = new() { PlaceholderText = "Cole o link do vídeo, live ou playlist do YouTube" };
    private readonly ComboBox videoQuality = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox audioQuality = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox folderBox = new() { ReadOnly = true };
    private readonly CheckedListBox playlist = new() { CheckOnClick = true, IntegralHeight = false };
    private readonly DataGridView grid = new() { ReadOnly = true, AllowUserToAddRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false };
    private readonly Label counter = new() { AutoSize = true, Text = "0 ativos • 0 na lista" };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly ConcurrentDictionary<string, DownloadJob> jobs = new();
    private readonly SemaphoreSlim slots = new(2, 2);
    private readonly string settingsPath;
    private string outputFolder;

    private string YtDlp => Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe");
    private string Ffmpeg => Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");

    public MainForm()
    {
        Text = "Baixador de Vídeos e Lives — Windows";
        Width = 1050; Height = 760; MinimumSize = new Size(900, 650); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Romildo", "BaixadorLivesWindows", "config.txt");
        outputFolder = LoadFolder();
        BuildUi();
        FormClosing += (_, _) => { foreach (var j in jobs.Values) TryKill(j); };
    }

    private void BuildUi()
    {
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 178, Padding = new Padding(12), ColumnCount = 6, RowCount = 4 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 1; i < 6; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); top.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); top.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        urlBox.Dock = DockStyle.Fill; top.Controls.Add(urlBox, 0, 0); top.SetColumnSpan(urlBox, 6);

        videoQuality.Items.AddRange(new object[] { "Melhor", "480p", "720p", "1080p", "1440p", "2160p" }); videoQuality.SelectedIndex = 0;
        audioQuality.Items.AddRange(new object[] { "128 kbps", "192 kbps", "256 kbps", "320 kbps" }); audioQuality.SelectedIndex = 1;
        top.Controls.Add(new Label { Text = "Vídeo:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1); top.Controls.Add(videoQuality, 1, 1);
        top.Controls.Add(new Label { Text = "MP3:", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 1); top.Controls.Add(audioQuality, 3, 1);
        top.Controls.Add(Button("Carregar playlist", async (_, _) => await LoadPlaylist()), 4, 1);
        top.Controls.Add(Button("Limpar seleção", (_, _) => { playlist.Items.Clear(); status.Text = "Seleção limpa."; }), 5, 1);

        folderBox.Text = outputFolder; folderBox.Dock = DockStyle.Fill; top.Controls.Add(folderBox, 0, 2); top.SetColumnSpan(folderBox, 4);
        top.Controls.Add(Button("Escolher pasta", (_, _) => ChooseFolder()), 4, 2);
        top.Controls.Add(Button("Abrir pasta", (_, _) => Process.Start(new ProcessStartInfo(outputFolder) { UseShellExecute = true })), 5, 2);
        top.Controls.Add(Button("Baixar vídeo", (_, _) => Queue(false)), 0, 3);
        top.Controls.Add(Button("Baixar MP3", (_, _) => Queue(true)), 1, 3);
        top.Controls.Add(Button("Selecionar todos", (_, _) => { for (int i = 0; i < playlist.Items.Count; i++) playlist.SetItemChecked(i, true); }), 2, 3);
        top.Controls.Add(Button("Desmarcar todos", (_, _) => { for (int i = 0; i < playlist.Items.Count; i++) playlist.SetItemChecked(i, false); }), 3, 3);
        top.Controls.Add(counter, 4, 3); top.Controls.Add(status, 5, 3);

        playlist.Dock = DockStyle.Top; playlist.Height = 145;
        grid.Dock = DockStyle.Fill; grid.RowHeadersVisible = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título", DataPropertyName = "Title", FillWeight = 35 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo", DataPropertyName = "Kind", FillWeight = 9 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Andamento", DataPropertyName = "Progress", FillWeight = 13 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado", DataPropertyName = "State", FillWeight = 22 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pasta", DataPropertyName = "Folder", FillWeight = 21 });

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(8) };
        actions.Controls.Add(Button("Pausar", (_, _) => PauseSelected())); actions.Controls.Add(Button("Continuar", (_, _) => ResumeSelected()));
        actions.Controls.Add(Button("Finalizar agora", async (_, _) => await FinalizeSelected())); actions.Controls.Add(Button("Interromper", (_, _) => StopSelected()));
        actions.Controls.Add(Button("Limpar concluídos", (_, _) => ClearRows(false))); actions.Controls.Add(Button("Limpar tudo", (_, _) => ClearRows(true)));
        Controls.Add(grid); Controls.Add(actions); Controls.Add(playlist); Controls.Add(top);
    }

    private static Button Button(string text, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(4) }; b.Click += click; return b;
    }

    private string LoadFolder()
    {
        try { if (File.Exists(settingsPath)) { var f = File.ReadAllText(settingsPath); if (Directory.Exists(f)) return f; } } catch { }
        var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Baixador de Vídeos e Lives");
        Directory.CreateDirectory(d); return d;
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { InitialDirectory = outputFolder, Description = "Escolha onde salvar os downloads" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        outputFolder = dialog.SelectedPath; folderBox.Text = outputFolder;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!); File.WriteAllText(settingsPath, outputFolder);
    }

    private async Task LoadPlaylist()
    {
        var url = urlBox.Text.Trim(); if (!ValidYouTube(url)) { MessageBox.Show("Cole um link válido do YouTube."); return; }
        status.Text = "Carregando playlist…"; playlist.Items.Clear();
        try
        {
            var json = await RunCapture(YtDlp, $"--flat-playlist --dump-single-json --playlist-end 500 {Q(url)}");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("entries", out var entries)) throw new Exception("O link não contém uma playlist.");
            foreach (var item in entries.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
                var title = item.TryGetProperty("title", out var t) ? t.GetString() : id;
                var web = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id)) playlist.Items.Add(new PlaylistItem(title ?? id!, web?.StartsWith("http") == true ? web : "https://www.youtube.com/watch?v=" + id));
            }
            status.Text = $"{playlist.Items.Count} vídeos carregados.";
        }
        catch (Exception ex) { status.Text = "Falha ao carregar."; MessageBox.Show(ex.Message, "Erro na playlist"); }
    }

    private void Queue(bool mp3)
    {
        var selected = playlist.CheckedItems.Cast<PlaylistItem>().ToList();
        if (selected.Count == 0)
        {
            var url = urlBox.Text.Trim(); if (!ValidYouTube(url)) { MessageBox.Show("Cole um link válido do YouTube."); return; }
            selected.Add(new PlaylistItem("Obtendo título…", url));
        }
        foreach (var item in selected)
        {
            var job = new DownloadJob { Id = Guid.NewGuid().ToString("N"), Title = item.Title, Url = item.Url, IsMp3 = mp3, Kind = mp3 ? "MP3" : "Vídeo", State = "Na fila", Progress = "0%" };
            jobs[job.Id] = job; _ = RunJob(job); AddOrRefresh(job);
        }
        UpdateCounter();
    }

    private async Task RunJob(DownloadJob job)
    {
        await slots.WaitAsync();
        try
        {
            if (job.State == "Interrompido") return;
            job.State = "Preparando"; AddOrRefresh(job);
            if (job.Title == "Obtendo título…") job.Title = await GetTitle(job.Url);
            job.Folder = UniqueFolder(SafeName(job.Title)); Directory.CreateDirectory(job.Folder);
            var format = job.IsMp3 ? "bestaudio/best" : VideoFormat();
            var args = new StringBuilder($"--newline --progress --continue --no-overwrites --live-from-start --hls-use-mpegts --concurrent-fragments 8 --retries infinite --fragment-retries infinite -f {Q(format)} ");
            args.Append($"-o {Q(Path.Combine(job.Folder, "%(title)s.%(ext)s"))} ");
            if (job.IsMp3) args.Append($"-x --audio-format mp3 --audio-quality {AudioRate()}K ");
            else args.Append("--merge-output-format mp4 ");
            args.Append(Q(job.Url));
            job.State = "Baixando"; AddOrRefresh(job);
            await RunDownload(job, args.ToString());
            if (job.State is "Pausado" or "Interrompido" or "Finalizando") return;
            job.Progress = "100%"; job.State = "Concluído";
        }
        catch (Exception ex) { if (job.State != "Pausado" && job.State != "Interrompido") job.State = "Erro: " + Short(ex.Message); }
        finally { slots.Release(); AddOrRefresh(job); UpdateCounter(); }
    }

    private async Task RunDownload(DownloadJob job, string args)
    {
        var psi = new ProcessStartInfo(YtDlp, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true }; job.Process = p; p.Start();
        async Task Read(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                var m = Regex.Match(line, @"(?<p>\d+(?:[.,]\d+)?)%");
                if (m.Success) { job.Progress = m.Groups["p"].Value.Replace(',', '.') + "%"; job.State = "Baixando"; AddOrRefresh(job); }
            }
        }
        await Task.WhenAll(Read(p.StandardOutput), Read(p.StandardError), p.WaitForExitAsync());
        job.Process = null;
        if (p.ExitCode != 0 && job.State is not ("Pausado" or "Interrompido" or "Finalizando")) throw new Exception("O download não foi concluído. Verifique o link ou a conexão.");
    }

    private string VideoFormat()
    {
        if (videoQuality.SelectedIndex <= 0) return "bestvideo+bestaudio/best";
        var h = Regex.Match(videoQuality.Text, @"\d+").Value;
        return $"bestvideo[height<={h}]+bestaudio/best[height<={h}]";
    }
    private string AudioRate() => Regex.Match(audioQuality.Text, @"\d+").Value;

    private void PauseSelected() { var j = Selected(); if (j == null) return; j.State = "Pausado"; TryKill(j); AddOrRefresh(j); UpdateCounter(); }
    private void ResumeSelected() { var j = Selected(); if (j == null || j.State != "Pausado") return; j.State = "Na fila"; _ = RunJob(j); AddOrRefresh(j); }
    private void StopSelected() { var j = Selected(); if (j == null) return; j.State = "Interrompido"; TryKill(j); AddOrRefresh(j); UpdateCounter(); }

    private async Task FinalizeSelected()
    {
        var j = Selected(); if (j == null) return; j.State = "Finalizando"; TryKill(j); AddOrRefresh(j);
        await Task.Delay(400);
        try
        {
            var partial = Directory.Exists(j.Folder) ? Directory.GetFiles(j.Folder).Where(f => f.EndsWith(".part") || f.EndsWith(".mp4") || f.EndsWith(".ts") || f.EndsWith(".webm")).OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault() : null;
            if (partial == null) throw new Exception("Ainda não havia dados suficientes para gerar o arquivo.");
            var ext = j.IsMp3 ? ".mp3" : ".mp4"; var output = Path.Combine(j.Folder, SafeName(j.Title) + " - parcial" + ext);
            var args = j.IsMp3 ? $"-y -i {Q(partial)} -vn -b:a {AudioRate()}k {Q(output)}" : $"-y -i {Q(partial)} -c copy -movflags +faststart {Q(output)}";
            await RunCapture(Ffmpeg, args); j.State = "Finalizado parcialmente";
        }
        catch (Exception ex) { j.State = "Erro ao finalizar: " + Short(ex.Message); }
        AddOrRefresh(j); UpdateCounter();
    }

    private void ClearRows(bool all)
    {
        foreach (var j in jobs.Values.ToArray())
        {
            bool active = j.State is "Baixando" or "Preparando" or "Na fila" or "Finalizando";
            if (!active && (all || j.State.StartsWith("Concluído") || j.State.StartsWith("Finalizado"))) jobs.TryRemove(j.Id, out _);
        }
        RefreshGrid(); UpdateCounter();
    }

    private DownloadJob? Selected()
    {
        if (grid.CurrentRow?.Tag is string id && jobs.TryGetValue(id, out var j)) return j;
        MessageBox.Show("Selecione uma tarefa na lista."); return null;
    }

    private static void TryKill(DownloadJob j) { try { if (j.Process is { HasExited: false }) j.Process.Kill(true); } catch { } }
    private async Task<string> GetTitle(string url) { var t = (await RunCapture(YtDlp, $"--no-playlist --print title --skip-download {Q(url)}")).Trim(); return string.IsNullOrWhiteSpace(t) ? "Vídeo do YouTube" : t.Split('\n')[0].Trim(); }
    private static bool ValidYouTube(string value) => Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme == "https" && (u.Host.EndsWith("youtube.com") || u.Host == "youtu.be");
    private static string Q(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
    private static string Short(string s) => s.Length > 100 ? s[..100] : s;
    private static string SafeName(string s) { var x = Regex.Replace(s, "[<>:\"/\\\\|?*\\x00-\\x1F]", "_").Trim().TrimEnd('.'); return string.IsNullOrWhiteSpace(x) ? "Vídeo do YouTube" : (x.Length > 100 ? x[..100] : x); }
    private string UniqueFolder(string title) { var p = Path.Combine(outputFolder, title); if (!Directory.Exists(p)) return p; return p + " - " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"); }

    private static async Task<string> RunCapture(string file, string args)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("Componente não encontrado: " + Path.GetFileName(file));
        var p = new Process { StartInfo = new ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
        p.Start(); var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new Exception(Short(await stderr)); return await stdout;
    }

    private void AddOrRefresh(DownloadJob _) { if (InvokeRequired) { BeginInvoke(RefreshGrid); return; } RefreshGrid(); }
    private void RefreshGrid()
    {
        var selected = grid.CurrentRow?.Tag as string; grid.Rows.Clear();
        foreach (var j in jobs.Values.OrderByDescending(x => x.Created)) { int i = grid.Rows.Add(j.Title, j.Kind, j.Progress, j.State, j.Folder); grid.Rows[i].Tag = j.Id; if (j.Id == selected) grid.Rows[i].Selected = true; }
    }
    private void UpdateCounter()
    {
        if (InvokeRequired) { BeginInvoke(UpdateCounter); return; }
        var active = jobs.Values.Count(j => j.State is "Baixando" or "Preparando" or "Finalizando"); counter.Text = $"{active} ativos • {jobs.Count} na lista";
    }
}

public sealed class DownloadJob
{
    public string Id { get; init; } = ""; public string Title { get; set; } = ""; public string Url { get; init; } = "";
    public bool IsMp3 { get; init; } public string Kind { get; init; } = ""; public string State { get; set; } = "";
    public string Progress { get; set; } = ""; public string Folder { get; set; } = ""; public Process? Process { get; set; }
    public DateTime Created { get; } = DateTime.Now;
}

public sealed record PlaylistItem(string Title, string Url)
{
    public override string ToString() => Title;
}
