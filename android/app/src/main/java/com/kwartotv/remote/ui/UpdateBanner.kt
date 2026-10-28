package com.kwartotv.remote.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kwartotv.remote.AppViewModel
import com.kwartotv.remote.UpdateState

/** "Update available" strip. The first time, Android asks to allow installs from this app: allow it, then press Install again. */
@Composable
fun UpdateBanner(vm: AppViewModel) {
    val state by vm.update.collectAsState()
    val s = state
    if (s is UpdateState.None) return
    Row(
        Modifier.fillMaxWidth().padding(vertical = 4.dp).clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surfaceContainer).padding(start = 14.dp, top = 4.dp, bottom = 4.dp, end = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        when (s) {
            is UpdateState.Available -> {
                Text("Update ${s.info.version} available", Modifier.weight(1f), fontSize = 14.sp)
                TextButton(onClick = { vm.installUpdate(s.info) }) { Text("Install") }
            }
            is UpdateState.Working -> Text(if (s.installing) "Installing…" else "Downloading update… ${s.percent}%", Modifier.weight(1f), fontSize = 14.sp)
            is UpdateState.Failed -> {
                Text(s.message, Modifier.weight(1f), fontSize = 13.sp, color = MaterialTheme.colorScheme.error)
                TextButton(onClick = { vm.installUpdate(s.info) }) { Text("Retry") }
            }
            UpdateState.None -> Unit
        }
    }
}
