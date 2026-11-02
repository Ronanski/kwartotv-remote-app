package com.kwartotv.remote.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.heightIn
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.automirrored.rounded.KeyboardArrowLeft
import androidx.compose.material.icons.automirrored.rounded.KeyboardArrowRight
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.KeyboardArrowDown
import androidx.compose.material.icons.rounded.KeyboardArrowUp
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.kwartotv.remote.Cmd

/** A round 56 dp key with its name under it (Back, Home, Voice…). */
@Composable
fun RoundKey(description: String, label: String, icon: ImageVector, modifier: Modifier = Modifier, repeat: Boolean = false,
             fill: Color = CardHigh, tint: Color = Ink, size: Dp = KEY, onFire: () -> Unit) {
    Column(modifier.width(maxOf(64.dp, size)), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        RemoteKey("", description, Modifier.size(size), repeat = repeat, shape = CircleShape, height = size, icon = icon, iconSize = 22.dp,
            fill = fill, tint = tint, outline = false, onFire = onFire)
        Text(label, style = T.mini, color = Muted, textAlign = TextAlign.Center)
    }
}

/**
 * A button that fires when the finger lets go (not while scrolling a list or a page): pills, wide buttons, list rows.
 * [description] is its spoken name.
 */
@Composable
fun Modifier.tap(description: String, shape: androidx.compose.ui.graphics.Shape, fill: Color, outline: Boolean, onClick: () -> Unit): Modifier {
    val ctx = androidx.compose.ui.platform.LocalContext.current
    return this.clip(shape).background(fill).then(if (outline) Modifier.border(1.dp, Hairline, shape) else Modifier)
        .clickable(onClickLabel = description) { com.kwartotv.remote.Haptics.click(ctx); onClick() }
        .semantics(mergeDescendants = true) { contentDescription = description; role = Role.Button }
}

/** A 44 dp pill: a choice (white when chosen) or a small action. */
@Composable
fun Pill(text: String, description: String, modifier: Modifier = Modifier, on: Boolean = false, onFire: () -> Unit) {
    Box(modifier.height(PILL).tap(description, RoundedCornerShape(PILL / 2), if (on) Ink else Card, !on, onFire), contentAlignment = Alignment.Center) {
        Text(text, style = T.label, color = if (on) Screen else Ink, textAlign = TextAlign.Center, modifier = Modifier.padding(horizontal = 8.dp))
    }
}

enum class CtaKind { Primary, White, Line, Lock }

/** A 52 dp wide button. */
@Composable
fun Cta(text: String, description: String, modifier: Modifier = Modifier, kind: CtaKind = CtaKind.Primary, icon: ImageVector? = null, height: Dp = CTA, onFire: () -> Unit) {
    val (fill, fg) = when (kind) {
        CtaKind.Primary -> Accent to Color.White
        CtaKind.White -> Ink to Screen
        CtaKind.Line -> Color.Transparent to Ink
        CtaKind.Lock -> LockPurple to Color.White
    }
    Box(modifier.fillMaxWidth().height(height).tap(description, RoundedCornerShape(height / 2), fill, kind == CtaKind.Line, onFire), contentAlignment = Alignment.Center) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(horizontal = 16.dp)) {
            if (icon != null) Icon(icon, null, tint = fg, modifier = Modifier.size(20.dp))
            Text(text, style = T.label.copy(fontSize = androidx.compose.ui.unit.TextUnit(15f, androidx.compose.ui.unit.TextUnitType.Sp)), color = fg, textAlign = TextAlign.Center)
        }
    }
}

/** A card: the one block on a screen that stands apart (what plays, a preview). */
@Composable
fun KCard(modifier: Modifier = Modifier, padding: Dp = 16.dp, content: @Composable ColumnScope.() -> Unit) {
    Column(modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(Card).border(1.dp, Hairline, RoundedCornerShape(20.dp)).padding(padding), content = content)
}

/** The round D-pad (240 dp) with OK in the middle. Arrows repeat while held. */
@Composable
fun DPad(send: (String, String?) -> Unit, size: Dp = 240.dp) {
    Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Box(Modifier.size(size).clip(CircleShape).background(Card).border(1.dp, Hairline, CircleShape), contentAlignment = Alignment.Center) {
            val key = Modifier.padding(6.dp).size(KEY)
            RemoteKey("", "Up", key.align(Alignment.TopCenter), repeat = true, bare = true, shape = CircleShape, height = KEY, icon = Icons.Rounded.KeyboardArrowUp, iconSize = 30.dp) { send(Cmd.UP, null) }
            RemoteKey("", "Down", key.align(Alignment.BottomCenter), repeat = true, bare = true, shape = CircleShape, height = KEY, icon = Icons.Rounded.KeyboardArrowDown, iconSize = 30.dp) { send(Cmd.DOWN, null) }
            RemoteKey("", "Left", key.align(Alignment.CenterStart), repeat = true, bare = true, shape = CircleShape, height = KEY, icon = Icons.AutoMirrored.Rounded.KeyboardArrowLeft, iconSize = 30.dp) { send(Cmd.LEFT, null) }
            RemoteKey("", "Right", key.align(Alignment.CenterEnd), repeat = true, bare = true, shape = CircleShape, height = KEY, icon = Icons.AutoMirrored.Rounded.KeyboardArrowRight, iconSize = 30.dp) { send(Cmd.RIGHT, null) }
            val ok = size * 0.4f
            RemoteKey("OK", "OK", Modifier.size(ok), shape = CircleShape, height = ok, fill = Color(0xFF2A2E38), outline = false, fontSize = 17) { send(Cmd.OK, null) }
        }
    }
}

/** Vol − · Mute · Vol + in one bar. */
@Composable
fun VolumeBar(send: (String, String?) -> Unit, modifier: Modifier = Modifier) {
    Row(modifier.fillMaxWidth().height(KEY).clip(RoundedCornerShape(KEY / 2)).background(Card).border(1.dp, Hairline, RoundedCornerShape(KEY / 2))) {
        @Composable
        fun RowScope.part(text: String, desc: String, cmd: String, repeat: Boolean) =
            RemoteKey("", desc, Modifier.weight(1f).fillMaxHeight(), repeat = repeat, bare = true, shape = RoundedCornerShape(0.dp), height = KEY,
                content = { Text(text, style = T.label, color = Ink) }) { send(cmd, null) }
        part("Vol −", "Volume down", Cmd.VOL_DOWN, true)
        Box(Modifier.width(1.dp).fillMaxHeight().background(Hairline))
        part("Mute", "Mute", Cmd.MUTE, false)
        Box(Modifier.width(1.dp).fillMaxHeight().background(Hairline))
        part("Vol +", "Volume up", Cmd.VOL_UP, true)
    }
}

/** A workspace's own top bar: ‹ back, its colour mark, its name, and a short state on the right. */
@Composable
fun WorkspaceBar(title: String, mark: Color, state: String?, onBack: (() -> Unit)?) {
    Row(Modifier.fillMaxWidth().height(56.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        if (onBack != null) RemoteKey("", "Back to the remote", Modifier.size(40.dp), bare = true, shape = CircleShape, height = 40.dp, icon = Icons.AutoMirrored.Rounded.ArrowBack, iconSize = 22.dp) { onBack() }
        Box(Modifier.width(8.dp).height(20.dp).clip(RoundedCornerShape(2.dp)).background(mark))
        Text(title, style = T.title, color = Ink, modifier = Modifier.weight(1f))
        if (state != null) Text(state, style = T.cap, color = Muted)
    }
}

/** A bottom sheet in the app's style: grabber, a title (and a line under it), a close button, then its content. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun KSheet(title: String, subtitle: String?, onClose: () -> Unit, content: @Composable ColumnScope.() -> Unit) {
    val state = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    ModalBottomSheet(
        onDismissRequest = onClose, sheetState = state, containerColor = Color(0xFF16181E), contentColor = Ink,
        shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp),
        dragHandle = { Box(Modifier.padding(top = 10.dp, bottom = 12.dp).width(40.dp).height(5.dp).clip(RoundedCornerShape(3.dp)).background(Color(0xFF3A3D46))) },
    ) {
        Column(Modifier.fillMaxWidth().navigationBarsPadding().padding(bottom = 24.dp)) {
            Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 16.dp), verticalAlignment = Alignment.Top, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Column(Modifier.weight(1f)) {
                    Text(title, style = T.title, color = Ink)
                    if (!subtitle.isNullOrBlank()) Text(subtitle, style = T.cap, color = Muted)
                }
                RemoteKey("", "Close", Modifier.size(40.dp), shape = CircleShape, height = 40.dp, fill = CardHigh, outline = false, icon = Icons.Rounded.Close, iconSize = 18.dp) { onClose() }
            }
            Spacer(Modifier.height(16.dp))
            content()
        }
    }
}

/** One choice in a sheet: a radio and its name (the chosen one is filled). */
@Composable
fun OptionRow(text: String, on: Boolean, modifier: Modifier = Modifier, sub: String? = null, description: String = text, lead: (@Composable () -> Unit)? = null, onPick: () -> Unit) {
    Row(modifier.fillMaxWidth().heightIn(min = 52.dp).tap(description, RoundedCornerShape(14.dp), if (on) Color(0xFF262A33) else Color.Transparent, false, onPick)
        .padding(horizontal = 14.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        if (lead != null) lead() else Box(Modifier.size(20.dp).clip(CircleShape).border(if (on) 6.dp else 2.dp, if (on) Ink else Color(0xFF5A5F6B), CircleShape))
        Column(Modifier.weight(1f)) {
            Text(text, style = if (on) T.body.copy(fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold) else T.body, color = Ink)
            if (sub != null) Text(sub, style = T.cap, color = Muted)
        }
    }
}
