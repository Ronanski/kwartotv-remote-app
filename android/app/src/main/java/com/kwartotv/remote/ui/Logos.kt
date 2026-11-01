package com.kwartotv.remote.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.LiveTv
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** The quick apps on the remote, each with its logo (drawn here, for personal use). */
enum class QuickApp(val title: String) { LiveTv("Live TV"), YouTube("YouTube"), YtMusic("YT Music"), Netflix("Netflix"), Bilibili("Bilibili") }

/** A logo tile: the app's logo on its own colour, its name under it. */
@Composable
fun AppTile(app: QuickApp, modifier: Modifier = Modifier, onOpen: () -> Unit) {
    RemoteKey("", "Open ${app.title}", modifier, height = 92.dp, content = {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
            AppLogo(app, 40.dp)
            Text(app.title, fontSize = 12.sp, fontWeight = FontWeight.Medium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }, onFire = onOpen)
}

@Composable
fun AppLogo(app: QuickApp, size: Dp) {
    val shape = RoundedCornerShape(size * 0.24f)
    when (app) {
        QuickApp.LiveTv -> Box(Modifier.size(size).clip(shape).background(AccentBrush), contentAlignment = Alignment.Center) {
            Icon(Icons.Rounded.LiveTv, null, tint = Color.White, modifier = Modifier.size(size * 0.6f))
        }
        QuickApp.YouTube -> Canvas(Modifier.size(size)) {
            // red rounded play button with a white triangle
            val w = this.size.width; val h = this.size.height
            val bh = h * 0.7f
            drawRoundRect(Color(0xFFFF0033), Offset(0f, (h - bh) / 2), Size(w, bh), CornerRadius(bh * 0.28f))
            val p = Path().apply { moveTo(w * 0.41f, h * 0.35f); lineTo(w * 0.66f, h * 0.5f); lineTo(w * 0.41f, h * 0.65f); close() }
            drawPath(p, Color.White)
        }
        QuickApp.YtMusic -> Canvas(Modifier.size(size)) {
            // red disc, a white ring and a white play triangle
            val w = this.size.width; val h = this.size.height
            drawCircle(Color(0xFFFF0033), w / 2, Offset(w / 2, h / 2))
            drawCircle(Color.White, w * 0.3f, Offset(w / 2, h / 2), style = androidx.compose.ui.graphics.drawscope.Stroke(w * 0.05f))
            val p = Path().apply { moveTo(w * 0.43f, h * 0.36f); lineTo(w * 0.64f, h * 0.5f); lineTo(w * 0.43f, h * 0.64f); close() }
            drawPath(p, Color.White)
        }
        QuickApp.Netflix -> Box(Modifier.size(size).clip(shape).background(Color.Black), contentAlignment = Alignment.Center) {
            Canvas(Modifier.size(size * 0.62f)) {
                // the red N: two posts and a ribbon from top left to bottom right
                val w = this.size.width; val h = this.size.height; val t = w * 0.27f
                drawRect(Color(0xFFB1060F), Offset(0f, 0f), Size(t, h))
                drawRect(Color(0xFFB1060F), Offset(w - t, 0f), Size(t, h))
                val ribbon = Path().apply { moveTo(0f, 0f); lineTo(t, 0f); lineTo(w, h); lineTo(w - t, h); close() }
                drawPath(ribbon, Color(0xFFE50914))
            }
        }
        QuickApp.Bilibili -> Box(Modifier.size(size).clip(shape).background(Color(0xFF00A1D6)), contentAlignment = Alignment.Center) {
            Canvas(Modifier.size(size * 0.68f)) {
                // a little TV with two antennae and two eyes
                val w = this.size.width; val h = this.size.height
                val stroke = w * 0.09f
                drawLine(Color.White, Offset(w * 0.32f, h * 0.06f), Offset(w * 0.44f, h * 0.22f), stroke, StrokeCap.Round)
                drawLine(Color.White, Offset(w * 0.68f, h * 0.06f), Offset(w * 0.56f, h * 0.22f), stroke, StrokeCap.Round)
                drawRoundRect(Color.White, Offset(w * 0.04f, h * 0.24f), Size(w * 0.92f, h * 0.7f), CornerRadius(w * 0.16f), style = androidx.compose.ui.graphics.drawscope.Stroke(stroke))
                drawLine(Color.White, Offset(w * 0.28f, h * 0.52f), Offset(w * 0.38f, h * 0.58f), stroke, StrokeCap.Round)
                drawLine(Color.White, Offset(w * 0.72f, h * 0.52f), Offset(w * 0.62f, h * 0.58f), stroke, StrokeCap.Round)
            }
        }
    }
}
