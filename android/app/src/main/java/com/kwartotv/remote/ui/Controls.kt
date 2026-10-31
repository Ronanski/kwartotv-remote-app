package com.kwartotv.remote.ui

import androidx.compose.foundation.background
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Icon
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import com.kwartotv.remote.Haptics
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.onClick
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * A big touch button: an icon (or a short label) with an optional caption under it. Fires on press (not on release) so
 * it feels instant, repeats while held when [repeat] is set (arrows, volume), and shrinks a little while pressed.
 * Works with TalkBack through the click semantics.
 */
@Composable
fun RemoteKey(
    label: String,
    description: String,
    modifier: Modifier = Modifier,
    primary: Boolean = false,
    danger: Boolean = false,
    repeat: Boolean = false,
    fontSize: Int = 22,
    height: Dp = 60.dp,
    icon: ImageVector? = null,
    caption: String? = null,
    content: (@Composable () -> Unit)? = null,
    onFire: () -> Unit,
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val fire by rememberUpdatedState(onFire)
    var pressed by remember { mutableStateOf(false) }
    val scale by animateFloatAsState(if (pressed) 0.95f else 1f, animationSpec = tween(90), label = "press")
    val shape = RoundedCornerShape(20.dp)
    val fg = when {
        primary -> Color.White
        danger -> Color(0xFFFF8A96)
        else -> MaterialTheme.colorScheme.onSurface
    }
    val base = Modifier
        .heightIn(min = height)
        .graphicsLayer { scaleX = scale; scaleY = scale }
        .clip(shape)
    val filled = when {
        primary -> base.background(AccentBrush)
        danger -> base.background(Color(0xFF2A161B)).border(1.dp, Color(0x33FF5C6C), shape)
        else -> base.background(if (pressed) CardHigh else Card).border(1.dp, Hairline, shape)
    }
    Box(
        modifier = modifier.then(filled)
            .semantics(mergeDescendants = true) {
                role = Role.Button
                contentDescription = description
                onClick { fire(); true }
            }
            .pointerInput(repeat) {
                awaitEachGesture {
                    awaitFirstDown()
                    pressed = true
                    if (primary) Haptics.heavy(context) else Haptics.click(context)
                    fire()
                    val job = if (repeat) scope.launch {
                        delay(380)
                        while (isActive) { fire(); delay(110) }
                    } else null
                    waitForUpOrCancellation()
                    job?.cancel()
                    pressed = false
                }
            },
        contentAlignment = Alignment.Center,
    ) {
        if (content != null) content() else Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(2.dp)) {
            if (icon != null) Icon(icon, contentDescription = null, tint = fg, modifier = Modifier.size(if (caption == null && label.isEmpty()) 30.dp else 24.dp))
            if (icon == null && label.isNotEmpty()) Text(label, color = fg, fontSize = fontSize.sp, fontWeight = FontWeight.SemiBold, letterSpacing = (-0.2).sp)
            val small = caption ?: if (icon != null) label.ifEmpty { null } else null
            if (small != null) Text(small, color = if (primary) Color.White.copy(alpha = 0.9f) else MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 12.sp, fontWeight = FontWeight.Medium)
        }
    }
}

/** A small rounded section title ("APPS", "VOLUME"...). */
@Composable
fun SectionLabel(text: String, modifier: Modifier = Modifier) {
    Text(text.uppercase(), modifier = modifier.padding(top = 8.dp, bottom = 2.dp), color = MaterialTheme.colorScheme.onSurfaceVariant,
        fontSize = 11.sp, fontWeight = FontWeight.SemiBold, letterSpacing = 1.2.sp)
}
