package com.kwartotv.remote.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val Accent = Color(0xFFFF7A45)
val AccentSoft = Color(0xFFFFB36B)
val Ok = Color(0xFF4CD08A)
val Bad = Color(0xFFFF5C6C)

private val Scheme = darkColorScheme(
    primary = Accent,
    onPrimary = Color(0xFF1A0D05),
    secondary = AccentSoft,
    background = Color(0xFF0D0F14),
    onBackground = Color(0xFFEEF1F7),
    surface = Color(0xFF0D0F14),
    onSurface = Color(0xFFEEF1F7),
    surfaceVariant = Color(0xFF1F2330),
    onSurfaceVariant = Color(0xFF9AA3B8),
    surfaceContainer = Color(0xFF171A22),
    outline = Color(0xFF2A2F3D),
    error = Bad,
)

/** The remote is always dark: it is used in a dim living room. */
@Composable
fun KwartoTheme(content: @Composable () -> Unit) {
    @Suppress("UNUSED_VARIABLE") val unused = isSystemInDarkTheme()
    MaterialTheme(colorScheme = Scheme, content = content)
}
