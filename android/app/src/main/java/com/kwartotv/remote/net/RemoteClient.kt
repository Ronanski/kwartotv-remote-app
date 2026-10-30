package com.kwartotv.remote.net

import android.os.Handler
import android.os.Looper
import com.kwartotv.remote.Cmd
import com.kwartotv.remote.Lineup
import com.kwartotv.remote.SavedTv
import com.kwartotv.remote.TvMessage
import com.kwartotv.remote.commandJson
import com.kwartotv.remote.parseTvMessage
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.util.concurrent.TimeUnit

sealed interface Conn {
    data object Idle : Conn
    data object Connecting : Conn
    data object Connected : Conn
    data object Retrying : Conn
    /** The TV refused this phone (removed, blocked or wrong token). Pair again. */
    data class Rejected(val reason: String) : Conn
}

data class Now(val number: Int, val name: String?)

/**
 * The WebSocket to one TV. Everything that changes the connection (socket, tv, generation, retry) runs on the main
 * thread: OkHttp and the port search post their results there.
 */
class RemoteClient {
    private val http = OkHttpClient.Builder()
        .pingInterval(10, TimeUnit.SECONDS)
        .connectTimeout(5, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .build()
    private val main = Handler(Looper.getMainLooper())

    private val _conn = MutableStateFlow<Conn>(Conn.Idle)
    val conn: StateFlow<Conn> = _conn
    private val _now = MutableStateFlow(Now(0, null))
    val now: StateFlow<Now> = _now
    private val _lastError = MutableStateFlow<String?>(null)
    /** Last "not done" message from the TV, e.g. "no such channel". Cleared by the UI after showing. */
    val lastError: StateFlow<String?> = _lastError
    private val _lineup = MutableStateFlow<Lineup?>(null)
    /** The TV's channel list, once asked for with [requestLineup]. Null until it arrives. */
    val lineup: StateFlow<Lineup?> = _lineup
    private val _lineupUnsupported = MutableStateFlow(false)
    /** The TV app is too old to send its channel list. */
    val lineupUnsupported: StateFlow<Boolean> = _lineupUnsupported

    /**
     * True while the app is on screen. In the background nothing reconnects (no retries, no watchdog, no move),
     * so the phone does not keep waking the radio.
     */
    @Volatile var foreground = false

    private val probeHttp = http.newBuilder().connectTimeout(1500, TimeUnit.MILLISECONDS).readTimeout(2, TimeUnit.SECONDS).build()

    /** Called (on the main thread) when the TV turned up on a different port; the caller saves the new port. */
    var onTvMoved: ((SavedTv) -> Unit)? = null

    private var lastRx = 0L

    /** Called (on the main thread) when the saved address does not answer: the VM looks for the TV again on the Wi-Fi (its IP may have changed). */
    var onTvUnreachable: ((SavedTv) -> Unit)? = null

    private var socket: WebSocket? = null
    private var tv: SavedTv? = null
    // Read from OkHttp threads to drop callbacks of an old socket early; written only on the main thread.
    @Volatile private var generation = 0
    private var retry = 0
    private var seq = 0
    private var lineupRequest: String? = null

    fun connect(target: SavedTv) {
        if (tv?.deviceId != target.deviceId) { _lineup.value = null; _lineupUnsupported.value = false }
        disconnect()
        tv = target
        val gen = ++generation
        retry = 0
        open(gen)
    }

    private fun open(gen: Int) {
        val target = tv ?: return
        if (gen != generation) return
        _conn.value = if (retry == 0) Conn.Connecting else Conn.Retrying
        val req = try { Request.Builder().url(target.wsUrl()).build() } catch (e: Exception) { _conn.value = Conn.Rejected("Bad TV address"); return }
        socket = http.newWebSocket(req, object : WebSocketListener() {
            // OkHttp calls these on its own threads: hop to the main thread, then check the generation again there.
            private fun onMain(block: () -> Unit) {
                if (gen != generation) return
                main.post { if (gen == generation) block() }
            }

            override fun onOpen(webSocket: WebSocket, response: Response) = onMain {
                lastRx = android.os.SystemClock.elapsedRealtime()
                retry = 0
                _conn.value = Conn.Connected
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                if (gen != generation) return
                // Parse here, off the main thread: a lineup can hold hundreds of channels.
                val m = parseTvMessage(text)
                onMain {
                    lastRx = android.os.SystemClock.elapsedRealtime()
                    when (m) {
                        is TvMessage.State -> _now.value = Now(m.number, m.channelName)
                        is TvMessage.Channels -> { _lineup.value = m.lineup; _lineupUnsupported.value = false }
                        is TvMessage.Ack -> when {
                            m.ok -> Unit
                            // An older TV app does not know the lineup request: say so on the list, not as an error toast.
                            m.id != null && m.id == lineupRequest -> _lineupUnsupported.value = true
                            else -> _lastError.value = m.error ?: "Not done"
                        }
                        TvMessage.Unknown -> Unit
                    }
                }
            }

            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                webSocket.close(1000, null)
                // onClosed or onFailure always follows and does the retry; retrying here too doubled every reconnect.
                if (code == 1008) onMain { _conn.value = Conn.Rejected("This phone was removed on the TV.") }
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) = onMain {
                if (code == 1008) _conn.value = Conn.Rejected("This phone was removed on the TV.") else scheduleRetry(gen)
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                val code = response?.code
                onMain { if (code == 401) _conn.value = Conn.Rejected("The TV does not accept this phone. Pair again.") else scheduleRetry(gen) }
            }
        })
    }

    private fun scheduleRetry(gen: Int) {
        if (gen != generation) return
        if (!foreground) { _conn.value = Conn.Idle; return }
        _conn.value = Conn.Retrying
        val delay = minOf(1000L shl minOf(retry, 3), 8000L)
        retry++
        if (retry == 3) {
            // The PC may have restarted on another port (Windows reserves port ranges) or got another IP address: look for it once.
            val target = tv
            if (target != null) Thread {
                val found = findPortBlocking(target.host, target.port)
                main.post {
                    if (gen != generation || !foreground) return@post
                    if (found != null && found != target.port) {
                        val moved = target.copy(port = found)
                        tv = moved
                        onTvMoved?.invoke(moved)
                    } else if (found == null) {
                        onTvUnreachable?.invoke(target)
                    }
                }
            }.start()
        }
        main.postDelayed({ open(gen) }, delay)
    }

    /** Finds the port KwartoTV answers on at [host] (tries [preferred] first), or null. Blocking; call off the main thread. */
    fun findPortBlocking(host: String, preferred: Int? = null): Int? {
        val ports = (listOfNotNull(preferred) + com.kwartotv.remote.CANDIDATE_PORTS).distinct()
        for (p in ports) {
            try {
                val req = Request.Builder().url("http://${com.kwartotv.remote.hostForUrl(host)}:$p/info").build()
                probeHttp.newCall(req).execute().use { r ->
                    if (r.isSuccessful && JSONObject(r.body?.string() ?: "{}").optString("app") == "KwartoTV") return p
                }
            } catch (_: Exception) { }
        }
        return null
    }

    suspend fun findPort(host: String, preferred: Int? = null): Int? = withContext(Dispatchers.IO) { findPortBlocking(host, preferred) }

    /** The TV's name from GET /info, or null when no KwartoTV answers there. */
    suspend fun infoName(host: String, port: Int): String? = withContext(Dispatchers.IO) {
        try {
            val req = Request.Builder().url("http://${com.kwartotv.remote.hostForUrl(host)}:$port/info").build()
            probeHttp.newCall(req).execute().use { r ->
                val o = JSONObject(r.body?.string() ?: "{}")
                if (r.isSuccessful && o.optString("app") == "KwartoTV") o.optString("name").ifBlank { null } else null
            }
        } catch (_: Exception) { null }
    }

    fun send(cmd: String, arg: String? = null): Boolean = sendWithId(cmd, arg) != null

    /** Sends a command; returns its id, or null when not connected. Call on the main thread. */
    private fun sendWithId(cmd: String, arg: String?): String? {
        val s = socket ?: return null
        if (_conn.value != Conn.Connected) return null
        val id = (++seq).toString()
        if (!s.send(commandJson(id, cmd, arg))) return null
        armWatchdog()
        return id
    }

    /** Asks the TV for its channel list (the answer arrives in [lineup]). False when not connected. */
    fun requestLineup(): Boolean {
        val id = sendWithId(Cmd.LINEUP, null) ?: return false
        lineupRequest = id
        return true
    }

    /**
     * A socket can look open after the phone slept or the Wi-Fi changed, while nothing gets through.
     * If the TV does not answer a command within a few seconds, connect again.
     */
    private fun armWatchdog() {
        val gen = generation
        val sentAt = android.os.SystemClock.elapsedRealtime()
        main.postDelayed({
            if (gen == generation && foreground && lastRx < sentAt && _conn.value == Conn.Connected) tv?.let { connect(it) }
        }, 3500)
    }

    /** Drops the connection and connects again now (screen on, network back, user pressed Reconnect). */
    fun reconnect() { tv?.let { connect(it) } }

    fun isConnected() = _conn.value == Conn.Connected

    /** The TV was found at a new address. Ignored while the app is in the background. */
    fun moveTo(moved: SavedTv) { if (foreground) { tv = moved; connect(moved) } }

    fun clearError() { _lastError.value = null }

    fun disconnect() {
        generation++
        main.removeCallbacksAndMessages(null)
        socket = null
        _conn.value = Conn.Idle
    }

    /** Pairs with the code shown on the TV. Throws PairException with a message the user can act on. */
    suspend fun pair(host: String, port: Int, pin: String, phoneName: String): SavedTv = withContext(Dispatchers.IO) {
        val probe = SavedTv("", "", "", host, port)
        val body = JSONObject().put("pin", pin).put("name", phoneName).toString()
            .toRequestBody("application/json".toMediaType())
        val req = try { Request.Builder().url(probe.pairUrl()).post(body).build() } catch (e: Exception) { throw PairException("That address does not look right.") }
        try {
            http.newCall(req).execute().use { resp ->
                when {
                    resp.code == 403 -> throw PairException("Wrong or expired code. Start pairing again on the TV.")
                    !resp.isSuccessful -> throw PairException("The TV answered with error ${resp.code}.")
                }
                val o = JSONObject(resp.body?.string() ?: throw PairException("Empty answer from the TV."))
                val info = tvName(host, port)
                SavedTv(o.getString("deviceId"), o.getString("token"), info, host, port)
            }
        } catch (e: PairException) {
            throw e
        } catch (e: java.io.IOException) {
            throw PairException(explain(e, host, port))
        }
    }

    private fun tvName(host: String, port: Int): String = try {
        val req = Request.Builder().url("http://${com.kwartotv.remote.hostForUrl(host)}:$port/info").build()
        http.newCall(req).execute().use { JSONObject(it.body?.string() ?: "{}").optString("name", host).ifBlank { host } }
    } catch (_: Exception) { host }
}

class PairException(message: String) : Exception(message)

/** Turns a network error into a next step. The technical reason is kept at the end so it can be reported. */
fun explain(e: Throwable, host: String, port: Int): String {
    val why = "(${e.javaClass.simpleName}${e.message?.let { ": $it" } ?: ""})"
    return when (e) {
        is java.net.SocketTimeoutException ->
            "The TV did not answer at $host:$port. Usually Windows Firewall is blocking it: on the TV press \"Fix network access\" in the pairing screen, then try again. $why"
        is java.net.ConnectException ->
            if (e.message?.contains("ECONNREFUSED") == true || e.message?.contains("refused", ignoreCase = true) == true)
                "The PC at $host answered but nothing is listening on port $port. Is KwartoTV open? $why"
            else "Cannot connect to $host:$port. Check the address on the TV screen and that the phone is on the same Wi-Fi (not mobile data or a guest network). $why"
        is java.net.NoRouteToHostException, is java.net.UnknownHostException ->
            "No route to $host. The phone and the PC are not on the same network. $why"
        else -> "Cannot reach the TV at $host:$port. $why"
    }
}
