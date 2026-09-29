package br.com.romildo.baixadorlives;

import android.content.ContentValues;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.os.Environment;
import android.provider.MediaStore;
import android.view.WindowManager;
import android.widget.*;
import androidx.appcompat.app.AppCompatActivity;
import com.yausername.youtubedl_android.YoutubeDL;
import com.yausername.youtubedl_android.YoutubeDLRequest;
import com.yausername.youtubedl_android.mapper.VideoInfo;
import java.io.*;
import java.util.*;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class MainActivity extends AppCompatActivity {
    private static final String PROCESS_ID = "romildo-download-principal";
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private EditText url, folder, playlistItems;
    private Spinner format, videoQuality, audioQuality;
    private TextView status, destination, history, activeCount;
    private ProgressBar progress;
    private volatile boolean downloading, paused, finalizing;
    private File staging;
    private String currentTitle = "Download do YouTube";

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        setContentView(R.layout.activity_main);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        bindViews();
        configureSpinners();
        receiveSharedLink(getIntent());
        findViewById(R.id.download).setOnClickListener(v -> startDownload(false));
        findViewById(R.id.pause).setOnClickListener(v -> pauseDownload());
        findViewById(R.id.resume).setOnClickListener(v -> startDownload(true));
        findViewById(R.id.finalize).setOnClickListener(v -> finalizeReceived());
        findViewById(R.id.clear).setOnClickListener(v -> history.setText("Nenhum download nesta sessão."));
        destination.setText("Destino: Downloads/" + folder.getText());
        folder.setOnFocusChangeListener((v, hasFocus) -> {
            if (!hasFocus) destination.setText("Destino: Downloads/" + safe(folder.getText().toString(), "Baixador Lives"));
        });
    }

    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent); receiveSharedLink(intent);
    }

    private void bindViews() {
        url = findViewById(R.id.url); folder = findViewById(R.id.folder);
        playlistItems = findViewById(R.id.playlist_items); format = findViewById(R.id.format);
        videoQuality = findViewById(R.id.video_quality); audioQuality = findViewById(R.id.audio_quality);
        status = findViewById(R.id.status); destination = findViewById(R.id.destination);
        history = findViewById(R.id.history); activeCount = findViewById(R.id.active_count);
        progress = findViewById(R.id.progress);
    }

    private void configureSpinners() {
        setItems(format, new String[]{"Vídeo", "MP3"});
        setItems(videoQuality, new String[]{"Melhor disponível", "2160p", "1440p", "1080p", "720p", "480p"});
        setItems(audioQuality, new String[]{"320 kbps", "256 kbps", "192 kbps", "128 kbps"});
    }

    private void setItems(Spinner spinner, String[] values) {
        ArrayAdapter<String> adapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, values);
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item); spinner.setAdapter(adapter);
    }

    private void receiveSharedLink(Intent intent) {
        if (Intent.ACTION_SEND.equals(intent.getAction())) {
            String shared = intent.getStringExtra(Intent.EXTRA_TEXT);
            if (shared != null) {
                java.util.regex.Matcher match = java.util.regex.Pattern.compile("https?://\\S+").matcher(shared);
                if (match.find()) url.setText(match.group());
            }
        }
    }

    private void startDownload(boolean continuing) {
        if (downloading) { toast("Já existe um download ativo."); return; }
        String link = url.getText().toString().trim();
        if (!link.startsWith("https://")) { url.setError("Cole um link válido do YouTube."); return; }
        paused = false; finalizing = false; downloading = true; updateState("Preparando…", 0);
        executor.execute(() -> {
            try {
                if (!continuing || staging == null) {
                    VideoInfo info = YoutubeDL.getInstance().getInfo(link);
                    currentTitle = safe(info.getTitle(), "Download do YouTube");
                    staging = new File(getExternalFilesDir("downloads"), String.valueOf(System.currentTimeMillis()));
                    if (!staging.mkdirs() && !staging.isDirectory()) throw new IOException("Não foi possível preparar a pasta temporária.");
                }
                YoutubeDLRequest request = buildRequest(link);
                YoutubeDL.getInstance().execute(request, PROCESS_ID, (value, eta, line) -> {
                    runOnUiThread(() -> updateState(translate(line, eta), Math.max(0, Math.min(100, value.intValue()))));
                    return kotlin.Unit.INSTANCE;
                });
                if (!paused && !finalizing) {
                    publishFiles(false);
                    runOnUiThread(() -> { updateState("Concluído", 100); addHistory("Concluído: " + currentTitle); });
                }
            } catch (Exception error) {
                if (!paused && !finalizing) runOnUiThread(() -> {
                    updateState("Erro: " + friendly(error.getMessage()), progress.getProgress());
                    addHistory("Erro: " + currentTitle);
                });
            } finally {
                downloading = false;
                runOnUiThread(() -> activeCount.setText("0 ativos"));
            }
        });
    }

    private YoutubeDLRequest buildRequest(String link) {
        YoutubeDLRequest request = new YoutubeDLRequest(link);
        request.addOption("--newline"); request.addOption("--continue"); request.addOption("--part");
        request.addOption("--windows-filenames"); request.addOption("--live-from-start");
        request.addOption("--hls-use-mpegts"); request.addOption("--retries", "infinite");
        request.addOption("--fragment-retries", "infinite"); request.addOption("--concurrent-fragments", "6");
        request.addOption("--socket-timeout", "30"); request.addOption("--no-write-info-json");
        String selected = playlistItems.getText().toString().trim();
        if (!selected.isEmpty()) request.addOption("--playlist-items", selected);
        boolean mp3 = format.getSelectedItemPosition() == 1;
        if (mp3) {
            String bitrate = audioQuality.getSelectedItem().toString().split(" ")[0];
            request.addOption("--extract-audio"); request.addOption("--audio-format", "mp3");
            request.addOption("--audio-quality", bitrate + "K");
        } else {
            String q = videoQuality.getSelectedItem().toString();
            String selector = q.startsWith("Melhor") ? "bv*+ba/b" :
                "bv*[height<=" + q.replaceAll("[^0-9]", "") + "]+ba/b[height<=" + q.replaceAll("[^0-9]", "") + "]/b";
            request.addOption("--format", selector); request.addOption("--merge-output-format", "mp4");
        }
        request.addOption("--output", new File(staging, "%(playlist_index&{} - |)s%(title).80B [%(id)s].%(ext)s").getAbsolutePath());
        return request;
    }

    private void pauseDownload() {
        if (!downloading) { toast("Nenhum download ativo."); return; }
        paused = true;
        try { YoutubeDL.getInstance().destroyProcessById(PROCESS_ID); } catch (Exception ignored) { }
        updateState("Pausado. Toque em Continuar.", progress.getProgress()); addHistory("Pausado: " + currentTitle);
    }

    private void finalizeReceived() {
        if (staging == null || !staging.exists()) { toast("Ainda não existem fragmentos para salvar."); return; }
        if (format.getSelectedItemPosition() == 1) { toast("Para MP3, use Pausar e depois Continuar até a conversão terminar."); return; }
        finalizing = true;
        try { YoutubeDL.getInstance().destroyProcessById(PROCESS_ID); } catch (Exception ignored) { }
        executor.execute(() -> {
            try {
                publishFiles(true);
                runOnUiThread(() -> { updateState("Parte recebida salva", progress.getProgress()); addHistory("Finalizado até aqui: " + currentTitle); });
            } catch (Exception e) {
                runOnUiThread(() -> updateState("Não foi possível salvar a parte recebida: " + friendly(e.getMessage()), progress.getProgress()));
            } finally { downloading = false; finalizing = false; }
        });
    }

    private void publishFiles(boolean partialOnly) throws IOException {
        List<File> files = new ArrayList<>(); collect(staging, files);
        if (partialOnly) {
            files.removeIf(f -> f.length() < 64 * 1024 || (!f.getName().contains(".part") && !f.getName().endsWith(".ts")));
            files.sort((a,b) -> Long.compare(b.length(), a.length()));
            if (files.size() > 1) files = new ArrayList<>(files.subList(0, 1));
        } else files.removeIf(f -> f.getName().endsWith(".part") || f.length() == 0);
        if (files.isEmpty()) throw new IOException("nenhum arquivo utilizável foi encontrado");
        String base = safe(folder.getText().toString(), "Baixador Lives") + "/" + safe(currentTitle, "Download");
        for (File source : files) copyToDownloads(source, base, partialOnly);
    }

    private void copyToDownloads(File source, String relative, boolean partial) throws IOException {
        String original = source.getName();
        String name = original.replace(".part", "");
        if (partial && !name.matches(".*\\.(ts|mp4|webm|m4a)$")) name += ".ts";
        ContentValues values = new ContentValues();
        values.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
        values.put(MediaStore.MediaColumns.MIME_TYPE, mime(name));
        values.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/" + relative);
        Uri uri = getContentResolver().insert(MediaStore.Files.getContentUri("external"), values);
        if (uri == null) throw new IOException("o Android recusou criar o arquivo");
        try (InputStream in = new FileInputStream(source); OutputStream out = getContentResolver().openOutputStream(uri)) {
            if (out == null) throw new IOException("não foi possível abrir o destino");
            byte[] buffer = new byte[1024 * 1024]; int read;
            while ((read = in.read(buffer)) > 0) out.write(buffer, 0, read);
        }
    }

    private void collect(File dir, List<File> out) {
        File[] entries = dir == null ? null : dir.listFiles(); if (entries == null) return;
        for (File entry : entries) { if (entry.isDirectory()) collect(entry, out); else out.add(entry); }
    }

    private void updateState(String text, int value) {
        status.setText(text); progress.setProgress(value); activeCount.setText(downloading ? "1 ativo" : "0 ativos");
    }
    private void addHistory(String text) { history.setText(text + "\n" + history.getText()); }
    private void toast(String text) { Toast.makeText(this, text, Toast.LENGTH_LONG).show(); }
    private String safe(String value, String fallback) {
        String clean = value == null ? "" : value.replaceAll("[<>:\"/\\\\|?*\\p{Cntrl}]", "_").trim();
        if (clean.isEmpty()) clean = fallback; return clean.length() > 60 ? clean.substring(0, 60).trim() : clean;
    }
    private String translate(String line, long eta) {
        if (line == null) return "Baixando…";
        if (line.contains("Read timed out") || line.contains("Retrying")) return "Conexão oscilou — tentando novamente…";
        return line.length() > 160 ? line.substring(0, 160) : line;
    }
    private String friendly(String value) { return value == null ? "falha desconhecida" : value.replace("ERROR:", "").trim(); }
    private String mime(String name) {
        String n = name.toLowerCase(Locale.ROOT);
        if (n.endsWith(".mp3")) return "audio/mpeg"; if (n.endsWith(".mp4")) return "video/mp4";
        if (n.endsWith(".webm")) return "video/webm"; if (n.endsWith(".m4a")) return "audio/mp4"; return "video/mp2t";
    }

    @Override protected void onDestroy() { super.onDestroy(); }
}
