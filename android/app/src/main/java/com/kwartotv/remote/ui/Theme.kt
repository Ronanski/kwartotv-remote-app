package com.kwartotv.remote.ui

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kwartotv.remote.R

// The palette: a near-black screen, soft grey cards, one blue accent; Netflix red and lock purple only in their workspaces.
val Screen = Color(0xFF0E0F13)
val Card = Color(0xFF181A20)
val CardHigh = Color(0xFF22252D)
val Hairline = Color(0xFF2A2D36)
val Muted = Color(0xFFA3A8B3)
val Soft = Color(0xFFD4D7DD)
val Ink = Color(0xFFF2F3F5)
val Accent = Color(0xFF3567E8)
val AccentSoft = Color(0xFF9DB8FF)
val NetflixRed = Color(0xFFD20A12)
val LockPurple = Color(0xFF7B5CFF)
val Ok = Color(0xFF3DD68C)
val Bad = Color(0xFFFF5C6C)
val Live = Color(0xFFE5484D)

/** The accent for primary buttons. */
val AccentBrush = Brush.linearGradient(listOf(Accent, Accent))

val Figtree = FontFamily(
    Font(R.font.figtree_regular, FontWeight.Normal), Font(R.font.figtree_medium, FontWeight.Medium),
    Font(R.font.figtree_semibold, FontWeight.SemiBold), Font(R.font.figtree_bold, FontWeight.Bold),
)

/**
 * The type scale: every text in the app is one of these six (as in the design). Long text wraps; nothing is cut.
 * title 20 · head 17 · body 15 · label 14 · cap 13 · mini 12.
 */
object T {
    val title = TextStyle(fontFamily = Figtree, fontSize = 20.sp, lineHeight = 28.sp, fontWeight = FontWeight.SemiBold)
    val head = TextStyle(fontFamily = Figtree, fontSize = 17.sp, lineHeight = 24.sp, fontWeight = FontWeight.SemiBold)
    val body = TextStyle(fontFamily = Figtree, fontSize = 15.sp, lineHeight = 22.sp, fontWeight = FontWeight.Medium)
    val label = TextStyle(fontFamily = Figtree, fontSize = 14.sp, lineHeight = 20.sp, fontWeight = FontWeight.SemiBold)
    val cap = TextStyle(fontFamily = Figtree, fontSize = 13.sp, lineHeight = 18.sp, fontWeight = FontWeight.Medium)
    val mini = TextStyle(fontFamily = Figtree, fontSize = 12.sp, lineHeight = 16.sp, fontWeight = FontWeight.Medium)
}

/** The sizes: every round button is [KEY], every pill [PILL], every wide button [CTA]. */
val KEY = 56.dp
val PILL = 44.dp
val CTA = 52.dp

private val Scheme = darkColorScheme(
    primary = Accent,
    onPrimary = Color.White,
    secondary = AccentSoft,
    background = Screen,
    onBackground = Ink,
    surface = Screen,
    onSurface = Ink,
    surfaceVariant = CardHigh,
    onSurfaceVariant = Muted,
    surfaceContainerLowest = Screen,
    surfaceContainerLow = Card,
    surfaceContainer = Card,
    surfaceContainerHigh = Color(0xFF16181E),
    surfaceContainerHighest = CardHigh,
    outline = Hairline,
    outlineVariant = Hairline,
    error = Bad,
)

private val Type = Typography(
    displaySmall = T.title, headlineLarge = T.title, headlineMedium = T.title, headlineSmall = T.title,
    titleLarge = T.title, titleMedium = T.head, titleSmall = T.label,
    bodyLarge = T.body, bodyMedium = T.cap, bodySmall = T.mini,
    labelLarge = T.label, labelMedium = T.mini, labelSmall = T.mini,
)

private val Shape = Shapes(
    extraSmall = RoundedCornerShape(8.dp), small = RoundedCornerShape(12.dp), medium = RoundedCornerShape(16.dp),
    large = RoundedCornerShape(20.dp), extraLarge = RoundedCornerShape(28.dp),
)

/** The remote is always dark: it is used in a dim living room. */
@Composable
fun KwartoTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = Scheme, typography = Type, shapes = Shape, content = content)
}
