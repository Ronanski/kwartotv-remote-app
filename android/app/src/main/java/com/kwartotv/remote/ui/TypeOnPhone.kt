package com.kwartotv.remote.ui

import android.text.InputType
import android.view.View
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.EditText
import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.rounded.Mic
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import com.kwartotv.remote.AppViewModel
import com.kwartotv.remote.Haptics
import com.kwartotv.remote.TextRequest

/**
 * When a text box on the TV waits for text (a Netflix or Bilibili sign-in, a search, a link), the phone opens its own
 * keyboard. The field is a real Android text field marked as e-mail / password / ..., so the phone's password manager
 * (Google, Samsung Pass) can fill it in one tap. The whole text then goes to the TV at once.
 */
@Composable
fun TypeOnPhone(vm: AppViewModel) {
    val client = vm.client
    val request by client.textRequest.collectAsState()
    val r = request ?: return
    val context = LocalContext.current
    val colors = MaterialTheme.colorScheme
    // One field per request (a new request, such as the password after the e-mail, starts fresh).
    val field = remember(r) {
        EditText(context).apply {
            setText(r.value); setSelection(text.length)
            setSingleLine(true)
            textSize = 17f
            setTextColor(colors.onSurface.toArgb()); setHintTextColor(colors.onSurfaceVariant.toArgb())
            hint = r.title ?: hintFor(r)
            inputType = inputTypeFor(r)
            imeOptions = EditorInfo.IME_ACTION_GO
            importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_YES
            autofillHintFor(r)?.let { setAutofillHints(it) }
        }
    }
    // What is typed here shows on the TV as it is typed (one text box, not two keyboards that fight).
    val live = remember(r) { android.os.Handler(android.os.Looper.getMainLooper()) }
    val sendLive = remember(r) { Runnable { client.sendText(field.text.toString(), false) } }
    remember(r) {
        field.addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
            override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
            override fun afterTextChanged(s: android.text.Editable?) { live.removeCallbacks(sendLive); live.postDelayed(sendLive, 250) }
        })
    }
    // What the TV's box shows now (the phone's text, sent as it is typed).
    var shown by remember(r) { mutableStateOf(r.value) }
    remember(r) {
        field.addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
            override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
            override fun afterTextChanged(s: android.text.Editable?) { shown = s?.toString() ?: "" }
        })
    }
    val voice = rememberVoice(r.title ?: titleFor(r)) { said -> field.setText(said); field.setSelection(field.text.length) }
    fun send(go: Boolean) {
        live.removeCallbacks(sendLive)
        Haptics.click(context)
        if (client.sendText(field.text.toString(), go)) client.dismissText()
        else { Toast.makeText(context, "Not connected to the TV. Reconnecting…", Toast.LENGTH_SHORT).show(); vm.reconnect() }
    }
    field.setOnEditorActionListener { _, _, _ -> send(true); true }

    KSheet("Type on the TV", r.title ?: titleFor(r), onClose = { live.removeCallbacks(sendLive); client.sendText(field.text.toString(), false); client.dismissText() }) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            // The TV's box, as it is now: what is typed here shows there right away.
            Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(Color(0xFF0A0B0F)).border(1.dp, Hairline, RoundedCornerShape(16.dp)).padding(12.dp)) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(6.dp).clip(CircleShape).background(Live))
                    Text("  On the TV now", style = T.mini.copy(fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold), color = Muted)
                }
                Text(
                    if (r.kind == "password") "•".repeat(shown.length).ifEmpty { " " } else shown.ifEmpty { " " },
                    style = T.body, color = Ink,
                    modifier = Modifier.padding(top = 8.dp).fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(Color(0xFF1E2027)).padding(horizontal = 14.dp, vertical = 9.dp),
                )
            }
            Box(Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(Screen).border(2.dp, Accent, RoundedCornerShape(18.dp)).padding(horizontal = 12.dp, vertical = 4.dp)) {
                AndroidView(factory = {
                    field.also { f ->
                        f.background = null
                        f.post {
                            f.requestFocus()
                            (f.context.getSystemService(InputMethodManager::class.java))?.showSoftInput(f, InputMethodManager.SHOW_IMPLICIT)
                        }
                    }
                }, modifier = Modifier.fillMaxWidth())
            }
            Text(
                if (r.kind == "password") "Type it or let your phone's password manager fill it. It goes to the TV only."
                else "What you type shows on the TV right away.",
                style = T.cap, color = Muted, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth(),
            )
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (r.kind != "password") Cta("Say it", "Say it", Modifier.weight(1f), kind = CtaKind.Line, icon = androidx.compose.material.icons.Icons.Rounded.Mic) { voice() }
                Cta(if (r.kind == "search") "Search" else "Go", if (r.kind == "search") "Search" else "Send and go", Modifier.weight(1f)) { send(true) }
            }
        }
    }
}

private fun titleFor(r: TextRequest) = when (r.kind) {
    "password" -> "Password for the TV"
    "email" -> "E-mail or username"
    "search" -> "Search on the TV"
    "url" -> "Web address"
    else -> "Type on the TV"
}

private fun hintFor(r: TextRequest) = when (r.kind) {
    "password" -> "Password"
    "email" -> "name@example.com"
    "search" -> "Search"
    "url" -> "https://"
    else -> "Text"
}

private fun inputTypeFor(r: TextRequest) = when (r.kind) {
    "password" -> InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
    "email" -> InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS
    "url" -> InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
    else -> InputType.TYPE_CLASS_TEXT
}

private fun autofillHintFor(r: TextRequest) = when (r.kind) {
    "password" -> View.AUTOFILL_HINT_PASSWORD
    "email" -> View.AUTOFILL_HINT_EMAIL_ADDRESS
    else -> null
}
