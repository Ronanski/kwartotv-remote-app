package com.kwartotv.remote.ui

import android.app.Application
import android.content.Context
import android.graphics.Bitmap
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.ComposeContentTestRule
import androidx.compose.ui.test.onRoot
import androidx.test.core.app.ApplicationProvider
import com.kwartotv.remote.AppViewModel
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.util.Base64

/** The phone app as the tests drive it: a paired TV, the TV's messages fed in, the commands sent kept. */
class PhoneUi(private val rule: ComposeContentTestRule) {
    val app: Application = ApplicationProvider.getApplicationContext()
    val vm: AppViewModel
    init {
        app.getSharedPreferences("kwartotv", Context.MODE_PRIVATE).edit()
            .putString("tvs", JSONArray().put(JSONObject().put("deviceId", "tv1").put("token", "t").put("name", "Living room TV").put("host", "192.168.1.20").put("port", 8765)).toString())
            .putString("selected", "tv1").commit()
        vm = AppViewModel(app)
        vm.client.record = true
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
        val bmp = rule.onRoot().captureToImage().asAndroidBitmap()
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
