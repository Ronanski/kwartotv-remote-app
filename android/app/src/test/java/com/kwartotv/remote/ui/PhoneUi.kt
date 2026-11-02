package com.kwartotv.remote.ui

import android.app.Application
import android.content.Context
import android.graphics.Bitmap
import androidx.activity.ComponentActivity
import androidx.compose.ui.test.junit4.AndroidComposeTestRule
import androidx.test.ext.junit.rules.ActivityScenarioRule
import androidx.test.core.app.ApplicationProvider
import com.kwartotv.remote.AppViewModel
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.util.Base64

/** The phone app as the tests drive it: a paired TV, the TV's messages fed in, the commands sent kept. */
class PhoneUi(private val rule: AndroidComposeTestRule<ActivityScenarioRule<ComponentActivity>, ComponentActivity>) {
    val app: Application = ApplicationProvider.getApplicationContext()
    val vm: AppViewModel
    init {
        // A test that hangs prints where every thread is (CI logs otherwise show nothing until the job times out).
        val started = System.currentTimeMillis()
        Thread {
            try { Thread.sleep(120_000) } catch (_: InterruptedException) { return@Thread }
            println("!! PhoneUi: still running after ${(System.currentTimeMillis() - started) / 1000}s, threads:")
            Thread.getAllStackTraces().forEach { (t, st) -> println("-- ${t.name}"); st.take(25).forEach { println("    at $it") } }
        }.apply { isDaemon = true; start() }
        app.getSharedPreferences("kwartotv", Context.MODE_PRIVATE).edit()
            .putString("tvs", JSONArray().put(JSONObject().put("deviceId", "tv1").put("token", "t").put("name", "Living room TV").put("host", "192.168.1.20").put("port", 8765)).toString())
            .putString("selected", "tv1").commit()
        com.kwartotv.remote.net.RemoteHub.client.record = true   // before the app starts: it never really connects
        vm = AppViewModel(app)
        vm.client.sent.clear()
        vm.client.pretendConnected()
    }

    fun show() {
        // The clock moves only when the test says so: endless animations and timers never keep the test waiting.
        rule.mainClock.autoAdvance = false
        rule.setContent { KwartoTheme { RemoteScreen(vm) } }
        settle()
    }

    /** Lets the screen catch up (half a second of its clock). */
    fun settle() { rule.mainClock.advanceTimeBy(500); rule.waitForIdle() }

    /** Messages from the TV (JSON), as they come over the connection. */
    fun tv(vararg json: String) { rule.runOnUiThread { json.forEach { vm.client.deliver(it) } }; settle() }

    fun sent(): List<String> = vm.client.sent.toList()
    fun clearSent() = vm.client.sent.clear()

    /** The screen as a picture: printed for the CI log (##SHOT name, then base64 JPEG) and saved under build/phone-shots. */
    fun shot(name: String) {
        settle()
        // Drawn straight from the screen (Compose's own capture waits for a frame, which never comes with the clock held).
        val bmp = rule.runOnUiThread {
            val v = rule.activity.window.decorView.rootView
            val b = Bitmap.createBitmap(v.width.coerceAtLeast(1), v.height.coerceAtLeast(1), Bitmap.Config.ARGB_8888)
            val canvas = android.graphics.Canvas(b)
            // every window, in order: the app, then its pop-ups (bottom sheets, menus, dialogs) on top
            val windows = try {
                val wmg = Class.forName("android.view.WindowManagerGlobal").getMethod("getInstance").invoke(null)
                @Suppress("UNCHECKED_CAST")
                (wmg.javaClass.getDeclaredField("mViews").apply { isAccessible = true }.get(wmg) as List<android.view.View>).toList()
            } catch (_: Exception) { listOf(v) }
            (if (v in windows) windows else listOf(v) + windows).forEach { w ->
                if (w.width <= 0 || w.height <= 0 || !w.isShown) return@forEach
                val at = IntArray(2); w.getLocationOnScreen(at)
                canvas.save(); canvas.translate(at[0].toFloat(), at[1].toFloat()); w.draw(canvas); canvas.restore()
            }
            b
        }
        val out = ByteArrayOutputStream()
        bmp.compress(Bitmap.CompressFormat.JPEG, 72, out)
        val dir = File("build/phone-shots").apply { mkdirs() }
        File(dir, "$name.jpg").writeBytes(out.toByteArray())
        println("##SHOT $name")
        println(Base64.getEncoder().encodeToString(out.toByteArray()))
    }

    companion object {
        fun fg(app: String) = """{"type":"fg","app":"$app"}"""
        fun channel(n: Int, name: String) = """{"type":"state","number":$n,"channel":{"name":"$name"}}"""
        const val SONG = """{"type":"music","on":true,"playing":true,"title":"Maybe the Night","artist":"Ben&Ben","art":null}"""
        const val NETFLIX = """{"type":"netflix.now","title":"Stranger Things","episode":"S4:E1 · The Hellfire Club","pos":754,"dur":4560,"paused":false,"rate":1,"still":false}"""
        val LINEUP = """{"type":"lineup","total":6,"channels":[""" + listOf("GMA 7|News", "Kapamilya|News", "TV5|News", "Cartoon Network|Kids", "HBO|Movies", "NBA TV|Sports")
            .mapIndexed { i, s -> val (n, g) = s.split('|'); """{"number":${i + 1},"name":"$n","group":"$g","favorite":${i == 0},"now":${if (i < 3) "\"Evening news  ·  6:00–7:00 PM\"" else "null"},"id":"c$i"}""" }
            .joinToString(",") + "]}"
    }
}
