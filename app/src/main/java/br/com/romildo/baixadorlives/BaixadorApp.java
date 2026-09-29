package br.com.romildo.baixadorlives;

import android.app.Application;
import android.util.Log;
import com.yausername.youtubedl_android.YoutubeDL;
import com.yausername.youtubedl_android.YoutubeDLException;
import com.yausername.youtubedl_android.ffmpeg.FFmpeg;
import com.yausername.youtubedl_android.aria2c.Aria2c;

public class BaixadorApp extends Application {
    @Override public void onCreate() {
        super.onCreate();
        try {
            YoutubeDL.getInstance().init(this);
            FFmpeg.getInstance().init(this);
            Aria2c.getInstance().init(this);
        } catch (YoutubeDLException error) {
            Log.e("BaixadorLives", "Falha ao preparar os componentes", error);
        }
    }
}
