package com.kwartotv.remote.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

// A calm, modern dark palette: near-black blue backgrounds, soft cards, one warm accent.
val Accent = Color(0xFFFF7A45)
val Accent2 = Color(0xFFFF5470)
val AccentSoft = Color(0xFFFFB36B)
val Ok = Color(0xFF3DDC97)
val Bad = Color(0xFFFF5C6C)
val Card = Color(0xFF151923)
val CardHigh = Color(0xFF1C2130)
val Hairline = Color(0x14FFFFFF)
val Muted = Color(0xFF8E97AD)

/** The accent as a gentle gradient (primary buttons, the active tab). */
val AccentBrush = Brush.linearGradient(listOf(Accent, Accent2))

private val Scheme = darkColorScheme(
    primary = Accent,
    onPrimary = Color.White,
    secondary = AccentSoft,
    background = Color(0xFF0A0C11),
    onBackground = Color(0xFFF2F4F8),
    surface = Color(0xFF0A0C11),
    onSurface = Color(0xFFF2F4F8),
    surfaceVariant = CardHigh,
    onSurfaceVariant = Muted,
    surfaceContainer = Card,
    surfaceContainerHigh = CardHigh,
    surfaceContainerHighest = Color(0xFF232938),
    outline = Color(0xFF2A3042),
    outlineVariant = Hairline,
    error = Bad,
)

private val Type = Typography(
    headlineSmall = TextStyle(fontSize = 22.sp, fontWeight = FontWeight.SemiBold, letterSpacing = (-0.2).sp),
    titleLarge = TextStyle(fontSize = 20.sp, fontWeight = FontWeight.SemiBold, letterSpacing = (-0.1).sp),
    titleMedium = TextStyle(fontSize = 16.sp, fontWeight = FontWeight.SemiBold),
    titleSmall = TextStyle(fontSize = 14.sp, fontWeight = FontWeight.Medium),
    bodyLarge = TextStyle(fontSize = 16.sp, lineHeight = 22.sp),
    bodyMedium = TextStyle(fontSize = 14.sp, lineHeight = 20.sp),
    bodySmall = TextStyle(fontSize = 12.sp, lineHeight = 16.sp),
    labelLarge = TextStyle(fontSize = 14.sp, fontWeight = FontWeight.Medium, letterSpacing = 0.1.sp),
    labelMedium = TextStyle(fontSize = 12.sp, fontWeight = FontWeight.Medium, letterSpacing = 0.2.sp),
    labelSmall = TextStyle(fontSize = 11.sp, fontWeight = FontWeight.Medium, letterSpacing = 0.3.sp),
)

private val Shape = Shapes(
    extraSmall = RoundedCornerShape(8.dp), small = RoundedCornerShape(12.dp), medium = RoundedCornerShape(16.dp),
    large = RoundedCornerShape(22.dp), extraLarge = RoundedCornerShape(28.dp),
)

/** The remote is always dark: it is used in a dim living room. */
@Composable
fun KwartoTheme(content: @Composable () -> Unit) {
    @Suppress("UNUSED_VARIABLE") val unused = isSystemInDarkTheme()
    MaterialTheme(colorScheme = Scheme, typography = Type, shapes = Shape, content = content)
}
