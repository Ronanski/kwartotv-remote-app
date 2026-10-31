package com.kwartotv.remote.ui

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.FilterChip
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kwartotv.remote.AppViewModel
import com.kwartotv.remote.CHOSEN_FILTER
import com.kwartotv.remote.Cmd
import com.kwartotv.remote.FAVORITES_FILTER
import com.kwartotv.remote.FinderChannel
import com.kwartotv.remote.Haptics
import com.kwartotv.remote.catalogGroups
import com.kwartotv.remote.filterCatalog
import com.kwartotv.remote.net.Conn
import java.text.NumberFormat

/**
 * The channel finder: the TV checks every channel of every playlist from its own internet connection, and only the
 * ones that work are listed here, by category. Tap a channel to add it to (or remove it from) the TV's channels,
 * tap ☆ to star it. Thousands of rows are fine (one lazy list, filtering off the network thread).
 */
@Composable
fun ChannelFinder(vm: AppViewModel) {
    val client = vm.client
    val context = LocalContext.current
    val conn by client.conn.collectAsState()
    val catalog by client.catalog.collectAsState()
    val unsupported by client.catalogUnsupported.collectAsState()
    var query by rememberSaveable { mutableStateOf("") }
    var group by rememberSaveable { mutableStateOf<String?>(null) }
    val nf = remember { NumberFormat.getIntegerInstance() }

    LaunchedEffect(conn) { if (conn == Conn.Connected) client.requestCatalog() }

    fun notConnected() {
        Toast.makeText(context, "Not connected to the TV. Reconnecting…", Toast.LENGTH_SHORT).show()
        vm.reconnect()
    }

    val channels = catalog?.channels.orEmpty()
    val groups = remember(channels) { catalogGroups(channels) }
    val hasFavorites = remember(channels) { channels.any { it.favorite } }
    val chosenCount = remember(channels) { channels.count { it.chosen } }
    val chosen = group?.takeIf { it in groups || it == CHOSEN_FILTER || (it == FAVORITES_FILTER && hasFavorites) }
    val rows = remember(channels, query, chosen) { filterCatalog(channels, query, chosen) }
    val scan = catalog?.scan

    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        // Scan status and the button that starts it.
        Column(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surfaceContainer).padding(12.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            val c = catalog
            when {
                unsupported -> Text("Update KwartoTV on the PC to use the channel finder.", fontSize = 14.sp)
                c == null -> Text(if (conn == Conn.Connected) "Loading…" else "Connect to the TV to find channels.", fontSize = 14.sp)
                scan?.running == true -> {
                    Text("Checking channels on the TV… ${nf.format(scan.done)} of ${nf.format(scan.total)}", fontSize = 15.sp, fontWeight = FontWeight.Medium)
                    LinearProgressIndicator(progress = { scan.percent }, modifier = Modifier.fillMaxWidth())
                    Text("${nf.format(scan.ok)} work · ${nf.format(scan.dead)} do not. Working ones appear below as they pass.", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                else -> {
                    val unchecked = (c.all - c.checked).coerceAtLeast(0)
                    Text(
                        when {
                            c.all == 0 -> "No playlists on the TV yet. Add one on the TV (Settings → Add channels)."
                            c.checked == 0 -> "${nf.format(c.all)} channels in your playlists. Scan them to see which ones work."
                            else -> "${nf.format(c.total)} working channels · ${nf.format(chosenCount)} on your TV"
                        },
                        fontSize = 15.sp, fontWeight = FontWeight.Medium,
                    )
                    if (unchecked > 0 && c.checked > 0) Text("${nf.format(unchecked)} not checked yet.", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            if (c != null && !unsupported && c.all > 0) Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (scan?.running == true) {
                    RemoteKey("■  Stop", "Stop checking", Modifier.weight(1f), fontSize = 16, height = 48.dp) {
                        if (!client.send(Cmd.SCAN_STOP, null)) notConnected()
                    }
                } else {
                    RemoteKey(if (c.checked == 0) "Scan channels" else "Scan again", "Check every channel on the TV", Modifier.weight(1f), primary = true, fontSize = 16, height = 48.dp) {
                        if (!client.send(Cmd.SCAN_START, null)) notConnected()
                    }
                    if (c.checked > 0) RemoteKey("Re-check all", "Check every channel again, also the ones checked today", Modifier.weight(1f), fontSize = 16, height = 48.dp) {
                        if (!client.send(Cmd.SCAN_START, "full")) notConnected()
                    }
                }
            }
        }

        if (channels.isNotEmpty()) {
            OutlinedTextField(
                value = query, onValueChange = { query = it.take(40) }, singleLine = true,
                label = { Text("Search working channels") },
                modifier = Modifier.fillMaxWidth(),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search, autoCorrectEnabled = false),
            )
            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                item { FilterChip(selected = chosen == null, onClick = { Haptics.click(context); group = null }, label = { Text("All ${nf.format(channels.size)}") }) }
                if (hasFavorites) item {
                    FilterChip(selected = chosen == FAVORITES_FILTER, onClick = { Haptics.click(context); group = FAVORITES_FILTER }, label = { Text("★ Favorites") })
                }
                item { FilterChip(selected = chosen == CHOSEN_FILTER, onClick = { Haptics.click(context); group = CHOSEN_FILTER }, label = { Text("✓ On my TV") }) }
                items(groups, key = { it }) { g ->
                    FilterChip(selected = chosen == g, onClick = { Haptics.click(context); group = g }, label = { Text(g, maxLines = 1) })
                }
            }
            // A category is open: add or remove all of it at once.
            if (chosen != null && chosen != FAVORITES_FILTER && chosen != CHOSEN_FILTER) {
                val inGroup = rows.size
                val allOn = rows.isNotEmpty() && rows.all { it.chosen }
                RemoteKey(
                    if (allOn) "Remove all $inGroup from my TV" else "Add all $inGroup to my TV",
                    if (allOn) "Remove every channel of $chosen" else "Add every working channel of $chosen",
                    Modifier.fillMaxWidth(), fontSize = 15, height = 44.dp,
                ) { if (!client.chooseGroup(chosen, !allOn)) notConnected() }
            }
        }

        val message = when {
            catalog == null || unsupported -> null
            channels.isEmpty() && scan?.running == true -> "Working channels will appear here…"
            channels.isEmpty() && (catalog?.checked ?: 0) > 0 -> "No working channels found. The playlists may be offline or blocked from the TV's internet."
            channels.isNotEmpty() && rows.isEmpty() -> if (query.isNotBlank()) "No working channel matches \"${query.trim()}\"." else "Nothing here yet."
            else -> null
        }
        if (message != null) Text(message, color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 14.sp, modifier = Modifier.padding(top = 8.dp))

        LazyColumn(Modifier.weight(1f).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            items(rows, key = { it.id }) { ch ->
                FinderRow(
                    ch,
                    onToggle = { Haptics.click(context); if (!client.choose(ch.id, !ch.chosen)) notConnected() },
                    onStar = { Haptics.click(context); if (!client.star(ch.id, !ch.favorite)) notConnected() },
                )
            }
            val c = catalog
            if (c != null && c.total > channels.size) item {
                Text("Showing ${nf.format(channels.size)} of ${nf.format(c.total)} working channels. Search to find the rest.", color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 13.sp, modifier = Modifier.padding(vertical = 8.dp))
            }
        }
    }
}

@Composable
private fun FinderRow(ch: FinderChannel, onToggle: () -> Unit, onStar: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp))
            .background(if (ch.chosen) colors.surfaceVariant else colors.surfaceContainer)
            .clickable(onClickLabel = if (ch.chosen) "Remove ${ch.name} from my TV" else "Add ${ch.name} to my TV", onClick = onToggle)
            .padding(start = 12.dp, top = 6.dp, bottom = 6.dp, end = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(if (ch.chosen) "✓" else "+", Modifier.width(32.dp), fontSize = 22.sp, fontWeight = FontWeight.Bold, color = if (ch.chosen) Ok else AccentSoft)
        Column(Modifier.weight(1f)) {
            Text(ch.name, fontSize = 16.sp, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(if (ch.chosen) "${ch.group} · on your TV" else ch.group, fontSize = 13.sp, color = colors.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Text(
            if (ch.favorite) "★" else "☆",
            Modifier.size(48.dp).clip(RoundedCornerShape(24.dp)).clickable(onClick = onStar).padding(top = 8.dp)
                .semantics { contentDescription = if (ch.favorite) "Remove ${ch.name} from favorites" else "Add ${ch.name} to favorites" },
            fontSize = 24.sp, color = if (ch.favorite) Accent else colors.onSurfaceVariant,
            textAlign = androidx.compose.ui.text.style.TextAlign.Center,
        )
    }
}
