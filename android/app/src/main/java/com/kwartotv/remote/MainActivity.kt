package com.kwartotv.remote

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.material3.Surface
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import com.kwartotv.remote.ui.PairScreen
import com.kwartotv.remote.ui.RemoteScreen
import com.kwartotv.remote.ui.KwartoTheme

class MainActivity : ComponentActivity() {
    private val vm: AppViewModel by viewModels()
    // The "connected to the TV" notification (with play/pause and volume) needs this on Android 13 and later.
    private val askNotifications = registerForActivityResult(ActivityResultContracts.RequestPermission()) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            askNotifications.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
        setContent {
            KwartoTheme {
                val tvs by vm.tvs.collectAsState()
                val adding by vm.adding.collectAsState()
                // Connect while the app is on screen, drop the connection when it is not.
                LifecycleEventEffect(Lifecycle.Event.ON_START) { vm.onStart() }
                LifecycleEventEffect(Lifecycle.Event.ON_STOP) { vm.onStop() }
                Surface(Modifier.fillMaxSize().safeDrawingPadding()) {
                    if (tvs.isEmpty() || adding) PairScreen(vm, canCancel = tvs.isNotEmpty()) else RemoteScreen(vm)
                }
            }
        }
    }
}
