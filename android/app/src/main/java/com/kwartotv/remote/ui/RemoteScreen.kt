package com.kwartotv.remote.ui

import android.widget.Toast
import androidx.compose.foundation.background
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
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kwartotv.remote.AppViewModel
import com.kwartotv.remote.Cmd
import com.kwartotv.remote.Haptics
import com.kwartotv.remote.net.Conn
import kotlin.math.abs
import kotlin.math.roundToInt

private enum class Tab(val label: String, val glyph: String) {
    Remote("Remote", "◉"), Media("Media", "▶"), Channels("Channels", "#"), Keys("Keyboard", "⌨"), Power("Power", "⏻")
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

    LaunchedEffect(error) {
        error?.let {
            Toast.makeText(context, if (it == "no such channel") "No such channel" else "Not done: $it", Toast.LENGTH_SHORT).show()
            client.clearError()
        }
    }
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
                        icon = { Text(t.glyph, fontSize = 20.sp) },
                        label = { Text(t.label, fontSize = 12.sp) },
                    )
                }
            }
        },
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize().padding(horizontal = 16.dp)) {
            // ---- header: TV picker + connection state
            Row(Modifier.fillMaxWidth().padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.weight(1f)) {
                    TextButton(onClick = { menu = true }) {
                        Text(tv.name, fontSize = 20.sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
                        Text("  ▾", fontSize = 16.sp)
                    }
                    DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                        tvs.forEach { t ->
                            DropdownMenuItem(
                                text = { Text(if (t.deviceId == tv.deviceId) "✓  ${t.name}" else "     ${t.name}") },
                                onClick = { menu = false; vm.select(t) },
                            )
                        }
                        DropdownMenuItem(text = { Text("+  Add another TV") }, onClick = { menu = false; vm.adding.value = true })
                        DropdownMenuItem(text = { Text("Reconnect") }, onClick = { menu = false; vm.reconnect() })
                        DropdownMenuItem(text = { Text("Check for updates") }, onClick = { menu = false; vm.checkForUpdate(manual = true) })
                        DropdownMenuItem(text = { Text("Forget ${tv.name}") }, onClick = { menu = false; vm.forget(tv) })
                    }
                }
                val (label, color) = when (conn) {
                    Conn.Connected -> "Connected" to Ok
                    Conn.Connecting, Conn.Retrying -> "Connecting…" to AccentSoft
                    is Conn.Rejected -> "Not allowed" to Bad
                    Conn.Idle -> "Offline" to Bad
                }
                Box(Modifier.size(10.dp).clip(CircleShape).background(color))
                Text("  $label", color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 14.sp)
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
                Row(
                    Modifier.fillMaxWidth().padding(vertical = 4.dp).clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surfaceContainer).padding(horizontal = 14.dp, vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(if (now.number > 0) now.number.toString() else "–", fontSize = 24.sp, fontWeight = FontWeight.Bold, color = AccentSoft)
                    Text("   ${now.name ?: "No channel yet"}", color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1)
                }
            }

            Box(Modifier.weight(1f).fillMaxWidth().padding(top = 8.dp)) {
                when (tab) {
                    Tab.Remote -> NavTab(vm, send)
                    Tab.Media -> MediaTab(send)
                    Tab.Channels -> ChannelsTab(send)
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
            RemoteKey("⟵", "Back", Modifier.weight(1f)) { send(Cmd.BACK, null) }
            RemoteKey("⌂", "Home", Modifier.weight(1f)) { send(Cmd.HOME, null) }
            RemoteKey("🔇", "Mute", Modifier.weight(1f)) { send(Cmd.MUTE, null) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("−", "Volume down", Modifier.weight(1f), repeat = true, fontSize = 30) { send(Cmd.VOL_DOWN, null) }
            Text("Volume", Modifier.weight(1f).align(Alignment.CenterVertically), textAlign = androidx.compose.ui.text.style.TextAlign.Center, color = MaterialTheme.colorScheme.onSurfaceVariant)
            RemoteKey("+", "Volume up", Modifier.weight(1f), repeat = true, fontSize = 30) { send(Cmd.VOL_UP, null) }
        }
        Text("Apps", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 6.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Live TV", "Open Live TV", Modifier.weight(1f), fontSize = 16) { send(Cmd.APP_LIVE, null) }
            RemoteKey("YouTube", "Open YouTube", Modifier.weight(1f), fontSize = 16) { send(Cmd.APP_YOUTUBE, null) }
            RemoteKey("Netflix", "Open Netflix", Modifier.weight(1f), fontSize = 16) { send(Cmd.APP_NETFLIX, null) }
        }
    }
}

@Composable
private fun DPad(send: (String, String?) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Box(Modifier.weight(1f)); RemoteKey("▲", "Up", Modifier.weight(1f), repeat = true, height = 72.dp) { send(Cmd.UP, null) }; Box(Modifier.weight(1f))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("◀", "Left", Modifier.weight(1f), repeat = true, height = 72.dp) { send(Cmd.LEFT, null) }
            RemoteKey("OK", "OK", Modifier.weight(1f), primary = true, height = 72.dp) { send(Cmd.OK, null) }
            RemoteKey("▶", "Right", Modifier.weight(1f), repeat = true, height = 72.dp) { send(Cmd.RIGHT, null) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Box(Modifier.weight(1f)); RemoteKey("▼", "Down", Modifier.weight(1f), repeat = true, height = 72.dp) { send(Cmd.DOWN, null) }; Box(Modifier.weight(1f))
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
            RemoteKey("⏮", "Previous", Modifier.weight(1f), height = 72.dp) { send(Cmd.PREV, null) }
            RemoteKey("⏯", "Play or pause", Modifier.weight(1f), primary = true, height = 72.dp) { send(Cmd.PLAY_PAUSE, null) }
            RemoteKey("⏭", "Next", Modifier.weight(1f), height = 72.dp) { send(Cmd.NEXT, null) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("↺ 10", "Back 10 seconds", Modifier.weight(1f), repeat = true, fontSize = 18) { send(Cmd.SEEK, "-10") }
            RemoteKey("⏹", "Stop", Modifier.weight(1f)) { send(Cmd.STOP, null) }
            RemoteKey("10 ↻", "Forward 10 seconds", Modifier.weight(1f), repeat = true, fontSize = 18) { send(Cmd.SEEK, "10") }
        }
        Text("Volume", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("−", "Volume down", Modifier.weight(1f), repeat = true, fontSize = 30, height = 80.dp) { send(Cmd.VOL_DOWN, null) }
            RemoteKey("🔇", "Mute", Modifier.weight(1f), height = 80.dp) { send(Cmd.MUTE, null) }
            RemoteKey("+", "Volume up", Modifier.weight(1f), repeat = true, fontSize = 30, height = 80.dp) { send(Cmd.VOL_UP, null) }
        }
    }
}

@Composable
private fun ChannelsTab(send: (String, String?) -> Unit) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("▲ CH", "Channel up", Modifier.weight(1f), repeat = true, fontSize = 18) { send(Cmd.CH_NEXT, null) }
            RemoteKey("↩ Last", "Last channel", Modifier.weight(1f), fontSize = 18) { send(Cmd.CH_LAST, null) }
            RemoteKey("▼ CH", "Channel down", Modifier.weight(1f), repeat = true, fontSize = 18) { send(Cmd.CH_PREV, null) }
        }
        Text("Number pad", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 6.dp))
        listOf("123", "456", "789").forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                row.forEach { d -> RemoteKey(d.toString(), "Digit $d", Modifier.weight(1f), fontSize = 26, height = 64.dp) { send(Cmd.CH_DIGIT, d.toString()) } }
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("★", "Add or remove favorite", Modifier.weight(1f), fontSize = 26, height = 64.dp) { send(Cmd.CH_FAVORITE, null) }
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
            RemoteKey("⌫", "Backspace", Modifier.weight(1f), repeat = true) { send(Cmd.BACKSPACE, null); if (text.isNotEmpty()) text = text.dropLast(1) }
            RemoteKey("⟵ Back", "Back", Modifier.weight(1f), fontSize = 18) { send(Cmd.BACK, null) }
            RemoteKey("⏎ Enter", "Enter", Modifier.weight(1f), primary = true, fontSize = 18) { send(Cmd.ENTER, null); text = "" }
        }
    }
}

@Composable
private fun PowerTab(send: (String, String?) -> Unit) {
    var confirm by remember { mutableStateOf<Pair<String, String>?>(null) }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Screen on", "Turn the TV screen on", Modifier.weight(1f), fontSize = 18, height = 72.dp) { send(Cmd.SCREEN_ON, null) }
            RemoteKey("Screen off", "Turn the TV screen off", Modifier.weight(1f), fontSize = 18, height = 72.dp) { send(Cmd.SCREEN_OFF, null) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            RemoteKey("Sleep PC", "Put the whole PC to sleep", Modifier.weight(1f), fontSize = 18, height = 72.dp) { confirm = Cmd.SLEEP to "Put the whole TV PC to sleep?" }
            RemoteKey("Restart PC", "Restart the whole PC", Modifier.weight(1f), danger = true, fontSize = 18, height = 72.dp) { confirm = Cmd.RESTART to "Restart the whole TV PC (laptop/computer), not just the app?" }
        }
        RemoteKey("Shut down PC", "Shut down the whole PC", Modifier.fillMaxWidth(), danger = true, fontSize = 18, height = 72.dp) {
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
