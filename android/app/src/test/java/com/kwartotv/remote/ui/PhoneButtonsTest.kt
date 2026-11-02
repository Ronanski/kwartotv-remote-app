package com.kwartotv.remote.ui

import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.SemanticsNodeInteraction
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onLast
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performImeAction
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performSemanticsAction
import androidx.compose.ui.test.performTextInput
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/**
 * Every button of the phone app, one by one, in each state the TV can be in: each press must send exactly the command
 * the TV expects (the TV side of each command is checked in tools/smoke-windows.ps1).
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [34], qualifiers = "w411dp-h891dp-port-hdpi")
class PhoneButtonsTest {
    @get:Rule val rule = createComposeRule()
    private lateinit var ui: PhoneUi
    private val log = mutableListOf<String>()

    private fun reach(n: SemanticsNodeInteraction): SemanticsNodeInteraction { try { n.performScrollTo() } catch (_: Throwable) { } ; return n }

    /** Presses the button named [what] (its spoken name) and checks the commands it sent. */
    private fun press(what: String, vararg expect: String) {
        ui.clearSent()
        reach(rule.onNodeWithContentDescription(what)).performClick()
        ui.settle()
        assertEquals("button \"$what\"", expect.toList(), ui.sent())
        log += "PRESS \"$what\" -> ${expect.joinToString(" + ").ifEmpty { "(nothing sent)" }}"
    }

    private fun pressText(text: String, vararg expect: String) {
        ui.clearSent()
        reach(rule.onAllNodesWithText(text).onLast()).performClick()
        ui.settle()
        assertEquals("\"$text\"", expect.toList(), ui.sent())
        log += "TAP \"$text\" -> ${expect.joinToString(" + ").ifEmpty { "(nothing sent)" }}"
    }

    private fun tab(name: String) { rule.onAllNodesWithText(name).onLast().performClick(); ui.settle() }

    private fun start(): PhoneUi { ui = PhoneUi(rule); ui.show(); return ui }

    private fun done(name: String) { println("== $name: ${log.size} buttons checked"); log.forEach { println("  $it") } }

    @Test fun homeAndTheRemote() {
        start(); ui.tv(PhoneUi.fg("Home"))
        press("Open Live TV", "app.livetv"); press("Open YouTube", "app.youtube"); press("Open YT Music", "app.ytmusic")
        press("Open Netflix", "app.netflix"); press("Open Bilibili", "app.bilibili")
        press("Up", "nav.up"); press("Down", "nav.down"); press("Left", "nav.left"); press("Right", "nav.right"); press("OK", "nav.ok")
        press("Back", "nav.back"); press("Home", "nav.home"); press("Play or pause", "media.playpause")
        press("Volume down", "media.voldown"); press("Mute", "media.mute"); press("Volume up", "media.volup")
        press("Lock button", "power.screenoff")
        // the swipe pad: a tap is OK
        pressText("Swipe"); ui.shot("b01-swipe-pad")
        pressText("Swipe to move\nTap = OK  ·  Long press = Back", "nav.ok")
        pressText("D-pad")
        press("Keyboard"); rule.onNodeWithText("Type on the TV").assertExists()
        done("Home")
    }

    @Test fun netflix() {
        start(); ui.tv(PhoneUi.fg("netflix"), PhoneUi.NETFLIX)
        press("Skip the intro or recap", "netflix.skip"); press("Play the next episode", "netflix.next")
        press("Play or pause", "media.playpause")
        // jump anywhere with the progress bar
        ui.clearSent()
        reach(rule.onNode(androidx.compose.ui.test.hasSetProgressAction())).performSemanticsAction(SemanticsActions.SetProgress) { it(1800f) }
        ui.settle(); assertEquals(listOf("netflix.seekto 1800"), ui.sent()); log += "DRAG progress bar to 30:00 -> netflix.seekto 1800"
        // Audio & Subtitles
        press("Choose the audio language and subtitles", "netflix.tracks")
        ui.tv("""{"type":"netflix.tracks","audio":[{"i":0,"n":"English [Original]","on":true},{"i":1,"n":"Japanese","on":false}],"subs":[{"i":0,"n":"Off","on":true},{"i":1,"n":"Filipino","on":false}]}""")
        ui.shot("b02-netflix-audio-subs")
        pressText("Japanese", "netflix.track a:1"); pressText("Filipino", "netflix.track s:1"); pressText("Done")
        // Episodes
        press("Choose an episode", "netflix.episodes")
        ui.tv("""{"type":"netflix.episodes","show":"Stranger Things","seasons":[{"n":"Season 1","eps":[{"id":"80057281","n":"1. The Vanishing","on":false}]},{"n":"Season 4","eps":[{"id":"81077842","n":"1. The Hellfire Club","on":true},{"id":"81077843","n":"2. Vecna's Curse","on":false}]}]}""")
        ui.shot("b03-netflix-episodes")
        pressText("2. Vecna's Curse", "netflix.episode 81077843")
        // Speed
        press("Playback speed"); ui.shot("b04-netflix-speed"); pressText("1.5×", "netflix.speed 1.5")
        // "Are you still watching?"
        ui.tv(PhoneUi.NETFLIX.replace("\"still\":false", "\"still\":true"))
        press("Answer Are you still watching", "netflix.continue")
        // search Netflix
        ui.clearSent()
        reach(rule.onNodeWithText("Search Netflix")).performTextInput("dark")
        rule.onNodeWithText("Search Netflix").performImeAction(); ui.settle()
        assertEquals(listOf("netflix.search dark"), ui.sent()); log += "TYPE \"dark\" + search -> netflix.search dark"
        done("Netflix")
    }

    @Test fun liveTvMusicVideosAndLock() {
        start(); ui.tv(PhoneUi.fg("live"), PhoneUi.channel(7, "GMA 7"), PhoneUi.LINEUP)
        press("Channel down", "channel.prev"); press("Channel list on the TV", "channel.list"); press("Channel up", "channel.next")
        press("Last channel", "channel.last"); press("Favorite channel", "channel.favorite"); press("Play or pause", "media.playpause")
        ui.tv(PhoneUi.fg("Music"), PhoneUi.SONG)
        press("Previous song", "music.prev"); press("Pause the song", "music.playpause"); press("Next song", "music.next"); press("Stop the music", "music.stop")
        press("Play or pause", "music.playpause")
        ui.tv(PhoneUi.fg("youtube"))
        press("Back 10 seconds", "media.seek -10"); press("Play or pause the video", "media.playpause"); press("Forward 10 seconds", "media.seek 10"); press("Next video", "media.next")
        ui.tv(PhoneUi.fg("web-bilibili"))
        press("Back 10 seconds", "media.seek -10"); press("Play or pause the video", "media.playpause"); press("Forward 10 seconds", "media.seek 10")
        ui.tv(PhoneUi.fg("locked"))
        press("Unlock the TV", "power.screenon"); press("Unlock button", "power.screenon")
        done("Live TV, music, videos, lock")
    }

    @Test fun channelsTab() {
        start(); ui.tv(PhoneUi.fg("live"), PhoneUi.channel(1, "GMA 7"), PhoneUi.LINEUP)
        tab("Channels"); ui.clearSent()
        pressText("HBO", "channel.number 5")
        pressText("Keypad")
        press("Open the channel list on the TV", "channel.list"); press("Channel up", "channel.next"); press("Last channel", "channel.last"); press("Channel down", "channel.prev")
        for (d in 0..9) press("Digit $d", "channel.digit $d")
        press("Add or remove favorite", "channel.favorite"); press("Go to typed channel", "channel.commit")
        done("Channels")
    }

    @Test fun musicTab() {
        start(); ui.tv(PhoneUi.fg("Music"), PhoneUi.SONG)
        tab("Music"); ui.clearSent()
        ui.tv("""{"type":"music.results","query":"ben&ben","items":[{"id":"hJhVURhdLEg","title":"Maybe The Night","artist":"Ben&Ben"},{"id":"bgCansrDpVo","title":"Make It With You","artist":"Ben&Ben"}]}""")
        ui.shot("b05-music-results")
        pressText("Make It With You", "music.play bgCansrDpVo")
        press("Previous song", "music.prev"); press("Next song", "music.next"); press("Stop the music", "music.stop")
        done("Music tab")
    }

    @Test fun moreTab() {
        start(); ui.tv(PhoneUi.fg("Home"))
        tab("More"); ui.clearSent()
        // Keyboard: letters as typed, Delete, Enter
        reach(rule.onNodeWithText("Type on the TV")).performTextInput("hi"); ui.settle()
        assertEquals(listOf("key.text hi"), ui.sent()); log += "TYPE \"hi\" -> key.text hi"
        press("Backspace", "key.backspace"); press("Enter", "key.enter"); press("Back", "nav.back")
        // Mouse
        pressText("Mouse"); press("Mouse click", "mouse.click"); press("Scroll up", "mouse.scroll -2"); press("Scroll down", "mouse.scroll 2")
        // More apps
        pressText("Apps"); ui.shot("b06-more-apps")
        listOf("primevideo" to "Prime Video", "disneyplus" to "Disney+", "hbomax" to "HBO Max", "viu" to "Viu", "iwanttfc" to "iWantTFC",
            "crunchyroll" to "Crunchyroll", "spotify" to "Spotify", "twitch" to "Twitch", "appletv" to "Apple TV", "plex" to "Plex").forEach { (id, name) -> press("Open $name", "app.open $id") }
        // Power: lock, the sleep timer, and the PC itself (asked first)
        pressText("Power"); ui.shot("b07-power")
        press("Lock the TV and show the clock", "power.screenoff"); press("Unlock the TV", "power.screenon")
        for (m in listOf(15, 30, 60, 90)) press("Sleep in $m minutes", "power.timer $m")
        press("Cancel the sleep timer", "power.timer 0")
        press("Put the whole PC to sleep"); pressText("Yes", "power.sleep")
        press("Restart the whole PC"); pressText("Yes", "power.restart")
        press("Shut down the whole PC"); pressText("Yes", "power.shutdown")
        done("More tab")
    }
}
