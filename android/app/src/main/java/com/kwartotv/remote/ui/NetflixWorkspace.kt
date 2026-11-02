package com.kwartotv.remote.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.List
import androidx.compose.material.icons.automirrored.rounded.Undo
import androidx.compose.material.icons.rounded.ClosedCaption
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.Keyboard
import androidx.compose.material.icons.rounded.Mic
import androidx.compose.material.icons.rounded.Pause
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Replay10
import androidx.compose.material.icons.rounded.Forward10
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Speed
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.kwartotv.remote.AppViewModel
import com.kwartotv.remote.Cmd
import com.kwartotv.remote.NetflixPlaying
import com.kwartotv.remote.clock

/**
 * Netflix's own remote, as its own workspace: it follows what the TV shows.
 * Watching: what plays and its progress, the D-pad, ±10 s and play/pause, Audio & Subs, Episodes, Speed.
 * A title's details open: a big Resume (the TV presses Netflix's own Play / Resume) and the episodes.
 * Picking a title: search, the D-pad.
 */
@Composable
internal fun NetflixWorkspace(vm: AppViewModel, send: (String, String?) -> Unit, onBack: () -> Unit, voice: () -> Unit, openType: () -> Unit) {
    val now by vm.client.netflixNow.collectAsState()
    var sheet by remember { mutableStateOf("") }
    val n = now
    val watching = n != null && n.dur > 0
    val title = n?.modal?.takeIf { !watching }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        WorkspaceBar("Netflix", NetflixRed, when { watching -> "Watching"; title != null -> "Title details"; else -> "Browsing" }, onBack)
        when {
            watching -> Watching(n!!, send, open = { sheet = it; if (it == "tracks") vm.client.askNetflixTracks(); if (it == "episodes") vm.client.askNetflixEpisodes() }, voice)
            title != null -> {
                KCard(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("Open on the TV", style = T.cap, color = Muted, textAlign = TextAlign.Center)
                    Text(title.title.ifBlank { "This title" }, style = T.head, color = Ink, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 4.dp))
                    Spacer(Modifier.height(16.dp))
                    Cta(title.play.ifBlank { "Play" }, "Play the title open on the TV", kind = CtaKind.White, icon = Icons.Rounded.PlayArrow) { send(Cmd.NETFLIX_PLAY, null) }
                    Spacer(Modifier.height(8.dp))
                    Cta("Pick another episode", "Choose an episode", kind = CtaKind.Line, icon = Icons.AutoMirrored.Rounded.List) { vm.client.askNetflixEpisodes(); sheet = "episodes" }
                }
                Spacer(Modifier.height(24.dp))
                DPad(send)
                Spacer(Modifier.height(24.dp))
                Keys(send, voice, openType, backLabel = "Close")
            }
            else -> {
                var q by rememberSaveable { mutableStateOf("") }
                OutlinedTextField(
                    value = q, onValueChange = { q = it.take(60) }, singleLine = true, modifier = Modifier.fillMaxWidth(),
                    placeholder = { Text("Search Netflix", style = T.body, color = Muted) }, textStyle = T.body,
                    leadingIcon = { Icon(Icons.Rounded.Search, null, tint = Muted) },
                    shape = RoundedCornerShape(26.dp),
                    colors = OutlinedTextFieldDefaults.colors(unfocusedContainerColor = Card, focusedContainerColor = Card, unfocusedBorderColor = Hairline, focusedBorderColor = Accent),
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    keyboardActions = KeyboardActions(onSearch = { if (q.isNotBlank()) send(Cmd.NETFLIX_SEARCH, q.trim()) }),
                )
                Spacer(Modifier.height(12.dp))
                KCard(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("On the TV: picking a title", style = T.cap, color = Muted, textAlign = TextAlign.Center)
                    Text("Move with the D-pad. OK opens the title, and its Resume shows here.", style = T.cap, color = Soft, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 4.dp))
                }
                Spacer(Modifier.height(24.dp))
                DPad(send)
                Spacer(Modifier.height(24.dp))
                Keys(send, voice, openType, backLabel = "Back")
            }
        }
        Spacer(Modifier.height(16.dp))
        VolumeBar(send)
        Spacer(Modifier.height(16.dp))
    }
    when (sheet) {
        "tracks" -> TracksSheet(vm, send) { sheet = "" }
        "episodes" -> EpisodesSheet(vm, send) { sheet = "" }
        "speed" -> SpeedSheet(n?.rate ?: 1.0, send) { sheet = "" }
    }
}

@Composable
private fun KCard(horizontalAlignment: Alignment.Horizontal, content: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit) =
    KCard { Column(Modifier.fillMaxWidth(), horizontalAlignment = horizontalAlignment, content = content) }

@Composable
private fun Keys(send: (String, String?) -> Unit, voice: () -> Unit, openType: () -> Unit, backLabel: String) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        RoundKey("Back", backLabel, Icons.AutoMirrored.Rounded.Undo) { send(Cmd.BACK, null) }
        RoundKey("Home", "Home", Icons.Rounded.Home) { send(Cmd.HOME, null) }
        RoundKey("Voice search", "Voice", Icons.Rounded.Mic) { voice() }
        RoundKey("Keyboard", "Type", Icons.Rounded.Keyboard) { openType() }
    }
}

@Composable
private fun Watching(n: NetflixPlaying, send: (String, String?) -> Unit, open: (String) -> Unit, voice: () -> Unit) {
    KCard(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(n.title.ifBlank { "Netflix" }, style = T.head, color = Ink, textAlign = TextAlign.Center)
        if (n.episode.isNotBlank()) Text(n.episode, style = T.cap, color = Muted, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 4.dp))
        var drag by remember { mutableStateOf<Float?>(null) }
        Spacer(Modifier.height(8.dp))
        SeekBar(n.pos.toFloat(), n.dur.toFloat(), drag, onDrag = { drag = it }, color = NetflixRed) { to -> drag = null; send(Cmd.NETFLIX_SEEKTO, to.toInt().toString()) }
        Row(Modifier.fillMaxWidth()) {
            Text(clock((drag ?: n.pos.toFloat()).toInt()), style = T.cap, color = Muted, modifier = Modifier.weight(1f))
            if (n.rate != 1.0) Text("${n.rate}×   ", style = T.cap, color = AccentSoft)
            Text(clock(n.dur), style = T.cap, color = Muted)
        }
        if (n.still) { Spacer(Modifier.height(12.dp)); Cta("Yes, continue watching", "Answer Are you still watching", kind = CtaKind.White) { send(Cmd.NETFLIX_CONTINUE, null) } }
        Spacer(Modifier.height(12.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Pill("Skip intro", "Skip the intro or recap", Modifier.weight(1f)) { send(Cmd.NETFLIX_SKIP, null) }
            Pill("Next episode", "Play the next episode", Modifier.weight(1f)) { send(Cmd.NETFLIX_NEXT, null) }
        }
    }
    Spacer(Modifier.height(24.dp))
    DPad(send, size = 224.dp)
    Spacer(Modifier.height(24.dp))
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(32.dp, Alignment.CenterHorizontally), verticalAlignment = Alignment.Top) {
        RoundKey("Back 10 seconds", "−10 sec", Icons.Rounded.Replay10, repeat = true) { send(Cmd.SEEK, "-10") }
        RoundKey("Play or pause", if (n.paused) "Play" else "Pause", if (n.paused) Icons.Rounded.PlayArrow else Icons.Rounded.Pause, fill = Ink, tint = Screen, size = 72.dp) { send(Cmd.PLAY_PAUSE, null) }
        RoundKey("Forward 10 seconds", "+10 sec", Icons.Rounded.Forward10, repeat = true) { send(Cmd.SEEK, "10") }
    }
    Spacer(Modifier.height(16.dp))
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        RoundKey("Back", "Back", Icons.AutoMirrored.Rounded.Undo) { send(Cmd.BACK, null) }
        RoundKey("Choose the audio language and subtitles", "Audio/Subs", Icons.Rounded.ClosedCaption) { open("tracks") }
        RoundKey("Choose an episode", "Episodes", Icons.AutoMirrored.Rounded.List) { open("episodes") }
        RoundKey("Playback speed", "Speed", Icons.Rounded.Speed) { open("speed") }
        RoundKey("Voice search", "Voice", Icons.Rounded.Mic) { voice() }
    }
}

@Composable
private fun TracksSheet(vm: AppViewModel, send: (String, String?) -> Unit, close: () -> Unit) {
    val tracks by vm.client.netflixTracks.collectAsState()
    val now by vm.client.netflixNow.collectAsState()
    KSheet("Audio & Subtitles", listOfNotNull(now?.title?.ifBlank { null }, now?.episode?.ifBlank { null }).joinToString(" · ").ifBlank { null }, close) {
        val t = tracks
        when {
            t == null -> Text("Asking Netflix on the TV…", style = T.body, color = Muted, modifier = Modifier.padding(horizontal = 20.dp))
            t.audio.isEmpty() && t.subs.isEmpty() -> Text("Play a video on Netflix first.", style = T.body, color = Muted, modifier = Modifier.padding(horizontal = 20.dp))
            else -> Row(Modifier.fillMaxWidth().heightIn(max = 460.dp).verticalScroll(rememberScrollState()).padding(horizontal = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Column(Modifier.weight(1f)) {
                    Text("Audio", style = T.label, color = Muted, modifier = Modifier.padding(start = 14.dp, bottom = 8.dp))
                    t.audio.forEach { a -> OptionRow(a.name, a.on) { send(Cmd.NETFLIX_TRACK, "a:${a.index}") } }
                }
                Column(Modifier.weight(1f)) {
                    Text("Subtitles", style = T.label, color = Muted, modifier = Modifier.padding(start = 14.dp, bottom = 8.dp))
                    t.subs.forEach { a -> OptionRow(a.name, a.on) { send(Cmd.NETFLIX_TRACK, "s:${a.index}") } }
                }
            }
        }
        Spacer(Modifier.height(16.dp))
        Box(Modifier.padding(horizontal = 16.dp)) { Cta("Done", "Done", kind = CtaKind.White) { close() } }
    }
}

@Composable
private fun EpisodesSheet(vm: AppViewModel, send: (String, String?) -> Unit, close: () -> Unit) {
    val eps by vm.client.netflixEpisodes.collectAsState()
    var season by remember { mutableIntStateOf(-1) }
    val e = eps
    KSheet("Episodes", e?.show?.ifBlank { null }, close) {
        when {
            e == null -> Text("Asking Netflix on the TV…", style = T.body, color = Muted, modifier = Modifier.padding(horizontal = 20.dp))
            e.seasons.isEmpty() && e.onTv -> Text("Netflix's episode list is open on the TV: choose with the D-pad and OK.", style = T.body, color = Muted, modifier = Modifier.padding(horizontal = 20.dp))
            e.seasons.isEmpty() -> Text("This title has no episodes. Use Resume or Play.", style = T.body, color = Muted, modifier = Modifier.padding(horizontal = 20.dp))
            else -> {
                val cur = if (season in e.seasons.indices) season else e.seasons.indexOfFirst { s -> s.episodes.any { it.on } }.coerceAtLeast(0)
                if (e.seasons.size > 1) LazyRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 16.dp)) {
                    items(e.seasons.size) { i -> Pill(e.seasons[i].name, e.seasons[i].name, on = i == cur) { season = i } }
                }
                Spacer(Modifier.height(12.dp))
                LazyColumn(Modifier.fillMaxWidth().heightIn(max = 480.dp).padding(horizontal = 8.dp)) {
                    items(e.seasons[cur].episodes) { ep ->
                        val m = Regex("""^(\d+)\.\s*(.*)$""").find(ep.name)
                        val no = m?.groupValues?.get(1) ?: ""
                        val name = m?.groupValues?.get(2)?.ifBlank { null } ?: ep.name
                        OptionRow(name, ep.on, sub = if (ep.on) "Now watching" else null, lead = {
                            Box(Modifier.size(40.dp).clip(CircleShape).background(if (ep.on) NetflixRed else CardHigh), contentAlignment = Alignment.Center) {
                                Text(no.ifBlank { "▶" }, style = T.label, color = Color.White)
                            }
                        }) { send(Cmd.NETFLIX_EPISODE, ep.id); close() }
                    }
                }
            }
        }
    }
}

@Composable
private fun SpeedSheet(rate: Double, send: (String, String?) -> Unit, close: () -> Unit) {
    KSheet("Playback speed", if (rate == 1.0) "Now 1× (normal)" else "Now $rate×", close) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf("0.5", "0.75", "1", "1.25", "1.5").forEach { r ->
                Pill("$r×", "Speed $r", Modifier.weight(1f), on = rate == r.toDouble()) { send(Cmd.NETFLIX_SPEED, r); close() }
            }
        }
        Spacer(Modifier.height(16.dp))
        Box(Modifier.padding(horizontal = 16.dp)) { Cta("Done", "Done", kind = CtaKind.White) { close() } }
    }
}

/** Something said on the phone while the TV shows Home: which app to search it in. */
@Composable
internal fun SearchWhereSheet(words: String, again: () -> Unit, close: () -> Unit, search: (String, String) -> Unit) {
    KSheet("Search where?", null, close) {
        KCard(Modifier.padding(horizontal = 16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Box(Modifier.size(40.dp).clip(CircleShape).background(Accent), contentAlignment = Alignment.Center) { Icon(Icons.Rounded.Mic, null, tint = Color.White, modifier = Modifier.size(20.dp)) }
                Column(Modifier.weight(1f)) {
                    Text("You said", style = T.cap, color = Muted)
                    Text("“$words”", style = T.head, color = Ink)
                }
                Pill("Say again", "Say it again") { again() }
            }
        }
        Spacer(Modifier.height(12.dp))
        Column(Modifier.padding(horizontal = 8.dp)) {
            listOf(Triple("netflix", "Netflix", NetflixRed), Triple("youtube", "YouTube", Color(0xFFE53935)), Triple("music", "YouTube Music", Color(0xFFFF6B6B)),
                Triple("bilibili", "Bilibili", Color(0xFF23ADE5)), Triple("channels", "Live TV channels", Accent)).forEach { (id, name, color) ->
                OptionRow(name, false, description = "Search $name", lead = { Box(Modifier.size(36.dp).clip(RoundedCornerShape(10.dp)).background(color)) }) { search(id, words) }
            }
        }
    }
}

