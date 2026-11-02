package com.kwartotv.remote.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.height
import androidx.compose.material.icons.automirrored.rounded.Undo
import androidx.compose.material.icons.rounded.Mic
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.automirrored.rounded.KeyboardArrowLeft
import androidx.compose.material.icons.automirrored.rounded.KeyboardArrowRight
import androidx.compose.material.icons.automirrored.rounded.List
import androidx.compose.material.icons.automirrored.rounded.VolumeDown
import androidx.compose.material.icons.automirrored.rounded.VolumeOff
import androidx.compose.material.icons.automirrored.rounded.VolumeUp
import androidx.compose.material.icons.rounded.Forward10
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.Keyboard
import androidx.compose.material.icons.rounded.KeyboardArrowDown
import androidx.compose.material.icons.rounded.KeyboardArrowUp
import androidx.compose.material.icons.rounded.LockOpen
import androidx.compose.material.icons.rounded.Pause
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Replay
import androidx.compose.material.icons.rounded.Replay10
import androidx.compose.material.icons.rounded.SkipNext
import androidx.compose.material.icons.rounded.SkipPrevious
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material.icons.rounded.Stop
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kwartotv.remote.AppViewModel
import com.kwartotv.remote.Cmd
import com.kwartotv.remote.Haptics

/** What the TV shows, as the Remote tab sees it (from the TV's "fg" message). */
internal enum class OnTv { Locked, Netflix, YouTube, BiliVideo, Live, Music, Home, OtherApp }

internal fun onTv(tvApp: String, musicOn: Boolean): OnTv = when (tvApp) {
    "locked" -> OnTv.Locked
    "netflix" -> OnTv.Netflix
    "youtube" -> OnTv.YouTube
    "web-bilibili" -> OnTv.BiliVideo
    "live" -> OnTv.Live
    "Music" -> OnTv.Music
    "", "Home", "Settings", "Library", "Bilibili", "Live TV", "Channels" -> if (musicOn) OnTv.Music else OnTv.Home
    else -> if (tvApp.startsWith("web-") || tvApp.isNotBlank()) OnTv.OtherApp else OnTv.Home
}

/**
 * The remote: what the TV shows, the apps, a card with the buttons for what plays (a channel, a song, a video), the
 * round D-pad (or the swipe pad), Back · Home · Play/Pause · Voice · Type, and the volume. All in the middle of the
 * screen, where the thumb reaches. Netflix has its own workspace (NetflixWorkspace); the lock screen its own tab.
 */
@Composable
internal fun RemoteTab(vm: AppViewModel, send: (String, String?) -> Unit, onSignIn: (String) -> Unit, voice: () -> Unit, openKeyboard: () -> Unit, openMusic: () -> Unit, openLock: () -> Unit, swipe: Boolean, openNetflix: () -> Unit) {
    val tvApp by vm.client.tvApp.collectAsState()
    val music by vm.client.music.collectAsState()
    val signedIn by vm.signedIn.collectAsState()
    val what = onTv(tvApp, music.on && music.title.isNotBlank())
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        Text("On the TV: " + onTvName(tvApp, what), style = T.cap, color = Muted, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(top = 4.dp))
        Spacer(Modifier.height(16.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AppSquare(QuickApp.LiveTv, Modifier.weight(1f)) { send(Cmd.APP_LIVE, null) }
            AppSquare(QuickApp.YouTube, Modifier.weight(1f)) { send(Cmd.APP_YOUTUBE, null) }
            AppSquare(QuickApp.YtMusic, Modifier.weight(1f)) { send(Cmd.APP_YTMUSIC, null) }
            AppSquare(QuickApp.Netflix, Modifier.weight(1f)) { openNetflix() }
            AppSquare(QuickApp.Bilibili, Modifier.weight(1f)) { send(Cmd.APP_BILIBILI, null) }
        }
        val card: (@Composable () -> Unit)? = when (what) {
            OnTv.Locked -> { { NowCard("The TV is locked", "The clock is showing. Music and volume still work.") {
                Cta("Unlock", "Unlock the TV", kind = CtaKind.White, icon = Icons.Rounded.LockOpen) { send(Cmd.SCREEN_ON, null) }
                Cta("Lock screen options", "Open the Lock tab", kind = CtaKind.Line) { openLock() }
            } } }
            OnTv.Live -> { { LiveCard(vm, send) } }
            OnTv.Music -> { { MusicCard(vm, send, openMusic) } }
            OnTv.YouTube -> { { VideoCard("YouTube", send, next = true) } }
            OnTv.BiliVideo -> { { VideoCard("Bilibili", send, next = false) } }
            OnTv.OtherApp -> { { VideoCard(tvApp.removePrefix("web-").replaceFirstChar { it.uppercase() }, send, next = true) } }
            OnTv.Netflix -> null
            OnTv.Home -> {
                val need = listOf("netflix" to "Netflix", "bilibili" to "Bilibili").filter { it.first !in signedIn }
                if (need.isEmpty()) null else { {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        need.forEach { (id, name) -> Pill("Sign in to $name", "Sign in to $name on this phone for the TV", Modifier.weight(1f)) { onSignIn(id) } }
                    }
                } }
            }
        }
        if (card != null) { Spacer(Modifier.height(16.dp)); card() }
        Spacer(Modifier.height(24.dp))
        if (swipe) Touchpad(send) else DPad(send)
        Spacer(Modifier.height(24.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            RoundKey("Back", "Back", Icons.AutoMirrored.Rounded.Undo) { send(Cmd.BACK, null) }
            RoundKey("Home", "Home", Icons.Rounded.Home) { send(Cmd.HOME, null) }
            RoundKey("Play or pause", "Play/Pause", Icons.Rounded.PlayArrow) { send(if (what == OnTv.Music) Cmd.MUSIC_PLAY_PAUSE else Cmd.PLAY_PAUSE, null) }
            RoundKey("Voice search", "Voice", Icons.Rounded.Mic) { voice() }
            RoundKey("Keyboard", "Type", Icons.Rounded.Keyboard) { openKeyboard() }
        }
        Spacer(Modifier.height(16.dp))
        VolumeBar(send)
        Spacer(Modifier.height(16.dp))
    }
}

private fun onTvName(tvApp: String, what: OnTv) = when (what) {
    OnTv.Locked -> "the lock screen"
    OnTv.Netflix -> "Netflix"
    OnTv.YouTube -> "YouTube"
    OnTv.BiliVideo -> "Bilibili"
    OnTv.Live -> "Live TV"
    OnTv.Music -> "YouTube Music"
    OnTv.Home -> tvApp.ifBlank { "Home" }.let { if (it == "Home") "Home" else it }
    OnTv.OtherApp -> tvApp.removePrefix("web-").replaceFirstChar { it.uppercase() }
}

/** An app on the remote: its logo in a square tile, its name under it (all five the same size). */
@Composable
private fun AppSquare(app: QuickApp, modifier: Modifier, onOpen: () -> Unit) {
    Column(modifier, horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        RemoteKey("", "Open ${app.title}", Modifier.fillMaxWidth().aspectRatio(1f), height = 56.dp, shape = RoundedCornerShape(18.dp), fill = Card, content = { AppLogo(app, 30.dp) }, onFire = onOpen)
        Text(app.title, style = T.mini, color = Muted, textAlign = TextAlign.Center)
    }
}

/** A card for what the TV shows now: a title, a line under it, and its buttons. */
@Composable
internal fun NowCard(title: String, line: String?, buttons: @Composable () -> Unit) {
    KCard {
        Text(title, style = T.head, color = Ink, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth())
        if (!line.isNullOrBlank()) Text(line, style = T.cap, color = Muted, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(top = 4.dp))
        Spacer(Modifier.height(12.dp))
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) { buttons() }
    }
}

/** Live TV: the channel and what is on, channel up/down, the TV's channel list, the last channel, favorite. */
@Composable
private fun LiveCard(vm: AppViewModel, send: (String, String?) -> Unit) {
    val now by vm.client.now.collectAsState()
    val lineup by vm.client.lineup.collectAsState()
    val on = lineup?.channels?.firstOrNull { it.number == now.number }
    NowCard(if (now.number > 0) "${now.number}  ${now.name.orEmpty()}" else "Live TV", on?.now ?: "Live TV on the TV") {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            RemoteKey("Ch −", "Channel down", Modifier.weight(1f), repeat = true, fontSize = 15, height = KEY) { send(Cmd.CH_PREV, null) }
            RemoteKey("Guide", "Channel list on the TV", Modifier.weight(1f), fontSize = 15, height = KEY, icon = Icons.AutoMirrored.Rounded.List) { send(Cmd.CH_LIST, null) }
            RemoteKey("Ch +", "Channel up", Modifier.weight(1f), repeat = true, fontSize = 15, height = KEY) { send(Cmd.CH_NEXT, null) }
            RemoteKey("Last", "Last channel", Modifier.weight(1f), fontSize = 15, height = KEY, icon = Icons.Rounded.Replay) { send(Cmd.CH_LAST, null) }
            RemoteKey("", "Favorite channel", Modifier.weight(1f), height = KEY, icon = Icons.Rounded.Star) { send(Cmd.CH_FAVORITE, null) }
        }
    }
}

/** A song from YouTube Music: what plays, previous / play-pause / next, stop. */
@Composable
private fun MusicCard(vm: AppViewModel, send: (String, String?) -> Unit, openMusic: () -> Unit) {
    val m by vm.client.music.collectAsState()
    if (!m.on || m.title.isBlank()) {
        NowCard("YouTube Music", "Pick a song in the Music tab, or say it with Voice.") {
            Cta("Choose a song", "Open the Music tab", kind = CtaKind.Line) { openMusic() }
        }
        return
    }
    NowCard(m.title, m.artist.ifBlank { if (m.playing) "Playing" else "Paused" }) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            RemoteKey("", "Previous song", Modifier.weight(1f), height = KEY, icon = Icons.Rounded.SkipPrevious) { send(Cmd.MUSIC_PREV, null) }
            RemoteKey("", if (m.playing) "Pause the song" else "Play the song", Modifier.weight(1f), primary = true, height = KEY,
                icon = if (m.playing) Icons.Rounded.Pause else Icons.Rounded.PlayArrow) { send(Cmd.MUSIC_PLAY_PAUSE, null) }
            RemoteKey("", "Next song", Modifier.weight(1f), height = KEY, icon = Icons.Rounded.SkipNext) { send(Cmd.MUSIC_NEXT, null) }
            RemoteKey("", "Stop the music", Modifier.weight(1f), height = KEY, icon = Icons.Rounded.Stop) { send(Cmd.MUSIC_STOP, null) }
        }
    }
}

/** A video (YouTube, a Bilibili episode, another app): back / forward 10 s, play-pause, next. */
@Composable
private fun VideoCard(name: String, send: (String, String?) -> Unit, next: Boolean) {
    NowCard(name, "On the TV") {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            RemoteKey("", "Back 10 seconds", Modifier.weight(1f), repeat = true, height = KEY, icon = Icons.Rounded.Replay10) { send(Cmd.SEEK, "-10") }
            RemoteKey("", "Play or pause the video", Modifier.weight(1f), primary = true, height = KEY, icon = Icons.Rounded.PlayArrow) { send(Cmd.PLAY_PAUSE, null) }
            RemoteKey("", "Forward 10 seconds", Modifier.weight(1f), repeat = true, height = KEY, icon = Icons.Rounded.Forward10) { send(Cmd.SEEK, "10") }
            if (next) RemoteKey("", "Next video", Modifier.weight(1f), height = KEY, icon = Icons.Rounded.SkipNext) { send(Cmd.NEXT, null) }
        }
    }
}

