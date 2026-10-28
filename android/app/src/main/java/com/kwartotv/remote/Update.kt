package com.kwartotv.remote

import org.json.JSONObject

/** What the project's "latest" release says is the newest Android remote. Same file the Windows app reads. */
data class UpdateInfo(val build: Int, val version: String, val apkUrl: String, val apkSha256: String, val apkVersionCode: Long, val notes: String?)

object Updates {
    const val MANIFEST_URL = "https://github.com/Ronanski/game-dev-era/releases/download/latest/KwartoTV-update.json"
    private const val ALLOWED_PREFIX = "https://github.com/Ronanski/game-dev-era/releases/download/"

    fun isAllowedUrl(url: String) = url.startsWith(ALLOWED_PREFIX)

    fun parse(json: String?): UpdateInfo? = try {
        val r = JSONObject(json ?: return null)
        val apk = r.getJSONObject("apk")
        val info = UpdateInfo(
            build = r.getInt("build"),
            version = r.optString("version"),
            apkUrl = apk.getString("url"),
            apkSha256 = apk.getString("sha256").lowercase(),
            apkVersionCode = apk.optLong("versionCode", r.getInt("build").toLong()),
            notes = if (r.has("notes") && !r.isNull("notes")) r.getString("notes") else null,
        )
        if (info.build > 0 && isAllowedUrl(info.apkUrl) && info.apkSha256.length == 64 && info.apkSha256.all { it in '0'..'9' || it in 'a'..'f' }) info else null
    } catch (_: Exception) {
        null
    }

    fun isNewer(info: UpdateInfo, installedVersionCode: Long) = info.apkVersionCode > installedVersionCode
}
