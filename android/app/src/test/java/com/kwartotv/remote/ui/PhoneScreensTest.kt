package com.kwartotv.remote.ui

import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onLast
import androidx.compose.ui.test.performClick
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/** Pictures of every screen of the phone app in each state the TV can be in (for the CI log). */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [34], qualifiers = "w411dp-h891dp-port-hdpi")
class PhoneScreensTest {
    @get:Rule val rule = createAndroidComposeRule<androidx.activity.ComponentActivity>()

    @Test fun screens() {
        val ui = PhoneUi(rule)
        ui.show()
        ui.tv(PhoneUi.fg("Home"))
        ui.shot("p01-remote-home")
        ui.tv(PhoneUi.fg("netflix"), PhoneUi.NETFLIX)
        ui.shot("p02-remote-netflix")
        ui.tv(PhoneUi.fg("live"), PhoneUi.channel(7, "GMA 7"))
        ui.shot("p03-remote-live")
        ui.tv(PhoneUi.fg("Music"), PhoneUi.SONG)
        ui.shot("p04-remote-music")
        ui.tv(PhoneUi.fg("locked"))
        ui.shot("p05-remote-locked")
        ui.tv(PhoneUi.fg("live"), PhoneUi.LINEUP)
        rule.onAllNodesWithText("Channels").onLast().performClick(); ui.settle()
        ui.shot("p06-channels")
        rule.onAllNodesWithText("Music").onLast().performClick(); ui.settle()
        ui.shot("p07-music")
        rule.onAllNodesWithText("More").onLast().performClick(); ui.settle()
        ui.shot("p08-more")
    }
}
