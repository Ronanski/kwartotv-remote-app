package com.kwartotv.remote

import android.app.Application
import android.os.Build
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.kwartotv.remote.net.Discovery
import com.kwartotv.remote.net.FoundTv
import com.kwartotv.remote.net.RemoteClient
import com.kwartotv.remote.net.Updater
import android.net.ConnectivityManager
import android.net.Network
import android.widget.Toast
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn

sealed interface UpdateState {
    data object None : UpdateState
    data class Available(val info: UpdateInfo) : UpdateState
    data class Working(val percent: Int, val installing: Boolean) : UpdateState
    data class Failed(val message: String, val info: UpdateInfo) : UpdateState
}

class AppViewModel(app: Application) : AndroidViewModel(app) {
    private val store = Store(app)
    /** Shared with RemoteService, which keeps it connected while the app is in the background. */
    val client: RemoteClient = com.kwartotv.remote.net.RemoteHub.client

    val tvs = MutableStateFlow(store.load())
    val selectedId = MutableStateFlow(store.selectedId)
    /** True while the user is adding another TV from the remote screen. */
    val adding = MutableStateFlow(false)

    /** TVs advertising themselves on the Wi-Fi. Only runs while the pairing screen is visible. */
    val found: StateFlow<List<FoundTv>> = Discovery.discover(app)
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    /** Netflix / Bilibili signed in on the TV (confirmed by the TV): their sign-in buttons are hidden. */
    val signedIn = MutableStateFlow(store.signedIn)
    fun markSignedIn(service: String, on: Boolean) {
        val next = if (on) signedIn.value + service else signedIn.value - service
        if (next != signedIn.value) { signedIn.value = next; store.signedIn = next }
    }

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
        com.kwartotv.remote.net.RemoteService.start(getApplication())
    }

    private val updater = Updater(app)
    val update = MutableStateFlow<UpdateState>(UpdateState.None)
    private var lastUpdateCheck = 0L

    private val connectivity = app.getSystemService(android.content.Context.CONNECTIVITY_SERVICE) as ConnectivityManager
    private var networkCallback: ConnectivityManager.NetworkCallback? = null

    /** True between ON_START and ON_STOP. Nothing connects while the app is in the background. */
    private val foreground get() = client.foreground

    init {
        checkForUpdate(manual = false)
        // The TV tells the phone when a newer phone app is out: look for it right away.
        viewModelScope.launch {
            client.tvSeesApk.collect { code ->
                if (code > updater.installedVersionCode() && update.value is UpdateState.None) checkForUpdate(manual = false)
            }
        }
        // While the app is on screen, look again every 5 minutes.
        viewModelScope.launch {
            while (true) {
                kotlinx.coroutines.delay(5 * 60_000L)
                if (client.foreground && update.value is UpdateState.None) checkForUpdate(manual = false)
            }
        }
        // When the Wi-Fi comes back (or changes) reconnect right away instead of waiting for the next retry.
        val callback = object : ConnectivityManager.NetworkCallback() {
            // Called on a ConnectivityManager thread: hop to the main thread before touching the client.
            override fun onAvailable(network: Network) { viewModelScope.launch { if (foreground && !client.isConnected()) connectSelected() } }
        }
        try { connectivity.registerDefaultNetworkCallback(callback); networkCallback = callback } catch (_: Exception) { }
        // The TV's IP address can change (router lease). Look for it by name on the Wi-Fi.
        client.onTvUnreachable = { lost ->
            viewModelScope.launch {
                // Only a service with the saved TV's own name, never "the only TV that answered": the token goes to it.
                val found = withTimeoutOrNull(7000) {
                    Discovery.discover(getApplication()).first { list -> list.any { it.name == lost.name } }
                }?.firstOrNull { it.name == lost.name }
                if (found != null && foreground && (found.host != lost.host || found.port != lost.port)
                    // Ask the new address who it is before saving it or sending the token there.
                    && client.infoName(found.host, found.port) == lost.name && foreground
                ) {
                    val moved = lost.copy(host = found.host, port = found.port)
                    val list = tvs.value.map { if (it.deviceId == moved.deviceId) moved else it }
                    tvs.value = list; store.save(list)
                    client.moveTo(moved)
                }
            }
        }
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
        list.firstOrNull()?.let { select(it) } ?: run {
            selectedId.value = null; store.selectedId = null
            com.kwartotv.remote.net.RemoteService.stop(getApplication())
        }
    }

    fun connectSelected() { if (foreground) selected()?.let { client.connect(it) } }

    /**
     * The app came on screen: keep the connection if it still works (the background service kept it), otherwise
     * connect; and make sure the service runs, so the remote stays connected when the app is left.
     */
    fun onStart() {
        client.foreground = true
        client.appVisible.value = true
        // The app can stay open for days (the remote runs in the background): look for an update each time it comes back.
        if (android.os.SystemClock.elapsedRealtime() - lastUpdateCheck > 20_000L && update.value !is UpdateState.Working) checkForUpdate(manual = false)
        val tv = selected() ?: return
        client.ensureConnected(tv)
        com.kwartotv.remote.net.RemoteService.start(getApplication())
    }

    /** The app left the screen: the background service keeps the connection (it has a Disconnect button). */
    fun onStop() {
        client.appVisible.value = false
        if (!com.kwartotv.remote.net.RemoteService.running) { client.foreground = false; client.disconnect() }
    }

    /** Connect again now (the connection may be stale after the phone was idle). */
    fun reconnect() { if (selected() != null) connectSelected() }

    fun checkForUpdate(manual: Boolean) {
        if (client.record) return   // the phone UI tests: no update checks over the internet
        lastUpdateCheck = android.os.SystemClock.elapsedRealtime()
        viewModelScope.launch {
            val info = updater.check()
            if (info != null) update.value = UpdateState.Available(info)
            else if (manual) Toast.makeText(getApplication(), "KwartoTV Remote is up to date.", Toast.LENGTH_SHORT).show()
        }
    }

    fun installUpdate(info: UpdateInfo) {
        viewModelScope.launch {
            update.value = UpdateState.Working(0, false)
            try {
                updater.install(info) { p -> update.value = UpdateState.Working(p, p >= 100) }
                // Android now shows its own "Update this app?" screen. Back here the banner can be tried again.
                update.value = UpdateState.Available(info)
            } catch (e: Exception) {
                update.value = UpdateState.Failed(e.message ?: "Update failed.", info)
            }
        }
    }
    fun disconnect() = client.disconnect()

    override fun onCleared() {
        networkCallback?.let { try { connectivity.unregisterNetworkCallback(it) } catch (_: Exception) { } }
        networkCallback = null
        // The background service owns the connection; without it, close it.
        if (!com.kwartotv.remote.net.RemoteService.running) { client.foreground = false; client.disconnect() }
    }
}
