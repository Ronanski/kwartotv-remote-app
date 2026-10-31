package com.kwartotv.remote.ui

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.automirrored.rounded.Backspace
import androidx.compose.material.icons.automirrored.rounded.KeyboardReturn
import androidx.compose.material.icons.automirrored.rounded.List
import androidx.compose.material.icons.automirrored.rounded.Send
import androidx.compose.material.icons.automirrored.rounded.Undo
import androidx.compose.material.icons.automirrored.rounded.VolumeDown
import androidx.compose.material.icons.automirrored.rounded.VolumeOff
import androidx.compose.material.icons.automirrored.rounded.VolumeUp
import androidx.compose.material.icons.rounded.Bedtime
import androidx.compose.material.icons.rounded.ContentPaste
import androidx.compose.material.icons.rounded.ExpandMore
import androidx.compose.material.icons.rounded.Forward10
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.Keyboard
import androidx.compose.material.icons.rounded.KeyboardArrowDown
import androidx.compose.material.icons.rounded.KeyboardArrowLeft
import androidx.compose.material.icons.rounded.KeyboardArrowRight
import androidx.compose.material.icons.rounded.KeyboardArrowUp
import androidx.compose.material.icons.rounded.LiveTv
import androidx.compose.material.icons.rounded.Lock
import androidx.compose.material.icons.rounded.LockOpen
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.PlayCircle
import androidx.compose.material.icons.rounded.PowerSettingsNew
import androidx.compose.material.icons.rounded.Replay10
import androidx.compose.material.icons.rounded.RestartAlt
import androidx.compose.material.icons.rounded.SettingsRemote
import androidx.compose.material.icons.rounded.SkipNext
import androidx.compose.material.icons.rounded.SkipPrevious
import androidx.compose.material.icons.rounded.StarOutline
import androidx.compose.material.icons.rounded.Stop
import androidx.compose.material.icons.rounded.Tv
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
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
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kwartotv.remote.AppViewModel
import com.kwartotv.remote.Cmd
import com.kwartotv.remote.FAVORITES_FILTER
import com.kwartotv.remote.LineupChannel
import com.kwartotv.remote.filterLineup
import com.kwartotv.remote.lineupGroups
import com.kwartotv.remote.Haptics
import com.kwartotv.remote.net.Conn
import kotlin.math.abs
import kotlin.math.roundToInt

private enum class Tab(val label: String, val icon: ImageVector) {
    Remote("Remote", Icons.Rounded.SettingsRemote), Media("Media", Icons.Rounded.PlayCircle), Channels("Channels", Icons.Rounded.LiveTv),
    Keys("Keyboard", Icons.Rounded.Keyboard), Power("Power", Icons.Rounded.PowerSettingsNew)
}

@Composable
fun RemoteScreen(vm: AppViewModel) {
    val client = vm.client
    val conn by client.conn.collectAsState()
    val now by client.now.collectAsState()
    val tvs by vm.tvs.collectAsState()
    val selectedId by vm.selectedId.collectAsState()
    val error by client.lastError.collectAsState()
    val context = LocalContext.current
    val tv = tvs.firstOrNull { it.deviceId == selectedId } ?: tvs.first()
    var tab by rememberSaveable { mutableStateOf(Tab.Remote) }
    var menu by remember { mutableStateOf(false) }
    // Sign in to Netflix / Bilibili for the TV on the phone (from the menu, or when the TV asks).
    var signInFor by remember { mutableStateOf<String?>(null) }
    SignInForTv(vm, signInFor) { signInFor = null }

    LaunchedEffect(error) {
        error?.let {
            Toast.makeText(context, if (it == "no such channel") "No such channel" else if (it.startsWith("The TV is locked")) it else "Not done: $it", Toast.LENGTH_SHORT).show()
            client.clearError()
        }
    }
    // A text box on the TV waits for text: the phone's keyboard opens (with its password manager).
    TypeOnPhone(vm)

    val send: (String, String?) -> Unit = { cmd, arg ->
        if (!client.send(cmd, arg)) {
            Toast.makeText(context, "Not connected to the TV. Reconnecting…", Toast.LENGTH_SHORT).show()
            vm.reconnect()
        }
    }

    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        bottomBar = {
            NavigationBar(containerColor = MaterialTheme.colorScheme.surfaceContainer) {
                Tab.entries.forEach { t ->
                    NavigationBarItem(
                        selected = tab == t, onClick = { Haptics.click(context); tab = t },
                        icon = { Icon(t.icon, contentDescription = null) },
                        label = { Text(t.label, fontSize = 11.sp, fontWeight = FontWeight.Medium) },
                        colors = NavigationBarItemDefaults.colors(
                            selectedIconColor = Accent, selectedTextColor = MaterialTheme.colorScheme.onSurface,
                            indicatorColor = Accent.copy(alpha = 0.16f),
                            unselectedIconColor = MaterialTheme.colorScheme.onSurfaceVariant, unselectedTextColor = MaterialTheme.colorScheme.onSurfaceVariant,
                        ),
                    )
                }
            }
        },
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize().padding(horizontal = 16.dp)) {
            // ---- header: TV picker + connection state
            Row(Modifier.fillMaxWidth().padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.weight(1f)) {
                    // The TV (tap for other TVs, sign-ins and settings) and how the connection is.
                    val (label, color) = when (conn) {
                        Conn.Connected -> "Connected" to Ok
                        Conn.Connecting, Conn.Retrying -> "Connecting…" to AccentSoft
                        is Conn.Rejected -> "Not allowed" to Bad
                        Conn.Idle -> "Offline" to Bad
                    }
                    Row(
                        Modifier.clip(RoundedCornerShape(16.dp)).clickable { menu = true }.padding(vertical = 6.dp, horizontal = 4.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Box(Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(AccentBrush), contentAlignment = Alignment.Center) {
                            Icon(Icons.Rounded.Tv, null, tint = Color.White, modifier = Modifier.size(22.dp))
                        }
                        Column(Modifier.padding(start = 12.dp)) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Text(tv.name, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                                Icon(Icons.Rounded.ExpandMore, null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(20.dp))
                            }
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Box(Modifier.size(7.dp).clip(CircleShape).background(color))
                                Text("  $label", color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 12.sp)
                            }
                        }
                    }
                    DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                        tvs.forEach { t ->
                            DropdownMenuItem(
                                text = { Text(if (t.deviceId == tv.deviceId) "✓  ${t.name}" else "     ${t.name}") },
                                onClick = { menu = false; vm.select(t) },
                            )
                        }
                        DropdownMenuItem(text = { Text("+  Add another TV") }, onClick = { menu = false; vm.adding.value = true })
                        DropdownMenuItem(text = { Text("Sign in to Netflix for the TV") }, onClick = { menu = false; signInFor = "netflix" })
                        DropdownMenuItem(text = { Text("Sign in to Bilibili for the TV") }, onClick = { menu = false; signInFor = "bilibili" })
                        DropdownMenuItem(text = { Text("Reconnect") }, onClick = { menu = false; vm.reconnect() })
                        DropdownMenuItem(text = { Text("Check for updates") }, onClick = { menu = false; vm.checkForUpdate(manual = true) })
                        DropdownMenuItem(text = { Text("Forget ${tv.name}") }, onClick = { menu = false; vm.forget(tv) })
                    }
                }
                Icon(Icons.Rounded.MoreVert, "Menu", tint = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.size(40.dp).clip(CircleShape).clickable { menu = true }.padding(8.dp))
            }

            UpdateBanner(vm)
            if (conn is Conn.Rejected) {
                Row(
                    Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surfaceContainer).padding(12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text((conn as Conn.Rejected).reason, Modifier.weight(1f), fontSize = 14.sp)
                    TextButton(onClick = { vm.forget(tv); vm.adding.value = true }) { Text("Pair again") }
                }
            } else {
                // What the TV is on now.
                Row(
                    Modifier.fillMaxWidth().padding(vertical = 6.dp).clip(RoundedCornerShape(18.dp)).background(Card)
                        .border(1.dp, Hairline, RoundedCornerShape(18.dp)).padding(horizontal = 14.dp, vertical = 12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text("LIVE", Modifier.clip(RoundedCornerShape(6.dp)).background(Accent.copy(alpha = 0.16f)).padding(horizontal = 7.dp, vertical = 3.dp),
                        color = Accent, fontSize = 10.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.sp)
                    Text(if (now.number > 0) "  ${now.number}" else "  –", fontSize = 18.sp, fontWeight = FontWeight.Bold)
                    Text("  ${now.name ?: "No channel yet"}", color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis, fontSize = 15.sp)
                }
            }

            Box(Modifier.weight(1f).fillMaxWidth().padding(top = 8.dp)) {
                when (tab) {
                    Tab.Remote -> NavTab(vm, send)
                    Tab.Media -> MediaTab(send)
                    Tab.Channels -> ChannelsTab(vm, send)
                    Tab.Keys -> KeysTab(send)
                    Tab.Power -> PowerTab(send)
                }
            }
        }
    }
}

@Composable
private fun NavTab(vm: AppViewModel, send: (String, String?) -> Unit) {
    var kind by remember { mutableIntStateOf(vm.padKind) }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            val ctx = LocalContext.current
            FilterChip(selected = kind == 0, onClick = { Haptics.click(ctx); kind = 0; vm.padKind = 0 }, label = { Text("D-pad") })
            FilterChip(selected = kind == 1, onClick = { Haptics.click(ctx); kind = 1; vm.padKind = 1 }, label = { Text("Touchpad") })
            FilterChip(selected = kind == 2, onClick = { Haptics.click(ctx); kind = 2; vm.padKind = 2 }, label = { Text("Mouse") })
        }
        when (kind) {
            1 -> Touchpad(send)
            2 -> MousePad(send)
            else -> DPad(send)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Back", "Back", Modifier.weight(1f), icon = Icons.AutoMirrored.Rounded.ArrowBack) { send(Cmd.BACK, null) }
            RemoteKey("Home", "Home", Modifier.weight(1f), icon = Icons.Rounded.Home) { send(Cmd.HOME, null) }
            RemoteKey("Mute", "Mute", Modifier.weight(1f), icon = Icons.AutoMirrored.Rounded.VolumeOff) { send(Cmd.MUTE, null) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Vol −", "Volume down", Modifier.weight(1f), repeat = true, icon = Icons.AutoMirrored.Rounded.VolumeDown) { send(Cmd.VOL_DOWN, null) }
            RemoteKey("Vol +", "Volume up", Modifier.weight(1f), repeat = true, icon = Icons.AutoMirrored.Rounded.VolumeUp) { send(Cmd.VOL_UP, null) }
        }
        SectionLabel("Apps")
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            AppTile(QuickApp.LiveTv, Modifier.weight(1f)) { send(Cmd.APP_LIVE, null) }
            AppTile(QuickApp.YouTube, Modifier.weight(1f)) { send(Cmd.APP_YOUTUBE, null) }
            AppTile(QuickApp.Netflix, Modifier.weight(1f)) { send(Cmd.APP_NETFLIX, null) }
            AppTile(QuickApp.Bilibili, Modifier.weight(1f)) { send(Cmd.APP_BILIBILI, null) }
        }
    }
}

@Composable
private fun DPad(send: (String, String?) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Box(Modifier.weight(1f)); RemoteKey("", "Up", Modifier.weight(1f), repeat = true, height = 72.dp, icon = Icons.Rounded.KeyboardArrowUp) { send(Cmd.UP, null) }; Box(Modifier.weight(1f))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("", "Left", Modifier.weight(1f), repeat = true, height = 72.dp, icon = Icons.Rounded.KeyboardArrowLeft) { send(Cmd.LEFT, null) }
            RemoteKey("OK", "OK", Modifier.weight(1f), primary = true, height = 72.dp) { send(Cmd.OK, null) }
            RemoteKey("", "Right", Modifier.weight(1f), repeat = true, height = 72.dp, icon = Icons.Rounded.KeyboardArrowRight) { send(Cmd.RIGHT, null) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Box(Modifier.weight(1f)); RemoteKey("", "Down", Modifier.weight(1f), repeat = true, height = 72.dp, icon = Icons.Rounded.KeyboardArrowDown) { send(Cmd.DOWN, null) }; Box(Modifier.weight(1f))
        }
    }
}

@Composable
private fun Touchpad(send: (String, String?) -> Unit) {
    val context = LocalContext.current
    val step = with(LocalDensity.current) { 44.dp.toPx() }
    Box(
        Modifier.fillMaxWidth().height(280.dp).clip(RoundedCornerShape(26.dp)).background(MaterialTheme.colorScheme.surfaceContainer)
            .pointerInput(Unit) { detectTapGestures(onTap = { Haptics.heavy(context); send(Cmd.OK, null) }, onLongPress = { Haptics.heavy(context); send(Cmd.BACK, null) }) }
            .pointerInput(step) {
                var acc = Offset.Zero
                detectDragGestures(onDragStart = { acc = Offset.Zero }) { change, drag ->
                    change.consume()
                    acc += drag
                    while (abs(acc.x) >= step || abs(acc.y) >= step) {
                        Haptics.tick(context)
                        if (abs(acc.x) >= abs(acc.y)) {
                            send(if (acc.x > 0) Cmd.RIGHT else Cmd.LEFT, null); acc = Offset(acc.x - step * (if (acc.x > 0) 1 else -1), 0f)
                        } else {
                            send(if (acc.y > 0) Cmd.DOWN else Cmd.UP, null); acc = Offset(0f, acc.y - step * (if (acc.y > 0) 1 else -1))
                        }
                    }
                }
            },
        contentAlignment = Alignment.Center,
    ) {
        Text("Swipe to move\nTap = OK  ·  Long press = Back", color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = androidx.compose.ui.text.style.TextAlign.Center)
    }
}

/** Moves the mouse pointer on the TV: drag = move, tap = click, long press = right click. For websites such as Netflix. */
@Composable
private fun MousePad(send: (String, String?) -> Unit) {
    val context = LocalContext.current
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Box(
            Modifier.fillMaxWidth().height(250.dp).clip(RoundedCornerShape(26.dp)).background(MaterialTheme.colorScheme.surfaceContainer)
                .pointerInput(Unit) { detectTapGestures(onTap = { Haptics.click(context); send(Cmd.MOUSE_CLICK, null) }, onLongPress = { Haptics.heavy(context); send(Cmd.MOUSE_RIGHT, null) }) }
                .pointerInput(Unit) {
                    var ax = 0f
                    var ay = 0f
                    var last = 0L
                    detectDragGestures(onDragStart = { ax = 0f; ay = 0f; last = 0L }) { change, drag ->
                        change.consume()
                        ax += drag.x * 2.2f; ay += drag.y * 2.2f
                        val now = System.currentTimeMillis()
                        if (now - last >= 25) {
                            val dx = ax.roundToInt().coerceIn(-300, 300); val dy = ay.roundToInt().coerceIn(-300, 300)
                            if (dx != 0 || dy != 0) { send(Cmd.MOUSE_MOVE, "$dx,$dy"); ax = 0f; ay = 0f; last = now }
                        }
                    }
                },
            contentAlignment = Alignment.Center,
        ) {
            Text("Drag to move the pointer\nTap = click  ·  Long press = right click", color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = androidx.compose.ui.text.style.TextAlign.Center)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Click", "Mouse click", Modifier.weight(1f), fontSize = 18) { send(Cmd.MOUSE_CLICK, null) }
            RemoteKey("▲ Scroll", "Scroll up", Modifier.weight(1f), repeat = true, fontSize = 16) { send(Cmd.MOUSE_SCROLL, "-2") }
            RemoteKey("▼ Scroll", "Scroll down", Modifier.weight(1f), repeat = true, fontSize = 16) { send(Cmd.MOUSE_SCROLL, "2") }
        }
    }
}

@Composable
private fun MediaTab(send: (String, String?) -> Unit) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("", "Previous", Modifier.weight(1f), height = 72.dp, icon = Icons.Rounded.SkipPrevious) { send(Cmd.PREV, null) }
            RemoteKey("Play / Pause", "Play or pause", Modifier.weight(1f), primary = true, height = 72.dp, icon = Icons.Rounded.PlayArrow) { send(Cmd.PLAY_PAUSE, null) }
            RemoteKey("", "Next", Modifier.weight(1f), height = 72.dp, icon = Icons.Rounded.SkipNext) { send(Cmd.NEXT, null) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("−10 s", "Back 10 seconds", Modifier.weight(1f), repeat = true, icon = Icons.Rounded.Replay10) { send(Cmd.SEEK, "-10") }
            RemoteKey("Stop", "Stop", Modifier.weight(1f), icon = Icons.Rounded.Stop) { send(Cmd.STOP, null) }
            RemoteKey("+10 s", "Forward 10 seconds", Modifier.weight(1f), repeat = true, icon = Icons.Rounded.Forward10) { send(Cmd.SEEK, "10") }
        }
        Text("Volume", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Vol −", "Volume down", Modifier.weight(1f), repeat = true, height = 80.dp, icon = Icons.AutoMirrored.Rounded.VolumeDown) { send(Cmd.VOL_DOWN, null) }
            RemoteKey("Mute", "Mute", Modifier.weight(1f), height = 80.dp, icon = Icons.AutoMirrored.Rounded.VolumeOff) { send(Cmd.MUTE, null) }
            RemoteKey("Vol +", "Volume up", Modifier.weight(1f), repeat = true, height = 80.dp, icon = Icons.AutoMirrored.Rounded.VolumeUp) { send(Cmd.VOL_UP, null) }
        }
    }
}

@Composable
private fun ChannelsTab(vm: AppViewModel, send: (String, String?) -> Unit) {
    val ctx = LocalContext.current
    // 0 = the TV's channel list, 1 = find working channels, 2 = number keypad
    var view by rememberSaveable { mutableIntStateOf(0) }
    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilterChip(selected = view == 0, onClick = { Haptics.click(ctx); view = 0 }, label = { Text("My channels") })
            FilterChip(selected = view == 1, onClick = { Haptics.click(ctx); view = 1 }, label = { Text("Find channels") })
            FilterChip(selected = view == 2, onClick = { Haptics.click(ctx); view = 2 }, label = { Text("Keypad") })
        }
        Box(Modifier.weight(1f).fillMaxWidth()) {
            when (view) {
                0 -> ChannelGuide(vm, send)
                1 -> ChannelFinder(vm)
                else -> ChannelKeypad(send)
            }
        }
    }
}

/** The TV's channels on the phone, like a cable guide: search, category chips, tap a row to watch it. */
@Composable
private fun ChannelGuide(vm: AppViewModel, send: (String, String?) -> Unit) {
    val client = vm.client
    val context = LocalContext.current
    val conn by client.conn.collectAsState()
    val lineup by client.lineup.collectAsState()
    val unsupported by client.lineupUnsupported.collectAsState()
    val now by client.now.collectAsState()
    var query by rememberSaveable { mutableStateOf("") }
    var group by rememberSaveable { mutableStateOf<String?>(null) }

    // Ask when the list opens, and again after every reconnect (the TV only pushes changes to a live connection).
    LaunchedEffect(conn) { if (conn == Conn.Connected) client.requestLineup() }

    val channels = lineup?.channels.orEmpty()
    val groups = remember(channels) { lineupGroups(channels) }
    val hasFavorites = remember(channels) { channels.any { it.favorite } }
    // A category that disappeared from the lineup (or favorites that were all removed) falls back to "All".
    val chosen = group?.takeIf { it in groups || (it == FAVORITES_FILTER && hasFavorites) }
    val rows = remember(channels, query, chosen) { filterLineup(channels, query, chosen) }

    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedTextField(
                value = query, onValueChange = { query = it.take(40) }, singleLine = true,
                label = { Text("Search name or number") },
                modifier = Modifier.weight(1f),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search, autoCorrectEnabled = false),
            )
            RemoteKey("↻", "Refresh the channel list", Modifier.size(56.dp), fontSize = 22, height = 56.dp) {
                if (!client.requestLineup()) {
                    Toast.makeText(context, "Not connected to the TV. Reconnecting…", Toast.LENGTH_SHORT).show()
                    vm.reconnect()
                }
            }
        }
        if (channels.isNotEmpty()) {
            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                item { FilterChip(selected = chosen == null, onClick = { Haptics.click(context); group = null }, label = { Text("All") }) }
                if (hasFavorites) item {
                    FilterChip(selected = chosen == FAVORITES_FILTER, onClick = { Haptics.click(context); group = FAVORITES_FILTER }, label = { Text("★ Favorites") })
                }
                items(groups, key = { it }) { g ->
                    FilterChip(selected = chosen == g, onClick = { Haptics.click(context); group = g }, label = { Text(g, maxLines = 1) })
                }
            }
        }
        val message = when {
            unsupported -> "This TV app is too old to send its channel list. Update KwartoTV on the PC, or use the keypad."
            lineup == null && conn != Conn.Connected -> "Connect to the TV to see its channels."
            lineup == null -> "Loading channels…"
            channels.isEmpty() -> "The TV has no channels yet. Open \"Find channels\" to scan your playlists and pick working ones."
            rows.isEmpty() -> "No channel matches \"${query.trim()}\"."
            else -> null
        }
        if (message != null) {
            Text(message, color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 14.sp, modifier = Modifier.padding(top = 12.dp))
        } else {
            LazyColumn(Modifier.weight(1f).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                items(rows, key = { it.number }) { ch ->
                    ChannelRow(ch, watching = ch.number == now.number, onStar = ch.id?.let { id ->
                        {
                            Haptics.click(context)
                            if (!client.star(id, !ch.favorite)) { Toast.makeText(context, "Not connected to the TV. Reconnecting…", Toast.LENGTH_SHORT).show(); vm.reconnect() }
                        }
                    }) {
                        Haptics.click(context)
                        send(Cmd.CH_NUMBER, ch.number.toString())
                    }
                }
                val total = lineup?.total ?: 0
                if (total > channels.size) item {
                    Text(
                        "Showing the first ${channels.size} of $total channels. Use the keypad for the rest.",
                        color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 13.sp, modifier = Modifier.padding(vertical = 8.dp),
                    )
                }
            }
        }
    }
}

@Composable
private fun ChannelRow(ch: LineupChannel, watching: Boolean, onStar: (() -> Unit)?, onTune: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp))
            .background(if (watching) colors.surfaceVariant else colors.surfaceContainer)
            .clickable(onClickLabel = "Watch ${ch.name}", onClick = onTune)
            .padding(horizontal = 14.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(ch.number.toString(), Modifier.width(52.dp), fontSize = 20.sp, fontWeight = FontWeight.Bold, color = if (watching) Accent else AccentSoft)
        Column(Modifier.weight(1f)) {
            Text(ch.name, fontSize = 16.sp, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(ch.now ?: ch.group, fontSize = 13.sp, color = colors.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        if (watching) Text("ON", fontSize = 12.sp, fontWeight = FontWeight.Bold, color = Ok)
        if (onStar != null) Text(
            if (ch.favorite) "★" else "☆",
            Modifier.size(44.dp).clip(CircleShape).clickable(onClickLabel = if (ch.favorite) "Remove from favorites" else "Add to favorites", onClick = onStar).padding(top = 6.dp),
            fontSize = 22.sp, color = if (ch.favorite) Accent else colors.onSurfaceVariant,
            textAlign = androidx.compose.ui.text.style.TextAlign.Center,
        )
    }
}

@Composable
private fun ChannelKeypad(send: (String, String?) -> Unit) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        // Opens the channel list on the TV, grouped by category: ◀ ▶ change category, ▲ ▼ choose, OK watches.
        RemoteKey("Channel list on TV", "Open the channel list on the TV", Modifier.fillMaxWidth(), primary = true, height = 64.dp, icon = Icons.AutoMirrored.Rounded.List) { send(Cmd.CH_LIST, null) }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("CH +", "Channel up", Modifier.weight(1f), repeat = true, icon = Icons.Rounded.KeyboardArrowUp) { send(Cmd.CH_NEXT, null) }
            RemoteKey("Last", "Last channel", Modifier.weight(1f), icon = Icons.AutoMirrored.Rounded.Undo) { send(Cmd.CH_LAST, null) }
            RemoteKey("CH −", "Channel down", Modifier.weight(1f), repeat = true, icon = Icons.Rounded.KeyboardArrowDown) { send(Cmd.CH_PREV, null) }
        }
        Text("Number pad", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 6.dp))
        listOf("123", "456", "789").forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                row.forEach { d -> RemoteKey(d.toString(), "Digit $d", Modifier.weight(1f), fontSize = 26, height = 64.dp) { send(Cmd.CH_DIGIT, d.toString()) } }
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Favorite", "Add or remove favorite", Modifier.weight(1f), height = 64.dp, icon = Icons.Rounded.StarOutline) { send(Cmd.CH_FAVORITE, null) }
            RemoteKey("0", "Digit 0", Modifier.weight(1f), fontSize = 26, height = 64.dp) { send(Cmd.CH_DIGIT, "0") }
            RemoteKey("Go", "Go to typed channel", Modifier.weight(1f), primary = true, fontSize = 22, height = 64.dp) { send(Cmd.CH_COMMIT, null) }
        }
    }
}

@Composable
private fun KeysTab(send: (String, String?) -> Unit) {
    var text by remember { mutableStateOf("") }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Select a text box on the TV, then type here. Letters appear on the TV as you type.", color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 14.sp)
        OutlinedTextField(
            value = text,
            onValueChange = { new ->
                val old = text
                when {
                    new.length > old.length && new.startsWith(old) -> new.substring(old.length).chunked(40).forEach { send(Cmd.TEXT, it) }
                    new.length < old.length && old.startsWith(new) -> repeat(old.length - new.length) { send(Cmd.BACKSPACE, null) }
                    else -> { repeat(old.length) { send(Cmd.BACKSPACE, null) }; new.chunked(40).forEach { send(Cmd.TEXT, it) } }
                }
                text = new
            },
            label = { Text("Type on the TV") },
            modifier = Modifier.fillMaxWidth(),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send, autoCorrectEnabled = false),
            keyboardActions = KeyboardActions(onSend = { send(Cmd.ENTER, null); text = "" }),
        )
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Delete", "Backspace", Modifier.weight(1f), repeat = true, icon = Icons.AutoMirrored.Rounded.Backspace) { send(Cmd.BACKSPACE, null); if (text.isNotEmpty()) text = text.dropLast(1) }
            RemoteKey("Back", "Back", Modifier.weight(1f), icon = Icons.AutoMirrored.Rounded.ArrowBack) { send(Cmd.BACK, null) }
            RemoteKey("Enter", "Enter", Modifier.weight(1f), primary = true, icon = Icons.AutoMirrored.Rounded.KeyboardReturn) { send(Cmd.ENTER, null); text = "" }
        }
        SendLinkBox(send)
    }
}

/** Paste a playlist (or stream) link on the phone and send it: the TV adds it and opens the channel picker. */
@Composable
private fun SendLinkBox(send: (String, String?) -> Unit) {
    val context = LocalContext.current
    var link by remember { mutableStateOf("") }
    Text("Send a playlist link to the TV", fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 18.dp))
    Text("Paste an .m3u / .m3u8 link here instead of typing it on the TV. The TV downloads it and lets you choose the channels.", color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 13.sp)
    OutlinedTextField(
        value = link, onValueChange = { link = it.trim() }, singleLine = true,
        label = { Text("https://…/playlist.m3u") },
        modifier = Modifier.fillMaxWidth(),
        keyboardOptions = KeyboardOptions(keyboardType = androidx.compose.ui.text.input.KeyboardType.Uri, imeAction = ImeAction.Send),
        keyboardActions = KeyboardActions(onSend = { if (link.isNotEmpty()) { send(Cmd.PLAYLIST_ADD, link); link = "" } }),
    )
    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        RemoteKey("Paste", "Paste a link from the clipboard", Modifier.weight(1f), icon = Icons.Rounded.ContentPaste) {
            val cm = context.getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
            link = cm.primaryClip?.getItemAt(0)?.coerceToText(context)?.toString()?.trim() ?: ""
            if (link.isEmpty()) Toast.makeText(context, "The clipboard is empty", Toast.LENGTH_SHORT).show()
        }
        RemoteKey("Send to TV", "Send the link to the TV", Modifier.weight(1f), primary = true, icon = Icons.AutoMirrored.Rounded.Send) {
            if (link.startsWith("http://") || link.startsWith("https://")) { send(Cmd.PLAYLIST_ADD, link); Toast.makeText(context, "Sent. Look at the TV.", Toast.LENGTH_SHORT).show(); link = "" }
            else Toast.makeText(context, "Paste a link that starts with https://", Toast.LENGTH_SHORT).show()
        }
    }
}

@Composable
private fun PowerTab(send: (String, String?) -> Unit) {
    var confirm by remember { mutableStateOf<Pair<String, String>?>(null) }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Unlock", "Unlock the TV", Modifier.weight(1f), primary = true, height = 80.dp, icon = Icons.Rounded.LockOpen) { send(Cmd.SCREEN_ON, null) }
            RemoteKey("Lock · clock", "Lock the TV and show the clock", Modifier.weight(1f), height = 80.dp, icon = Icons.Rounded.Lock) { send(Cmd.SCREEN_OFF, null) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Sleep PC", "Put the whole PC to sleep", Modifier.weight(1f), height = 80.dp, icon = Icons.Rounded.Bedtime) { confirm = Cmd.SLEEP to "Put the whole TV PC to sleep?" }
            RemoteKey("Restart PC", "Restart the whole PC", Modifier.weight(1f), danger = true, height = 80.dp, icon = Icons.Rounded.RestartAlt) { confirm = Cmd.RESTART to "Restart the whole TV PC (laptop/computer), not just the app?" }
        }
        RemoteKey("Shut down PC", "Shut down the whole PC", Modifier.fillMaxWidth(), danger = true, height = 80.dp, icon = Icons.Rounded.PowerSettingsNew) {
            confirm = Cmd.SHUTDOWN to "Shut down the whole TV PC (laptop/computer)? You will need to switch it on by hand."
        }
        Text("These control the whole PC, not only the KwartoTV app.", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 6.dp))
    }
    confirm?.let { (cmd, question) ->
        AlertDialog(
            onDismissRequest = { confirm = null },
            title = { Text(question) },
            confirmButton = { TextButton(onClick = { send(cmd, null); confirm = null }) { Text("Yes") } },
            dismissButton = { TextButton(onClick = { confirm = null }) { Text("Cancel") } },
        )
    }
}
