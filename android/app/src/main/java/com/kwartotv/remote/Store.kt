package com.kwartotv.remote

import android.content.Context
import org.json.JSONArray

/** The TVs this phone is paired with. The pairing token stays in the app's private storage. */
class Store(context: Context) {
    private val prefs = context.getSharedPreferences("kwartotv", Context.MODE_PRIVATE)

    fun load(): List<SavedTv> = try {
        val a = JSONArray(prefs.getString("tvs", "[]"))
        (0 until a.length()).map { SavedTv.fromJson(a.getJSONObject(it)) }
    } catch (_: Exception) { emptyList() }

    fun save(tvs: List<SavedTv>) {
        val a = JSONArray(); tvs.forEach { a.put(it.toJson()) }
        prefs.edit().putString("tvs", a.toString()).apply()
    }

    var selectedId: String?
        get() = prefs.getString("selected", null)
        set(v) { prefs.edit().putString("selected", v).apply() }

    /** 0 = D-pad, 1 = touchpad (arrows), 2 = mouse. */
    var padKind: Int
        get() = prefs.getInt("padKind", 0)
        set(v) { prefs.edit().putInt("padKind", v).apply() }
}
