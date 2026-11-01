package com.kwartotv.remote.ui

import android.annotation.SuppressLint
import android.webkit.CookieManager
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Toast
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.kwartotv.remote.AppViewModel
import com.kwartotv.remote.SIGN_IN_SERVICES
import com.kwartotv.remote.SignInService
import com.kwartotv.remote.hasSessionCookie
import com.kwartotv.remote.isSignedIn

/**
 * Signing in to Netflix or Bilibili for the TV, on the phone: the service's own sign-in page opens here (where typing,
 * the password manager and e-mail codes are easy). As soon as the phone is signed in, the sign-in goes to the TV, which
 * opens the service signed in, ready for the arrows, OK and Back.
 *
 * Opens by itself when the TV says a service is not signed in; can also be opened from the menu ([manual]).
 */
@Composable
fun SignInForTv(vm: AppViewModel, manual: String?, onManualDone: () -> Unit) {
    val client = vm.client
    val context = LocalContext.current
    val request by client.signIn.collectAsState()
    var open by remember { mutableStateOf<SignInService?>(null) }

    // The TV says it worked.
    LaunchedEffect(request) {
        val r = request ?: return@LaunchedEffect
        vm.markSignedIn(r.service, r.done)
        if (r.done) { Toast.makeText(context, "${r.name} is signed in on the TV.", Toast.LENGTH_SHORT).show(); client.clearSignIn() }
    }
    LaunchedEffect(manual) { if (manual != null) open = SIGN_IN_SERVICES[manual] }

    val asked = request?.takeIf { !it.done }
    if (asked != null && open == null) {
        AlertDialog(
            onDismissRequest = { client.clearSignIn() },
            title = { Text("Sign in to ${asked.name}") },
            text = { Text("${asked.name} on the TV is not signed in. Sign in here on your phone (much easier than on the TV); the TV signs in by itself.") },
            confirmButton = { TextButton(onClick = { open = SIGN_IN_SERVICES[asked.service]; client.clearSignIn() }) { Text("Sign in on phone") } },
            dismissButton = { TextButton(onClick = { client.clearSignIn() }) { Text("Not now") } },
        )
    }

    val service = open ?: return
    fun close() { open = null; onManualDone() }
    Dialog(onDismissRequest = { close() }, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Surface(Modifier.fillMaxSize()) {
            Column(Modifier.fillMaxSize()) {
                Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text("Sign in to ${service.name} for the TV", fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
                        Text("Use e-mail or phone sign-in. The TV signs in by itself when you are in.", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    TextButton(onClick = { close() }) { Text("Close") }
                }
                fun send(cookies: String) {
                    if (client.sendSignIn(service.id, cookies)) {
                        Toast.makeText(context, "Signed in. Look at the TV.", Toast.LENGTH_LONG).show()
                        close()
                    } else {
                        Toast.makeText(context, "Signed in, but the TV is not connected. Reconnecting… then press Send to TV again.", Toast.LENGTH_LONG).show()
                        vm.reconnect()
                    }
                }
                // Signed in but the TV did not pick it up by itself (the service went to a page we do not know): send it by hand.
                Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("Already signed in?", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(1f))
                    TextButton(onClick = {
                        val text = CookieManager.getInstance().getCookie("https://www.${service.domain}")
                        if (hasSessionCookie(service, text)) send(text.orEmpty())
                        else Toast.makeText(context, "Not signed in yet. Finish signing in on the page first.", Toast.LENGTH_LONG).show()
                    }) { Text("Send to TV") }
                }
                SignInPage(service) { cookies -> send(cookies) }
            }
        }
    }
}

@SuppressLint("SetJavaScriptEnabled")
@Composable
private fun SignInPage(service: SignInService, onSignedIn: (String) -> Unit) {
    var sent by remember(service) { mutableStateOf(false) }
    AndroidView(
        modifier = Modifier.fillMaxSize(),
        factory = { ctx ->
            WebView(ctx).apply {
                settings.javaScriptEnabled = true
                settings.domStorageEnabled = true
                val cookies = CookieManager.getInstance()
                cookies.setAcceptCookie(true)
                cookies.setAcceptThirdPartyCookies(this, true)
                fun check(url: String?) {
                    if (sent) return
                    val text = cookies.getCookie("https://www.${service.domain}")
                    if (isSignedIn(service, text, url)) {
                        sent = true
                        cookies.flush()
                        onSignedIn(text.orEmpty())
                    }
                }
                webViewClient = object : WebViewClient() {
                    override fun onPageFinished(view: WebView, url: String?) { super.onPageFinished(view, url); check(url) }
                    // The sites change pages without loading a new one (single-page apps): look at those too.
                    override fun doUpdateVisitedHistory(view: WebView, url: String?, isReload: Boolean) { super.doUpdateVisitedHistory(view, url, isReload); check(url) }
                }
                loadUrl(service.page)
            }
        },
    )
}
