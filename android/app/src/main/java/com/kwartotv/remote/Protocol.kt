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

    /** Channel finder: the TV checks every channel; the phone lists the ones that work and picks or stars them. */
    const val SCAN_START = "scan.start"
    const val SCAN_STOP = "scan.stop"
    const val CATALOG = "catalog.get"
    const val CHOOSE = "catalog.choose"
    const val CHOOSE_GROUP = "catalog.choosegroup"
    const val STAR = "catalog.favorite"

    /** The whole text for the box waiting on the TV: "0:" + text, or "1:" + text to also press Go. */
    const val TEXT_SET = "text.set"

    /** A sign-in made on the phone, handed to the TV: "netflix|cookie=value; ...". */
    const val SIGN_IN = "web.signin"

    // YouTube Music on the TV (plays on behind the lock screen)
    const val MUSIC_OPEN = "music.open"
    const val MUSIC_PLAY_PAUSE = "music.playpause"
    const val MUSIC_NEXT = "music.next"
    const val MUSIC_PREV = "music.prev"
    const val MUSIC_STOP = "music.stop"
    const val MUSIC_SEARCH = "music.search"
    const val MUSIC_PLAY = "music.play"
    /** Opens YouTube Music's sign-in on the TV (for Premium: no ads). */
    const val MUSIC_SIGNIN = "music.signin"
    /** Ask the TV for YouTube Music's Quick picks (shown before anything is searched). */
    const val MUSIC_HOME = "music.home"

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
    const val APP_BILIBILI = "app.bilibili"
    const val APP_YTMUSIC = "app.ytmusic"
    /** Sleep timer: minutes (0 = off). At the end the TV stops what plays and locks. */
    const val SLEEP_TIMER = "power.timer"
    // Netflix's own player on the TV
    const val NETFLIX_SKIP = "netflix.skip"
    const val NETFLIX_NEXT = "netflix.next"
    const val NETFLIX_SEARCH = "netflix.search"
    /** Asks for Netflix's Audio & Subtitles lists (the TV answers with "netflix.tracks"). */
    const val NETFLIX_TRACKS = "netflix.tracks"
    /** Picks one: "a:<i>" audio, "s:<i>" subtitles. */
    const val NETFLIX_TRACK = "netflix.track"
    /** Jump to a time (seconds) in the Netflix video. */
    const val NETFLIX_SEEKTO = "netflix.seekto"
    /** Playback speed: 0.5, 0.75, 1, 1.25, 1.5. */
    const val NETFLIX_SPEED = "netflix.speed"
    /** Asks for the episodes (the TV answers with "netflix.episodes"). */
    const val NETFLIX_EPISODES = "netflix.episodes"
    /** Plays an episode (its Netflix id). */
    const val NETFLIX_EPISODE = "netflix.episode"
    /** Answers "Are you still watching?". */
    const val NETFLIX_CONTINUE = "netflix.continue"
    /** Presses Play / Resume of the title whose details are open on the TV. */
    const val NETFLIX_PLAY = "netflix.play"
    /** The Lock workspace: "lock.set key=value" (clock, color, dim, date, song, verse, bg, folder, every, saver). */
    const val LOCK_SET = "lock.set"
    /** Asks for the lock screen's options (the TV answers with "lock.state"). */
    const val LOCK_GET = "lock.get"
    /** Asks for photo N of the slideshow, small, for the live preview (the TV answers with "lock.photo"). */
    const val LOCK_PHOTO = "lock.photo"
    /** Asks for the folders the photos can come from (the TV answers with "lock.folders"). */
    const val LOCK_FOLDERS = "lock.folders"
    /** A search said or typed on the phone: "app|words" (netflix, youtube, bilibili, music, channels). */
    const val APP_SEARCH = "app.search"
    /** An app of the TV's catalog, by id (arg). */
    const val APP_OPEN = "app.open"

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
    data class Found(val catalog: Catalog) : TvMessage
    data class Scanning(val scan: Scan) : TvMessage
    data class TextWanted(val request: TextRequest?) : TvMessage
    data class SignInWanted(val request: SignInRequest) : TvMessage
    data class MusicNow(val music: MusicState) : TvMessage
    /** What the TV shows now: "netflix", "youtube", "live", "Home", "Music"… (the remote shows the buttons for it). */
    data class Foreground(val app: String) : TvMessage
    /** The TV saw a phone app release with this build. */
    data class AppUpdate(val versionCode: Long) : TvMessage
    data class MusicFound(val results: MusicResults) : TvMessage
    /** Netflix's Audio & Subtitles (empty lists: no video playing). */
    data class NetflixTracksNow(val tracks: NetflixTracks) : TvMessage
    /** What Netflix plays on the TV (every second while a video is open). */
    data class NetflixNowPlaying(val now: NetflixPlaying) : TvMessage
    data class NetflixEpisodesNow(val episodes: NetflixEpisodes) : TvMessage
    data class LockNow(val state: LockState) : TvMessage
    data class LockPhotoNow(val photo: LockPhoto) : TvMessage
    data class LockFoldersNow(val folders: List<LockFolder>) : TvMessage
    data object Unknown : TvMessage
}

fun parseTvMessage(text: String): TvMessage = try {
    val o = JSONObject(text)
    when (o.optString("type")) {
        "state" -> TvMessage.State(o.optInt("number", 0), o.optJSONObject("channel")?.optString("name"))
        "ack" -> TvMessage.Ack(o.optString("id").ifEmpty { null }, o.optBoolean("ok"), if (o.isNull("error")) null else o.optString("error"))
        "lineup" -> TvMessage.Channels(parseLineup(o))
        "catalog" -> TvMessage.Found(parseCatalog(o))
        "scan" -> TvMessage.Scanning(parseScan(o))
        "text" -> TvMessage.TextWanted(parseTextRequest(o))
        "signin" -> parseSignIn(o)?.let { TvMessage.SignInWanted(it) } ?: TvMessage.Unknown
        "music" -> TvMessage.MusicNow(parseMusic(o))
        "appupdate" -> TvMessage.AppUpdate(o.optLong("versionCode"))
        "fg" -> TvMessage.Foreground(o.optString("app").take(40))
        "music.results" -> TvMessage.MusicFound(parseMusicResults(o))
        "netflix.now" -> TvMessage.NetflixNowPlaying(NetflixPlaying(o.optString("title").take(120), o.optString("episode").take(120),
            o.optInt("pos").coerceIn(0, 86400), o.optInt("dur").coerceIn(0, 86400), o.optBoolean("paused"), o.optDouble("rate", 1.0), o.optBoolean("still"),
            o.optJSONObject("modal")?.let { m -> NetflixTitleOpen(m.optString("title").take(120), m.optString("play").take(40), m.optString("id").take(12)) }))
        "lock.state" -> TvMessage.LockNow(parseLockState(o))
        "lock.photo" -> TvMessage.LockPhotoNow(LockPhoto(o.optInt("i"), o.optInt("n"),
            o.optString("jpg").takeIf { it.isNotEmpty() && it.length < 400_000 }?.let { try { java.util.Base64.getMimeDecoder().decode(it) } catch (_: Exception) { null } }))
        "lock.folders" -> TvMessage.LockFoldersNow((o.optJSONArray("folders") ?: org.json.JSONArray()).let { a ->
            (0 until minOf(a.length(), 40)).mapNotNull { i -> a.optJSONObject(i)?.let { f -> LockFolder(f.optString("path").take(400), f.optString("name").take(80)) } }.filter { it.path.isNotEmpty() } })
        "netflix.episodes" -> TvMessage.NetflixEpisodesNow(parseEpisodes(o))
        "netflix.tracks" -> TvMessage.NetflixTracksNow(NetflixTracks(parseTracks(o, "audio"), parseTracks(o, "subs")))
        else -> TvMessage.Unknown
    }
} catch (_: Exception) {
    TvMessage.Unknown
}

/** The Netflix video playing on the TV. [dur] 0: no video open. Times in seconds. */
data class NetflixPlaying(val title: String, val episode: String, val pos: Int, val dur: Int, val paused: Boolean, val rate: Double, val still: Boolean, val modal: NetflixTitleOpen? = null)

/** A title's details open on the TV (not playing yet): its name, the label of its Play button ("Resume"…), its number. */
data class NetflixTitleOpen(val title: String, val play: String, val id: String)

/** The lock screen's look and state, as the TV reports it ("lock.state"). */
data class LockState(
    val locked: Boolean, val clock: String, val color: String, val dim: Int, val date: Boolean, val song: Boolean, val verse: Boolean,
    val bg: String, val folder: String, val folderName: String, val photos: Int, val every: Int, val saver: Int, val timer: Int,
)

fun parseLockState(o: JSONObject) = LockState(
    o.optBoolean("locked"), o.optString("clock", "classic"), o.optString("color", "FFFFFF").take(6), o.optInt("dim", 100), o.optBoolean("date", true),
    o.optBoolean("song", true), o.optBoolean("verse"), o.optString("bg", "black"), o.optString("folder").take(400), o.optString("folderName").take(80),
    o.optInt("photos"), o.optInt("every", 20), o.optInt("saver", 10), o.optInt("timer"),
)

/** One photo of the slideshow for the phone's live preview ([jpeg] null: no photos). */
class LockPhoto(val index: Int, val count: Int, val jpeg: ByteArray?)
data class LockFolder(val path: String, val name: String)

data class NetflixEpisode(val id: String, val name: String, val on: Boolean)
data class NetflixSeason(val name: String, val episodes: List<NetflixEpisode>)
/** Netflix's episodes; [onTv]: the phone could not get them, Netflix's own episode list was opened on the TV instead. */
data class NetflixEpisodes(val show: String, val seasons: List<NetflixSeason>, val onTv: Boolean)

fun parseEpisodes(o: JSONObject): NetflixEpisodes {
    val ss = o.optJSONArray("seasons")
    val seasons = (0 until minOf(ss?.length() ?: 0, 30)).mapNotNull { i ->
        val s = ss!!.optJSONObject(i) ?: return@mapNotNull null
        val es = s.optJSONArray("eps") ?: return@mapNotNull null
        val eps = (0 until minOf(es.length(), 200)).mapNotNull { k ->
            val e = es.optJSONObject(k) ?: return@mapNotNull null
            val id = e.optString("id")
            if (id.isEmpty() || id.length > 12 || !id.all { it.isDigit() }) null else NetflixEpisode(id, e.optString("n").take(100).ifBlank { "Episode" }, e.optBoolean("on"))
        }
        if (eps.isEmpty()) null else NetflixSeason(s.optString("n").take(60).ifBlank { "Season ${i + 1}" }, eps)
    }
    return NetflixEpisodes(o.optString("show").take(120), seasons, o.optBoolean("onTv"))
}

/** 1:05:09 or 5:09. */
fun clock(seconds: Int): String {
    val h = seconds / 3600; val m = seconds % 3600 / 60; val s = seconds % 60
    return if (h > 0) "%d:%02d:%02d".format(h, m, s) else "%d:%02d".format(m, s)
}

/** Which app a search said on the phone goes to, from what the TV shows (null: ask which). */
fun searchAppFor(tvApp: String): String? = when (tvApp) {
    "netflix" -> "netflix"; "youtube" -> "youtube"; "Bilibili", "web-bilibili" -> "bilibili"; "Music" -> "music"; "live" -> "channels"
    else -> null
}

/** One audio or subtitle choice of the Netflix video on the TV. */
data class NetflixTrack(val index: Int, val name: String, val on: Boolean)
data class NetflixTracks(val audio: List<NetflixTrack>, val subs: List<NetflixTrack>)

private fun parseTracks(o: JSONObject, key: String): List<NetflixTrack> {
    val a = o.optJSONArray(key) ?: return emptyList()
    return (0 until minOf(a.length(), 40)).mapNotNull { k ->
        val t = a.optJSONObject(k) ?: return@mapNotNull null
        val i = t.optInt("i", -1)
        if (i !in 0..99) null else NetflixTrack(i, t.optString("n").take(60).ifBlank { "Track ${i + 1}" }, t.optBoolean("on"))
    }
}

/** One row of the TV's channel list, as the phone shows it. [now] is "Programme  ·  8:00–9:00 PM" or null. */
data class LineupChannel(val number: Int, val name: String, val group: String, val favorite: Boolean, val now: String?, val id: String? = null)

/** The TV's lineup. [total] can be more than [channels] when the TV has more channels than it sends (it caps the list). */
data class Lineup(val channels: List<LineupChannel>, val total: Int)

private const val MAX_LINEUP_ROWS = 3000

private fun JSONObject.text(key: String): String? = if (isNull(key)) null else optString(key).ifBlank { null }

fun parseLineup(o: JSONObject): Lineup {
    val a = o.optJSONArray("channels")
    val rows = ArrayList<LineupChannel>()
    if (a != null) {
        for (i in 0 until minOf(a.length(), MAX_LINEUP_ROWS)) {
            val c = a.optJSONObject(i) ?: continue
            val number = c.optInt("number", 0)
            if (number <= 0) continue
            rows += LineupChannel(number, c.text("name") ?: "Channel $number", c.text("group") ?: "Other", c.optBoolean("favorite"), c.text("now"), c.text("id"))
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

/**
 * A text box on the TV is waiting for text. [kind] is password, email, search, url or text (for the phone's keyboard and
 * its password manager); [value] is what the box holds now (never sent for a password).
 */
data class TextRequest(val kind: String, val title: String?, val value: String)

/**
 * Sign in to a service (Netflix, Bilibili) on the phone for the TV. [page] is the service's own sign-in page; signed in
 * means the [sessionCookie] is set for [domain]. [done]: the TV says it is signed in now.
 */
data class SignInRequest(val service: String, val name: String, val page: String, val domain: String, val done: Boolean) {
    val sessionCookie: String get() = SIGN_IN_SERVICES[service]?.sessionCookie ?: ""
}

/** The services the phone can sign in to for the TV (same list as PhoneSignIn on the TV). */
data class SignInService(val id: String, val name: String, val page: String, val domain: String, val sessionCookie: String, val signedInPaths: List<String>)

val SIGN_IN_SERVICES = mapOf(
    // Netflix keeps NetflixId for visitors too, so it counts only once the page is past the sign-in. On a phone Netflix
    // often goes to its "get the app" or account pages instead of /browse: anything that is not a sign-in/sign-up page.
    "netflix" to SignInService("netflix", "Netflix", "https://www.netflix.com/login", "netflix.com", "NetflixId", listOf("/browse", "/profiles", "/youraccount", "/account", "/title", "/watch", "/mobile", "/latest", "/search", "/settings")),
    "bilibili" to SignInService("bilibili", "Bilibili", "https://www.bilibili.tv/en/login", "bilibili.tv", "SESSDATA", emptyList()),
    // Signed in on the TV itself (Google's sign-in); the TV only tells the phone when it is done.
    "ytmusic" to SignInService("ytmusic", "YouTube Music", "https://music.youtube.com/", "youtube.com", "SAPISID", emptyList()),
)

fun parseSignIn(o: JSONObject): SignInRequest? {
    val id = o.text("service") ?: return null
    val known = SIGN_IN_SERVICES[id] ?: return null
    return SignInRequest(id, o.text("name") ?: known.name, known.page, known.domain, o.optBoolean("done"))
}

/**
 * Signed in, from the phone's cookies for the service and the page it is on: the session cookie is set and (for a
 * service that keeps a cookie for visitors too) the page is past the sign-in.
 */
fun hasSessionCookie(service: SignInService, cookies: String?): Boolean =
    cookies.orEmpty().split(';').any { it.trim().startsWith(service.sessionCookie + "=") && it.trim().length > service.sessionCookie.length + 1 }

fun isSignedIn(service: SignInService, cookies: String?, url: String?): Boolean {
    if (!hasSessionCookie(service, cookies)) return false
    if (service.signedInPaths.isEmpty()) return true
    val path = runCatching { java.net.URI(url ?: "").path.orEmpty().lowercase() }.getOrDefault("")
    return service.signedInPaths.any { path.contains(it) }
}

/** Null when the TV says no box is waiting any more. */
fun parseTextRequest(o: JSONObject): TextRequest? =
    if (!o.optBoolean("active")) null
    else TextRequest(o.text("kind") ?: "text", o.text("title"), o.text("value").orEmpty())

/** Where the TV's check of every channel is. */
data class Scan(val running: Boolean, val done: Int, val total: Int, val ok: Int, val dead: Int) {
    val percent: Float get() = if (total <= 0) 0f else (done.toFloat() / total).coerceIn(0f, 1f)
}

/** A channel that works on the TV. [chosen]: it is in the TV's channels. [favorite]: starred. */
data class FinderChannel(val id: String, val name: String, val group: String, val chosen: Boolean, val favorite: Boolean)

/** The finder's list: [total] working channels (rows may be fewer), [checked] of [all] channels checked so far. */
data class Catalog(val channels: List<FinderChannel>, val total: Int, val checked: Int, val all: Int, val scan: Scan)

private const val MAX_FINDER_ROWS = 6000

fun parseScan(o: JSONObject?): Scan = Scan(
    o?.optBoolean("running") ?: false, o?.optInt("done") ?: 0, o?.optInt("total") ?: 0, o?.optInt("ok") ?: 0, o?.optInt("dead") ?: 0,
)

fun parseCatalog(o: JSONObject): Catalog {
    val a = o.optJSONArray("channels")
    val rows = ArrayList<FinderChannel>()
    if (a != null) for (i in 0 until minOf(a.length(), MAX_FINDER_ROWS)) {
        val c = a.optJSONObject(i) ?: continue
        val id = c.text("i") ?: continue
        rows += FinderChannel(id, c.text("n") ?: "Channel", c.text("g") ?: "Other", c.optBoolean("s"), c.optBoolean("f"))
    }
    return Catalog(rows, maxOf(o.optInt("total", rows.size), rows.size), o.optInt("checked"), o.optInt("all"), parseScan(o.optJSONObject("scan")))
}

/** Chip value that shows only the channels already on the TV. */
const val CHOSEN_FILTER = "\u0001chosen"

/** "1:value" turns something on, "0:value" off (the TV's catalog.* commands). */
fun toggleArg(on: Boolean, value: String) = (if (on) "1:" else "0:") + value

/** Categories in the order the TV sent them (TV order), each once. */
fun catalogGroups(channels: List<FinderChannel>): List<String> = channels.map { it.group }.distinct()

/** Finder rows for the search box and chip ([group] null = all working channels). */
fun filterCatalog(channels: List<FinderChannel>, query: String, group: String?): List<FinderChannel> {
    val q = query.trim()
    return channels.filter { ch ->
        val inGroup = when (group) {
            null -> true
            FAVORITES_FILTER -> ch.favorite
            CHOSEN_FILTER -> ch.chosen
            else -> ch.group == group
        }
        inGroup && (q.isEmpty() || ch.name.contains(q, ignoreCase = true))
    }
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

/** What YouTube Music on the TV is playing. */
data class MusicState(val on: Boolean, val playing: Boolean, val title: String, val artist: String, val art: String?)

data class MusicTrack(val id: String, val title: String, val artist: String)

/** Songs found for [query]; [error] when the search did not work. */
data class MusicResults(val query: String, val tracks: List<MusicTrack>, val error: Boolean)

fun parseMusic(o: JSONObject) = MusicState(
    on = o.optBoolean("on"), playing = o.optBoolean("playing"),
    title = o.optString("title").take(120), artist = o.optString("artist").take(120),
    art = o.text("art")?.takeIf { it.startsWith("https://") },
)

private val TRACK_ID = Regex("^[A-Za-z0-9_-]{11}$")

fun parseMusicResults(o: JSONObject): MusicResults {
    val arr = o.optJSONArray("items")
    val list = ArrayList<MusicTrack>()
    if (arr != null) for (i in 0 until minOf(arr.length(), 30)) {
        val t = arr.optJSONObject(i) ?: continue
        val id = t.optString("id")
        val title = t.optString("title").take(120)
        if (!TRACK_ID.matches(id) || title.isBlank()) continue
        list.add(MusicTrack(id, title, t.optString("artist").take(120)))
    }
    return MusicResults(o.optString("q").take(64), list, o.optBoolean("error"))
}

/** The TV's app catalog (same ids as AppCatalog on the TV). */
val MORE_APPS = listOf(
    "primevideo" to "Prime Video", "disneyplus" to "Disney+", "hbomax" to "HBO Max", "viu" to "Viu", "iwanttfc" to "iWantTFC",
    "crunchyroll" to "Crunchyroll", "spotify" to "Spotify", "twitch" to "Twitch", "appletv" to "Apple TV", "plex" to "Plex",
)
