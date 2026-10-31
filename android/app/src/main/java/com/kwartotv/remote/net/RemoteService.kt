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
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import com.kwartotv.remote.Cmd
import com.kwartotv.remote.MainActivity
import com.kwartotv.remote.R
import com.kwartotv.remote.Store
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

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private val client get() = RemoteHub.client

    override fun onBind(intent: Intent?): IBinder? = null

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
        // Started again by Android after the app was closed: connect to the saved TV by itself.
        client.foreground = true
        if (!client.isConnected()) {
            val store = Store(this)
            val tvs = store.load()
            (tvs.firstOrNull { it.deviceId == store.selectedId } ?: tvs.firstOrNull())?.let { client.ensureConnected(it) }
        }
        scope.launch {
            combine(client.conn, client.now) { c, n -> c to n }.collect { (c, n) ->
                try { nm.notify(ID, build(c, n)) } catch (_: SecurityException) { }
            }
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                client.foreground = false
                client.disconnect()
                ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
                stopSelf()
                return START_NOT_STICKY
            }
            ACTION_CMD -> intent.getStringExtra(EXTRA_CMD)?.let { if (!client.send(it)) client.reconnect() }
        }
        val type = if (Build.VERSION.SDK_INT >= 29) ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE else 0
        try { ServiceCompat.startForeground(this, ID, build(client.conn.value, client.now.value), type) } catch (_: Exception) { stopSelf() }
        return START_STICKY
    }

    override fun onDestroy() {
        running = false
        scope.cancel()
        super.onDestroy()
    }

    private fun command(cmd: String, code: Int): PendingIntent = PendingIntent.getService(
        this, code, Intent(this, RemoteService::class.java).setAction(ACTION_CMD).putExtra(EXTRA_CMD, cmd),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private fun build(conn: Conn, now: Now): Notification {
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP), PendingIntent.FLAG_IMMUTABLE)
        val stop = PendingIntent.getService(this, 9, Intent(this, RemoteService::class.java).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE)
        val text = when (conn) {
            Conn.Connected -> if (now.number > 0) "Connected · ${now.number}  ${now.name.orEmpty()}" else "Connected to the TV"
            Conn.Connecting, Conn.Retrying -> "Reconnecting to the TV…"
            is Conn.Rejected -> conn.reason
            Conn.Idle -> "Not connected. Tap to open the remote."
        }
        return NotificationCompat.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.ic_stat_remote)
            .setContentTitle("KwartoTV Remote")
            .setContentText(text)
            .setContentIntent(open)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .addAction(0, "⏯ Play/Pause", command(Cmd.PLAY_PAUSE, 1))
            .addAction(0, "Vol −", command(Cmd.VOL_DOWN, 2))
            .addAction(0, "Vol +", command(Cmd.VOL_UP, 3))
            .addAction(0, "Disconnect", stop)
            .build()
    }
}
