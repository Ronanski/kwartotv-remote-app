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

class UpdateTest {
    private val h = "a".repeat(64)
    private fun manifest(url: String = "https://github.com/Ronanski/game-dev-era/releases/download/latest/KwartoTV-Remote.apk", sha: String = h) =
        """{"build":42,"version":"1.0.42","exe":{"url":"https://github.com/Ronanski/game-dev-era/releases/download/latest/KwartoTV.exe","sha256":"$h"},"apk":{"url":"$url","sha256":"$sha","versionCode":42},"notes":"x"}"""

    @org.junit.Test fun parsesTheManifestTheBuildProduces() {
        val i = Updates.parse(manifest())!!
        org.junit.Assert.assertEquals(42L, i.apkVersionCode); org.junit.Assert.assertEquals("1.0.42", i.version)
    }

    @org.junit.Test fun newerOnlyWhenVersionCodeIsHigher() {
        val i = Updates.parse(manifest())!!
        org.junit.Assert.assertTrue(Updates.isNewer(i, 41)); org.junit.Assert.assertFalse(Updates.isNewer(i, 42)); org.junit.Assert.assertFalse(Updates.isNewer(i, 99))
    }

    @org.junit.Test fun refusesOtherHostsHttpAndBadChecksum() {
        org.junit.Assert.assertNull(Updates.parse(manifest(url = "https://evil.example/a.apk")))
        org.junit.Assert.assertNull(Updates.parse(manifest(url = "http://github.com/Ronanski/game-dev-era/releases/download/latest/a.apk")))
        org.junit.Assert.assertNull(Updates.parse(manifest(sha = "xyz")))
        org.junit.Assert.assertNull(Updates.parse("{}")); org.junit.Assert.assertNull(Updates.parse("junk")); org.junit.Assert.assertNull(Updates.parse(null))
    }
}

class LineupTest {
    // What RemoteServer.LineupMessage on the TV sends.
    private val json = """{"type":"lineup","id":"7","total":3,"channels":[
        {"number":1,"name":"GMA","group":"News","favorite":true,"now":"Eat Bulaga  ·  12:00–1:00 PM"},
        {"number":2,"name":"Cartoon Fun","group":"Kids","favorite":false,"now":null},
        {"number":12,"name":"News 12","group":"News","favorite":false},
        {"number":0,"name":"broken"}, "junk"]}"""

    private fun lineup() = (parseTvMessage(json) as TvMessage.Channels).lineup

    @Test fun parsesTheLineupMessage() {
        val l = lineup()
        assertEquals(3, l.channels.size)
        assertEquals(3, l.total)
        assertEquals(LineupChannel(1, "GMA", "News", true, "Eat Bulaga  ·  12:00–1:00 PM"), l.channels[0])
        assertNull(l.channels[1].now) // JSON null, not the text "null"
        assertNull(l.channels[2].now)
        assertEquals(listOf("News", "Kids"), lineupGroups(l.channels))
    }

    @Test fun totalIsNeverLessThanTheRows() {
        val l = parseLineup(org.json.JSONObject("""{"channels":[{"number":5,"name":"","group":""}]}"""))
        assertEquals(1, l.total)
        assertEquals(LineupChannel(5, "Channel 5", "Other", false, null), l.channels[0])
        assertTrue(parseLineup(org.json.JSONObject("{}")).channels.isEmpty())
    }

    @Test fun searchMatchesNameOrNumber() {
        val c = lineup().channels
        assertEquals(listOf("Cartoon Fun"), filterLineup(c, "cartoon", null).map { it.name })
        // Digits match the start of the number, the exact number first; "News 12" also matches by name.
        assertEquals(listOf(1, 12), filterLineup(c, "1", null).map { it.number })
        assertEquals(listOf(12), filterLineup(c, " 12 ", null).map { it.number })
        assertEquals(3, filterLineup(c, "", null).size)
        assertTrue(filterLineup(c, "zzz", null).isEmpty())
    }

    @Test fun categoryAndFavoriteChips() {
        val c = lineup().channels
        assertEquals(listOf(1, 12), filterLineup(c, "", "News").map { it.number })
        assertEquals(listOf(12), filterLineup(c, "12", "News").map { it.number })
        assertEquals(listOf(1), filterLineup(c, "", FAVORITES_FILTER).map { it.number })
        assertFalse(filterLineup(c, "", "Kids").any { it.group != "Kids" })
    }
}
