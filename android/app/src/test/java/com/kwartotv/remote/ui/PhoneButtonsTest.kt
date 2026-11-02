package com.kwartotv.remote.ui

import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.SemanticsNodeInteraction
import androidx.compose.ui.test.junit4.createAndroidComposeRule
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
import org.junit.Assert.assertTrue
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
@org.junit.FixMethodOrder(org.junit.runners.MethodSorters.NAME_ASCENDING)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [34], qualifiers = "w411dp-h891dp-port-hdpi")
class PhoneButtonsTest {
    @get:Rule val rule = createAndroidComposeRule<androidx.activity.ComponentActivity>()
    private lateinit var ui: PhoneUi
    private val log = mutableListOf<String>()

    private fun reach(n: SemanticsNodeInteraction): SemanticsNodeInteraction { try { n.performScrollTo() } catch (_: Throwable) { } ; return n }

    /** Presses the button named [what] (its spoken name) and checks the commands it sent. */
    private fun press(what: String, vararg expect: String) {
        ui.clearSent()
        reach(rule.onNodeWithContentDescription(what)).performClick()
        ui.settle(); ui.settle()
        assertEquals("button \"$what\"", expect.toList(), ui.sent())
        log += "PRESS \"$what\" -> ${expect.joinToString(" + ").ifEmpty { "(nothing sent)" }}"
    }

    private fun pressText(text: String, vararg expect: String) {
        ui.clearSent()
        val n = reach(rule.onAllNodesWithText(text).onLast())
        // in a pop-up (menu, dialog) the touch cannot always be injected: press it the way TalkBack does
        try { n.performClick() } catch (_: AssertionError) {
            if (rule.onAllNodesWithText(text).fetchSemanticsNodes().isNotEmpty()) rule.onAllNodesWithText(text).onLast().performSemanticsAction(SemanticsActions.OnClick)
        }
        ui.settle()
        assertEquals("\"$text\"", expect.toList(), ui.sent())
        log += "TAP \"$text\" -> ${expect.joinToString(" + ").ifEmpty { "(nothing sent)" }}"
    }

    private fun tab(name: String) { rule.onAllNodesWithText(name).onLast().performClick(); ui.settle(); ui.settle() }

    private fun start(): PhoneUi { ui = PhoneUi(rule); ui.show(); return ui }

    private val TINY_JPEG = run {
        val b = android.graphics.Bitmap.createBitmap(32, 18, android.graphics.Bitmap.Config.ARGB_8888).apply { eraseColor(android.graphics.Color.rgb(60, 90, 140)) }
        val o = java.io.ByteArrayOutputStream(); b.compress(android.graphics.Bitmap.CompressFormat.JPEG, 80, o); java.util.Base64.getEncoder().encodeToString(o.toByteArray())
    }

    private fun done(name: String) { println("== $name: ${log.size} buttons checked"); log.forEach { println("  $it") } }

    @Test fun t1_homeAndTheRemote() {
        start(); ui.tv(PhoneUi.fg("Home"))
        ui.shot("b00-remote")
        press("Open Live TV", "app.livetv"); press("Open YouTube", "app.youtube"); press("Open YT Music", "app.ytmusic")
        press("Open Netflix", "app.netflix"); press("Open Bilibili", "app.bilibili")
        press("Up", "nav.up"); press("Down", "nav.down"); press("Left", "nav.left"); press("Right", "nav.right"); press("OK", "nav.ok")
        press("Back", "nav.back"); press("Home", "nav.home"); press("Play or pause", "media.playpause")
        press("Volume down", "media.voldown"); press("Mute", "media.mute"); press("Volume up", "media.volup")
        // the swipe pad (from the menu): a tap is OK
        press("Menu"); pressText("Use the swipe pad"); ui.shot("b01-swipe-pad")
        pressText("Swipe to move\nTap = OK  ·  Long press = Back", "nav.ok")
        press("Menu"); pressText("Use the D-pad"); rule.onNodeWithContentDescription("Up").assertExists()
        press("Keyboard"); ui.shot("b12-after-keyboard-button"); rule.onAllNodesWithText("Keyboard").onLast().assertExists()
        done("Home")
    }

    @Test fun t6_netflixBrowsing() {
        start(); ui.tv(PhoneUi.fg("netflix"))
        ui.shot("b10-netflix-browsing")
        press("Up", "nav.up"); press("OK", "nav.ok"); press("Back", "nav.back"); press("Home", "nav.home"); press("Volume up", "media.volup")
        ui.clearSent()
        val box = rule.onAllNodes(androidx.compose.ui.test.hasSetTextAction())[0]
        reach(box).performTextInput("dark"); box.performImeAction(); ui.settle()
        assertEquals(listOf("netflix.search dark"), ui.sent()); log += "TYPE \"dark\" + search -> netflix.search dark"
        // A title's details open on the TV: Resume here presses its Resume there.
        ui.tv("""{"type":"netflix.now","title":"","episode":"","pos":0,"dur":0,"paused":true,"rate":1,"still":false,"modal":{"title":"Frieren: Beyond Journey's End","play":"Resume","id":"81234"}}""")
        ui.shot("b13-netflix-title-open")
        rule.onNodeWithText("Resume").assertExists()
        press("Play the title open on the TV", "netflix.play"); press("Choose an episode", "netflix.episodes")
        ui.tv("""{"type":"netflix.episodes","show":"Frieren","seasons":[{"n":"Season 1","eps":[{"id":"81234009","n":"9. Aura the Guillotine","on":true},{"id":"81234010","n":"10. A Powerful Mage","on":false}]}]}""")
        ui.shot("b14-netflix-title-episodes")
        pressText("A Powerful Mage", "netflix.episode 81234010")
        // ‹ goes back to the usual remote; the Netflix tile brings Netflix's remote back
        press("Back to the remote"); ui.shot("b18-netflix-left"); rule.onNodeWithContentDescription("Open Live TV").assertExists()
        press("Open Netflix", "app.netflix"); ui.shot("b19-netflix-again"); rule.onNodeWithContentDescription("Play the title open on the TV").assertExists()
        done("Netflix browsing")
    }

    @Test fun t7_netflixWatching() {
        start(); ui.tv(PhoneUi.fg("netflix"), PhoneUi.NETFLIX)
        ui.shot("b11-netflix-watching")
        press("Skip the intro or recap", "netflix.skip"); press("Play the next episode", "netflix.next")
        press("Play or pause", "media.playpause"); press("Back 10 seconds", "media.seek -10"); press("Forward 10 seconds", "media.seek 10")
        press("Back", "nav.back"); press("OK", "nav.ok"); press("Volume down", "media.voldown")
        // jump anywhere with the progress bar
        ui.clearSent()
        reach(rule.onNode(androidx.compose.ui.test.SemanticsMatcher.keyIsDefined(SemanticsActions.SetProgress))).performSemanticsAction(SemanticsActions.SetProgress) { it(1800f) }
        ui.settle(); assertEquals(listOf("netflix.seekto 1800"), ui.sent()); log += "MOVE progress bar to 30:00 -> netflix.seekto 1800"
        // a tap on the bar jumps there too (the middle of the bar = half the video)
        ui.clearSent(); rule.onNodeWithContentDescription("Video position").performClick(); ui.settle()
        assertEquals(1, ui.sent().size); assertTrue(ui.sent()[0].startsWith("netflix.seekto ")); log += "TAP progress bar -> ${ui.sent()[0]}"
        // Audio & Subtitles
        press("Choose the audio language and subtitles", "netflix.tracks")
        ui.tv("""{"type":"netflix.tracks","audio":[{"i":0,"n":"English [Original]","on":true},{"i":1,"n":"Japanese","on":false}],"subs":[{"i":0,"n":"Off","on":true},{"i":1,"n":"Filipino","on":false}]}""")
        ui.shot("b02-netflix-audio-subs")
        pressText("Japanese", "netflix.track a:1"); pressText("Filipino", "netflix.track s:1"); press("Done")
        // Episodes
        press("Choose an episode", "netflix.episodes")
        ui.tv("""{"type":"netflix.episodes","show":"Stranger Things","seasons":[{"n":"Season 1","eps":[{"id":"80057281","n":"1. The Vanishing","on":false}]},{"n":"Season 4","eps":[{"id":"81077842","n":"1. The Hellfire Club","on":true},{"id":"81077843","n":"2. Vecna's Curse","on":false}]}]}""")
        ui.shot("b03-netflix-episodes")
        pressText("Vecna's Curse", "netflix.episode 81077843")
        // Speed
        press("Playback speed"); ui.shot("b04-netflix-speed"); pressText("1.5×", "netflix.speed 1.5")
        // "Are you still watching?"
        ui.tv(PhoneUi.NETFLIX.replace("\"still\":false", "\"still\":true"))
        press("Answer Are you still watching", "netflix.continue")
        done("Netflix")
    }

    @Test fun t2_liveTvMusicVideosAndLock() {
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
        press("Unlock the TV", "power.screenon"); press("Open the Lock tab"); ui.shot("b20-lock-from-remote"); rule.onNodeWithText("Lock & Screensaver").assertExists()
        done("Live TV, music, videos, lock")
    }

    /** The Lock workspace: every option goes to the TV (the preview's own photo requests are left out of the check). */
    @Test fun t8_lockTab() {
        start(); ui.tv(PhoneUi.fg("Home"), PhoneUi.SONG)
        tab("Lock")
        println("sent on the Lock tab: " + ui.sent())
        assertTrue("asks the TV for the lock screen", ui.sent().contains("lock.get"))
        ui.tv("""{"type":"lock.state","locked":false,"clock":"classic","color":"FFFFFF","dim":100,"date":true,"song":true,"verse":true,"bg":"photos","folder":"C:\\Users\\me\\Pictures","folderName":"Pictures","photos":24,"every":20,"saver":10,"timer":0}""")
        ui.tv("""{"type":"lock.photo","i":0,"n":24,"jpg":"$TINY_JPEG"}""")
        ui.shot("b15-lock")
        lockPress("Lock the TV and show the clock", "power.screenoff"); lockPress("Unlock the TV", "power.screenon")
        lockPress("Black", "lock.set bg=black"); lockPress("Midnight", "lock.set bg=midnight")
        lockPress("10 sec", "lock.set every=10"); lockPress("5 min", "lock.set every=300")
        lockPress("Big", "lock.set clock=big"); lockPress("Corner", "lock.set clock=corner")
        lockPress("Clock color Blue", "lock.set color=9DB8FF"); lockPress("Dim", "lock.set dim=40")
        lockPress("Hide the date", "lock.set date=0"); lockPress("Hide the song", "lock.set song=0"); lockPress("Hide the Bible verse", "lock.set verse=0")
        lockPress("Screensaver after 5 min", "lock.set saver=5"); lockPress("Screensaver after Off", "lock.set saver=0")
        lockPress("Sleep in 30 minutes", "power.timer 30"); lockPress("Cancel the sleep timer", "power.timer 0")
        lockPress("Choose the photo folder", "lock.folders")
        ui.tv("""{"type":"lock.folders","folders":[{"path":"C:\\Users\\me\\Pictures","name":"Pictures"},{"path":"C:\\Users\\me\\Pictures\\Trips","name":"Trips"}]}""")
        ui.shot("b16-lock-folders")
        lockPress("Photos from Trips", "lock.set folder=C:\\Users\\me\\Pictures\\Trips")
        // what the TV reports shows: a big clock on midnight blue, locked
        ui.tv("""{"type":"lock.state","locked":true,"clock":"big","color":"9DB8FF","dim":70,"date":false,"song":true,"verse":false,"bg":"midnight","folder":"","folderName":"","photos":0,"every":20,"saver":5,"timer":30}""")
        ui.shot("b17-lock-big-midnight")
        rule.onNodeWithText("Locked").assertExists(); rule.onNodeWithText("The TV stops and locks in 30 min").assertExists()
        done("Lock")
    }

    private fun lockPress(what: String, vararg expect: String) {
        ui.clearSent()
        reach(rule.onNodeWithContentDescription(what)).performClick()
        ui.settle()
        assertEquals("button \"$what\"", expect.toList(), ui.sent().filterNot { it.startsWith("lock.photo") || it == "lock.get" })
        log += "PRESS \"$what\" -> ${expect.joinToString(" + ")}"
    }

    @Test fun t3_channelsTab() {
        start(); ui.tv(PhoneUi.fg("live"), PhoneUi.channel(1, "GMA 7"), PhoneUi.LINEUP)
        tab("Channels"); ui.clearSent()
        ui.shot("b09-channels")
        pressText("HBO", "channel.number 5")
        pressText("Keypad")
        press("Open the channel list on the TV", "channel.list"); press("Channel up", "channel.next"); press("Last channel", "channel.last"); press("Channel down", "channel.prev")
        for (d in 0..9) press("Digit $d", "channel.digit $d")
        press("Add or remove favorite", "channel.favorite"); press("Go to typed channel", "channel.commit")
        done("Channels")
    }

    @Test fun t4_musicTab() {
        start(); ui.tv(PhoneUi.fg("Music"), PhoneUi.SONG)
        tab("Music"); ui.clearSent()
        ui.tv("""{"type":"music.results","query":"ben&ben","items":[{"id":"hJhVURhdLEg","title":"Maybe The Night","artist":"Ben&Ben"},{"id":"bgCansrDpVo","title":"Make It With You","artist":"Ben&Ben"}]}""")
        ui.shot("b05-music-results")
        pressText("Make It With You", "music.play bgCansrDpVo")
        press("Previous song", "music.prev"); press("Next song", "music.next"); press("Stop the music", "music.stop")
        done("Music tab")
    }

    @Test fun t5_moreTab() {
        start(); ui.tv(PhoneUi.fg("Home"))
        tab("More"); ui.clearSent()
        // Keyboard: letters as typed, Delete, Enter
        ui.shot("b08-keyboard")
        reach(rule.onAllNodes(androidx.compose.ui.test.hasSetTextAction())[0]).performTextInput("hi"); ui.settle()
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
        press("Put the whole PC to sleep"); pressText("Yes", "power.sleep")
        press("Restart the whole PC"); pressText("Yes", "power.restart")
        press("Shut down the whole PC"); pressText("Yes", "power.shutdown")
        done("More tab")
    }
}
