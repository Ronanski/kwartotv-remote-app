package com.kwartotv.remote.net

import org.junit.Assert.assertTrue
import org.junit.Test

class ExplainTest {
    @Test fun timeoutPointsAtTheFirewall() {
        val m = explain(java.net.SocketTimeoutException("failed to connect to /192.168.1.86 (port 8765) after 5000ms"), "192.168.1.86", 8765)
        assertTrue(m.contains("Fix network access") && m.contains("SocketTimeoutException"))
    }

    @Test fun refusedMeansAppNotRunning() {
        val m = explain(java.net.ConnectException("failed to connect: ECONNREFUSED (Connection refused)"), "192.168.1.86", 8765)
        assertTrue(m.contains("Is KwartoTV open"))
    }

    @Test fun otherErrorsKeepTheReason() {
        val m = explain(java.io.IOException("unexpected end of stream"), "h", 1)
        assertTrue(m.contains("unexpected end of stream"))
    }
}
