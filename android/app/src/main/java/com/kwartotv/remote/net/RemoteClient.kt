package com.kwartotv.remote.net

import android.os.Handler
import android.os.Looper
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

class RemoteClient {
    private val http = OkHttpClient.Builder()
        .pingInterval(15, TimeUnit.SECONDS)
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

    private var socket: WebSocket? = null
    private var tv: SavedTv? = null
    private var generation = 0
    private var retry = 0
    private var seq = 0

    fun connect(target: SavedTv) {
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
            override fun onOpen(webSocket: WebSocket, response: Response) {
                if (gen != generation) return
                retry = 0
                _conn.value = Conn.Connected
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                if (gen != generation) return
                when (val m = parseTvMessage(text)) {
                    is TvMessage.State -> _now.value = Now(m.number, m.channelName)
                    is TvMessage.Ack -> if (!m.ok) _lastError.value = m.error ?: "Not done"
                    TvMessage.Unknown -> Unit
                }
            }

            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                webSocket.close(1000, null)
                if (gen != generation) return
                if (code == 1008) _conn.value = Conn.Rejected("This phone was removed on the TV.") else scheduleRetry(gen)
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                if (gen != generation) return
                if (code == 1008) _conn.value = Conn.Rejected("This phone was removed on the TV.") else scheduleRetry(gen)
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                if (gen != generation) return
                if (response?.code == 401) _conn.value = Conn.Rejected("The TV does not accept this phone. Pair again.") else scheduleRetry(gen)
            }
        })
    }

    private fun scheduleRetry(gen: Int) {
        if (gen != generation) return
        _conn.value = Conn.Retrying
        val delay = minOf(1000L shl minOf(retry, 3), 8000L)
        retry++
        main.postDelayed({ open(gen) }, delay)
    }

    fun send(cmd: String, arg: String? = null): Boolean {
        val s = socket ?: return false
        if (_conn.value != Conn.Connected) return false
        return s.send(commandJson((++seq).toString(), cmd, arg))
    }

    fun clearError() { _lastError.value = null }

    fun disconnect() {
        generation++
        main.removeCallbacksAndMessages(null)
        socket?.cancel()
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
            throw PairException("Cannot reach the TV. Check that both are on the same Wi-Fi.")
        }
    }

    private fun tvName(host: String, port: Int): String = try {
        val req = Request.Builder().url("http://${com.kwartotv.remote.hostForUrl(host)}:$port/info").build()
        http.newCall(req).execute().use { JSONObject(it.body?.string() ?: "{}").optString("name", host).ifBlank { host } }
    } catch (_: Exception) { host }
}

class PairException(message: String) : Exception(message)
