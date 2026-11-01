package com.kwartotv.remote

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class NowShowingTest {
    private val song = MusicState(on = true, playing = true, title = "Song", artist = "Band", art = null)
    private val off = MusicState(false, false, "", "", null)

    @Test fun followsWhatIsOnTheTv() {
        val nf = nowShowing(true, "Connected", 0, null, off, "netflix")
        assertEquals(ShowMode.Video, nf.mode); assertEquals("Watching Netflix", nf.title)
        assertEquals(listOf("media.seek:-10", Cmd.PLAY_PAUSE, "media.seek:10", Cmd.VOL_DOWN, Cmd.VOL_UP), nf.buttons.map { it.cmd })
        assertEquals("Watching YouTube", nowShowing(true, "", 0, null, song, "youtube").title)
        val live = nowShowing(true, "", 12, "GMA", off, "live")
        assertEquals(ShowMode.Live, live.mode); assertEquals("12  GMA", live.title)
        assertEquals(listOf(Cmd.CH_PREV, Cmd.PLAY_PAUSE, Cmd.CH_NEXT, Cmd.VOL_DOWN, Cmd.VOL_UP), live.buttons.map { it.cmd })
    }

    @Test fun musicWhenNoVideoAndBehindTheLock() {
        for (app in listOf("Home", "Music", "locked")) {
            val m = nowShowing(true, "", 0, null, song, app)
            assertEquals(ShowMode.Music, m.mode); assertEquals("Song", m.title); assertEquals("Band", m.text)
            assertEquals(listOf(Cmd.MUSIC_PREV, Cmd.MUSIC_PLAY_PAUSE, Cmd.MUSIC_NEXT, Cmd.VOL_DOWN, Cmd.VOL_UP), m.buttons.map { it.cmd })
        }
    }

    @Test fun plainWhenNothingPlaysOrOffline() {
        val p = nowShowing(true, "Connected to the TV", 0, null, off, "Home")
        assertEquals(ShowMode.Plain, p.mode); assertTrue(p.buttons.any { it.cmd == Cmd.VOL_UP })
        assertEquals(ShowMode.Plain, nowShowing(false, "Reconnecting", 0, null, song, "netflix").mode)
        assertEquals("media.seek" to "-10", splitButton("media.seek:-10")); assertEquals(Cmd.VOL_UP to null, splitButton(Cmd.VOL_UP))
    }

    @Test fun netflixTracksMessage() {
        val m = parseTvMessage("""{"type":"netflix.tracks","audio":[{"i":0,"n":"English","on":true},{"i":1,"n":"Japanese","on":false}],"subs":[{"i":0,"n":"Off","on":true},{"i":200,"n":"bad"}]}""")
        assertTrue(m is TvMessage.NetflixTracksNow)
        val t = (m as TvMessage.NetflixTracksNow).tracks
        assertEquals(listOf("English", "Japanese"), t.audio.map { it.name }); assertTrue(t.audio[0].on)
        assertEquals(listOf("Off"), t.subs.map { it.name })
    }
}
