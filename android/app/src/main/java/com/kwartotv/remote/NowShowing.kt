package com.kwartotv.remote

/** What the phone's notification (and lock screen) shows and controls: it follows what is on the TV. */
enum class ShowMode { Music, Video, Live, Plain }

/** One button: [cmd] is sent to the TV. */
data class NotifButton(val cmd: String, val label: String)

data class NowShowing(val mode: ShowMode, val title: String, val text: String, val playing: Boolean, val buttons: List<NotifButton>)

private val VOLUME = listOf(NotifButton(Cmd.VOL_DOWN, "Vol −"), NotifButton(Cmd.VOL_UP, "Vol +"))

/**
 * [tvApp] is what the TV reports ("netflix", "youtube", "live", "locked", "Home"…). A video or channel on screen wins;
 * otherwise the music (also behind the lock screen); otherwise the plain connection line.
 */
fun nowShowing(connected: Boolean, status: String, channelNumber: Int, channelName: String?, music: MusicState, tvApp: String): NowShowing {
    if (!connected) return NowShowing(ShowMode.Plain, "KwartoTV Remote", status, false, emptyList())
    val videoApp = when (tvApp) { "netflix" -> "Netflix"; "youtube" -> "YouTube"; else -> null }
    return when {
        videoApp != null -> NowShowing(ShowMode.Video, "Watching $videoApp", "On the TV", true, listOf(
            NotifButton(Cmd.SEEK + ":-10", "−10 s"), NotifButton(Cmd.PLAY_PAUSE, "Play/Pause"), NotifButton(Cmd.SEEK + ":10", "+10 s")) + VOLUME)
        tvApp == "live" -> NowShowing(ShowMode.Live, if (channelNumber > 0) "$channelNumber  ${channelName.orEmpty()}".trim() else "Live TV", "Live TV on the TV", true, listOf(
            NotifButton(Cmd.CH_PREV, "Ch −"), NotifButton(Cmd.PLAY_PAUSE, "Play/Pause"), NotifButton(Cmd.CH_NEXT, "Ch +")) + VOLUME)
        music.on && music.title.isNotBlank() -> NowShowing(ShowMode.Music, music.title, music.artist.ifBlank { "YouTube Music on the TV" }, music.playing, listOf(
            NotifButton(Cmd.MUSIC_PREV, "Previous"), NotifButton(Cmd.MUSIC_PLAY_PAUSE, if (music.playing) "Pause" else "Play"), NotifButton(Cmd.MUSIC_NEXT, "Next")) + VOLUME)
        else -> NowShowing(ShowMode.Plain, "KwartoTV Remote", status, false, listOf(NotifButton(Cmd.PLAY_PAUSE, "⏯ Play/Pause")) + VOLUME)
    }
}

/** A button's command and its argument ("media.seek:-10" → media.seek, -10). */
fun splitButton(cmd: String): Pair<String, String?> = cmd.indexOf(':').let { if (it < 0) cmd to null else cmd.substring(0, it) to cmd.substring(it + 1) }
