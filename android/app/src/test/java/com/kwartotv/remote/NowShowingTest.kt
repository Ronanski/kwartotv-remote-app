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

    @Test fun netflixTitleAndTime() {
        val nf = NetflixPlaying("Dark", "S1:E2 · Lies", 125, 3000, paused = true, rate = 1.0, still = false)
        val s = nowShowing(true, "", 0, null, off, "netflix", nf)
        assertEquals("Dark", s.title); assertEquals("S1:E2 · Lies · 2:05 / 50:00", s.text); assertEquals(false, s.playing)
        assertEquals("Watching Netflix", nowShowing(true, "", 0, null, off, "netflix", nf.copy(dur = 0)).title)
        assertEquals("1:05:09", clock(3909)); assertEquals("0:07", clock(7))
    }

    @Test fun voiceSearchGoesToTheAppOnTheTv() {
        assertEquals("netflix", searchAppFor("netflix")); assertEquals("youtube", searchAppFor("youtube")); assertEquals("bilibili", searchAppFor("Bilibili"))
        assertEquals("music", searchAppFor("Music")); assertEquals("channels", searchAppFor("live")); assertEquals(null, searchAppFor("Home"))
    }

    @Test fun netflixNowAndEpisodesMessages() {
        val m = parseTvMessage("""{"type":"netflix.now","title":"Dark","episode":"S1:E2","pos":125,"dur":3000,"paused":false,"rate":1.5,"still":true}""") as TvMessage.NetflixNowPlaying
        assertEquals(NetflixPlaying("Dark", "S1:E2", 125, 3000, false, 1.5, true), m.now)
        val e = (parseTvMessage("""{"type":"netflix.episodes","show":"Dark","seasons":[{"n":"Season 1","eps":[{"id":"80100172","n":"1. Secrets","on":false},{"id":"x1","n":"bad"},{"id":"80100173","n":"2. Lies","on":true}]}]}""") as TvMessage.NetflixEpisodesNow).episodes
        assertEquals("Dark", e.show); assertEquals(listOf("80100172", "80100173"), e.seasons[0].episodes.map { it.id }); assertTrue(e.seasons[0].episodes[1].on)
        val onTv = (parseTvMessage("""{"type":"netflix.episodes","show":"","seasons":[],"onTv":true}""") as TvMessage.NetflixEpisodesNow).episodes
        assertTrue(onTv.onTv && onTv.seasons.isEmpty())
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
