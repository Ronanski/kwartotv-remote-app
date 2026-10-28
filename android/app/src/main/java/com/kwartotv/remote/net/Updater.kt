package com.kwartotv.remote.net

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.os.Build
import com.kwartotv.remote.UpdateInfo
import com.kwartotv.remote.Updates
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.security.MessageDigest
import java.util.concurrent.TimeUnit

/** Checks the "latest" release and installs a newer APK over this app (Android asks you to confirm). */
class Updater(private val context: Context) {
    private val http = OkHttpClient.Builder().connectTimeout(10, TimeUnit.SECONDS).readTimeout(60, TimeUnit.SECONDS).build()

    fun installedVersionCode(): Long = try {
        val p = context.packageManager.getPackageInfo(context.packageName, 0)
        if (Build.VERSION.SDK_INT >= 28) p.longVersionCode else @Suppress("DEPRECATION") p.versionCode.toLong()
    } catch (_: Exception) { 0L }

    /** The newer build, or null when this app is up to date (or the check failed). */
    suspend fun check(): UpdateInfo? = withContext(Dispatchers.IO) {
        try {
            http.newCall(Request.Builder().url(Updates.MANIFEST_URL).build()).execute().use { r ->
                if (!r.isSuccessful) return@use null
                Updates.parse(r.body?.string())?.takeIf { Updates.isNewer(it, installedVersionCode()) }
            }
        } catch (_: Exception) { null }
    }

    /** Downloads the APK, checks its SHA-256, and hands it to the system installer. Throws with a readable message. */
    suspend fun install(info: UpdateInfo, onProgress: (Int) -> Unit) = withContext(Dispatchers.IO) {
        if (!Updates.isAllowedUrl(info.apkUrl)) throw IllegalStateException("Update address is not allowed.")
        val dir = File(context.cacheDir, "updates").apply { mkdirs() }
        val file = File(dir, "KwartoTV-Remote.apk")
        val digest = MessageDigest.getInstance("SHA-256")
        http.newCall(Request.Builder().url(info.apkUrl).build()).execute().use { r ->
            if (!r.isSuccessful) throw IllegalStateException("Download failed (${r.code}).")
            val body = r.body ?: throw IllegalStateException("Download failed.")
            val total = body.contentLength()
            body.byteStream().use { input ->
                file.outputStream().use { out ->
                    val buf = ByteArray(64 * 1024)
                    var done = 0L
                    while (true) {
                        val n = input.read(buf)
                        if (n < 0) break
                        out.write(buf, 0, n); digest.update(buf, 0, n); done += n
                        if (total > 0) onProgress((done * 100 / total).toInt())
                    }
                }
            }
        }
        val sha = digest.digest().joinToString("") { "%02x".format(it) }
        if (sha != info.apkSha256) { file.delete(); throw IllegalStateException("The downloaded update is damaged. Try again.") }

        val installer = context.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL)
        val id = installer.createSession(params)
        installer.openSession(id).use { session ->
            file.inputStream().use { input -> session.openWrite("KwartoTV-Remote.apk", 0, file.length()).use { out -> input.copyTo(out); session.fsync(out) } }
            val intent = Intent(context, InstallReceiver::class.java).setAction(InstallReceiver.ACTION)
            val pi = PendingIntent.getBroadcast(context, id, intent, PendingIntent.FLAG_MUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            session.commit(pi.intentSender)
        }
    }
}

/** Receives the installer's result. When Android needs the user to confirm, this opens the confirm screen. */
class InstallReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                @Suppress("DEPRECATION")
                val confirm = intent.getParcelableExtra<Intent>(Intent.EXTRA_INTENT)
                confirm?.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)?.let { context.startActivity(it) }
            }
            else -> Unit
        }
    }

    companion object { const val ACTION = "com.kwartotv.remote.INSTALL_RESULT" }
}
