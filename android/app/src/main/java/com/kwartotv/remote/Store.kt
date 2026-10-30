package com.kwartotv.remote

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import org.json.JSONArray
import java.security.KeyStore
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * The TVs this phone is paired with. The pairing token is encrypted with a key that never leaves the phone's
 * Android Keystore, so the app's preferences file alone does not give the token away.
 */
class Store(context: Context) {
    private val prefs = context.getSharedPreferences("kwartotv", Context.MODE_PRIVATE)

    fun load(): List<SavedTv> {
        val tvs = try {
            val a = JSONArray(prefs.getString("tvs", "[]"))
            (0 until a.length()).map { a.getJSONObject(it) }
        } catch (_: Exception) { return emptyList() }
        // Tokens saved by older versions are plain text: read them as they are and save them encrypted right away.
        var plain = false
        val list = tvs.mapNotNull { o ->
            try {
                val stored = o.getString("token")
                if (!TokenCipher.isSealed(stored)) plain = true
                // A token that no longer decrypts (keystore reset) becomes empty: the TV refuses it and the app asks to pair again.
                SavedTv.fromJson(o.put("token", TokenCipher.open(stored) ?: ""))
            } catch (_: Exception) { null }
        }
        if (plain && list.isNotEmpty()) save(list)
        return list
    }

    fun save(tvs: List<SavedTv>) {
        val a = JSONArray(); tvs.forEach { a.put(it.toJson().put("token", TokenCipher.seal(it.token))) }
        prefs.edit().putString("tvs", a.toString()).apply()
    }

    var selectedId: String?
        get() = prefs.getString("selected", null)
        set(v) { prefs.edit().putString("selected", v).apply() }

    /** 0 = D-pad, 1 = touchpad (arrows), 2 = mouse. */
    var padKind: Int
        get() = prefs.getInt("padKind", 0)
        set(v) { prefs.edit().putInt("padKind", v).apply() }
}

/** AES-GCM with a Keystore key. Sealed text looks like "k1:<iv base64>:<ciphertext base64>". */
private object TokenCipher {
    private const val ALIAS = "kwartotv-tokens"
    private const val PREFIX = "k1:"

    fun isSealed(text: String) = text.startsWith(PREFIX)

    private fun key(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(
            KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build()
        )
        return gen.generateKey()
    }

    /** Encrypts; if the keystore is broken on this phone, keeps the token as before rather than losing the pairing. */
    fun seal(token: String): String = try {
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, key())
        val b64 = Base64.getEncoder()
        PREFIX + b64.encodeToString(c.iv) + ":" + b64.encodeToString(c.doFinal(token.toByteArray(Charsets.UTF_8)))
    } catch (_: Exception) { token }

    /** Plain text (old versions) comes back as it is; null when sealed text cannot be decrypted. */
    fun open(text: String): String? {
        if (!isSealed(text)) return text
        return try {
            val parts = text.removePrefix(PREFIX).split(':')
            if (parts.size != 2) return null
            val b64 = Base64.getDecoder()
            val c = Cipher.getInstance("AES/GCM/NoPadding")
            c.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, b64.decode(parts[0])))
            String(c.doFinal(b64.decode(parts[1])), Charsets.UTF_8)
        } catch (_: Exception) { null }
    }
}
