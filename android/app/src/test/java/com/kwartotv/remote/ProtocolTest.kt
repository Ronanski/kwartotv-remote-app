package com.kwartotv.remote

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ProtocolTest {
    @Test fun pairLinkRoundTrip() {
        val link = PairLink.parse(PairLink.build("192.168.1.20", 8765, "012345"))
        assertEquals(PairLink("192.168.1.20", 8765, "012345"), link)
    }

    @Test fun pairLinkMatchesWhatTheTvPrints() {
        // Exactly what the Windows app's PairLink.Build produces.
        assertEquals(PairLink("10.0.0.5", 8765, "123456"), PairLink.parse("kwartotv://pair?host=10.0.0.5&port=8765&pin=123456"))
    }

    @Test fun pairLinkRejectsBadInput() {
        assertNull(PairLink.parse(null))
        assertNull(PairLink.parse("https://evil.example/pair?host=a&port=1&pin=123456"))
        assertNull(PairLink.parse("kwartotv://pair?host=a&port=1&pin=12"))
        assertNull(PairLink.parse("kwartotv://pair?host=a&port=99999&pin=123456"))
        assertNull(PairLink.parse("kwartotv://pair?host=&port=8765&pin=123456"))
        assertNull(PairLink.parse("kwartotv://pair?host=a&port=8765&pin=12345a"))
    }

    @Test fun hostPortParsing() {
        assertEquals("192.168.1.20" to null, parseHostPort("192.168.1.20"))
        assertEquals("192.168.1.20" to 9000, parseHostPort(" http://192.168.1.20:9000/ "))
        assertEquals("192.168.1.20" to 18765, parseHostPort("https://192.168.1.20:18765"))
        assertEquals("kwarto-pc.local" to null, parseHostPort("kwarto-pc.local"))
        assertEquals(8765, CANDIDATE_PORTS.first())
        assertNull(parseHostPort(""))
        assertNull(parseHostPort("host:99999"))
        assertNull(parseHostPort("host:abc"))
    }

    @Test fun commandJsonMatchesServerFormat() {
        val j = org.json.JSONObject(commandJson("7", Cmd.TEXT, "héllo"))
        assertEquals("7", j.getString("id")); assertEquals("key.text", j.getString("cmd")); assertEquals("héllo", j.getString("arg"))
        assertFalse(org.json.JSONObject(commandJson("8", Cmd.OK, null)).has("arg"))
    }

    @Test fun parsesServerMessages() {
        val s = parseTvMessage("""{"type":"state","number":12,"channel":{"id":"a","name":"News One","group":"News"}}""")
        assertEquals(TvMessage.State(12, "News One"), s)
        assertEquals(TvMessage.State(0, null), parseTvMessage("""{"type":"state","number":0,"channel":null}"""))
        assertEquals(TvMessage.Ack("3", false, "no such channel"), parseTvMessage("""{"type":"ack","id":"3","ok":false,"error":"no such channel"}"""))
        assertEquals(TvMessage.Ack("4", true, null), parseTvMessage("""{"type":"ack","id":"4","ok":true,"error":null}"""))
        assertEquals(TvMessage.Unknown, parseTvMessage("not json"))
    }

    @Test fun savedTvUrlsAndJson() {
        val tv = SavedTv("dev1", "tok-en_1", "Living Room", "192.168.1.20", 8765)
        assertEquals("ws://192.168.1.20:8765/ws?device=dev1&token=tok-en_1", tv.wsUrl())
        assertEquals("http://192.168.1.20:8765/pair", tv.pairUrl())
        assertEquals(tv, SavedTv.fromJson(tv.toJson()))
        assertEquals("http://[fe80::1]:8765/pair", SavedTv("a", "b", "c", "fe80::1", 8765).pairUrl())
        assertNotNull(tv); assertTrue(tv.wsUrl().startsWith("ws://"))
    }
}
