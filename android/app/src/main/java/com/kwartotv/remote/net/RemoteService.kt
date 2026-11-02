package com.kwartotv.remote.net

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.drawable.Icon
import android.media.MediaMetadata
import android.media.session.MediaSession
import android.media.session.PlaybackState
import android.os.Bundle
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import com.kwartotv.remote.Cmd
import com.kwartotv.remote.MainActivity
import com.kwartotv.remote.R
import com.kwartotv.remote.NowShowing
import com.kwartotv.remote.ShowMode
import com.kwartotv.remote.Store
import com.kwartotv.remote.nowShowing
import com.kwartotv.remote.splitButton
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch

/** The one connection to the TV, shared by the screen and the background service. */
object RemoteHub {
    val client: RemoteClient by lazy { RemoteClient() }
}

/**
 * Keeps the remote connected to the TV while the app is in the background or the phone is idle, so it is ready the
 * moment the app is opened again (no "Reconnecting…"). Shows a quiet notification with play/pause and volume, and a
 * Disconnect button that ends it.
 */
class RemoteService : Service() {
    companion object {
        private const val CHANNEL = "remote"
        private const val ID = 7
        private const val TYPE_ID = 8
        private const val TYPE_CHANNEL = "type"
        private const val ACTION_STOP = "com.kwartotv.remote.STOP"
        private const val ACTION_CMD = "com.kwartotv.remote.CMD"
        private const val EXTRA_CMD = "cmd"

        @Volatile var running = false
            private set

        fun start(context: Context) {
            try { ContextCompat.startForegroundService(context, Intent(context, RemoteService::class.java)) } catch (_: Exception) { }
        }

        fun stop(context: Context) {
            try { context.startService(Intent(context, RemoteService::class.java).setAction(ACTION_STOP)) } catch (_: Exception) { }
        }
    }

    private val artHttp by lazy { OkHttpClient() }
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private val client get() = RemoteHub.client

    override fun onBind(intent: Intent?): IBinder? = null

    private var session: MediaSession? = null
    private var shown: NowShowing? = null
    private var art: Pair<String, Bitmap?>? = null   // the song's picture (address → picture), loaded once per song

    override fun onCreate() {
        super.onCreate()
        running = true
        val nm = getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= 26) {
            nm.createNotificationChannel(NotificationChannel(CHANNEL, "Remote connection", NotificationManager.IMPORTANCE_LOW).apply {
                description = "Keeps the remote connected to the TV"
                setShowBadge(false)
            })
        }
        // The phone's media controls (notification, lock screen, quick settings) for what plays on the TV.
        session = MediaSession(this, "KwartoTV").apply {
            setCallback(object : MediaSession.Callback() {
                override fun onPlay() = press(1)
                override fun onPause() = press(1)
                override fun onSkipToPrevious() = press(0)
                override fun onSkipToNext() = press(2)
                override fun onRewind() = press(0)
                override fun onFastForward() = press(2)
                override fun onCustomAction(action: String, extras: Bundle?) = sendCmd(action)
            })
        }
        // Started again by Android after the app was closed: connect to the saved TV by itself.
        client.foreground = true
        if (!client.isConnected()) {
            val store = Store(this)
            val tvs = store.load()
            (tvs.firstOrNull { it.deviceId == store.selectedId } ?: tvs.firstOrNull())?.let { client.ensureConnected(it) }
        }
        // The TV waits for text while the remote app is not on screen: a notification that opens the type box.
        if (Build.VERSION.SDK_INT >= 26) {
            nm.createNotificationChannel(NotificationChannel(TYPE_CHANNEL, "TV wants text", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "When a box on the TV waits for text (search, sign-in)"
            })
        }
        scope.launch {
            combine(client.textRequest, client.appVisible) { r, visible -> if (visible) null else r }.collect { r ->
                if (r == null) { nm.cancel(TYPE_ID); return@collect }
                val n = Notification.Builder(this@RemoteService, TYPE_CHANNEL)
                    .setSmallIcon(R.drawable.ic_stat_remote)
                    .setContentTitle(r.title ?: "The TV is waiting for text")
                    .setContentText("Tap to type it on the phone")
                    .setContentIntent(openApp())
                    .setAutoCancel(true)
                    .setCategory(Notification.CATEGORY_MESSAGE)
                    .build()
                try { nm.notify(TYPE_ID, n) } catch (_: SecurityException) { }
            }
        }
        scope.launch {
            combine(client.conn, client.now, client.music, client.tvApp, client.netflixNow) { c, n, m, app, nf -> showing(c, n, m, app, nf) }.collect { s ->
                shown = s
                update(s)
                try { nm.notify(ID, build(s)) } catch (_: SecurityException) { }
                loadArt(s, nm)
            }
        }
    }

    private fun showing(c: Conn, n: Now, m: com.kwartotv.remote.MusicState, app: String, nf: com.kwartotv.remote.NetflixPlaying? = null): NowShowing {
        val status = when (c) {
            Conn.Connected -> if (n.number > 0) "Connected · ${n.number}  ${n.name.orEmpty()}" else "Connected to the TV"
            Conn.Connecting, Conn.Retrying -> "Reconnecting to the TV…"
            is Conn.Rejected -> c.reason
            Conn.Idle -> "Not connected. Tap to open the remote."
        }
        return nowShowing(c == Conn.Connected, status, n.number, n.name, m, app, nf)
    }

    /** One of the three main buttons of what is shown (0 left, 1 play/pause, 2 right). */
    private fun press(i: Int) { shown?.buttons?.getOrNull(i)?.let { sendCmd(it.cmd) } }

    private fun sendCmd(button: String) {
        val (cmd, arg) = splitButton(button)
        if (!client.send(cmd, arg)) client.reconnect()
    }

    /** The media session follows what is on the TV (off when nothing plays: no media player is shown then). */
    private fun update(s: NowShowing) {
        val ms = session ?: return
        if (s.mode == ShowMode.Plain) { ms.isActive = false; return }
        val md = MediaMetadata.Builder()
            .putString(MediaMetadata.METADATA_KEY_TITLE, s.title)
            .putString(MediaMetadata.METADATA_KEY_ARTIST, s.text)
        art?.let { (url, bmp) -> if (bmp != null && s.mode == ShowMode.Music && url == client.music.value.art) md.putBitmap(MediaMetadata.METADATA_KEY_ART, bmp) }
        ms.setMetadata(md.build())
        var actions = PlaybackState.ACTION_PLAY_PAUSE or PlaybackState.ACTION_PLAY or PlaybackState.ACTION_PAUSE
        val state = PlaybackState.Builder()
        when (s.mode) {
            ShowMode.Music, ShowMode.Live -> actions = actions or PlaybackState.ACTION_SKIP_TO_PREVIOUS or PlaybackState.ACTION_SKIP_TO_NEXT
            ShowMode.Video -> s.buttons.take(3).filterIndexed { i, _ -> i != 1 }.forEach { state.addCustomAction(it.cmd, it.label, iconFor(it.cmd)) }
            ShowMode.Plain -> {}
        }
        s.buttons.drop(3).forEach { state.addCustomAction(it.cmd, it.label, iconFor(it.cmd)) }
        state.setActions(actions).setState(if (s.playing) PlaybackState.STATE_PLAYING else PlaybackState.STATE_PAUSED, PlaybackState.PLAYBACK_POSITION_UNKNOWN, 1f)
        ms.setPlaybackState(state.build())
        ms.setSessionActivity(openApp())
        ms.isActive = true
    }

    private fun iconFor(cmd: String): Int = when {
        cmd == Cmd.VOL_UP -> R.drawable.ic_n_vol_up
        cmd == Cmd.VOL_DOWN -> R.drawable.ic_n_vol_down
        cmd.startsWith(Cmd.SEEK + ":-") -> R.drawable.ic_n_rewind
        cmd.startsWith(Cmd.SEEK) -> R.drawable.ic_n_forward
        cmd == Cmd.MUSIC_PREV || cmd == Cmd.CH_PREV -> R.drawable.ic_n_prev
        cmd == Cmd.MUSIC_NEXT || cmd == Cmd.CH_NEXT -> R.drawable.ic_n_next
        else -> if (shown?.playing == false) R.drawable.ic_n_play else R.drawable.ic_n_pause
    }

    /** The song's picture for the lock screen (fetched once per song, off the main thread). */
    private fun loadArt(s: NowShowing, nm: NotificationManager) {
        val url = client.music.value.art
        if (s.mode != ShowMode.Music || url.isNullOrBlank() || !url.startsWith("https://") || art?.first == url) return
        art = url to null
        scope.launch {
            val bmp = withContext(Dispatchers.IO) {
                try {
                    artHttp.newCall(Request.Builder().url(url).build()).execute().use { r ->
                        val bytes = r.body?.bytes()
                        if (!r.isSuccessful || bytes == null || bytes.size > 2_000_000) null else BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                    }
                } catch (_: Exception) { null }
            }
            if (art?.first != url || bmp == null) return@launch
            art = url to bmp
            shown?.let { cur -> update(cur); try { nm.notify(ID, build(cur)) } catch (_: SecurityException) { } }
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                client.foreground = false
                client.disconnect()
                session?.isActive = false
                ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
                stopSelf()
                return START_NOT_STICKY
            }
            ACTION_CMD -> intent.getStringExtra(EXTRA_CMD)?.let { sendCmd(it) }
        }
        val type = if (Build.VERSION.SDK_INT >= 29) ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE else 0
        val s = shown ?: showing(client.conn.value, client.now.value, client.music.value, client.tvApp.value, client.netflixNow.value)
        try { ServiceCompat.startForeground(this, ID, build(s), type) } catch (_: Exception) { stopSelf() }
        return START_STICKY
    }

    override fun onDestroy() {
        running = false
        scope.cancel()
        session?.release(); session = null
        super.onDestroy()
    }

    private fun command(cmd: String, code: Int): PendingIntent = PendingIntent.getService(
        this, code, Intent(this, RemoteService::class.java).setAction(ACTION_CMD).putExtra(EXTRA_CMD, cmd),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private fun openApp(): PendingIntent =
        PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP), PendingIntent.FLAG_IMMUTABLE)

    private fun build(s: NowShowing): Notification {
        val stop = PendingIntent.getService(this, 9, Intent(this, RemoteService::class.java).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE)
        val b = Notification.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.ic_stat_remote)
            .setContentTitle(if (s.mode == ShowMode.Music) "${if (s.playing) "♪" else "❚❚"} ${s.title}" else s.title)
            .setContentText(s.text)
            .setContentIntent(openApp())
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setVisibility(Notification.VISIBILITY_PUBLIC)
            .setCategory(Notification.CATEGORY_SERVICE)
        if (Build.VERSION.SDK_INT >= 31) b.setForegroundServiceBehavior(Notification.FOREGROUND_SERVICE_IMMEDIATE)
        s.buttons.forEachIndexed { i, btn ->
            b.addAction(Notification.Action.Builder(Icon.createWithResource(this, iconFor(btn.cmd)), btn.label, command(btn.cmd, 10 + i)).build())
        }
        if (s.mode == ShowMode.Plain) {
            b.addAction(Notification.Action.Builder(Icon.createWithResource(this, R.drawable.ic_n_close), "Disconnect", stop).build())
        } else {
            art?.second?.let { if (s.mode == ShowMode.Music) b.setLargeIcon(it) }
            b.setStyle(Notification.MediaStyle().setMediaSession(session?.sessionToken).setShowActionsInCompactView(0, 1, 2))
        }
        return b.build()
    }
}
