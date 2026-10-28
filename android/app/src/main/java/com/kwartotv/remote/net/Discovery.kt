package com.kwartotv.remote.net

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow

data class FoundTv(val name: String, val host: String, val port: Int)

/** Finds TVs on the Wi-Fi that advertise "_kwartotv._tcp" (the Windows app does this with mDNS). */
object Discovery {
    private const val TYPE = "_kwartotv._tcp."

    fun discover(context: Context): Flow<List<FoundTv>> = callbackFlow {
        val nsd = context.getSystemService(Context.NSD_SERVICE) as NsdManager
        val found = linkedMapOf<String, FoundTv>()
        val queue = ArrayDeque<NsdServiceInfo>()
        var resolving = false

        fun next() {
            if (resolving) return
            val info = queue.removeFirstOrNull() ?: return
            resolving = true
            @Suppress("DEPRECATION")
            nsd.resolveService(info, object : NsdManager.ResolveListener {
                override fun onResolveFailed(serviceInfo: NsdServiceInfo, errorCode: Int) { resolving = false; next() }
                override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
                    @Suppress("DEPRECATION")
                    val host = serviceInfo.host?.hostAddress
                    if (host != null) {
                        synchronized(found) { found[serviceInfo.serviceName] = FoundTv(serviceInfo.serviceName, host, serviceInfo.port) }
                        trySend(synchronized(found) { found.values.toList() })
                    }
                    resolving = false; next()
                }
            })
        }

        val listener = object : NsdManager.DiscoveryListener {
            override fun onDiscoveryStarted(serviceType: String) {}
            override fun onDiscoveryStopped(serviceType: String) {}
            override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {}
            override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {}
            override fun onServiceFound(serviceInfo: NsdServiceInfo) { queue.addLast(serviceInfo); next() }
            override fun onServiceLost(serviceInfo: NsdServiceInfo) {
                synchronized(found) { found.remove(serviceInfo.serviceName) }
                trySend(synchronized(found) { found.values.toList() })
            }
        }
        nsd.discoverServices(TYPE, NsdManager.PROTOCOL_DNS_SD, listener)
        trySend(emptyList())
        awaitClose { try { nsd.stopServiceDiscovery(listener) } catch (_: Exception) {} }
    }
}
