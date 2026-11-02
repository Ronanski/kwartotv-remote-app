package com.kwartotv.remote.ui

import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Lock
import androidx.compose.material.icons.rounded.LockOpen
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material.icons.rounded.PhotoLibrary
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kwartotv.remote.AppViewModel
import com.kwartotv.remote.Cmd
import com.kwartotv.remote.LockState
import com.kwartotv.remote.net.Conn
import kotlinx.coroutines.delay
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

private val DEFAULT_LOCK = LockState(false, "classic", "FFFFFF", 100, true, true, true, "black", "", "", 0, 20, 10, 0)

/**
 * The Lock & Screensaver workspace: a live preview of what the TV's lock screen looks like (with the slideshow's own
 * photos, from the TV), Lock and Unlock, and every option of the lock screen. A change shows in the preview and on the TV.
 */
@Composable
internal fun LockTab(vm: AppViewModel, send: (String, String?) -> Unit) {
    val client = vm.client
    val conn by client.conn.collectAsState()
    val reported by client.lock.collectAsState()
    val tvApp by client.tvApp.collectAsState()
    val music by client.music.collectAsState()
    val photo by client.lockPhoto.collectAsState()
    val s = reported ?: DEFAULT_LOCK
    val locked = s.locked || tvApp == "locked"
    var folders by remember { mutableStateOf(false) }
    LaunchedEffect(conn) { if (conn == Conn.Connected) send(Cmd.LOCK_GET, null) }
    // The preview's photos: the next one every few seconds (on the TV they change at the chosen speed).
    LaunchedEffect(s.bg, s.folder, s.photos, conn) {
        if (s.bg != "photos" || s.photos <= 0 || conn != Conn.Connected) return@LaunchedEffect
        var i = 0
        while (true) { send(Cmd.LOCK_PHOTO, i.toString()); i++; delay(6000) }
    }
    fun set(key: String, value: String) = send(Cmd.LOCK_SET, "$key=$value")

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        WorkspaceBar("Lock & Screensaver", LockPurple, if (locked) "Locked" else "Unlocked", null)
        KCard(padding = 12.dp) {
            Row(Modifier.fillMaxWidth().padding(bottom = 10.dp), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(6.dp).clip(CircleShape).background(Live))
                Text("  Live preview: what the TV shows", style = T.mini.copy(fontWeight = FontWeight.SemiBold), color = Muted)
            }
            Preview(s, photo?.jpeg, music.on && music.title.isNotBlank(), music.title, music.artist)
            if (s.bg == "photos" && (photo?.count ?: 0) > 0) Text("Photo ${(photo!!.index % photo!!.count) + 1} of ${photo!!.count}", style = T.mini, color = Muted, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(top = 8.dp))
        }
        Spacer(Modifier.height(16.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Cta("Lock TV", "Lock the TV and show the clock", Modifier.weight(1f), kind = CtaKind.Lock, icon = Icons.Rounded.Lock, height = KEY) { send(Cmd.SCREEN_OFF, null) }
            Cta("Unlock", "Unlock the TV", Modifier.weight(1f), kind = if (locked) CtaKind.White else CtaKind.Line, icon = Icons.Rounded.LockOpen, height = KEY) { send(Cmd.SCREEN_ON, null) }
        }
        Text("Locked, the TV shows this screen. Music and volume still work.", style = T.cap, color = Muted, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(top = 8.dp))

        Group("Background") {
            Choices(listOf("photos" to "Photos", "black" to "Black", "midnight" to "Midnight"), s.bg) { set("bg", it) }
        }
        if (s.bg == "photos") {
            Spacer(Modifier.height(16.dp))
            KCard {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Icon(Icons.Rounded.PhotoLibrary, null, tint = Ink, modifier = Modifier.size(24.dp))
                    Column(Modifier.weight(1f)) {
                        Text(s.folderName.ifBlank { "No folder yet" }, style = T.label, color = Ink)
                        Text(if (s.folderName.isBlank()) "Choose the photos" else "${s.photos} photos", style = T.cap, color = Muted)
                    }
                    Pill("Change", "Choose the photo folder") { client.askLockFolders(); folders = true }
                }
            }
            Group("Next photo every") {
                Choices(listOf("10" to "10 sec", "20" to "20 sec", "30" to "30 sec", "60" to "1 min", "300" to "5 min"), s.every.toString()) { set("every", it) }
            }
        }
        Group("Clock style") { Choices(listOf("classic" to "Classic", "big" to "Big", "corner" to "Corner"), s.clock) { set("clock", it) } }
        Group("Clock color") {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterHorizontally)) {
                listOf("FFFFFF" to "White", "FFD8A8" to "Warm", "9DB8FF" to "Blue", "C9B6FF" to "Lavender").forEach { (hex, name) ->
                    val on = s.color.equals(hex, ignoreCase = true)
                    Box(Modifier.size(PILL).tap("Clock color $name", CircleShape, Color(("FF$hex").toLong(16)), false) { set("color", hex) }
                        .border(3.dp, if (on) Accent else Screen, CircleShape))
                }
            }
        }
        Group("Brightness") { Choices(listOf("100" to "Bright", "70" to "Normal", "40" to "Dim"), s.dim.toString()) { set("dim", it) } }
        Group("Show on the screen") {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Pill("Date", if (s.date) "Hide the date" else "Show the date", Modifier.weight(1f), on = s.date) { set("date", if (s.date) "0" else "1") }
                Pill("Song", if (s.song) "Hide the song" else "Show the song", Modifier.weight(1f), on = s.song) { set("song", if (s.song) "0" else "1") }
                Pill("Bible verse", if (s.verse) "Hide the Bible verse" else "Show the Bible verse", Modifier.weight(1f), on = s.verse) { set("verse", if (s.verse) "0" else "1") }
            }
        }
        Group("Screensaver when nothing plays") {
            Choices(listOf("0" to "Off", "5" to "5 min", "10" to "10 min", "30" to "30 min"), s.saver.toString(), prefix = "Screensaver after ") { set("saver", it) }
        }
        Spacer(Modifier.height(24.dp))
        KCard {
            Text("Sleep timer", style = T.label, color = Muted, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth())
            Text(if (s.timer > 0) "The TV stops and locks in ${s.timer} min" else "Off: the TV stays on", style = T.cap, color = Soft, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(top = 4.dp, bottom = 12.dp))
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf(15, 30, 60, 90).forEach { m -> Pill(if (m < 60) "$m min" else if (m == 60) "1 hr" else "1.5 hr", "Sleep in $m minutes", Modifier.weight(1f)) { send(Cmd.SLEEP_TIMER, m.toString()) } }
            }
            Spacer(Modifier.height(8.dp))
            Pill("Cancel the timer", "Cancel the sleep timer", Modifier.fillMaxWidth(), on = false) { send(Cmd.SLEEP_TIMER, "0") }
        }
        Spacer(Modifier.height(24.dp))
    }
    if (folders) FolderSheet(vm, s.folder, close = { folders = false }) { path -> set("folder", path); folders = false }
}

@Composable
private fun Group(label: String, content: @Composable () -> Unit) {
    Spacer(Modifier.height(24.dp))
    Text(label, style = T.label, color = Muted, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp))
    content()
}

@Composable
private fun Choices(options: List<Pair<String, String>>, current: String, prefix: String = "", pick: (String) -> Unit) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        options.forEach { (value, text) -> Pill(text, prefix + text, Modifier.weight(1f), on = value == current) { pick(value) } }
    }
}

/** The lock screen as the TV draws it, small: the background (photo, black or midnight), the clock in its style and color, the date, the song, the verse. */
@Composable
private fun Preview(s: LockState, jpeg: ByteArray?, songOn: Boolean, song: String, artist: String) {
    var now by remember { mutableStateOf(LocalDateTime.now()) }
    LaunchedEffect(Unit) { while (true) { delay(15_000); now = LocalDateTime.now() } }
    val bitmap = remember(jpeg) { jpeg?.let { try { BitmapFactory.decodeByteArray(it, 0, it.size)?.asImageBitmap() } catch (_: Exception) { null } } }
    val color = try { Color(("FF" + s.color).toLong(16)) } catch (_: Exception) { Color.White }
    Box(Modifier.fillMaxWidth().aspectRatio(16f / 9f).clip(RoundedCornerShape(12.dp)).background(if (s.bg == "midnight") Color(0xFF0B1430) else Color.Black)) {
        if (s.bg == "photos" && bitmap != null) {
            Image(bitmap, null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
            Box(Modifier.fillMaxSize().background(Color.Black.copy(alpha = 0.45f)))
        }
        // the classic clock sits in a thin ring, as on the TV
        if (s.clock == "classic") androidx.compose.foundation.Canvas(Modifier.fillMaxSize()) {
            val r = size.height * 0.42f
            drawCircle(Color.White.copy(alpha = 0.12f), r, center, style = androidx.compose.ui.graphics.drawscope.Stroke(1.5f))
            drawArc(Color(0xFFFF7A45).copy(alpha = 0.8f), -90f, 120f, false, androidx.compose.ui.geometry.Offset(center.x - r, center.y - r),
                androidx.compose.ui.geometry.Size(r * 2, r * 2), style = androidx.compose.ui.graphics.drawscope.Stroke(2.5f))
        }
        val corner = s.clock == "corner"
        Column(
            Modifier.fillMaxSize().padding(16.dp),
            horizontalAlignment = if (corner) Alignment.Start else Alignment.CenterHorizontally,
            verticalArrangement = if (corner) Arrangement.Bottom else Arrangement.Center,
        ) {
            val size = when (s.clock) { "big" -> 56.sp; "corner" -> 30.sp; else -> 40.sp }
            Row(verticalAlignment = Alignment.Bottom) {
                Text(now.format(DateTimeFormatter.ofPattern("h:mm")), color = color, fontFamily = Figtree, fontSize = size, lineHeight = size,
                    fontWeight = if (s.clock == "big") FontWeight.Normal else FontWeight.Medium)
                Text(" " + now.format(DateTimeFormatter.ofPattern("a")).lowercase(), color = color.copy(alpha = 0.7f), style = T.mini, modifier = Modifier.padding(bottom = 4.dp))
            }
            if (s.date) Text(now.format(DateTimeFormatter.ofPattern("EEEE, MMMM d")), color = color.copy(alpha = 0.8f), style = T.mini)
            if (s.verse && !corner) Text("“The Lord is my shepherd; I shall not want.”", color = Color.White.copy(alpha = 0.85f), style = T.mini.copy(fontStyle = FontStyle.Italic), textAlign = TextAlign.Center, modifier = Modifier.padding(top = 6.dp))
            if (s.song && songOn) Row(
                Modifier.padding(top = 6.dp).clip(RoundedCornerShape(12.dp)).background(Color.Black.copy(alpha = 0.45f)).padding(horizontal = 10.dp, vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Icon(Icons.Rounded.MusicNote, null, tint = color, modifier = Modifier.size(12.dp))
                Text(" " + listOf(song, artist).filter { it.isNotBlank() }.joinToString(" · "), color = color, style = T.mini)
            }
        }
        val dim = when (s.dim) { 70 -> 0.3f; 40 -> 0.6f; else -> 0f }
        if (dim > 0f) Box(Modifier.fillMaxSize().background(Color.Black.copy(alpha = dim)))
    }
}

/** The folders on the TV PC the photos can come from. */
@Composable
private fun FolderSheet(vm: AppViewModel, current: String, close: () -> Unit, pick: (String) -> Unit) {
    val list by vm.client.lockFolders.collectAsState()
    KSheet("Photos from", "Folders on the TV PC", close) {
        val l = list
        when {
            l == null -> Text("Asking the TV…", style = T.body, color = Muted, modifier = Modifier.padding(horizontal = 20.dp))
            l.isEmpty() -> Text("No photo folders on the TV PC. Put photos in its Pictures folder.", style = T.body, color = Muted, modifier = Modifier.padding(horizontal = 20.dp))
            else -> LazyColumn(Modifier.fillMaxWidth().heightIn(max = 480.dp).padding(horizontal = 8.dp)) {
                items(l) { f -> OptionRow(f.name, f.path.equals(current, ignoreCase = true), sub = f.path, description = "Photos from ${f.name}") { pick(f.path) } }
            }
        }
    }
}
