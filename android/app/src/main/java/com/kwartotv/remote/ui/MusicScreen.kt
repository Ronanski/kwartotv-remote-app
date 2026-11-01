package com.kwartotv.remote.ui

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material.icons.rounded.Pause
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.SkipNext
import androidx.compose.material.icons.rounded.SkipPrevious
import androidx.compose.material.icons.rounded.Stop
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kwartotv.remote.AppViewModel
import com.kwartotv.remote.Cmd
import com.kwartotv.remote.Haptics

/**
 * YouTube Music on the TV: what is playing, play/pause, next, previous, and a song search. Tap a song to play it on the
 * TV; YouTube Music then keeps going with similar songs. It plays on behind the lock screen, which shows the song.
 */
@Composable
fun MusicTab(vm: AppViewModel) {
    val client = vm.client
    val context = LocalContext.current
    val focus = LocalFocusManager.current
    val music by client.music.collectAsState()
    val results by client.musicResults.collectAsState()
    val searching by client.musicSearching.collectAsState()
    var query by rememberSaveable { mutableStateOf("") }
    val colors = MaterialTheme.colorScheme
    val conn by client.conn.collectAsState()
    // Nothing searched yet: show YouTube Music's Quick picks.
    androidx.compose.runtime.LaunchedEffect(conn) { if (conn == com.kwartotv.remote.net.Conn.Connected && results == null) client.send(Cmd.MUSIC_HOME) }

    fun send(cmd: String, arg: String? = null) {
        Haptics.click(context)
        if (!client.send(cmd, arg)) { Toast.makeText(context, "Not connected to the TV. Reconnecting…", Toast.LENGTH_SHORT).show(); vm.reconnect() }
    }
    fun search() {
        if (query.isBlank()) return
        focus.clearFocus()
        Haptics.click(context)
        if (!client.searchMusic(query)) { Toast.makeText(context, "Not connected to the TV. Reconnecting…", Toast.LENGTH_SHORT).show(); vm.reconnect() }
    }

    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        // Now playing
        Row(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(colors.surfaceContainer).padding(14.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(Modifier.size(52.dp).clip(RoundedCornerShape(14.dp)).background(Accent.copy(alpha = 0.18f)), contentAlignment = Alignment.Center) {
                Icon(Icons.Rounded.MusicNote, null, tint = Accent, modifier = Modifier.size(28.dp))
            }
            Column(Modifier.weight(1f).padding(start = 14.dp)) {
                Text(
                    when {
                        music.title.isNotBlank() -> music.title
                        music.on -> "YouTube Music is open on the TV"
                        else -> "No music playing"
                    },
                    fontSize = 17.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis,
                )
                Text(
                    when {
                        music.title.isNotBlank() -> (if (music.playing) "" else "Paused · ") + music.artist
                        else -> "Search a song below and tap it to play on the TV"
                    },
                    fontSize = 13.sp, color = colors.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis,
                )
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("", "Previous song", Modifier.weight(1f), height = 64.dp, icon = Icons.Rounded.SkipPrevious) { send(Cmd.MUSIC_PREV) }
            RemoteKey(
                if (music.playing) "Pause" else "Play", "Play or pause the music", Modifier.weight(1.3f), primary = true, height = 64.dp, fontSize = 18,
                icon = if (music.playing) Icons.Rounded.Pause else Icons.Rounded.PlayArrow,
            ) { send(Cmd.MUSIC_PLAY_PAUSE) }
            RemoteKey("", "Next song", Modifier.weight(1f), height = 64.dp, icon = Icons.Rounded.SkipNext) { send(Cmd.MUSIC_NEXT) }
            if (music.on) RemoteKey("", "Stop the music", Modifier.weight(0.8f), height = 64.dp, icon = Icons.Rounded.Stop) { send(Cmd.MUSIC_STOP) }
        }

        // The music plays through YouTube on the TV: its sign-in counts (Premium has no ads).
        androidx.compose.material3.TextButton(onClick = { send(Cmd.MUSIC_SIGNIN) }) {
            Text("Ads? Sign in to YouTube Music on the TV once (Premium = no ads)", fontSize = 13.sp)
        }

        OutlinedTextField(
            value = query, onValueChange = { query = it.take(64) }, singleLine = true,
            label = { Text("Search songs or artists") },
            leadingIcon = { Icon(Icons.Rounded.Search, null) },
            modifier = Modifier.fillMaxWidth(),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { search() }),
        )
        if (searching) LinearProgressIndicator(Modifier.fillMaxWidth())

        val r = results
        val message = when {
            r == null -> null
            r.error -> "The search did not work. Check the TV's internet and try again."
            r.tracks.isEmpty() -> "No songs found for \"${r.query}\"."
            query.isBlank() && r.query.isNotBlank() -> r.query
            else -> null
        }
        if (message != null) Text(message, fontSize = 14.sp, color = colors.onSurfaceVariant)

        LazyColumn(Modifier.weight(1f).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            items(r?.tracks.orEmpty(), key = { it.id }) { t ->
                val current = music.title.isNotBlank() && music.title == t.title
                Row(
                    Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp))
                        .background(if (current) colors.surfaceVariant else colors.surfaceContainer)
                        .clickable(onClickLabel = "Play ${t.title} on the TV") { send(Cmd.MUSIC_PLAY, t.id) }
                        .padding(horizontal = 14.dp, vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Icon(if (current) Icons.Rounded.MusicNote else Icons.Rounded.PlayArrow, null, tint = if (current) Accent else colors.onSurfaceVariant, modifier = Modifier.size(24.dp))
                    Column(Modifier.weight(1f).padding(start = 12.dp)) {
                        Text(t.title, fontSize = 16.sp, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        if (t.artist.isNotBlank()) Text(t.artist, fontSize = 13.sp, color = colors.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
            }
        }
    }
}
