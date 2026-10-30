package com.kwartotv.remote

import org.json.JSONObject
import java.net.URLDecoder
import java.net.URLEncoder

/** Commands the TV understands. Same names as the Windows app and the browser remote. */
object Cmd {
    const val UP = "nav.up"
    const val DOWN = "nav.down"
    const val LEFT = "nav.left"
    const val RIGHT = "nav.right"
    const val OK = "nav.ok"
    const val BACK = "nav.back"
    const val HOME = "nav.home"

    const val PLAY_PAUSE = "media.playpause"
    const val NEXT = "media.next"
    const val PREV = "media.prev"
    const val STOP = "media.stop"
    const val MUTE = "media.mute"
    const val SEEK = "media.seek"
    const val VOL_UP = "media.volup"
    const val VOL_DOWN = "media.voldown"

    const val CH_NEXT = "channel.next"
    const val CH_PREV = "channel.prev"
    const val CH_LAST = "channel.last"
    const val CH_DIGIT = "channel.digit"
    const val CH_COMMIT = "channel.commit"
    const val CH_FAVORITE = "channel.favorite"
    const val CH_LIST = "channel.list"
    const val CH_NUMBER = "channel.number"
    /** Asks for the numbered lineup (the TV answers with a "lineup" message, and sends it again when it changes). */
    const val LINEUP = "lineup.get"
    const val PLAYLIST_ADD = "playlist.add"

    const val MOUSE_MOVE = "mouse.move"
    const val MOUSE_CLICK = "mouse.click"
    const val MOUSE_RIGHT = "mouse.rightclick"
    const val MOUSE_SCROLL = "mouse.scroll"

    const val TEXT = "key.text"
    const val BACKSPACE = "key.backspace"
    const val ENTER = "key.enter"

    const val APP_LIVE = "app.livetv"
    const val APP_YOUTUBE = "app.youtube"
    const val APP_NETFLIX = "app.netflix"

    const val SCREEN_ON = "power.screenon"
    const val SCREEN_OFF = "power.screenoff"
    const val SLEEP = "power.sleep"
    const val RESTART = "power.restart"
    const val SHUTDOWN = "power.shutdown"
}

/** What the pairing QR code on the TV contains: kwartotv://pair?host=192.168.1.20&port=8765&pin=123456 */
data class PairLink(val host: String, val port: Int, val pin: String) {
    companion object {
        fun parse(text: String?): PairLink? {
            val t = text?.trim() ?: return null
            val prefix = "kwartotv://pair?"
            if (!t.startsWith(prefix)) return null
            val q = t.removePrefix(prefix).split('&').mapNotNull {
                val i = it.indexOf('=')
                if (i <= 0) null else it.substring(0, i) to URLDecoder.decode(it.substring(i + 1), "UTF-8")
            }.toMap()
            val host = q["host"]?.takeIf { it.isNotBlank() } ?: return null
            val port = q["port"]?.toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
            val pin = q["pin"]?.takeIf { it.length == 6 && it.all(Char::isDigit) } ?: return null
            return PairLink(host, port, pin)
        }

        fun build(host: String, port: Int, pin: String) =
            "kwartotv://pair?host=${URLEncoder.encode(host, "UTF-8")}&port=$port&pin=$pin"
    }
}

/** One paired TV, kept on the phone. */
data class SavedTv(val deviceId: String, val token: String, val name: String, val host: String, val port: Int) {
    fun toJson(): JSONObject = JSONObject()
        .put("deviceId", deviceId).put("token", token).put("name", name).put("host", host).put("port", port)

    fun wsUrl() = "ws://${hostForUrl(host)}:$port/ws?device=${URLEncoder.encode(deviceId, "UTF-8")}&token=${URLEncoder.encode(token, "UTF-8")}"
    fun pairUrl() = "http://${hostForUrl(host)}:$port/pair"

    companion object {
        fun fromJson(o: JSONObject) = SavedTv(o.getString("deviceId"), o.getString("token"), o.getString("name"), o.getString("host"), o.getInt("port"))
    }
}

fun hostForUrl(host: String) = if (host.contains(':') && !host.startsWith("[")) "[$host]" else host

/** Parsed message from the TV. */
sealed interface TvMessage {
    data class State(val number: Int, val channelName: String?) : TvMessage
    data class Ack(val id: String?, val ok: Boolean, val error: String?) : TvMessage
    data class Channels(val lineup: Lineup) : TvMessage
    data object Unknown : TvMessage
}

fun parseTvMessage(text: String): TvMessage = try {
    val o = JSONObject(text)
    when (o.optString("type")) {
        "state" -> TvMessage.State(o.optInt("number", 0), o.optJSONObject("channel")?.optString("name"))
        "ack" -> TvMessage.Ack(o.optString("id").ifEmpty { null }, o.optBoolean("ok"), if (o.isNull("error")) null else o.optString("error"))
        "lineup" -> TvMessage.Channels(parseLineup(o))
        else -> TvMessage.Unknown
    }
} catch (_: Exception) {
    TvMessage.Unknown
}

/** One row of the TV's channel list, as the phone shows it. [now] is "Programme  ·  8:00–9:00 PM" or null. */
data class LineupChannel(val number: Int, val name: String, val group: String, val favorite: Boolean, val now: String?)

/** The TV's lineup. [total] can be more than [channels] when the TV has more channels than it sends (it caps the list). */
data class Lineup(val channels: List<LineupChannel>, val total: Int)

private const val MAX_LINEUP_ROWS = 1000

private fun JSONObject.text(key: String): String? = if (isNull(key)) null else optString(key).ifBlank { null }

fun parseLineup(o: JSONObject): Lineup {
    val a = o.optJSONArray("channels")
    val rows = ArrayList<LineupChannel>()
    if (a != null) {
        for (i in 0 until minOf(a.length(), MAX_LINEUP_ROWS)) {
            val c = a.optJSONObject(i) ?: continue
            val number = c.optInt("number", 0)
            if (number <= 0) continue
            rows += LineupChannel(number, c.text("name") ?: "Channel $number", c.text("group") ?: "Other", c.optBoolean("favorite"), c.text("now"))
        }
    }
    return Lineup(rows, maxOf(o.optInt("total", rows.size), rows.size))
}

/** Chip value that shows only the favorites (not a real category name). */
const val FAVORITES_FILTER = "\u0001favorites"

/** Categories in lineup order, each once. */
fun lineupGroups(channels: List<LineupChannel>): List<String> = channels.map { it.group }.distinct()

/**
 * The rows matching the search box and the chosen category chip ([group] null = all).
 * The search matches the name (any case) or, for digits, the start of the channel number; an exact number comes first.
 */
fun filterLineup(channels: List<LineupChannel>, query: String, group: String?): List<LineupChannel> {
    val q = query.trim()
    val digits = q.isNotEmpty() && q.all(Char::isDigit)
    return channels.filter { ch ->
        val inGroup = group == null || (if (group == FAVORITES_FILTER) ch.favorite else ch.group == group)
        inGroup && (q.isEmpty() || ch.name.contains(q, ignoreCase = true) || (digits && ch.number.toString().startsWith(q)))
    }.sortedByDescending { digits && it.number.toString() == q }
}

fun commandJson(id: String, cmd: String, arg: String?): String {
    val o = JSONObject().put("id", id).put("cmd", cmd)
    if (arg != null) o.put("arg", arg)
    return o.toString()
}

/**
 * Ports the TV may be listening on, in order. Windows reserves changing port blocks for Hyper-V/WSL,
 * so the TV falls back to the next free one. Same list as RemoteServer.CandidatePorts on the TV.
 */
val CANDIDATE_PORTS = listOf(8765, 8766, 8767, 8768, 8769, 18765, 28765, 38765, 48765, 58765)

/** Splits "192.168.1.20:8765" into host and port. The port is null when not typed (then the app searches for it). */
fun parseHostPort(text: String): Pair<String, Int?>? {
    val t = text.trim().removePrefix("http://").removePrefix("https://").removeSuffix("/")
    if (t.isEmpty()) return null
    val i = t.lastIndexOf(':')
    if (i > 0 && !t.endsWith("]") && t.count { it == ':' } == 1) {
        val port = t.substring(i + 1).toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
        return t.substring(0, i) to port
    }
    return t to null
}
