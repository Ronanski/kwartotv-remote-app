package com.kwartotv.remote.ui

import androidx.compose.foundation.background
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
 * A big touch button. Fires on press (not on release) so it feels instant, and repeats while held
 * when [repeat] is set (arrows, volume). Works with TalkBack through the click semantics.
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
    onFire: () -> Unit,
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val fire by rememberUpdatedState(onFire)
    var pressed by remember { mutableStateOf(false) }
    val colors = MaterialTheme.colorScheme
    val bg = when {
        pressed -> colors.primary
        primary -> colors.primary.copy(alpha = 0.9f)
        danger -> androidx.compose.ui.graphics.Color(0xFF3A1D24)
        else -> colors.surfaceVariant
    }
    val fg = when {
        pressed || primary -> colors.onPrimary
        danger -> androidx.compose.ui.graphics.Color(0xFFFFB3BB)
        else -> colors.onSurface
    }
    Box(
        modifier = modifier
            .heightIn(min = height)
            .clip(RoundedCornerShape(20.dp))
            .background(bg)
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
        Text(label, color = fg, fontSize = fontSize.sp, fontWeight = FontWeight.Medium)
    }
}
