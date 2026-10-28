package com.kwartotv.remote

import android.app.Application
import android.os.Build
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.kwartotv.remote.net.Discovery
import com.kwartotv.remote.net.FoundTv
import com.kwartotv.remote.net.RemoteClient
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn

class AppViewModel(app: Application) : AndroidViewModel(app) {
    private val store = Store(app)
    val client = RemoteClient()

    val tvs = MutableStateFlow(store.load())
    val selectedId = MutableStateFlow(store.selectedId)
    /** True while the user is adding another TV from the remote screen. */
    val adding = MutableStateFlow(false)

    /** TVs advertising themselves on the Wi-Fi. Only runs while the pairing screen is visible. */
    val found: StateFlow<List<FoundTv>> = Discovery.discover(app)
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    var padKind: Int
        get() = store.padKind
        set(v) { store.padKind = v }

    val phoneName: String
        get() = listOf(Build.MANUFACTURER, Build.MODEL).filter { it.isNotBlank() }.joinToString(" ")
            .replaceFirstChar { it.uppercase() }.ifBlank { "Android phone" }

    fun selected(): SavedTv? = tvs.value.firstOrNull { it.deviceId == selectedId.value } ?: tvs.value.firstOrNull()

    fun select(tv: SavedTv) {
        selectedId.value = tv.deviceId
        store.selectedId = tv.deviceId
        client.connect(tv)
    }

    init {
        client.onTvMoved = { moved ->
            val list = tvs.value.map { if (it.deviceId == moved.deviceId) moved else it }
            tvs.value = list
            store.save(list)
        }
    }

    /** Pairs with the TV. When [port] is null (only an address was typed) the app searches the TV's ports first. */
    suspend fun pair(host: String, port: Int?, pin: String): Result<SavedTv> = runCatching {
        val p = port ?: client.findPort(host)
            ?: throw com.kwartotv.remote.net.PairException("KwartoTV did not answer at $host. Check the address on the TV screen, that KwartoTV is open, and that the phone is on the same Wi-Fi.")
        val tv = client.pair(host, p, pin, phoneName)
        val list = tvs.value.filterNot { it.deviceId == tv.deviceId || (it.host == tv.host && it.port == tv.port) } + tv
        tvs.value = list
        store.save(list)
        adding.value = false
        select(tv)
        tv
    }

    fun forget(tv: SavedTv) {
        val list = tvs.value.filterNot { it.deviceId == tv.deviceId }
        tvs.value = list
        store.save(list)
        client.disconnect()
        list.firstOrNull()?.let { select(it) } ?: run { selectedId.value = null; store.selectedId = null }
    }

    fun connectSelected() { selected()?.let { client.connect(it) } }
    fun disconnect() = client.disconnect()

    override fun onCleared() { client.disconnect() }
}
