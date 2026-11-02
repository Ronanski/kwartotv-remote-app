package com.kwartotv.remote.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
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
 * The remote, on one screen: the apps, a card for what the TV shows now (its own buttons: Netflix, a channel, a song,
 * a video), the round D-pad (or a swipe pad), Back · Home · Play/Pause · Keyboard, and the volume. No scrolling on a
 * normal phone.
 */
@Composable
internal fun RemoteTab(vm: AppViewModel, send: (String, String?) -> Unit, onSignIn: (String) -> Unit, openKeyboard: () -> Unit, openMusic: () -> Unit) {
    val tvApp by vm.client.tvApp.collectAsState()
    val music by vm.client.music.collectAsState()
    val signedIn by vm.signedIn.collectAsState()
    val what = onTv(tvApp, music.on && music.title.isNotBlank())
    var pad by remember { mutableIntStateOf(if (vm.padKind == 1) 1 else 0) }
    val ctx = LocalContext.current
    // The top part scrolls when it does not fit; Back · Home · Play/Pause · Type and the volume stay at the bottom, always in sight.
    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
    Column(Modifier.weight(1f).fillMaxWidth().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AppTile(QuickApp.LiveTv, Modifier.weight(1f), height = 70.dp, logo = 30.dp) { send(Cmd.APP_LIVE, null) }
            AppTile(QuickApp.YouTube, Modifier.weight(1f), height = 70.dp, logo = 30.dp) { send(Cmd.APP_YOUTUBE, null) }
            AppTile(QuickApp.YtMusic, Modifier.weight(1f), height = 70.dp, logo = 30.dp) { send(Cmd.APP_YTMUSIC, null) }
            AppTile(QuickApp.Netflix, Modifier.weight(1f), height = 70.dp, logo = 30.dp) { send(Cmd.APP_NETFLIX, null) }
            AppTile(QuickApp.Bilibili, Modifier.weight(1f), height = 70.dp, logo = 30.dp) { send(Cmd.APP_BILIBILI, null) }
        }
        when (what) {
            OnTv.Locked -> NowCard("The TV is locked", "The clock is showing. Music and volume still work.") {
                RemoteKey("Unlock", "Unlock the TV", Modifier.fillMaxWidth(), primary = true, fontSize = 17, height = 50.dp, icon = Icons.Rounded.LockOpen) { send(Cmd.SCREEN_ON, null) }
            }
            OnTv.Netflix -> NetflixPanel(vm, send)
            OnTv.Live -> LiveCard(vm, send)
            OnTv.Music -> MusicCard(vm, send, openMusic)
            OnTv.YouTube -> VideoCard("YouTube", send, next = true)
            OnTv.BiliVideo -> VideoCard("Bilibili", send, next = false)
            OnTv.OtherApp -> VideoCard(tvApp.removePrefix("web-").replaceFirstChar { it.uppercase() }, send, next = true)
            OnTv.Home -> {
                val need = listOf("netflix" to "Netflix", "bilibili" to "Bilibili").filter { it.first !in signedIn }
                if (need.isNotEmpty()) Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    need.forEach { (id, name) -> RemoteKey("Sign in to $name", "Sign in to $name on this phone for the TV", Modifier.weight(1f), fontSize = 14, height = 40.dp) { onSignIn(id) } }
                }
            }
        }
        // The pad: the round D-pad, or a swipe pad (as on the Google TV remote).
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center) {
            FilterChip(selected = pad == 0, onClick = { Haptics.click(ctx); pad = 0; vm.padKind = 0 }, label = { Text("D-pad") })
            Box(Modifier.size(8.dp))
            FilterChip(selected = pad == 1, onClick = { Haptics.click(ctx); pad = 1; vm.padKind = 1 }, label = { Text("Swipe") })
        }
        if (pad == 0) RoundPad(send) else Touchpad(send)
    }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Back", "Back", Modifier.weight(1f), height = 56.dp, icon = Icons.AutoMirrored.Rounded.ArrowBack) { send(Cmd.BACK, null) }
            RemoteKey("Home", "Home", Modifier.weight(1f), height = 56.dp, icon = Icons.Rounded.Home) { send(Cmd.HOME, null) }
            RemoteKey("", "Play or pause", Modifier.weight(1f), primary = true, height = 56.dp, icon = Icons.Rounded.PlayArrow) {
                send(if (what == OnTv.Music) Cmd.MUSIC_PLAY_PAUSE else Cmd.PLAY_PAUSE, null)
            }
            RemoteKey("Type", "Keyboard", Modifier.weight(1f), height = 56.dp, icon = Icons.Rounded.Keyboard) { openKeyboard() }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("", "Volume down", Modifier.weight(1f), repeat = true, height = 52.dp, icon = Icons.AutoMirrored.Rounded.VolumeDown) { send(Cmd.VOL_DOWN, null) }
            RemoteKey("", "Mute", Modifier.weight(1f), height = 52.dp, icon = Icons.AutoMirrored.Rounded.VolumeOff) { send(Cmd.MUTE, null) }
            RemoteKey("", "Volume up", Modifier.weight(1f), repeat = true, height = 52.dp, icon = Icons.AutoMirrored.Rounded.VolumeUp) { send(Cmd.VOL_UP, null) }
        }
    }
}

/** A card for what the TV shows now: a title, a line under it, and its buttons. */
@Composable
internal fun NowCard(title: String, line: String?, buttons: @Composable () -> Unit) {
    Column(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(Card).border(1.dp, Hairline, RoundedCornerShape(18.dp)).padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Text(title, fontSize = 17.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
        if (!line.isNullOrBlank()) Text(line, fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2, overflow = TextOverflow.Ellipsis)
        buttons()
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
            RemoteKey("Ch −", "Channel down", Modifier.weight(1f), repeat = true, fontSize = 16, height = 48.dp) { send(Cmd.CH_PREV, null) }
            RemoteKey("Guide", "Channel list on the TV", Modifier.weight(1f), fontSize = 15, height = 48.dp, icon = Icons.AutoMirrored.Rounded.List) { send(Cmd.CH_LIST, null) }
            RemoteKey("Ch +", "Channel up", Modifier.weight(1f), repeat = true, fontSize = 16, height = 48.dp) { send(Cmd.CH_NEXT, null) }
            RemoteKey("Last", "Last channel", Modifier.weight(1f), fontSize = 15, height = 48.dp, icon = Icons.Rounded.Replay) { send(Cmd.CH_LAST, null) }
            RemoteKey("", "Favorite channel", Modifier.weight(0.8f), height = 48.dp, icon = Icons.Rounded.Star) { send(Cmd.CH_FAVORITE, null) }
        }
    }
}

/** A song from YouTube Music: what plays, previous / play-pause / next, stop. */
@Composable
private fun MusicCard(vm: AppViewModel, send: (String, String?) -> Unit, openMusic: () -> Unit) {
    val m by vm.client.music.collectAsState()
    if (!m.on || m.title.isBlank()) {
        NowCard("YouTube Music", "Pick a song in the Music tab, or say it with 🎤.") {
            RemoteKey("Choose a song", "Open the Music tab", Modifier.fillMaxWidth(), fontSize = 15, height = 46.dp) { openMusic() }
        }
        return
    }
    NowCard((if (m.playing) "♪ " else "❚❚ ") + m.title, m.artist) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            RemoteKey("", "Previous song", Modifier.weight(1f), height = 48.dp, icon = Icons.Rounded.SkipPrevious) { send(Cmd.MUSIC_PREV, null) }
            RemoteKey("", if (m.playing) "Pause the song" else "Play the song", Modifier.weight(1.3f), primary = true, height = 48.dp,
                icon = if (m.playing) Icons.Rounded.Pause else Icons.Rounded.PlayArrow) { send(Cmd.MUSIC_PLAY_PAUSE, null) }
            RemoteKey("", "Next song", Modifier.weight(1f), height = 48.dp, icon = Icons.Rounded.SkipNext) { send(Cmd.MUSIC_NEXT, null) }
            RemoteKey("", "Stop the music", Modifier.weight(0.8f), height = 48.dp, icon = Icons.Rounded.Stop) { send(Cmd.MUSIC_STOP, null) }
        }
    }
}

/** A video (YouTube, a Bilibili episode, another app): back / forward 10 s, play-pause, next. */
@Composable
private fun VideoCard(name: String, send: (String, String?) -> Unit, next: Boolean) {
    NowCard(name, "On the TV") {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            RemoteKey("", "Back 10 seconds", Modifier.weight(1f), repeat = true, height = 48.dp, icon = Icons.Rounded.Replay10) { send(Cmd.SEEK, "-10") }
            RemoteKey("", "Play or pause the video", Modifier.weight(1.3f), primary = true, height = 48.dp, icon = Icons.Rounded.PlayArrow) { send(Cmd.PLAY_PAUSE, null) }
            RemoteKey("", "Forward 10 seconds", Modifier.weight(1f), repeat = true, height = 48.dp, icon = Icons.Rounded.Forward10) { send(Cmd.SEEK, "10") }
            if (next) RemoteKey("", "Next video", Modifier.weight(1f), height = 48.dp, icon = Icons.Rounded.SkipNext) { send(Cmd.NEXT, null) }
        }
    }
}

/** The round D-pad with OK in the middle, as on a TV remote. Arrows repeat while held. */
@Composable
internal fun RoundPad(send: (String, String?) -> Unit) {
    Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Box(
            Modifier.size(250.dp).clip(CircleShape).background(Card).border(1.dp, Hairline, CircleShape),
            contentAlignment = Alignment.Center,
        ) {
            val key = Modifier.size(76.dp)
            RemoteKey("", "Up", key.align(Alignment.TopCenter), repeat = true, bare = true, shape = CircleShape, height = 76.dp, icon = Icons.Rounded.KeyboardArrowUp, iconSize = 38.dp) { send(Cmd.UP, null) }
            RemoteKey("", "Down", key.align(Alignment.BottomCenter), repeat = true, bare = true, shape = CircleShape, height = 76.dp, icon = Icons.Rounded.KeyboardArrowDown, iconSize = 38.dp) { send(Cmd.DOWN, null) }
            RemoteKey("", "Left", key.align(Alignment.CenterStart), repeat = true, bare = true, shape = CircleShape, height = 76.dp, icon = Icons.AutoMirrored.Rounded.KeyboardArrowLeft, iconSize = 38.dp) { send(Cmd.LEFT, null) }
            RemoteKey("", "Right", key.align(Alignment.CenterEnd), repeat = true, bare = true, shape = CircleShape, height = 76.dp, icon = Icons.AutoMirrored.Rounded.KeyboardArrowRight, iconSize = 38.dp) { send(Cmd.RIGHT, null) }
            RemoteKey("OK", "OK", Modifier.size(100.dp), primary = true, shape = CircleShape, height = 100.dp, fontSize = 22) { send(Cmd.OK, null) }
        }
    }
}

