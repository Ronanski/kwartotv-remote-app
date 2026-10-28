package com.kwartotv.remote

import android.content.Context
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager

/**
 * A short buzz on every press, like the buttons of a real remote. Uses the vibrator directly, because the
 * view-based haptics are switched off by many phones (Samsung "touch vibration" setting).
 */
object Haptics {
    private var vibrator: Vibrator? = null

    private fun vib(context: Context): Vibrator? {
        vibrator?.let { return it }
        val v = if (Build.VERSION.SDK_INT >= 31)
            (context.applicationContext.getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as? VibratorManager)?.defaultVibrator
        else @Suppress("DEPRECATION") context.applicationContext.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
        vibrator = v
        return v
    }

    /** Button press. */
    fun click(context: Context) = play(context, VibrationEffect.EFFECT_CLICK, 18, 160)

    /** Light tick for each step while dragging a pad. */
    fun tick(context: Context) = play(context, VibrationEffect.EFFECT_TICK, 8, 90)

    /** Heavier buzz for the OK key and long press. */
    fun heavy(context: Context) = play(context, VibrationEffect.EFFECT_HEAVY_CLICK, 30, 255)

    private fun play(context: Context, predefined: Int, fallbackMs: Long, amplitude: Int) {
        val v = vib(context) ?: return
        if (!v.hasVibrator()) return
        try {
            val effect = if (Build.VERSION.SDK_INT >= 29) VibrationEffect.createPredefined(predefined)
            else VibrationEffect.createOneShot(fallbackMs, amplitude)
            v.vibrate(effect)
        } catch (_: Exception) { }
    }
}
