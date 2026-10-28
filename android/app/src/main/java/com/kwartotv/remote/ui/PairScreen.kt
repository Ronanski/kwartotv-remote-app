package com.kwartotv.remote.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import com.kwartotv.remote.AppViewModel
import com.kwartotv.remote.PairLink
import com.kwartotv.remote.parseHostPort
import kotlinx.coroutines.launch

@Composable
fun PairScreen(vm: AppViewModel, canCancel: Boolean) {
    val found by vm.found.collectAsState()
    val scope = rememberCoroutineScope()
    var address by remember { mutableStateOf("") }
    var pin by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }

    fun pairWith(host: String, port: Int?, code: String) {
        if (busy) return
        busy = true; error = null
        scope.launch {
            vm.pair(host, port, code).onFailure { error = it.message ?: "Could not pair." }
            busy = false
        }
    }

    val scanner = rememberLauncherForActivityResult(ScanContract()) { result ->
        val text = result.contents ?: return@rememberLauncherForActivityResult
        val link = PairLink.parse(text)
        if (link == null) error = "That QR code is not from KwartoTV."
        else pairWith(link.host, link.port, link.pin)
    }

    Column(
        Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        UpdateBanner(vm)
        if (canCancel) TextButton(onClick = { vm.adding.value = false }) { Text("‹ Back to remote") }
        Text("Connect to your TV", fontSize = 28.sp, fontWeight = FontWeight.Bold)
        Text(
            "On the TV open Settings → Paired devices → Pair new device. Then scan the QR code, or type the 6-digit code.",
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        Button(
            onClick = {
                scanner.launch(
                    ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE).setPrompt("Scan the code on the TV")
                        .setBeepEnabled(false).setOrientationLocked(false)
                )
            },
            enabled = !busy,
            modifier = Modifier.fillMaxWidth().padding(top = 4.dp),
        ) { Text("Scan QR code", fontSize = 18.sp, modifier = Modifier.padding(vertical = 8.dp)) }

        Text("TVs on this Wi-Fi", fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 8.dp))
        if (found.isEmpty()) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                Text("Looking…", color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        found.forEach { tv ->
            Surface(
                shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.surfaceContainer,
                modifier = Modifier.fillMaxWidth().clickable { address = "${tv.host}:${tv.port}" },
            ) {
                Column(Modifier.padding(16.dp)) {
                    Text(tv.name, fontWeight = FontWeight.SemiBold)
                    Text("${tv.host}:${tv.port}", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }

        OutlinedTextField(
            value = address, onValueChange = { address = it }, singleLine = true,
            label = { Text("TV address (e.g. 192.168.1.20)") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
            modifier = Modifier.fillMaxWidth(),
        )
        OutlinedTextField(
            value = pin, onValueChange = { v -> pin = v.filter(Char::isDigit).take(6) }, singleLine = true,
            label = { Text("6-digit code") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
            modifier = Modifier.fillMaxWidth(),
        )
        error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        OutlinedButton(
            onClick = {
                val hp = parseHostPort(address)
                when {
                    hp == null -> error = "Enter the TV address shown on the TV."
                    pin.length != 6 -> error = "Enter the 6-digit code shown on the TV."
                    else -> pairWith(hp.first, hp.second, pin)
                }
            },
            enabled = !busy,
            modifier = Modifier.fillMaxWidth(),
        ) {
            if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
            else Text("Pair", fontSize = 18.sp, modifier = Modifier.padding(vertical = 6.dp))
        }
    }
}
